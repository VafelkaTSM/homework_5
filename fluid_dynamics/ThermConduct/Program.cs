using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using ThermConductSim;
using ThermSliceViewer;

namespace Program;

/// <summary>
/// Точка входа приложения. Здесь только вызовы API расчётного ядра (ThermConductSim)
/// и окна визуализации (ThermSliceViewer), плюс начальные условия тестового запуска.
/// </summary>
class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .AfterSetup(builder =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.MainWindow = new MainWindow(TestRun.CreateTestMesh());
                }
            });
}

/// <summary>
/// Начальные и граничные условия тестового запуска: сетка и массивы температур.
/// </summary>
public static class TestRun
{
    /// <summary>Число ячеек по каждой оси куба.</summary>
    private const int N = 100;

    /// <summary>
    /// Сетка 32x32x32 (h = 0.001 м между центрами ячеек) с горячим пятном в центре:
    /// грань x = min — постоянный источник 100 К, остальные грани — 0 К.
    /// Начальные и граничные условия передаются при создании сетки массивами температур.
    /// </summary>
    public static Mesh CreateTestMesh()
    {
        // Начальное условие: горячее пятно в центре (0..100 К)
        double[,,] temps = new double[N, N, N];
        double center = N / 2.0;
        for (int x = 0; x < N; x++)
        {
            for (int y = 0; y < N; y++)
            {
                for (int z = 0; z < N; z++)
                {
                    double rad = Math.Sqrt(Math.Pow(x - center, 2) + Math.Pow(y - center, 2) + Math.Pow(z - center, 2));
                    temps[x, y, z] = Math.Max(0.0, 100.0 - rad * 3.5);
                }
            }
        }

        double[,] cold = Plane(N, N, 0.0);     // остальные грани — 0 К
        double[,] hot = Plane(N, N, 100.0);    // грань x = min — нагреваемая стенка

        return new Mesh(temps,
            xPast: hot, xNext: cold,
            yPast: cold, yNext: cold,
            zPast: cold, zNext: cold);
    }

    /// <summary>Плоскость грани куба, заполненная одной температурой.</summary>
    private static double[,] Plane(int d0, int d1, double value)
    {
        double[,] plane = new double[d0, d1];
        for (int i = 0; i < d0; i++)
        {
            for (int j = 0; j < d1; j++)
            {
                plane[i, j] = value;
            }
        }
        return plane;
    }
}
