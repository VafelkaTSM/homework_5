#include <iostream>
#include <fstream>
#include <cmath>
#include <iomanip>

int main() {
    // Параметры системы
    const double A0 = 0.2;        // Начальная амплитуда, м
    const double sigma = 0.01;    // Коэффициент затухания, с^-1
    
    // Собственная частота незатухающих колебаний
    const double omega0 = 0.1;
    
    // Частота затухающих колебаний w = sqrt(w0^2 - sigma^2)
    const double omega = std::sqrt(omega0 * omega0 - sigma * sigma);
    
    // Начальная фаза phi
    const double phi = 0.0;

    // Временные параметры
    const double t_max = 1000.0;   // Общее время моделирования, с
    const double dt = 1;        // Шаг по времени, с

    // Открываем CSV файл для записи результатов
    std::ofstream file("pendulum_results.csv");
    if (!file.is_open()) {
        std::cerr << "Ошибка при открытии файла!" << std::endl;
        return 1;
    }

    // Заголовки столбцов CSV
    file << "time_s,undamped_m_x,undamped_m_y,damped_m_x,damped_m_y\n";

    // Расчет координат от времени
    for (double t = 0.0; t <= t_max + 1e-9; t += dt) {
        // 1. Незатухающие колебания (sigma = 0)
        double s_undamped_x = A0 * std::sin(omega0 * t + phi);
        double s_undamped_y = A0 * std::cos(omega0 * t + phi);
        
        // 2. Затухающие колебания: s = A0 * e^(-sigma * t) * sin(w * t + phi)
        double s_damped_x = A0 * std::exp(-sigma * t) * std::sin(omega * t + phi);
        double s_damped_y = A0 * std::exp(-sigma * t) * std::cos(omega * t + phi);

        // Сохранение результатов в CSV (разделение запятыми)
        file << t << "," 
             << s_undamped_x << ","
             << s_undamped_y << ","
             << s_damped_x << ","
             << s_damped_y << "\n";
    }

    file.close();
    std::cout << "Расчет успешно завершен! Результаты сохранены в 'pendulum_results.csv'." << std::endl;

    return 0;
}