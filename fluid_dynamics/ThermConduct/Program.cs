using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using ThermConductSim;
using ThermSliceViewer;

namespace Program;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .AfterSetup(builder =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    Mesh mesh = new(9, 9, 9);
                    mesh.TestTempDistr();
                    desktop.MainWindow = new MainWindow(mesh);
                }
            });
}