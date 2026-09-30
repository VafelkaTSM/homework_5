// ЛР2: Моделирование движения частиц и упругих столкновений (без гравитации)
// Система координат совпадает с экранной (SFML): ось Y направлена вниз,
// y = 0 — верхняя граница окна, поэтому координата y используется напрямую
// и для расчётов, и для отрисовки (без инвертирования и смещения).
//
// Управление:
//   ЛКМ + протяжка мышью — создать шар (натяжение задаёт вектор скорости);
//   колесо мыши           — радиус создаваемых шаров;
//   Пробел                — пауза/продолжение;
//   кнопка «+10 random…»  — добавить 10 шаров в случайных положениях
//                           со случайными по направлению и модулю скоростями.
// Блок «Iterations» — не кнопка, а индикатор: он не добавляет итерации,
// а раз в 100 итераций обновляет свои показания (см. runIteration).
//
// Каждая выполненная итерация моделирования пишется в файл simulation_log.csv
// в формате: итерация, количество шаров, кинетическая энергия, ошибка.
#include <SFML/Graphics.hpp>
#include <algorithm>
#include <cmath>
#include <fstream>
#include <iostream>
#include <optional>
#include <random>
#include <string>
#include <vector>

// Структура для описания отдельной частицы (шара)
struct Particle {
    float x;                             // Координата X (м), отсчёт слева
    float y;                            // Координата Y (м), отсчёт сверху вниз
    float vx;                            // Скорость по оси X (м/с)
    float vy;                            // Скорость по оси Y (м/с), вниз — положительная
    float m;                             // Масса частицы (кг)
    sf::Color color = sf::Color::Green;         // Цвет шара
};

// Один шаг численного интегрирования: движение частиц, упругое отражение от стенок
// и упругие соударения шаров между собой.
// border — отступ стенки от края окна (1 px, совпадает с нарисованной рамкой).
void stepParticles(std::vector<Particle>& particles, float dt, float radius,
                   float boxW, float boxH, float border)
{
    const float rPhys = radius;                 // Радиус шара в метрах (1 px = 1 м)
    const float minX = border + rPhys;          // Пределы для центра шара
    const float maxX = boxW - border - rPhys;
    const float minY = border + rPhys;
    const float maxY = boxH - border - rPhys;

    // Упругое отражение от стенок: положение возвращается в область моделирования,
    // модуль скорости не меняется (энергия сохраняется).
    auto resolveWalls = [&](Particle& p) {
        if (p.x < minX) { p.x = minX; if (p.vx < 0.f) p.vx = -p.vx; }
        if (p.x > maxX) { p.x = maxX; if (p.vx > 0.f) p.vx = -p.vx; }
        if (p.y < minY) { p.y = minY; if (p.vy < 0.f) p.vy = -p.vy; }
        if (p.y > maxY) { p.y = maxY; if (p.vy > 0.f) p.vy = -p.vy; }
    };

    // 1. Движение частиц (метод Эйлера, без гравитации) и столкновение со стенками
    for (auto& p : particles) {
        p.x += p.vx * dt;
        p.y += p.vy * dt;
        resolveWalls(p);
    }

    // 2. Упругие соударения шаров между собой по формуле
    const float minDist = 2.f * rPhys;           // Минимальное расстояние (2 радиуса)
    for (size_t i = 0; i < particles.size(); ++i) {
        for (size_t j = i + 1; j < particles.size(); ++j) {
            Particle& p1 = particles[i];
            Particle& p2 = particles[j];

            // Разность координат: x1 - x2
            const float dx = p1.x - p2.x;
            const float dy = p1.y - p2.y;
            const float distSq = dx * dx + dy * dy;   // ||x1 - x2||^2

            // Совпавшие центры: нормаль не определена, удар пропускаем,
            // иначе получим деление на ноль и NaN в скоростях
            if (distSq <= 0.f || distSq > minDist * minDist)
                continue;

            const float dist = std::sqrt(distSq);     // ||x1 - x2||

            // Разность скоростей: v1 - v2
            const float dvx = p1.vx - p2.vx;
            const float dvy = p1.vy - p2.vy;

            // Скалярное произведение: <v1 - v2, x1 - x2>
            const float dot = dvx * dx + dvy * dy;

            // Удар учитываем только если частицы двигаются навстречу друг другу
            if (dot >= 0.f)
                continue;

            // Коэффициенты упругого удара
            const float k1 = (2.0f * p2.m) / (p1.m + p2.m) * (dot / distSq);
            const float k2 = (2.0f * p1.m) / (p1.m + p2.m) * (dot / distSq);

            // Пересчет скоростей по формулам
            p1.vx -= k1 * dx;
            p1.vy -= k1 * dy;
            p2.vx += k2 * dx;
            p2.vy += k2 * dy;

            // Разведение пересекшихся шаров (массы одинаковы — смещение пополам)
            const float overlap = 0.5f * (minDist - dist);
            const float nx = dx / dist;
            const float ny = dy / dist;
            p1.x += nx * overlap;
            p1.y += ny * overlap;
            p2.x -= nx * overlap;
            p2.y -= ny * overlap;

            // Разведение не должно вытолкнуть шары за стенки
            resolveWalls(p1);
            resolveWalls(p2);
        }
    }
}

// Полная кинетическая энергия системы частиц (Дж)
float kineticEnergy(const std::vector<Particle>& particles)
{
    float energy = 0.f;
    for (const Particle& p : particles)
        energy += (p.vx * p.vx + p.vy * p.vy) * p.m / 2.f;
    return energy;
}

class TextBox: public sf::Drawable {
    // ВАЖНО: шрифт объявлен до sf::Text и живёт столько же, сколько сам TextBox.
    // sf::Text хранит только указатель на sf::Font, поэтому ссылаться он должен
    // на собственное поле font, а не на параметр конструктора.
    sf::Font font;
    sf::Vector2f size;
    sf::Vector2f position;
    sf::RectangleShape box;
    sf::Text text;

    public:
        // Шрифт принимается по ссылке и копируется внутрь TextBox: копия параметра
        // (как в варианте "TextBox(sf::Font f)") умирала бы вместе с конструктором,
        // и text ссылался бы на уже не существующий объект
        TextBox(const sf::Font& f): font(f), text(font, "", 14) {
            box.setOutlineThickness(1.f);
        }

        // Копирование запрещено: text в копии ссылался бы на шрифт оригинала
        TextBox(const TextBox&) = delete;
        TextBox& operator=(const TextBox&) = delete;
            
        void setPosition(sf::Vector2f p) {
            position = p;
            box.setPosition(position);
            
            const sf::FloatRect textBounds = text.getLocalBounds();
            text.setOrigin({
                textBounds.position.x + textBounds.size.x / 2.f,
                textBounds.position.y + textBounds.size.y / 2.f
            });
            text.setPosition({
                position.x + size.x / 2.f,
                position.y + size.y / 2.f
            });
        }

        void setSize(sf::Vector2f s) {
            size = s;
            box.setSize(size);
            setPosition(position);
        }

        void setOutlineBoxThickness(float s) {
            box.setOutlineThickness(s);
        }

        void setString(const std::string& s) {
            text.setString(s);
            setPosition(position);      // пересчет origin: строка изменила размеры
        }

        void setCharacterSize(unsigned s) {
            text.setCharacterSize(s);
            setPosition(position);      // пересчет origin: изменилась высота строки
        }

        void setTextColor(sf::Color c) {
            text.setFillColor(c);
            text.setOutlineColor(c);
        }

        void setFillBoxColor(sf::Color c) {
            box.setFillColor(c);
        }

        void setOutlineBoxColor(sf::Color c) {
            box.setOutlineColor(c);
        }

        // Попала ли точка (в координатах окна) внутрь прямоугольника блока.
        // Нужна, чтобы использовать TextBox как кнопку: по этой проверке
        // нажатие ЛКМ превращается в действие кнопки, а не в создание шара.
        bool contains(sf::Vector2f p) const {
            return p.x >= position.x && p.x <= position.x + size.x &&
                   p.y >= position.y && p.y <= position.y + size.y;
        }


        void draw(sf::RenderTarget& target, sf::RenderStates states) const {
            target.draw(box, states);
            target.draw(text, states);
        };

        ~TextBox() override {};
};

int main()
{
    // Физические константы и параметры окна
    const float dt = 0.01f;                     // Шаг по времени (с)
    const unsigned winW = 800;                  // Ширина окна в пикселях
    const unsigned winH = 600;                  // Высота окна в пикселях

    // Размеры области моделирования в метрах
    const float boxW = (float)winW;
    const float boxH = (float)winH;

    // Отступ стенки от края окна: совпадает с положением нарисованной рамки
    const float border = 1.f;

    // Создание графического окна SFML
    sf::RenderWindow window(sf::VideoMode({winW, winH}), "LR2: particle flight");
    // Ограничение частоты кадров не задаём: темп задаёт sf::sleep(dt) ниже,
    // иначе симуляция шла бы медленнее реального времени (60 к/с вместо 100 к/с)

    // Границы замкнутой области (сдвинуты внутрь окна на border = 1 px)
    sf::Vertex boxBorders[] = {
        // Нижняя граница (y = winH - 1)
        {{0.f, winH - 1.f}, sf::Color::White}, {{(float)winW, winH - 1.f}, sf::Color::White},
        // Верхняя граница (y = 1)
        {{0.f, 1.f}, sf::Color::White},        {{(float)winW, 1.f}, sf::Color::White},
        // Левая граница (x = 1)
        {{1.f, 0.f}, sf::Color::White},        {{1.f, (float)winH}, sf::Color::White},
        // Правая граница (x = winW - 1)
        {{winW - 1.f, 0.f}, sf::Color::White}, {{winW - 1.f, (float)winH}, sf::Color::White}
    };

    // Радиус шара в пикселях
    float radius = 5.f;

    // Вектор частиц
    std::vector<Particle> particles;

    // Цвета для добавляемых частиц
    const sf::Color colors[] = {
        sf::Color::Green, 
        sf::Color::Yellow, 
        sf::Color::Cyan,                 
        sf::Color::Magenta, 
        sf::Color::Red
    };
    size_t colorIdx = 0;

    // Круг для отрисовки частиц
    sf::CircleShape circle(radius);

    bool isPaused = false;                      // Состояние паузы
    const float mass = 1.0f;                    // Масса добавляемых частиц (кг)

    // Положение центра нового шара, ограниченное рамкой (учитывает текущий радиус)
    const auto clampInside = [&](const sf::Vector2f& p) {
        return sf::Vector2f(std::clamp(p.x, border + radius, boxW - border - radius),
                            std::clamp(p.y, border + radius, boxH - border - radius));
    };

    // Состояние натягивания вектора скорости для создания шара
    bool isAiming = false;
    sf::Vector2f aimStartPx;
    sf::Vector2f aimCurrentPx;

    float currentEnergy = 0;
    float initialEnergy = 0;
    sf::Font font;
    if (!font.openFromFile("./DejaVuSans.ttf")) {
        return -1;
    }

    TextBox energyText(font);
    energyText.setSize({140.f, 36.f});          // размер под две строки текста
    energyText.setPosition({6.f, 6.f});
    energyText.setOutlineBoxThickness(-1.f);    // рамка внутрь, чтобы не менять габарит блока
    energyText.setCharacterSize(11);
    energyText.setFillBoxColor({73, 79, 82});
    energyText.setOutlineBoxColor(sf::Color::White);
    energyText.setTextColor(sf::Color::White);

    TextBox errorText(font);
    errorText.setSize({140.f, 36.f});
    errorText.setPosition({151.f, 6.f});        // справа от предыдущего блока
    errorText.setOutlineBoxThickness(-1.f);
    errorText.setCharacterSize(11);
    errorText.setFillBoxColor({73, 79, 82});
    errorText.setOutlineBoxColor(sf::Color::White);
    errorText.setTextColor(sf::Color::White);

    // --- Элементы управления ---
    const sf::Color buttonIdle(73, 79, 82);     // цвет блока/кнопки без наведения курсора
    const sf::Color buttonHover(34, 102, 54);   // цвет кнопки под курсором

    // Номер последней выполненной итерации моделирования (шага по времени).
    // Раз в 100 итераций это значение выводится в блоке iterationsText
    unsigned long long iteration = 0;

    // Блок с количеством итераций: не кнопка, нажатия по нему не обрабатываются.
    // Свои показания он обновляет раз в 100 итераций внутри runIteration()
    TextBox iterationsText(font);
    iterationsText.setSize({140.f, 36.f});    // размер под две строки текста
    iterationsText.setPosition({296.f, 6.f}); // справа от блока с ошибкой
    iterationsText.setOutlineBoxThickness(-1.f);
    iterationsText.setCharacterSize(11);
    iterationsText.setFillBoxColor(buttonIdle);
    iterationsText.setOutlineBoxColor(sf::Color::White);
    iterationsText.setTextColor(sf::Color::White);
    iterationsText.setString("Iterations:\n0");  // начальные показания (до 100-й итерации)

    // Кнопка добавления 10 случайных шаров
    TextBox addBallsButton(font);
    addBallsButton.setSize({140.f, 36.f});
    addBallsButton.setPosition({441.f, 6.f});   // справа от блока итераций
    addBallsButton.setOutlineBoxThickness(-1.f);
    addBallsButton.setCharacterSize(11);
    addBallsButton.setString("+10 random balls");
    addBallsButton.setFillBoxColor(buttonIdle);
    addBallsButton.setOutlineBoxColor(sf::Color::White);
    addBallsButton.setTextColor(sf::Color::White);

    // Журнал эксперимента: итерация, количество шаров, кинетическая энергия, ошибка
    std::ofstream logFile("simulation_log.csv");
    if (!logFile)
        std::cerr << "Не удалось открыть simulation_log.csv для записи\n";
    logFile << "iteration,balls,kinetic_energy,error\n";

    // Одна итерация моделирования: шаг физики, пересчёт энергии и запись строки в журнал
    const auto runIteration = [&]() {
        // Движение, отражение от стенок и упругие соударения шаров
        stepParticles(particles, dt, radius, boxW, boxH, border);
        ++iteration;
        // Полная кинетическая энергия: при упругих ударах она сохраняется,
        // поэтому её отличие от начальной энергии — ошибка метода
        currentEnergy = kineticEnergy(particles);
        logFile << iteration << ',' << particles.size() << ','
                << currentEnergy << ',' << (currentEnergy - initialEnergy) << '\n';
        if (iteration % 100 == 0) {
            // Показания блока итераций обновляются не каждый кадр, а раз в 100 итераций
            iterationsText.setString("Iterations:\n" + std::to_string(iteration));
            logFile.flush();                    // строки не теряются при аварийном выходе
        }
    };

    // Генератор случайных чисел для кнопки добавления шаров
    std::mt19937 rng(std::random_device{}());
    const float minSpeed = 25.f;                // Минимальный модуль скорости (м/с)
    const float maxSpeed = 600.f;               // Максимальный модуль скорости (м/с)
    const int maxSpawnTries = 100;              // Попытки найти свободное место под шар

    // Добавление count шаров: случайное положение внутри рамки и случайная скорость
    const auto addRandomParticles = [&](int count) {
        std::uniform_real_distribution<float> posX(border + radius, boxW - border - radius);
        std::uniform_real_distribution<float> posY(border + radius, boxH - border - radius);
        std::uniform_real_distribution<float> angle(0.f, 2.f * (float)std::acos(-1.0));
        std::uniform_real_distribution<float> speed(minSpeed, maxSpeed);

        const float energyBefore = kineticEnergy(particles);
        for (int i = 0; i < count; ++i) {
            // Случайное положение: не пересекается с уже существующими шарами,
            // иначе шары появятся «внутри» друг друга и получат лишний импульс
            sf::Vector2f pos(posX(rng), posY(rng));
            for (int attempt = 0; attempt < maxSpawnTries; ++attempt) {
                bool overlaps = false;
                for (const Particle& p : particles) {
                    const float dx = pos.x - p.x;
                    const float dy = pos.y - p.y;
                    if (dx * dx + dy * dy < 4.f * radius * radius) {
                        overlaps = true;
                        break;
                    }
                }
                if (!overlaps)
                    break;
                pos = sf::Vector2f(posX(rng), posY(rng));
            }

            // Случайное направление и случайный модуль скорости
            const float a = angle(rng);
            const float v = speed(rng);
            particles.push_back({pos.x, pos.y, v * std::cos(a), v * std::sin(a),
                                 mass, colors[colorIdx++ % 5]});
        }
        // Энергия добавленных шаров входит в начальную: иначе «ошибка»
        // изменилась бы скачком из-за самих шаров, а не из-за погрешности метода
        initialEnergy += kineticEnergy(particles) - energyBefore;
        currentEnergy = kineticEnergy(particles);   // энергия видна и в паузе
    };

    // Главный цикл симуляции и визуализации
    while (window.isOpen()) {
        // Обработка событий SFML
        while (const std::optional event = window.pollEvent()) {
            if (event->is<sf::Event::Closed>()) {
                window.close();                 // Закрытие окна по крестику
            } else if (const auto* key = event->getIf<sf::Event::KeyPressed>()) {
                if (key->code == sf::Keyboard::Key::Space)
                    isPaused = !isPaused;       // Пауза по нажатию пробела
            } else if (const auto* wheel = event->getIf<sf::Event::MouseWheelScrolled>()) {
                if (wheel->wheel == sf::Mouse::Wheel::Vertical)
                    radius = std::clamp(radius + wheel->delta, 2.0f, 50.0f); // Изменение радиуса
            } else if (const auto* mouse = event->getIf<sf::Event::MouseButtonPressed>()) {
                if (mouse->button == sf::Mouse::Button::Left) {
                    const sf::Vector2f clickPos((float)mouse->position.x, (float)mouse->position.y);
                    if (addBallsButton.contains(clickPos)) {
                        // Кнопка добавления 10 шаров со случайными параметрами
                        addRandomParticles(10);
                    } else {
                        // Нажатие вне кнопки — начало натягивания вектора скорости
                        // (блок iterationsText — индикатор, его нажатие создаёт шар)
                        isAiming = true;
                        aimStartPx = clickPos;
                        aimCurrentPx = aimStartPx;
                    }
                }
            } else if (const auto* mouseMove = event->getIf<sf::Event::MouseMoved>()) {
                const sf::Vector2f mousePos((float)mouseMove->position.x, (float)mouseMove->position.y);
                // Подсветка кнопки под курсором (блок итераций — индикатор, не подсвечивается)
                addBallsButton.setFillBoxColor(addBallsButton.contains(mousePos) ? buttonHover : buttonIdle);
                if (isAiming) {
                    aimCurrentPx = mousePos;
                }
            } else if (const auto* mouseRelease = event->getIf<sf::Event::MouseButtonReleased>()) {
                if (mouseRelease->button == sf::Mouse::Button::Left && isAiming) {
                    isAiming = false;
                    aimCurrentPx = sf::Vector2f((float)mouseRelease->position.x, (float)mouseRelease->position.y);

                    // Положение точки создания (ограничиваем пределами рамки);
                    // координата y мыши совпадает с координатой y в области моделирования
                    const sf::Vector2f spawn = clampInside(aimStartPx);

                    // Вектор скорости: от точки натягивания к точке запуска (как рогатка)
                    // 1 пиксель натяжения = 1 м/с (1 px = 1 м)
                    const float vx = (aimStartPx.x - aimCurrentPx.x);
                    const float vy = (aimStartPx.y - aimCurrentPx.y);

                    particles.push_back({spawn.x, spawn.y, vx, vy, mass, colors[colorIdx++ % 5]});
                    initialEnergy += (vx * vx + vy * vy) * mass / 2.f;
                    currentEnergy = kineticEnergy(particles);   // энергия видна и в паузе
                }
            }
        }

        // Численный расчет физики.
        // Без паузы за кадр выполняется одна итерация (шаг dt); в паузе
        // итерации идут только по нажатию кнопки «Iterations» (+10 за клик)
        if (!isPaused) {
            runIteration();
        }

        // Отрисовка графики
        window.clear(sf::Color::Black);         // Очистка экрана
        window.draw(boxBorders, 8, sf::PrimitiveType::Lines); // Отрисовка рамки границ

        circle.setRadius(radius);               // Установка актуального радиуса
        circle.setOrigin({radius, radius});     // Центр круга в его середине

        // Отрисовка шаров
        for (const auto& p : particles) {
                circle.setFillColor(p.color);
                // Координаты моделирования совпадают с экранными, y используется напрямую
                circle.setPosition({p.x, p.y});
                window.draw(circle);
        }

        // Отрисовка индикатора натягивания скорости
        if (isAiming) {
            sf::Vertex aimLine[] = {
                {aimStartPx, sf::Color::Yellow},
                {aimCurrentPx, sf::Color::Red}
            };
            window.draw(aimLine, 2, sf::PrimitiveType::Lines);

            // Полупрозрачный предварительный просмотр шара в точке, где он появится
            circle.setFillColor(sf::Color(255, 255, 255, 128));
            circle.setPosition(clampInside(aimStartPx));
            window.draw(circle);
        }

        energyText.setString("Kinetic energy:\n" + std::to_string(currentEnergy) + " J");
        window.draw(energyText);
        errorText.setString("Error:\n" + std::to_string(currentEnergy - initialEnergy) + " J");
        window.draw(errorText);

        // Блок итераций (строка обновляется в runIteration раз в 100 итераций)
        // и кнопка добавления 10 случайных шаров
        window.draw(iterationsText);
        window.draw(addBallsButton);

        window.display();                       // Отображение на мониторе

        // Синхронизация по времени
        sf::sleep(isPaused ? sf::milliseconds(16) : sf::seconds(dt));
    }

    logFile.flush();                            // сброс всех строк журнала на диск
    logFile.close();
    return 0;
}
