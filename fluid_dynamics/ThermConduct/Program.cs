using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ThermConductSim;
using ThermSliceViewer;

namespace Program;

/// <summary>
/// Точка входа приложения.
/// Порядок работы: начальные и граничные условия читаются из json → создаётся объект
/// расчёта (сетка) и объект интерфейса (окно) → по кнопке «Старт» вызывается
/// Mesh.StartCalc с делегатом, который передаёт результаты в окно.
/// </summary>
class Program
{
    // Файлы условий: формат описан в MeshConditions (число — равномерное поле/грань,
    // массив — своя температура у каждой ячейки/участка грани).
    private const string InitialConditionsFile = "initial_conditions.json";
    private const string BoundaryConditionsFile = "boundary_conditions.json";

    [STAThread]
    public static void Main(string[] args)
    {
        // 1. Начальные условия: из json создаются массив температур ячеек [X, Y, Z] и
        //    шесть массивов граничных условий. По умолчанию поле температур равномерное.
        MeshConditions conditions = MeshConditions.Load(InitialConditionsFile, BoundaryConditionsFile);

        // 2. Объект расчёта — сетка вместе с начальными и граничными условиями.
        Mesh mesh = conditions.CreateMesh();

        // 3. Настройка Avalonia; окно создаётся в AfterSetup, когда движок уже готов.
        BuildAvaloniaApp(conditions, mesh).StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp(MeshConditions conditions, Mesh mesh) =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .AfterSetup(builder =>
            {
                if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                {
                    return;
                }

                // 4. Объект интерфейса: окно получает начальное поле (его форма задаёт размеры
                //    сетки) и шаг сетки; о расчёте оно ничего не знает — только рисует данные.
                var window = new MainWindow(conditions.Initial, mesh.CellSize);

                // 5. Связь интерфейса и расчёта: кнопки окна поднимают события, расчёт
                //    возвращает данные через делегат CalcUpdateHandler (см. RunCalculation).
                window.StartRequested += async (_, settings) => await RunCalculation(window, mesh, settings);
                window.StopRequested += (_, _) => mesh.StopCalc();
                window.ResetRequested += (_, _) =>
                {
                    mesh.SetTemps(conditions.Initial);      // вернуть начальные температуры
                    window.ShowReset(mesh.TempSnapshot());  // перерисовать поле и очистить график
                };

                desktop.MainWindow = window;
            });

    /// <summary>
    /// Запуск расчёта по кнопке «Старт». Mesh.StartCalc синхронный (занимает вызвавший
    /// поток), поэтому он выполняется в фоновой задаче, а окно остаётся отзывчивым.
    /// </summary>
    private static async Task RunCalculation(MainWindow window, Mesh mesh, CalcSettings settings)
    {
        try
        {
            await Task.Run(() => mesh.StartCalc(
                settings.Iterations,
                settings.DeltaTime,
                settings.CourantNumber,
                (m, iteration, time, sumAbsDeltaT) => OnMeshUpdated(window, m, iteration, time, sumAbsDeltaT),
                settings.UpdatePeriod));

            window.ShowFinished(mesh.WasStopped, mesh.Iteration, mesh.Time, mesh.ThermalDiffusivity);
        }
        catch (Exception ex)
        {
            window.ShowError(ex.Message);
        }
    }

    /// <summary>
    /// Делегат «расчёт → интерфейс», который передаётся в Mesh.StartCalc. Вызывается на
    /// потоке расчёта: здесь делается снимок температур, а отрисовка ставится в очередь
    /// UI-потока (Avalonia разрешает менять элементы окна только из него).
    /// </summary>
    private static void OnMeshUpdated(MainWindow window, Mesh mesh, int iteration, double time, double sumAbsDeltaT)
    {
        double[,,] snapshot = mesh.TempSnapshot();     // копия — безопасно передать в другой поток
        Dispatcher.UIThread.Post(() => window.ShowFrame(snapshot, iteration, time, sumAbsDeltaT));
    }
}
