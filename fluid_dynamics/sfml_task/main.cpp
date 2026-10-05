// Моделирование 2D-движения и упругих соударений частиц (без гравитации).
//
// Управление:
//   ЛКМ + drag & drop   — создание частицы (вектор натяжения задает скорость);
//   Колесо мыши         — изменение радиуса частиц;
//   Пробел              — пауза;
//   Кнопка «+10 random» — генерация 10 случайных частиц.
//
// Логирование: параметры каждой итерации пишутся в simulation_log.csv.

#include <SFML/Graphics.hpp>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <fstream>
#include <iostream>
#include <optional>
#include <random>
#include <string>
#include <vector>

/// Физическое состояние частицы
struct Particle {
    float x;                             // X-координата центра (м)
    float y;                             // Y-координата центра (м)
    float vx;                            // Скорость по X (м/с)
    float vy;                            // Скорость по Y (м/с)
    float m;                             // Масса (кг)
    sf::Color color = sf::Color::Green;
};

/**
 * @brief Численный шаг интегрирования: метод Эйлера, упругие удары о стенки и соударения шаров.
 * @param particles Список частиц симуляции.
 * @param dt Временной шаг (с).
 * @param radius Радиус шара (м).
 * @param boxW Ширина рабочей области (м).
 * @param boxH Высота рабочей области (м).
 * @param border Толщина внешней рамки (м).
 */
void stepParticles(std::vector<Particle>& particles, float dt, float radius,
                   float boxW, float boxH, float border)
{
    const float minX = border + radius;
    const float maxX = boxW - border - radius;
    const float minY = border + radius;
    const float maxY = boxH - border - radius;

    // Упругий отскок от границ
    auto resolveWalls = [&](Particle& p) {
        if (p.x < minX) { p.x = minX; if (p.vx < 0.f) p.vx = -p.vx; }
        if (p.x > maxX) { p.x = maxX; if (p.vx > 0.f) p.vx = -p.vx; }
        if (p.y < minY) { p.y = minY; if (p.vy < 0.f) p.vy = -p.vy; }
        if (p.y > maxY) { p.y = maxY; if (p.vy > 0.f) p.vy = -p.vy; }
    };

    // 1. Интегрирование координат и обработка стенок
    for (auto& p : particles) {
        p.x += p.vx * dt;
        p.y += p.vy * dt;
        resolveWalls(p);
    }

    // 2. Парные упругие соударения шаров
    const float minDist = 2.f * radius;
    const float minDistSq = minDist * minDist;

    for (size_t i = 0; i < particles.size(); ++i) {
        for (size_t j = i + 1; j < particles.size(); ++j) {
            Particle& p1 = particles[i];
            Particle& p2 = particles[j];

            const float dx = p1.x - p2.x;
            const float dy = p1.y - p2.y;
            const float distSq = dx * dx + dy * dy;

            if (distSq <= 0.f || distSq > minDistSq)
                continue;

            const float dvx = p1.vx - p2.vx;
            const float dvy = p1.vy - p2.vy;
            const float dot = dvx * dx + dvy * dy;

            // Обрабатываем только сближающиеся частицы
            if (dot >= 0.f)
                continue;

            const float dist = std::sqrt(distSq);
            const float totalMass = p1.m + p2.m;
            const float k1 = (2.0f * p2.m) / totalMass * (dot / distSq);
            const float k2 = (2.0f * p1.m) / totalMass * (dot / distSq);

            // Обновление скоростей
            p1.vx -= k1 * dx;
            p1.vy -= k1 * dy;
            p2.vx += k2 * dx;
            p2.vy += k2 * dy;

            // Коррекция взаимного перекрытия
            const float overlap = 0.5f * (minDist - dist);
            const float nx = dx / dist;
            const float ny = dy / dist;
            p1.x += nx * overlap;
            p1.y += ny * overlap;
            p2.x -= nx * overlap;
            p2.y -= ny * overlap;

            resolveWalls(p1);
            resolveWalls(p2);
        }
    }
}

/// Вычисление суммарной кинетической энергии системы (Дж)
float kineticEnergy(const std::vector<Particle>& particles)
{
    float energy = 0.f;
    for (const Particle& p : particles)
        energy += (p.vx * p.vx + p.vy * p.vy) * p.m / 2.f;
    return energy;
}

/// Форматирование числа с фиксированным числом знаков после запятой
std::string formatNumber(float value, int decimals)
{
    char buffer[32];
    std::snprintf(buffer, sizeof(buffer), "%.*f", decimals, value);
    return std::string(buffer);
}

/// UI-виджет блока с центрированным текстом и границей
class TextBox : public sf::Drawable {
    sf::Font font;                      // Шрифт копируется внутрь для корректного времени жизни
    sf::Vector2f size;
    sf::Vector2f position;
    sf::RectangleShape box;
    sf::Text text;

public:
    explicit TextBox(const sf::Font& f) : font(f), text(font, "", 14) {
        box.setOutlineThickness(1.f);
    }

    TextBox(const TextBox&) = delete;
    TextBox& operator=(const TextBox&) = delete;

    void setPosition(sf::Vector2f p) {
        position = p;
        box.setPosition(position);

        const sf::FloatRect bounds = text.getLocalBounds();
        text.setOrigin({bounds.position.x + bounds.size.x / 2.f,
                        bounds.position.y + bounds.size.y / 2.f});
        text.setPosition({position.x + size.x / 2.f, position.y + size.y / 2.f});
    }

    void setSize(sf::Vector2f s) {
        size = s;
        box.setSize(size);
        setPosition(position);
    }

    void setOutlineBoxThickness(float s) { box.setOutlineThickness(s); }

    void setString(const std::string& s) {
        text.setString(s);
        setPosition(position);
    }

    void setCharacterSize(unsigned s) {
        text.setCharacterSize(s);
        setPosition(position);
    }

    void setTextColor(sf::Color c) {
        text.setFillColor(c);
        text.setOutlineColor(c);
    }

    void setFillBoxColor(sf::Color c) { box.setFillColor(c); }
    void setOutlineBoxColor(sf::Color c) { box.setOutlineColor(c); }

    /// Проверка попадания точки в границы блока (для кнопок)
    bool contains(sf::Vector2f p) const {
        return p.x >= position.x && p.x <= position.x + size.x &&
               p.y >= position.y && p.y <= position.y + size.y;
    }

    void draw(sf::RenderTarget& target, sf::RenderStates states) const override {
        target.draw(box, states);
        target.draw(text, states);
    }

    ~TextBox() override = default;
};

int main()
{
    const float dt = 0.01f;
    const unsigned winW = 800;
    const unsigned winH = 600;
    const float boxW = static_cast<float>(winW);
    const float boxH = static_cast<float>(winH);
    const float border = 1.f;

    sf::RenderWindow window(sf::VideoMode({winW, winH}), "LR2: particle flight");

    // Рамка границ области симуляции
    sf::Vertex boxBorders[] = {
        {{0.f, winH - 1.f}, sf::Color::White}, {{boxW, winH - 1.f}, sf::Color::White},
        {{0.f, 1.f}, sf::Color::White},        {{boxW, 1.f}, sf::Color::White},
        {{1.f, 0.f}, sf::Color::White},        {{1.f, boxH}, sf::Color::White},
        {{winW - 1.f, 0.f}, sf::Color::White}, {{winW - 1.f, boxH}, sf::Color::White}
    };

    float radius = 5.f;
    const float mass = 1.0f;
    bool isPaused = false;
    std::vector<Particle> particles;

    const sf::Color colors[] = {
        sf::Color::Green, sf::Color::Yellow, sf::Color::Cyan,
        sf::Color::Magenta, sf::Color::Red
    };
    size_t colorIdx = 0;

    sf::CircleShape circle(radius);

    // Ограничение координат центра шара рамкой окна
    const auto clampInside = [&](const sf::Vector2f& p) {
        return sf::Vector2f(std::clamp(p.x, border + radius, boxW - border - radius),
                            std::clamp(p.y, border + radius, boxH - border - radius));
    };

    // Состояние прицеливания (задание начальной скорости)
    bool isAiming = false;
    sf::Vector2f aimStartPx;
    sf::Vector2f aimCurrentPx;

    float currentEnergy = 0.f;
    float initialEnergy = 0.f;

    sf::Font font;
    if (!font.openFromFile("./DejaVuSans.ttf")) {
        return -1;
    }

    // Инициализация виджетов панели метрик
    const sf::Color buttonIdle(73, 79, 82);
    const sf::Color buttonHover(34, 102, 54);

    auto setupWidget = [&](TextBox& w, float xPos) {
        w.setSize({126.f, 36.f});
        w.setPosition({xPos, 6.f});
        w.setOutlineBoxThickness(-1.f);
        w.setCharacterSize(11);
        w.setFillBoxColor(buttonIdle);
        w.setOutlineBoxColor(sf::Color::White);
        w.setTextColor(sf::Color::White);
    };

    TextBox energyText(font);     setupWidget(energyText, 6.f);
    TextBox errorText(font);      setupWidget(errorText, 136.f);
    TextBox iterationsText(font); setupWidget(iterationsText, 266.f);
    TextBox radiusText(font);     setupWidget(radiusText, 396.f);
    TextBox ballsText(font);      setupWidget(ballsText, 526.f);
    TextBox addBallsButton(font); setupWidget(addBallsButton, 656.f);

    iterationsText.setString("Iterations:\n0");
    radiusText.setString("Radius:\n" + formatNumber(radius, 1) + " px");
    ballsText.setString("Balls:\n0");
    addBallsButton.setString("+10 random balls");

    // Инициализация CSV-журнала
    std::ofstream logFile("simulation_log.csv");
    if (!logFile)
        std::cerr << "Не удалось открыть simulation_log.csv для записи\n";
    logFile << "iteration,balls,radius,kinetic_energy,error\n";

    unsigned long long iteration = 0;

    // Выполнение шага моделирования, обновление энергии и лога
    const auto runIteration = [&]() {
        stepParticles(particles, dt, radius, boxW, boxH, border);
        ++iteration;
        currentEnergy = kineticEnergy(particles);

        logFile << iteration << ',' << particles.size() << ',' << radius << ','
                << currentEnergy << ',' << (currentEnergy - initialEnergy) << '\n';

        if (iteration % 100 == 0) {
            iterationsText.setString("Iterations:\n" + std::to_string(iteration));
            logFile.flush();
        }
    };

    // Спавн n непересекающихся частиц со случайной скоростью
    std::mt19937 rng(std::random_device{}());
    const auto addRandomParticles = [&](int count) {
        std::uniform_real_distribution<float> posX(border + radius, boxW - border - radius);
        std::uniform_real_distribution<float> posY(border + radius, boxH - border - radius);
        std::uniform_real_distribution<float> angle(0.f, 2.f * std::acos(-1.0f));
        std::uniform_real_distribution<float> speed(25.f, 600.f);

        const float energyBefore = kineticEnergy(particles);
        for (int i = 0; i < count; ++i) {
            sf::Vector2f pos(posX(rng), posY(rng));
            for (int attempt = 0; attempt < 100; ++attempt) {
                bool overlaps = false;
                for (const Particle& p : particles) {
                    const float dx = pos.x - p.x;
                    const float dy = pos.y - p.y;
                    if (dx * dx + dy * dy < 4.f * radius * radius) {
                        overlaps = true;
                        break;
                    }
                }
                if (!overlaps) break;
                pos = sf::Vector2f(posX(rng), posY(rng));
            }

            const float a = angle(rng);
            const float v = speed(rng);
            particles.push_back({pos.x, pos.y, v * std::cos(a), v * std::sin(a),
                                 mass, colors[colorIdx++ % 5]});
        }

        initialEnergy += kineticEnergy(particles) - energyBefore;
        currentEnergy = kineticEnergy(particles);
    };

    // Основной цикл приложения
    while (window.isOpen()) {
        while (const std::optional event = window.pollEvent()) {
            if (event->is<sf::Event::Closed>()) {
                window.close();
            } else if (const auto* key = event->getIf<sf::Event::KeyPressed>()) {
                if (key->code == sf::Keyboard::Key::Space)
                    isPaused = !isPaused;
            } else if (const auto* wheel = event->getIf<sf::Event::MouseWheelScrolled>()) {
                if (wheel->wheel == sf::Mouse::Wheel::Vertical)
                    radius = std::clamp(radius + wheel->delta, 2.0f, 50.0f);
            } else if (const auto* mouse = event->getIf<sf::Event::MouseButtonPressed>()) {
                if (mouse->button == sf::Mouse::Button::Left) {
                    const sf::Vector2f clickPos(static_cast<float>(mouse->position.x),
                                                static_cast<float>(mouse->position.y));
                    if (addBallsButton.contains(clickPos)) {
                        addRandomParticles(10);
                    } else {
                        isAiming = true;
                        aimStartPx = clickPos;
                        aimCurrentPx = aimStartPx;
                    }
                }
            } else if (const auto* mouseMove = event->getIf<sf::Event::MouseMoved>()) {
                const sf::Vector2f mousePos(static_cast<float>(mouseMove->position.x),
                                            static_cast<float>(mouseMove->position.y));
                addBallsButton.setFillBoxColor(addBallsButton.contains(mousePos) ? buttonHover : buttonIdle);
                if (isAiming) aimCurrentPx = mousePos;
            } else if (const auto* mouseRelease = event->getIf<sf::Event::MouseButtonReleased>()) {
                if (mouseRelease->button == sf::Mouse::Button::Left && isAiming) {
                    isAiming = false;
                    aimCurrentPx = sf::Vector2f(static_cast<float>(mouseRelease->position.x),
                                                static_cast<float>(mouseRelease->position.y));

                    const sf::Vector2f spawn = clampInside(aimStartPx);
                    const float vx = aimStartPx.x - aimCurrentPx.x;
                    const float vy = aimStartPx.y - aimCurrentPx.y;

                    particles.push_back({spawn.x, spawn.y, vx, vy, mass, colors[colorIdx++ % 5]});
                    initialEnergy += (vx * vx + vy * vy) * mass / 2.f;
                    currentEnergy = kineticEnergy(particles);
                }
            }
        }

        if (!isPaused) {
            runIteration();
        }

        // Рендеринг
        window.clear(sf::Color::Black);
        window.draw(boxBorders, 8, sf::PrimitiveType::Lines);

        circle.setRadius(radius);
        circle.setOrigin({radius, radius});

        for (const auto& p : particles) {
            circle.setFillColor(p.color);
            circle.setPosition({p.x, p.y});
            window.draw(circle);
        }

        // Вектор натяжения и превью создаваемого шара
        if (isAiming) {
            sf::Vertex aimLine[] = {
                {aimStartPx, sf::Color::Yellow},
                {aimCurrentPx, sf::Color::Red}
            };
            window.draw(aimLine, 2, sf::PrimitiveType::Lines);

            circle.setFillColor(sf::Color(255, 255, 255, 128));
            circle.setPosition(clampInside(aimStartPx));
            window.draw(circle);
        }

        // Отрисовка UI
        energyText.setString("Kinetic energy:\n" + formatNumber(currentEnergy, 2) + " J");
        errorText.setString("Error:\n" + formatNumber(currentEnergy - initialEnergy, 2) + " J");
        radiusText.setString("Radius:\n" + formatNumber(radius, 1) + " px");
        ballsText.setString("Balls:\n" + std::to_string(particles.size()));

        window.draw(energyText);
        window.draw(errorText);
        window.draw(radiusText);
        window.draw(ballsText);
        window.draw(iterationsText);
        window.draw(addBallsButton);

        window.display();
        sf::sleep(isPaused ? sf::milliseconds(16) : sf::seconds(dt));
    }

    logFile.flush();
    logFile.close();
    return 0;
}