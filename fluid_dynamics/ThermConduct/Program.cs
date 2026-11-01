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
/// Начальные условия тестового запуска: сетка и граничные условия.
/// </summary>
public static class TestRun
{
    /// <summary>
    /// Сетка 32x32x32 (h = 0.001 м между центрами ячеек) с горячим пятном в центре:
    /// грань x = min — постоянный источник 100 К, остальные грани — 0 К.
    /// </summary>
    public static Mesh CreateTestMesh()
    {
        Mesh mesh = new(32, 32, 32);
        mesh.TestTempDistr();          // начальное условие: горячее пятно в центре (0..100 К)
        mesh.BoundaryCond(
            xPast: _ => 100.0,         // грань x = min — нагреваемая стенка
            xNext: _ => 0.0,           // грань x = max — холодная стенка
            yPast: _ => 0.0,
            yNext: _ => 0.0,
            zPast: _ => 0.0,
            zNext: _ => 0.0);

        return mesh;
    }
}
