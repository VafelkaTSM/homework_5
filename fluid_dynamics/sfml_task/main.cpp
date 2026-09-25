// ЛР2: Полет частицы
#include <SFML/Graphics.hpp>
#include <algorithm>
#include <fstream>
#include <iostream>
#include <vector>

struct Particle {
    float x = 1.0f, y = 15.0f;
    float vx = 3.0f, vy = 0.0f;
    sf::Color color = sf::Color::Green;
    sf::VertexArray trail{sf::PrimitiveType::LineStrip};
    bool active = true;
};

int main()
{
    const float dt = 0.01f, g = 9.8f, scale = 20.0f;
    const unsigned winW = 800, winH = 600;

    std::ofstream out("particle.txt");
    if (!out) {
        std::cerr << "Не удалось открыть particle.txt\n";
        return 1;
    }
    out << "t\tx\ty\tvx\tvy\n";

    sf::RenderWindow window(sf::VideoMode({winW, winH}), "LR2: particle flight");
    window.setFramerateLimit(60);

    // Оси координат
    sf::Vertex axes[] = {
        {{0.f, winH - 1.f}, sf::Color::White}, {{(float)winW, winH - 1.f}, sf::Color::White},
        {{1.f, 0.f}, sf::Color::White},        {{1.f, (float)winH}, sf::Color::White}
    };

    float radius = 5.f;
    std::vector<Particle> particles(1);
    const sf::Color colors[] = {sf::Color::Yellow, sf::Color::Cyan, sf::Color::Magenta, sf::Color::Red};
    size_t colorIdx = 0;

    sf::CircleShape circle(radius);
    bool isPaused = false;
    float t = 0.0f;
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
                if (mouse->button == sf::Mouse::Button::Right) {
                    float sx = std::max(0.f, mouse->position.x / scale);
                    float sy = std::max(0.f, (winH - mouse->position.y) / scale);
                    particles.push_back({sx, sy, 3.0f, 0.0f, colors[colorIdx++ % 4]});
                }
            }
        }

        // --- Численный расчет ---
        if (!isPaused) {
            bool hasActive = false;
            for (size_t i = 0; i < particles.size(); ++i) {
                auto& p = particles[i];
                if (!p.active) continue;

                p.x  += p.vx * dt;
                p.y  += p.vy * dt;
                p.vy -= g  * dt;

                if (p.x < 0.f) { p.x = -p.x; p.vx = -p.vx; }
                if (p.y < 0.f) { p.y = -p.y; p.vy = -p.vy; }

                if (i == 0)
                    out << t << '\t' << p.x << '\t' << p.y << '\t' << p.vx << '\t' << p.vy << '\n';

                float px = p.x * scale;
                float py = winH - p.y * scale;

                if (px > winW) {
                    p.active = false;
                } else {
                    p.trail.append({{px, py}, p.color});
                    hasActive = true;
                }
            }

            if (!hasActive) break;
            t += dt;
        }

        // --- Отрисовка ---
        window.clear(sf::Color::Black);
        window.draw(axes, 4, sf::PrimitiveType::Lines);

        circle.setRadius(radius);
        circle.setOrigin({radius, radius});

        for (const auto& p : particles) {
            window.draw(p.trail);
            if (p.active) {
                circle.setFillColor(p.color);
                circle.setPosition({p.x * scale, winH - p.y * scale});
                window.draw(circle);
            }
        }
        window.display();

        sf::sleep(isPaused ? sf::milliseconds(16) : sf::seconds(dt));
    }

    out.close();
    std::cout << "Расчет завершен, результаты в particle.txt\n";
    return 0;
}

