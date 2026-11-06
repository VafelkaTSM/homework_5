using System;
using System.Threading;

namespace SodaTest
{
    /// <summary>
    /// Начальные условия: по координате центра ячейки <paramref name="x"/> задают
    /// плотность, скорость и давление газа.
    /// </summary>
    public delegate void InitialCondition(double x, out double density, out double velocity, out double pressure);

    /// <summary>
    /// Граничные условия: должны заполнить фиктивные ячейки массива (индексы 0 и ^1)
    /// по его внутренним значениям. Длина массива равна N + 2 (N — число ячеек).
    /// Применяются к примитивным переменным (ρ, u, p).
    /// </summary>
    public delegate void BoundaryCondition(double[] values);

    /// <summary>
    /// Универсальный делегат передачи промежуточных результатов наружу (в интерфейс).
    /// Тип <typeparamref name="TReport"/> выбирает вызывающая сторона, поэтому
    /// расчёт никак не зависит от конкретной реализации визуализации.
    /// </summary>
    public delegate void SnapshotHandler<in TReport>(TReport report);

    /// <summary>Параметры расчёта (задаются из интерфейса).</summary>
    public sealed class SolverParameters
    {
        /// <summary>Число расчётных ячеек на отрезке [XMin, XMax].</summary>
        public int Cells { get; set; } = 1500;

        /// <summary>Число шагов по времени.</summary>
        public int Iterations { get; set; } = 1500;

        /// <summary>Заданный шаг по времени δt (может быть уменьшен по условию Куранта).</summary>
        public double TimeStep { get; set; } = 1e-4;

        /// <summary>Число Куранта: δt ≤ C·Δx/max(|u| + c). Значение ≤ 0 отключает ограничение.</summary>
        public double Courant { get; set; } = 0.2;

        /// <summary>Показатель адиабаты γ.</summary>
        public double Gamma { get; set; } = 1.4;

        /// <summary>Левая граница области.</summary>
        public double XMin { get; set; } = 0.0;

        /// <summary>Правая граница области.</summary>
        public double XMax { get; set; } = 1.0;
    }

    /// <summary>
    /// Снимок решения: копии массивов в расчётных ячейках (без фиктивных),
    /// пригодные для отрисовки в другом потоке.
    /// </summary>
    public sealed class SimulationSnapshot
    {
        public int Iteration { get; init; }
        public double Time { get; init; }
        public double TimeStep { get; init; }
        public double Gamma { get; init; }

        public double[] X { get; init; } = Array.Empty<double>();
        public double[] Density { get; init; } = Array.Empty<double>();
        public double[] Velocity { get; init; } = Array.Empty<double>();
        public double[] Pressure { get; init; } = Array.Empty<double>();

        /// <summary>Удельная полная энергия E = p/((γ-1)ρ) + u²/2.</summary>
        public double[] Energy { get; init; } = Array.Empty<double>();
    }

    /// <summary>
    /// Реализация метода крупных частиц (Белоцерковский–Давыдов) для одномерных
    /// уравнений газовой динамики (задача о распаде разрыва, тест Сода).
    /// </summary>
    public static class Calculation
    {
        /// <summary>Минимально допустимое давление (защита от отрицательных значений).</summary>
        private const double MinPressure = 1e-10;

        /// <summary>
        /// Выполняет расчёт и через каждые <paramref name="reportEvery"/> шагов вызывает
        /// <paramref name="onReport"/>, передавая данные, полученные из снимка решения
        /// функцией <paramref name="reportFactory"/>.
        /// </summary>
        /// <typeparam name="TReport">Тип данных, который получает интерфейс.</typeparam>
        public static SimulationSnapshot Run<TReport>(
            SolverParameters parameters,
            InitialCondition initial,
            BoundaryCondition boundary,
            int reportEvery,
            Func<SimulationSnapshot, TReport> reportFactory,
            SnapshotHandler<TReport> onReport,
            CancellationToken cancellationToken = default)
        {
            if (parameters is null) throw new ArgumentNullException(nameof(parameters));
            if (initial is null) throw new ArgumentNullException(nameof(initial));
            if (boundary is null) throw new ArgumentNullException(nameof(boundary));
            if (reportFactory is null) throw new ArgumentNullException(nameof(reportFactory));
            if (onReport is null) throw new ArgumentNullException(nameof(onReport));

            int n = parameters.Cells;
            if (n < 3) throw new ArgumentOutOfRangeException(nameof(parameters), "Требуется не менее 3 ячеек.");
            if (reportEvery < 1) reportEvery = 1;

            double gamma = parameters.Gamma;
            double dx = (parameters.XMax - parameters.XMin) / n;
            int size = n + 2;                       // две фиктивные ячейки: 0 и n+1
            int faces = n + 1;                      // грани между ячейками

            // ---------- Память выделяется один раз (в цикле по времени аллокаций нет) ----------
            var x = new double[size];               // координаты центров ячеек
            var rho = new double[size];             // ρ
            var u = new double[size];               // u
            var p = new double[size];               // p
            var rhoNext = new double[size];         // новый слой
            var uNext = new double[size];
            var pNext = new double[size];
            var m = new double[size];               // ρu на текущем слое
            var e = new double[size];               // ρE на текущем слое
            var rho1 = new double[size];            // 1-й (эйлеров) этап
            var u1 = new double[size];
            var p1 = new double[size];
            var m1 = new double[size];
            var e1 = new double[size];
            var us = new double[size];              // 2-й (лагранжев) этап
            var ps = new double[size];
            var ms = new double[size];
            var es = new double[size];
            var mNext = new double[size];           // ρu и ρE нового слоя
            var eNext = new double[size];

            // ---------- Начальные условия ----------
            for (int i = 1; i <= n; i++)
            {
                x[i] = parameters.XMin + (i - 0.5) * dx;
                initial(x[i], out rho[i], out u[i], out p[i]);
            }
            x[0] = parameters.XMin - 0.5 * dx;
            x[n + 1] = parameters.XMax + 0.5 * dx;
            boundary(rho);
            boundary(u);
            boundary(p);

            double time = 0.0;
            double dt = LimitTimeStep(parameters, gamma, dx, rho, u, p, n);

            // Начальный снимок, чтобы интерфейс сразу показал разрыв ("мембрану").
            onReport(reportFactory(CreateSnapshot(0, time, dt, gamma, x, rho, u, p, n)));

            // Консервативные переменные (ρu и ρE) начального слоя. Далее они
            // поддерживаются пересчётом, поэтому лишний проход по сетке не нужен.
            for (int i = 0; i < size; i++)
            {
                m[i] = rho[i] * u[i];
                e[i] = p[i] / (gamma - 1.0) + 0.5 * rho[i] * u[i] * u[i];
            }

            for (int step = 1; step <= parameters.Iterations; step++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                double lambda = dt / dx;

                // ========== 1-й этап: эйлеров (перенос без учёта давления) ==========
                // Идём по граням: скорость на грани — полусумма скоростей соседних ячеек,
                // значения берутся из донорской (наветренной) ячейки. Разность потоков
                // сразу записывается в ячейку слева от текущей грани, поэтому отдельные
                // массивы потоков не нужны.
                {
                    double prevMass = 0.0, prevMom = 0.0, prevEn = 0.0;
                    for (int f = 0; f < faces; f++)
                    {
                        double uf = 0.5 * (u[f] + u[f + 1]);
                        int d = uf >= 0.0 ? f : f + 1;              // донорская ячейка
                        double mass = uf * rho[d];
                        double mom = uf * m[d];
                        double en = uf * e[d];

                        if (f > 0)                                  // грань f — правая грань ячейки f
                        {
                            rho1[f] = rho[f] - lambda * (mass - prevMass);
                            m1[f] = m[f] - lambda * (mom - prevMom);
                            e1[f] = e[f] - lambda * (en - prevEn);

                            u1[f] = m1[f] / rho1[f];
                            p1[f] = Math.Max(MinPressure, (gamma - 1.0) * (e1[f] - 0.5 * m1[f] * u1[f]));
                        }

                        prevMass = mass;
                        prevMom = mom;
                        prevEn = en;
                    }
                }
                boundary(rho1);
                boundary(u1);
                boundary(p1);

                // ========== 2-й этап: лагранжев (работа сил давления) ==========
                // Ячейка рассматривается как "крупная частица" массы rho1[i]*dx:
                // давление на её границах меняет импульс, а его работа — энергию.
                for (int i = 1; i <= n; i++)
                {
                    double pLeft = 0.5 * (p1[i - 1] + p1[i]);
                    double pRight = 0.5 * (p1[i] + p1[i + 1]);
                    double uLeft = 0.5 * (u1[i - 1] + u1[i]);
                    double uRight = 0.5 * (u1[i] + u1[i + 1]);

                    ms[i] = m1[i] - lambda * (pRight - pLeft);
                    es[i] = e1[i] - lambda * p1[i] * (uRight - uLeft);

                    us[i] = ms[i] / rho1[i];
                    ps[i] = Math.Max(MinPressure, (gamma - 1.0) * (es[i] - 0.5 * ms[i] * us[i]));
                }
                boundary(us);
                boundary(ps);
                // Масса на лагранжевом этапе не меняется; для фиктивных ячеек
                // восстанавливаем ρu и ρE по новым u и p.
                ms[0] = rho1[0] * us[0];
                ms[n + 1] = rho1[n + 1] * us[n + 1];
                es[0] = ps[0] / (gamma - 1.0) + 0.5 * rho1[0] * us[0] * us[0];
                es[n + 1] = ps[n + 1] / (gamma - 1.0) + 0.5 * rho1[n + 1] * us[n + 1] * us[n + 1];

                // ===== 3-й этап: заключительный, эйлеров (пересчёт на неподвижную сетку) =====
                // Потоки строятся по параметрам после лагранжева этапа и применяются
                // к величинам n-го слоя, что делает схему полностью консервативной.
                // ===== 3-й этап: заключительный, эйлеров (пересчёт на неподвижную сетку) =====
                // Потоки строятся по параметрам после лагранжева этапа и применяются
                // к величинам n-го слоя, что делает схему полностью консервативной.
                // Заодно запоминаем консервативные величины ρu, ρE нового слоя и
                // оцениваем максимальную скорость возмущений для следующего шага.
                double maxWaveSpeed = 0.0;
                {
                    double prevMass = 0.0, prevMom = 0.0, prevEn = 0.0;
                    for (int f = 0; f < faces; f++)
                    {
                        double uf = 0.5 * (us[f] + us[f + 1]);
                        double pf = 0.5 * (ps[f] + ps[f + 1]);
                        int d = uf >= 0.0 ? f : f + 1;
                        double mass = uf * rho1[d];
                        double mom = uf * ms[d] + pf;
                        double en = uf * (es[d] + ps[d]);

                        if (f > 0)
                        {
                            double density = rho[f] - lambda * (mass - prevMass);
                            double momentum = m[f] - lambda * (mom - prevMom);
                            double energy = e[f] - lambda * (en - prevEn);

                            double velocity = momentum / density;
                            double pressure = Math.Max(MinPressure, (gamma - 1.0) * (energy - 0.5 * momentum * velocity));

                            rhoNext[f] = density;
                            uNext[f] = velocity;
                            pNext[f] = pressure;
                            mNext[f] = momentum;
                            eNext[f] = energy;

                            maxWaveSpeed = Math.Max(maxWaveSpeed,
                                Math.Abs(velocity) + Math.Sqrt(gamma * pressure / density));
                        }

                        prevMass = mass;
                        prevMom = mom;
                        prevEn = en;
                    }
                }
                boundary(rhoNext);
                boundary(uNext);
                boundary(pNext);

                // Переход на новый слой — обмен ссылками, без копирования массивов.
                (rho, rhoNext) = (rhoNext, rho);
                (u, uNext) = (uNext, u);
                (p, pNext) = (pNext, p);
                (m, mNext) = (mNext, m);
                (e, eNext) = (eNext, e);

                // Фиктивные ячейки консервативных величин согласуем с ρ, u, p.
                for (int i = 0; i < size; i += n + 1)
                {
                    m[i] = rho[i] * u[i];
                    e[i] = p[i] / (gamma - 1.0) + 0.5 * rho[i] * u[i] * u[i];
                }

                time += dt;

                // Шаг на следующую итерацию — по состоянию, только что полученному.
                if (parameters.Courant > 0.0 && maxWaveSpeed > 0.0)
                    dt = Math.Min(parameters.TimeStep, parameters.Courant * dx / maxWaveSpeed);

                if (step % reportEvery == 0 || step == parameters.Iterations)
                    onReport(reportFactory(CreateSnapshot(step, time, dt, gamma, x, rho, u, p, n)));
            }

            return CreateSnapshot(parameters.Iterations, time, dt, gamma, x, rho, u, p, n);
        }

        /// <summary>Максимальная скорость распространения возмущений max(|u| + c).</summary>
        private static double MaxWaveSpeed(double[] rho, double[] u, double[] p, int n, double gamma)
        {
            double max = 0.0;
            for (int i = 1; i <= n; i++)
                max = Math.Max(max, Math.Abs(u[i]) + Math.Sqrt(gamma * p[i] / rho[i]));
            return max;
        }

        /// <summary>Шаг по времени: не больше заданного δt и не больше C·dx/max(|u| + c).</summary>
        private static double LimitTimeStep(
            SolverParameters parameters, double gamma, double dx,
            double[] rho, double[] u, double[] p, int n)
        {
            double dt = parameters.TimeStep;
            if (parameters.Courant > 0.0)
            {
                double maxSpeed = MaxWaveSpeed(rho, u, p, n, gamma);
                if (maxSpeed > 0.0) dt = Math.Min(dt, parameters.Courant * dx / maxSpeed);
            }
            return dt;
        }

        /// <summary>Создаёт снимок решения: копирует расчётные ячейки 1..n.</summary>
        private static SimulationSnapshot CreateSnapshot(
            int iteration, double time, double dt, double gamma,
            double[] x, double[] rho, double[] u, double[] p, int n)
        {
            var xc = new double[n];
            var density = new double[n];
            var velocity = new double[n];
            var pressure = new double[n];
            var energy = new double[n];

            for (int i = 0; i < n; i++)
            {
                int k = i + 1;
                xc[i] = x[k];
                density[i] = rho[k];
                velocity[i] = u[k];
                pressure[i] = p[k];
                energy[i] = p[k] / ((gamma - 1.0) * rho[k]) + 0.5 * u[k] * u[k];
            }

            return new SimulationSnapshot
            {
                Iteration = iteration,
                Time = time,
                TimeStep = dt,
                Gamma = gamma,
                X = xc,
                Density = density,
                Velocity = velocity,
                Pressure = pressure,
                Energy = energy
            };
        }
    }
}

