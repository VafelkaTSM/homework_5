// ЛР2: Моделирование движения частиц и упругих столкновений (без гравитации)
#include <SFML/Graphics.hpp>
#include <algorithm>
#include <cmath>
#include <fstream>
#include <iostream>
#include <vector>

// Структура для описания отдельной частицы (шара)
struct Particle {
    float x = 1.0f;                             // Координата X (м)
    float y = 15.0f;                            // Координата Y (м)
    float vx = 0.0f;                            // Скорость по оси X (м/с)
    float vy = 0.0f;                            // Скорость по оси Y (м/с)
    float m = 1.0f;                             // Масса частицы (кг)
    sf::Color color = sf::Color::Green;         // Цвет шара
    sf::VertexArray trail{sf::PrimitiveType::LineStrip}; // Траектория полета
    bool active = true;                         // Флаг активности
};

int main()
{
    // Физические константы и параметры окна
    const float dt = 0.01f;                     // Шаг по времени (с)
    const float scale = 20.0f;                  // Масштаб: количество пикселей в одном метре
    const unsigned winW = 800;                  // Ширина окна в пикселях
    const unsigned winH = 600;                  // Высота окна в пикселях

    // Размеры области моделирования в метрах
    const float boxW = (float)winW / scale;
    const float boxH = (float)winH / scale;

    // Открытие файла для записи параметров первой частицы
    std::ofstream out("particle.txt");
    if (!out) {
        std::cerr << "Не удалось открыть particle.txt\n";
        return 1;
    }
    out << "t\tx\ty\tvx\tvy\n";                 // Заголовок таблицы

    // Создание графического окна SFML
    sf::RenderWindow window(sf::VideoMode({winW, winH}), "LR2: particle flight");
    window.setFramerateLimit(60);

    // Границы замкнутой области (верхняя сдвинута вниз на 1 px: y = 1, левая вправо на 1 px: x = 1)
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
    const sf::Color colors[] = {sf::Color::Green, sf::Color::Yellow, sf::Color::Cyan, sf::Color::Magenta, sf::Color::Red};
    size_t colorIdx = 0;

    // Круг для отрисовки частиц
    sf::CircleShape circle(radius);

    bool isPaused = false;                      // Состояние паузы
    float t = 0.0f;                             // Время моделирования (с)

    // Состояние натягивания вектора скорости для создания шара
    bool isAiming = false;
    sf::Vector2f aimStartPx;
    sf::Vector2f aimCurrentPx;

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
                    isAiming = true;
                    aimStartPx = sf::Vector2f((float)mouse->position.x, (float)mouse->position.y);
                    aimCurrentPx = aimStartPx;
                }
            } else if (const auto* mouseMove = event->getIf<sf::Event::MouseMoved>()) {
                if (isAiming) {
                    aimCurrentPx = sf::Vector2f((float)mouseMove->position.x, (float)mouseMove->position.y);
                }
            } else if (const auto* mouseRelease = event->getIf<sf::Event::MouseButtonReleased>()) {
                if (mouseRelease->button == sf::Mouse::Button::Left && isAiming) {
                    isAiming = false;
                    aimCurrentPx = sf::Vector2f((float)mouseRelease->position.x, (float)mouseRelease->position.y);

                    float rPhys = radius / scale;
                    // Положение точки создания в метрах (ограничиваем пределами экрана)
                    float sx = std::clamp(aimStartPx.x / scale, rPhys, boxW - rPhys);
                    float sy = std::clamp((winH - aimStartPx.y) / scale, rPhys, boxH - rPhys);

                    // Вектор скорости: от точки натягивания к точке запуска (как рогатка)
                    // 1 пиксель натяжения = 1 м/с при делении на scale
                    float vx = (aimStartPx.x - aimCurrentPx.x) / scale;
                    float vy = (aimCurrentPx.y - aimStartPx.y) / scale;

                    particles.push_back({sx, sy, vx, vy, 1.0f, colors[colorIdx++ % 5]});
                }
            }
        }

        // Численный расчет физики
        if (!isPaused) {
            bool hasActive = false;
            float rPhys = radius / scale;       // Радиус шара в метрах

            // 1. Движение частиц и упругое столкновение со стенками с учетом радиуса
            for (size_t i = 0; i < particles.size(); ++i) {
                auto& p = particles[i];
                if (!p.active) continue;

                // Интегрирование скорости и положения (метод Эйлера, без гравитации)
                p.x  += p.vx * dt;
                p.y  += p.vy * dt;

                // Столкновение с левой стенкой (x = rPhys)
                if (p.x < rPhys) {
                    p.x = rPhys;
                    if (p.vx < 0.f) p.vx = -p.vx;
                }
                // Столкновение с правой стенкой (x = boxW - rPhys)
                if (p.x > boxW - rPhys) {
                    p.x = boxW - rPhys;
                    if (p.vx > 0.f) p.vx = -p.vx;
                }
                // Столкновение с нижней стенкой (y = rPhys)
                if (p.y < rPhys) {
                    p.y = rPhys;
                    if (p.vy < 0.f) p.vy = -p.vy;
                }
                // Столкновение с верхней стенкой (y = boxH - rPhys)
                if (p.y > boxH - rPhys) {
                    p.y = boxH - rPhys;
                    if (p.vy > 0.f) p.vy = -p.vy;
                }

                // Запись параметров первой частицы в файл
                if (i == 0)
                    out << t << '\t' << p.x << '\t' << p.y << '\t' << p.vx << '\t' << p.vy << '\n';

                // Добавление точки в след траектории
                float px = p.x * scale;
                float py = winH - p.y * scale;
                p.trail.append({{px, py}, p.color});
                hasActive = true;
            }

            // 2. Упругие соударения шаров между собой по формуле
            for (size_t i = 0; i < particles.size(); ++i) {
                if (!particles[i].active) continue;
                for (size_t j = i + 1; j < particles.size(); ++j) {
                    if (!particles[j].active) continue;

                    auto& p1 = particles[i];
                    auto& p2 = particles[j];

                    // Разность координат: x1 - x2
                    float dx = p1.x - p2.x;
                    float dy = p1.y - p2.y;
                    float distSq = dx * dx + dy * dy;        // ||x1 - x2||^2
                    float minDist = 2.0f * rPhys;             // Минимальное расстояние (2 радиуса)

                    // Условие соприкосновения
                    if (distSq <= minDist * minDist) {
                        float dist = std::sqrt(distSq);       // ||x1 - x2||

                        // Разность скоростей: v1 - v2
                        float dvx = p1.vx - p2.vx;
                        float dvy = p1.vy - p2.vy;

                        // Скалярное произведение: <v1 - v2, x1 - x2>
                        float dot = dvx * dx + dvy * dy;

                        // Проверяем, что частицы двигаются навстречу друг другу
                        if (dot < 0.f) {
                            // Коэффициенты упругого удара
                            float k1 = (2.0f * p2.m) / (p1.m + p2.m) * (dot / distSq);
                            float k2 = (2.0f * p1.m) / (p1.m + p2.m) * (dot / distSq);

                            // Пересчет скоростей по формулам
                            p1.vx -= k1 * dx;
                            p1.vy -= k1 * dy;
                            p2.vx += k2 * dx;
                            p2.vy += k2 * dy;

                            // Разведение пересекшихся шаров
                            float overlap = 0.5f * (minDist - dist);
                            float nx = dx / dist;
                            float ny = dy / dist;
                            p1.x += nx * overlap;
                            p1.y += ny * overlap;
                            p2.x -= nx * overlap;
                            p2.y -= ny * overlap;
                        }
                    }
                }
            }

            if (!particles.empty() && !hasActive) break;
            t += dt;                            // Приращение времени
        }

        // Отрисовка графики
        window.clear(sf::Color::Black);         // Очистка экрана
        window.draw(boxBorders, 8, sf::PrimitiveType::Lines); // Отрисовка рамки границ

        circle.setRadius(radius);               // Установка актуального радиуса
        circle.setOrigin({radius, radius});     // Центр круга в его середине

        // Отрисовка следов и самих шаров
        for (const auto& p : particles) {
            window.draw(p.trail);
            if (p.active) {
                circle.setFillColor(p.color);
                circle.setPosition({p.x * scale, winH - p.y * scale});
                window.draw(circle);
            }
        }

        // Отрисовка индикатора натягивания скорости
        if (isAiming) {
            sf::Vertex aimLine[] = {
                {aimStartPx, sf::Color::Yellow},
                {aimCurrentPx, sf::Color::Red}
            };
            window.draw(aimLine, 2, sf::PrimitiveType::Lines);

            // Полупрозрачный предварительный просмотр шара
            circle.setFillColor(sf::Color(255, 255, 255, 128));
            circle.setPosition(aimStartPx);
            window.draw(circle);
        }

        window.display();                       // Отображение на мониторе

        // Синхронизация по времени
        sf::sleep(isPaused ? sf::milliseconds(16) : sf::seconds(dt));
    }

    out.close();                                // Закрытие файла
    std::cout << "Расчет завершен, результаты в particle.txt\n";
    return 0;
}
