// ЛР2: Полет частицы
// Частица в первой четверти (x >= 0, y >= 0), гравитация g = 9.8 м/с^2,
// абсолютно упругие удары о стенки. Шаг по времени dt = 0.01 c.
// Расчетные параметры на каждом шаге выводятся в файл particle.txt,
// визуализация выполняется средствами SFML.

#include <SFML/Graphics.hpp>
#include <cmath>
#include <fstream>
#include <iostream>

int main()
{
    // --- Параметры задачи ---
    const float dt = 0.01f;   // шаг по времени, с
    const float g  = 9.8f;    // ускорение свободного падения, м/с^2
    const float tEnd = 15.0f; // время окончания счета, с

    // Начальные условия (заданы самостоятельно)
    float x = 1.0f,  y = 15.0f; // положение, м
    float vx = 3.0f, vy = 0.0f; // скорость, м/с

    // --- Вывод результатов в файл ---
    std::ofstream out("particle.txt");
    if (!out) {
        std::cerr << "Не удалось открыть файл particle.txt\n";
        return 1;
    }
    out << "t\tx\ty\tvx\tvy\n";

    // --- Визуализация (SFML) ---
    const unsigned winW = 800, winH = 600;   // размер окна, пиксели
    const float scale = 20.0f;               // пикселей на метр

    sf::RenderWindow window(sf::VideoMode({winW, winH}), "LR2: particle flight");
    window.setFramerateLimit(60);

    // Оси координат: OX внизу, OY слева
    sf::Vertex axisX[] = {
        sf::Vertex{sf::Vector2f(0.f, winH - 1.f), sf::Color::White},
        sf::Vertex{sf::Vector2f((float)winW, winH - 1.f), sf::Color::White}
    };
    sf::Vertex axisY[] = {
        sf::Vertex{sf::Vector2f(1.f, 0.f), sf::Color::White},
        sf::Vertex{sf::Vector2f(1.f, (float)winH), sf::Color::White}
    };

    // Частица
    const float radius = 5.f;
    sf::CircleShape particle(radius);
    particle.setFillColor(sf::Color::Green);
    particle.setOrigin({radius, radius});

    // След траектории
    sf::VertexArray trail(sf::PrimitiveType::LineStrip);

    float t = 0.0f;
    while (window.isOpen() && t <= tEnd) {
        while (const std::optional event = window.pollEvent()) {
            if (event->is<sf::Event::Closed>())
                window.close();
        }

        // --- Численный расчет на временном шаге ---
        x  += vx * dt;
        y  += vy * dt;
        vy -= g  * dt;   // пересчет вертикальной скорости

        // Абсолютно упругие удары о стенки
        if (x < 0.f) { x = -x; vx = -vx; }
        if (y < 0.f) { y = -y; vy = -vy; }

        out << t << '\t' << x << '\t' << y << '\t' << vx << '\t' << vy << '\n';

        // --- Отрисовка ---
        // Перевод метров в пиксели (ось Y окна направлена вниз)
        float px = x * scale;
        float py = winH - y * scale;
        if (px > winW) { // если частица улетела за правый край — остановка счета
            std::cout << "Частица покинула область наблюдения\n";
            break;
        }

        particle.setPosition({px, py});
        trail.append(sf::Vertex{sf::Vector2f(px, py), sf::Color::Green});

        window.clear(sf::Color::Black);
        window.draw(axisX, 2, sf::PrimitiveType::Lines);
        window.draw(axisY, 2, sf::PrimitiveType::Lines);
        window.draw(trail);
        window.draw(particle);
        window.display();

        t += dt;
        sf::sleep(sf::seconds(dt)); // реальное время ~ времени расчета
    }

    out.close();
    std::cout << "Расчет завершен, результаты в particle.txt\n";
    return 0;
}

