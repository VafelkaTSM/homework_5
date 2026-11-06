using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace SodaTest
{
    /// <summary>Отображаемая на графике величина.</summary>
    internal enum PlotVariable
    {
        Density,
        Velocity,
        Pressure,
        Energy
    }

    /// <summary>Приложение Avalonia (стили и главное окно создаются в коде, без XAML).</summary>
    internal sealed class App : Application
    {
        public override void Initialize()
        {
            // Светлая тема: тёмный текст на светлой панели параметров.
            RequestedThemeVariant = ThemeVariant.Light;
            Styles.Add(new FluentTheme());
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = new MainWindow();

            base.OnFrameworkInitializationCompleted();
        }
    }

    /// <summary>Точка входа графической части программы.</summary>
    internal static class Gui
    {
        public static void Start() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(Array.Empty<string>());
    }

    /// <summary>Главное окно: слева — параметры расчёта, справа — график решения.</summary>
    internal sealed class MainWindow : Window
    {
        private readonly PlotControl _plot = new();
        private readonly TextBlock _status = new()
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        };

        private NumericUpDown _cellsBox = null!;
        private NumericUpDown _iterationsBox = null!;
        private NumericUpDown _timeStepBox = null!;
        private NumericUpDown _courantBox = null!;
        private NumericUpDown _reportBox = null!;
        private ComboBox _variableBox = null!;
        private Button _startButton = null!;
        private Button _stopButton = null!;

        private CancellationTokenSource? _cancellation;
        private Task? _task;

        public MainWindow()
        {
            Title = "Метод крупных частиц — тест Сода";
            Width = 1100;
            Height = 720;
            MinWidth = 820;
            MinHeight = 520;

            var sidebar = BuildSidebar();
            DockPanel.SetDock(sidebar, Dock.Left);

            var root = new DockPanel { LastChildFill = true };
            root.Children.Add(sidebar);
            root.Children.Add(new Border
            {
                Child = _plot,
                Background = Brushes.White,
                BorderBrush = Brushes.Gainsboro,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(8)
            });
            Content = root;

            ShowInitialState();
        }

        /// <summary>Левая панель с настройками и кнопками управления расчётом.</summary>
        private Control BuildSidebar()
        {
            var panel = new StackPanel { Margin = new Thickness(14), Spacing = 2, Width = 250 };

            panel.Children.Add(new TextBlock
            {
                Text = "Метод крупных частиц",
                FontSize = 18,
                FontWeight = FontWeight.Bold
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Тест Сода: распад разрыва на отрезке [0, 1], мембрана в x = 0.5.",
                FontSize = 12,
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 8)
            });

            _cellsBox = AddField(panel, "Ячеек на прямой N", 10, 200000, 1500, 50, "0");
            _iterationsBox = AddField(panel, "Итераций", 0, 20000000, 1500, 100, "0");
            _timeStepBox = AddField(panel, "Шаг по времени δt", 1e-8, 1, 1e-4, 1e-5, "0.00000##");
            _courantBox = AddField(panel, "Число Куранта C", 0, 1, 0.2, 0.05, "0.0##");
            _reportBox = AddField(panel, "Обновлять каждые k итераций", 1, 100000, 50, 10, "0");

            panel.Children.Add(new TextBlock
            {
                Text = "Отображаемая величина",
                FontSize = 12,
                Margin = new Thickness(0, 12, 0, 2)
            });
            _variableBox = new ComboBox
            {
                ItemsSource = new[] { "Плотность ρ", "Скорость u", "Давление p", "Энергия E" },
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _variableBox.SelectionChanged += (_, _) =>
            {
                _plot.Variable = (PlotVariable)Math.Max(0, _variableBox.SelectedIndex);
                _plot.InvalidateVisual();
            };
            panel.Children.Add(_variableBox);

            _startButton = new Button
            {
                Content = "Старт",
                MinWidth = 96,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            _stopButton = new Button
            {
                Content = "Стоп",
                MinWidth = 96,
                IsEnabled = false,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            _startButton.Click += OnStartClick;
            _stopButton.Click += (_, _) => _cancellation?.Cancel();

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 14, 0, 0) };
            buttons.Children.Add(_startButton);
            buttons.Children.Add(_stopButton);
            panel.Children.Add(buttons);
            panel.Children.Add(_status);

            return new Border
            {
                Child = new ScrollViewer { Content = panel },
                Background = new SolidColorBrush(Color.FromRgb(248, 248, 250)),
                BorderBrush = Brushes.Gainsboro,
                BorderThickness = new Thickness(0, 0, 1, 0)
            };
        }

        /// <summary>Добавляет подпись и числовое поле ввода в панель.</summary>
        private static NumericUpDown AddField(
            StackPanel panel, string caption, double min, double max, double value, double step, string format)
        {
            panel.Children.Add(new TextBlock { Text = caption, FontSize = 12, Margin = new Thickness(0, 8, 0, 2) });
            var box = new NumericUpDown
            {
                Minimum = (decimal)min,
                Maximum = (decimal)max,
                Value = (decimal)value,
                Increment = (decimal)step,
                FormatString = format
            };
            panel.Children.Add(box);
            return box;
        }

        /// <summary>Читает параметры расчёта из полей ввода. null — если значения некорректны.</summary>
        private SolverParameters? ReadParameters()
        {
            int cells = (int)(_cellsBox.Value ?? 1500);
            int iterations = (int)(_iterationsBox.Value ?? 1500);
            double timeStep = (double)(_timeStepBox.Value ?? 0.0001m);
            double courant = (double)(_courantBox.Value ?? 0.2m);

            if (cells < 10)
            {
                _status.Text = "Число ячеек должно быть не меньше 10.";
                return null;
            }
            if (timeStep <= 0.0)
            {
                _status.Text = "Шаг по времени должен быть положительным.";
                return null;
            }

            return new SolverParameters
            {
                Cells = cells,
                Iterations = iterations,
                TimeStep = timeStep,
                Courant = courant,
                Gamma = 1.4,
                XMin = 0.0,
                XMax = 1.0
            };
        }

        /// <summary>Показывает начальное состояние (t = 0) без расчёта.</summary>
        private void ShowInitialState()
        {
            var parameters = ReadParameters();
            if (parameters is null) return;

            // Расчёт с нулевым числом итераций возвращает только начальные данные.
            var snapshot = Calculation.Run(
                new SolverParameters { Cells = parameters.Cells, Iterations = 0, Gamma = parameters.Gamma },
                Program.SodInitialConditions,
                Program.ZeroGradientBoundary,
                1,
                s => s,
                _ => { });

            _plot.Snapshot = snapshot;
            _plot.InvalidateVisual();
            _status.Text = FormattableString.Invariant(
                $"t = 0\nδt = {parameters.TimeStep:E2}\nN = {parameters.Cells}");
        }

        /// <summary>Запуск расчёта в фоновом потоке.</summary>
        private async void OnStartClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_task is { IsCompleted: false }) return;

            var parameters = ReadParameters();
            if (parameters is null) return;

            int reportEvery = Math.Max(1, (int)(_reportBox.Value ?? 1));
            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;
            SetRunning(true);

            try
            {
                _task = Task.Run(() => Calculation.Run(
                    parameters,
                    Program.SodInitialConditions,
                    Program.ZeroGradientBoundary,
                    reportEvery,
                    // "Универсальный" делегат: тип TReport = SimulationSnapshot, данные
                    // передаются в интерфейс без преобразования.
                    s => s,
                    snapshot => Dispatcher.UIThread.Post(() => ShowSnapshot(snapshot, parameters.Iterations)),
                    token));

                await _task;
            }
            catch (Exception ex)
            {
                _status.Text = "Ошибка расчёта: " + ex.Message;
            }
            finally
            {
                SetRunning(false);
                _cancellation?.Dispose();
                _cancellation = null;
            }
        }

        /// <summary>Выводит снимок решения на график и обновляет строку состояния.</summary>
        private void ShowSnapshot(SimulationSnapshot snapshot, int totalIterations)
        {
            _plot.Snapshot = snapshot;
            _plot.InvalidateVisual();

            double dx = snapshot.X.Length > 1 ? snapshot.X[1] - snapshot.X[0] : 1.0;
            double maxSpeed = 0.0;
            for (int i = 0; i < snapshot.Density.Length; i++)
                maxSpeed = Math.Max(maxSpeed,
                    Math.Abs(snapshot.Velocity[i]) + Math.Sqrt(snapshot.Gamma * snapshot.Pressure[i] / snapshot.Density[i]));

            _status.Text = FormattableString.Invariant(
                $"итерация {snapshot.Iteration} / {totalIterations}\nt = {snapshot.Time:F5}\nδt = {snapshot.TimeStep:E3}\nCFL = {snapshot.TimeStep * maxSpeed / dx:F3}");
        }

        /// <summary>Блокирует поля ввода на время расчёта.</summary>
        private void SetRunning(bool running)
        {
            _startButton.IsEnabled = !running;
            _stopButton.IsEnabled = running;
            _cellsBox.IsEnabled = !running;
            _iterationsBox.IsEnabled = !running;
            _timeStepBox.IsEnabled = !running;
            _courantBox.IsEnabled = !running;
            _reportBox.IsEnabled = !running;
        }
    }

    /// <summary>
    /// Простой график выбранной величины по координате x. Собственный Control
    /// рисует одну ломаную и работает заметно быстрее, чем тысячи фигур на Canvas.
    /// </summary>
    internal sealed class PlotControl : Control
    {
        private const double MarginLeft = 62;
        private const double MarginRight = 18;
        private const double MarginTop = 30;
        private const double MarginBottom = 38;

        private static readonly IBrush AxisBrush = new SolidColorBrush(Color.FromRgb(90, 90, 90));
        private static readonly IBrush GridBrush = new SolidColorBrush(Color.FromRgb(232, 232, 238));
        private static readonly IBrush CurveBrush = new SolidColorBrush(Color.FromRgb(20, 110, 220));
        private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(70, 70, 75));

        public SimulationSnapshot? Snapshot { get; set; }

        public PlotVariable Variable { get; set; } = PlotVariable.Density;

        public override void Render(DrawingContext context)
        {
            var snapshot = Snapshot;
            if (snapshot is null || snapshot.X.Length < 2) return;

            var size = Bounds.Size;
            double width = size.Width - MarginLeft - MarginRight;
            double height = size.Height - MarginTop - MarginBottom;
            if (width < 20 || height < 20) return;

            double[] values = Variable switch
            {
                PlotVariable.Velocity => snapshot.Velocity,
                PlotVariable.Pressure => snapshot.Pressure,
                PlotVariable.Energy => snapshot.Energy,
                _ => snapshot.Density
            };

            // Диапазон значений по вертикали (с запасом 5%).
            double min = double.MaxValue, max = double.MinValue;
            foreach (double value in values)
            {
                if (value < min) min = value;
                if (value > max) max = value;
            }
            if (max - min < 1e-12) { max += 0.5; min -= 0.5; }
            double pad = 0.05 * (max - min);
            min -= pad;
            max += pad;

            // Отображение "физические координаты -> пиксели".
            double xFirst = snapshot.X[0], xLast = snapshot.X[^1];
            double MapX(double x) => MarginLeft + (x - xFirst) / (xLast - xFirst) * width;
            double MapY(double v) => MarginTop + height - (v - min) / (max - min) * height;

            var axisPen = new Pen(AxisBrush, 1);
            var gridPen = new Pen(GridBrush, 1);

            // Горизонтальная сетка с подписями значений.
            for (int k = 0; k <= 5; k++)
            {
                double value = min + (max - min) * k / 5.0;
                double y = MapY(value);
                context.DrawLine(gridPen, new Point(MarginLeft, y), new Point(MarginLeft + width, y));
                DrawText(context, value.ToString("0.###", CultureInfo.InvariantCulture), 6, y - 7);
            }

            // Вертикальная сетка с подписями координаты.
            for (int k = 0; k <= 4; k++)
            {
                double x = xFirst + (xLast - xFirst) * k / 4.0;
                double px = MapX(x);
                context.DrawLine(gridPen, new Point(px, MarginTop), new Point(px, MarginTop + height));
                DrawText(context, x.ToString("0.##", CultureInfo.InvariantCulture), px - 8, MarginTop + height + 6);
            }

            context.DrawRectangle(null, axisPen, new Rect(MarginLeft, MarginTop, width, height));

            // Сама кривая — одной геометрией (быстро даже для десятков тысяч точек).
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(MapX(snapshot.X[0]), MapY(values[0])), false);
                for (int i = 1; i < snapshot.X.Length; i++)
                    ctx.LineTo(new Point(MapX(snapshot.X[i]), MapY(values[i])));
                ctx.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(CurveBrush, 1.4), geometry);

            string title = Variable switch
            {
                PlotVariable.Velocity => "скорость u(x)",
                PlotVariable.Pressure => "давление p(x)",
                PlotVariable.Energy => "полная энергия E(x)",
                _ => "плотность ρ(x)"
            };
            DrawText(context, FormattableString.Invariant($"{title},  t = {snapshot.Time:F4}"), MarginLeft, 8, 13);
        }

        private static void DrawText(DrawingContext context, string text, double x, double y, double fontSize = 11)
        {
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), fontSize, TextBrush);
            context.DrawText(formatted, new Point(x, y));
        }
    }
}

