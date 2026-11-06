using System;
using System.Diagnostics;
using System.Globalization;

namespace SodaTest
{
    internal static class Program
    {
        // ---------- Параметры задачи Сода (тест на распад разрыва) ----------
        private const double Membrane = 0.5;        // x_m — положение мембраны
        private const double DensityLeft = 1.0;     // ρ_L
        private const double PressureLeft = 1.0;    // p_L
        private const double DensityRight = 0.125;  // ρ_R
        private const double PressureRight = 0.1;   // p_R

        /// <summary>
        /// Точка входа. Без аргументов запускается графический интерфейс,
        /// с аргументом --test — только расчёт (для проверки без окна).
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && (args[0] == "--test" || args[0] == "-t"))
            {
                var parameters = new SolverParameters();
                if (args.Length > 1 && int.TryParse(args[1], out int iterations) && iterations > 0)
                    parameters.Iterations = iterations;
                if (args.Length > 2 && int.TryParse(args[2], out int cells) && cells >= 3)
                    parameters.Cells = cells;

                RunSelfTest(parameters);
                return;
            }

            Gui.Start();
        }

        /// <summary>
        /// Начальные условия задачи Сода: при x &lt; x_m — покоящийся газ высокого давления,
        /// при x &gt; x_m — разреженный газ. Скорость всюду равна нулю.
        /// </summary>
        public static void SodInitialConditions(
            double x, out double density, out double velocity, out double pressure)
        {
            if (x < Membrane)
            {
                density = DensityLeft;
                pressure = PressureLeft;
            }
            else
            {
                density = DensityRight;
                pressure = PressureRight;
            }
            velocity = 0.0;
        }

        /// <summary>
        /// Граничные условия: нулевой градиент (значение "затягивается" из соседней
        /// внутренней ячейки), т.е. граница свободно пропускает возмущения.
        /// </summary>
        public static void ZeroGradientBoundary(double[] values)
        {
            values[0] = values[1];
            values[^1] = values[^2];
        }

        /// <summary>
        /// Проверка расчёта без графического интерфейса: сравнение с известными
        /// значениями точного решения задачи Сода (γ = 1.4).
        /// </summary>
        private static void RunSelfTest(SolverParameters parameters)
        {
            var stopwatch = Stopwatch.StartNew();
            var snapshot = Calculation.Run(
                parameters,
                SodInitialConditions,
                ZeroGradientBoundary,
                parameters.Iterations,
                s => s,
                _ => { });
            stopwatch.Stop();

            double t = snapshot.Time;
            double dx = (parameters.XMax - parameters.XMin) / parameters.Cells;

            // Точное решение задачи Сода в области x = 0.5 + const * t.
            // Скорость и давление в обеих областях между контактным разрывом и скачком:
            double exactU = 0.92745, exactP = 0.30313;
            double exactRhoLow = 0.42632;   // между веером разрежения и контактным разрывом
            double exactRhoHigh = 0.26557;  // между контактным разрывом и ударной волной
            double shockSpeed = 1.75215;
            double soundStar = 0.99785;     // скорость звука в области за контактным разрывом

            // Средние точки "полок" — пробы берём в их серединах.
            double xLow = 0.5 + 0.5 * ((exactU - soundStar) + exactU) * t;
            double xHigh = 0.5 + 0.5 * (exactU + shockSpeed) * t;

            double mass = 0.0;
            foreach (var value in snapshot.Density) mass += value * dx;

            Console.WriteLine("Тест Сода (метод крупных частиц)");
            Console.WriteLine(FormattableString.Invariant(
                $"  ячеек: {parameters.Cells}, итераций: {parameters.Iterations}, t = {t:F5}, dt = {snapshot.TimeStep:E3}"));
            Console.WriteLine(FormattableString.Invariant(
                $"  время расчёта: {stopwatch.Elapsed.TotalMilliseconds:F1} мс"));

            // Точное значение массы: 0.5 * (ρ_L + ρ_R) при длине области 1.
            Console.WriteLine(FormattableString.Invariant(
                $"  масса ∫ρ dx = {mass:F6} (точно {0.5 * (DensityLeft + DensityRight):F6})"));
            Console.WriteLine(FormattableString.Invariant(
                $"  ρ({xLow:F3}) = {Sample(snapshot.X, snapshot.Density, xLow):F4} (точно {exactRhoLow:F4})"));
            Console.WriteLine(FormattableString.Invariant(
                $"  ρ({xHigh:F3}) = {Sample(snapshot.X, snapshot.Density, xHigh):F4} (точно {exactRhoHigh:F4})"));
            Console.WriteLine(FormattableString.Invariant(
                $"  u({xHigh:F3}) = {Sample(snapshot.X, snapshot.Velocity, xHigh):F4} (точно {exactU:F4})"));
            Console.WriteLine(FormattableString.Invariant(
                $"  p({xHigh:F3}) = {Sample(snapshot.X, snapshot.Pressure, xHigh):F4} (точно {exactP:F4})"));

            // Контроль устойчивости: величины не должны выходить за физичные пределы.
            double minRho = double.MaxValue, maxRho = double.MinValue;
            double minP = double.MaxValue, maxP = double.MinValue;
            double maxU = 0.0;
            for (int i = 0; i < snapshot.Density.Length; i++)
            {
                minRho = Math.Min(minRho, snapshot.Density[i]);
                maxRho = Math.Max(maxRho, snapshot.Density[i]);
                minP = Math.Min(minP, snapshot.Pressure[i]);
                maxP = Math.Max(maxP, snapshot.Pressure[i]);
                maxU = Math.Max(maxU, Math.Abs(snapshot.Velocity[i]));
            }

            Console.WriteLine(FormattableString.Invariant(
                $"  диапазоны: ρ ∈ [{minRho:F4}; {maxRho:F4}], p ∈ [{minP:F4}; {maxP:F4}], max|u| = {maxU:F4}"));

            bool ok = Math.Abs(mass - 0.5 * (DensityLeft + DensityRight)) < 1e-10
                      && minRho > 0.0 && minP > 0.0
                      && Math.Abs(Sample(snapshot.X, snapshot.Velocity, xHigh) - exactU) < 0.02
                      && Math.Abs(Sample(snapshot.X, snapshot.Pressure, xHigh) - exactP) < 0.02;

            Console.WriteLine(ok ? "  результат: ОК" : "  результат: РАСХОЖДЕНИЕ (проверьте параметры)");
        }

        /// <summary>Линейная интерполяция табличной функции (x — равномерная сетка).</summary>
        private static double Sample(double[] x, double[] values, double point)
        {
            int index = Array.BinarySearch(x, point);
            if (index < 0) index = ~index;
            if (index <= 0) return values[0];
            if (index >= x.Length) return values[^1];

            double t = (point - x[index - 1]) / (x[index] - x[index - 1]);
            return values[index - 1] + t * (values[index] - values[index - 1]);
        }
    }
}
