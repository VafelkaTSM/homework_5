using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
namespace ThermSliceViewer;

/// <summary>
/// Параметры расчёта, введённые пользователем. Окно только читает и проверяет их,
/// а сам расчёт запускает вызывающий код (Program.cs), подписанный на событие
/// <see cref="MainWindow.StartRequested"/>.
/// </summary>
/// <param name="Iterations">Сколько шагов по времени выполнить.</param>
/// <param name="DeltaTime">Шаг по времени одной итерации, с.</param>
/// <param name="CourantNumber">Число Куранта C = a·dt/h² (устойчиво при C ≤ 1/6).</param>
/// <param name="UpdatePeriod">Через сколько итераций обновлять интерфейс.</param>
public sealed record CalcSettings(int Iterations, double DeltaTime, double CourantNumber, int UpdatePeriod);

/// <summary>
/// Главное окно — только графический интерфейс: срезы сетки, параметры расчёта, график
/// и кнопки. Окно ничего не считает и не хранит Mesh: о нажатиях кнопок оно сообщает
/// событиями StartRequested / StopRequested / ResetRequested, а данные расчёта принимает
/// методами ShowFrame / ShowReset / ShowFinished / ShowError.
/// </summary>
public class MainWindow : Window
{
    // Размеры сетки — берутся из формы начального поля температур
    private readonly int _nx, _ny, _nz;

    // Шаг сетки h, м: нужен для подписей и для коэффициента температуропроводности a
    private readonly double _cellSize;

    // Текущие температуры ячеек — только для отрисовки среза
    private double[,,] _temps;

    // Диапазон температур для цветовой шкалы
    private double _colorMin, _colorMax;

    // Компоненты интерфейса
    private readonly ComboBox _axisComboBox;
    private readonly Slider _sliceSlider;
    private readonly TextBlock _sliceInfoText;
    private readonly Image _sliceImageView;
    private readonly TextBox _iterationsBox;
    private readonly TextBox _deltaTimeBox;
    private readonly TextBox _courantBox;
    private readonly TextBox _updatePeriodBox;
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private readonly Button _resetButton;
    private readonly TextBlock _statusText;
    private readonly SumDeltaChart _chart;

    // Тёмное оформление: чёрный фон и белый шрифт
    private static readonly IBrush WindowBrush = new SolidColorBrush(Colors.Black);
    private static readonly IBrush InputBackgroundBrush = new SolidColorBrush(Color.Parse("#FF151515"));
    private static readonly IBrush InputBorderBrush = new SolidColorBrush(Color.Parse("#FF666666"));
    private static readonly IBrush SeparatorBrush = new SolidColorBrush(Color.Parse("#FF444444"));

    private bool _running;                     // идёт ли расчёт (состояние кнопок и строки состояния)
    private bool _closed;                      // окно закрыто — обновления интерфейса игнорируются
    private double _lastCourant = 0.1;         // последнее число Куранта — для строки состояния
    private string _warning = string.Empty;    // предупреждение о неустойчивой схеме

    /// <summary>Граница устойчивости явной 3D-схемы: C ≤ 1/6.</summary>
    private const double StableCourant = 1.0 / 6.0;

    /// <summary>Пользователь нажал «Старт расчёта»; параметры уже проверены окном.</summary>
    public event EventHandler<CalcSettings>? StartRequested;

    /// <summary>Пользователь нажал «Остановить».</summary>
    public event EventHandler? StopRequested;

    /// <summary>Пользователь нажал «Сброс» — нужно вернуть начальные условия.</summary>
    public event EventHandler? ResetRequested;

    /// <summary>
    /// Окно создаётся по начальному полю температур (его форма задаёт размеры сетки)
    /// и шагу сетки. Объект расчёта окну не передаётся: оно только рисует данные,
    /// которые приходят в методы ShowFrame / ShowReset / ShowFinished / ShowError.
    /// </summary>
    /// <param name="initialTemps">Начальные температуры ячеек [X, Y, Z], К.</param>
    /// <param name="cellSize">Шаг сетки h, м.</param>
    public MainWindow(double[,,] initialTemps, double cellSize)
    {
        Title = "Теплопередача: срезы сетки и сумма модулей изменений температуры";
        Width = 980;
        Height = 920;
        Background = WindowBrush;
        RequestedThemeVariant = ThemeVariant.Dark;

        // 1. Начальное поле температур: его форма задаёт размеры сетки
        ArgumentNullException.ThrowIfNull(initialTemps);
        if (cellSize <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), "шаг сетки должен быть > 0");
        }

        _temps = initialTemps;
        _cellSize = cellSize;
        _nx = initialTemps.GetLength(0);
        _ny = initialTemps.GetLength(1);
        _nz = initialTemps.GetLength(2);
        if (_nx <= 0 || _ny <= 0 || _nz <= 0)
        {
            throw new ArgumentException("начальное поле температур пустое", nameof(initialTemps));
        }

        (_colorMin, _colorMax) = FindRange(_temps);
        if (_colorMax - _colorMin < 1e-9)
        {
            _colorMax = _colorMin + 1.0;
        }

        // 2. Панель управления срезами
        var controlPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(10)
        };

        controlPanel.Children.Add(MakeLabel("Нормаль среза:"));

        _axisComboBox = new ComboBox
        {
            ItemsSource = new[] { "Ось X (плоскость YZ)", "Ось Y (плоскость XZ)", "Ось Z (плоскость XY)" },
            SelectedIndex = 2,
            VerticalAlignment = VerticalAlignment.Center
        };
        controlPanel.Children.Add(_axisComboBox);

        controlPanel.Children.Add(MakeLabel("Индекс слоя:"));

        _sliceSlider = new Slider
        {
            Minimum = 0,
            Maximum = _nz - 1,
            Value = _nz / 2,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Width = 200,
            VerticalAlignment = VerticalAlignment.Center
        };
        controlPanel.Children.Add(_sliceSlider);

        _sliceInfoText = MakeLabel($"Срез {_sliceSlider.Value} / {_nz - 1}");
        controlPanel.Children.Add(_sliceInfoText);

        // 3. Элемент Image для вывода пиксельной карты среза
        _sliceImageView = new Image
        {
            Stretch = Stretch.Uniform,
            Margin = new Thickness(10)
        };
        RenderOptions.SetBitmapInterpolationMode(_sliceImageView, BitmapInterpolationMode.None);

        // 4. Панель параметров расчёта и график (снизу окна)
        _iterationsBox = MakeInputBox("200", 90, "Целое число итераций (шагов по времени)");
        _deltaTimeBox = MakeInputBox("1,0", 90, "Шаг по времени одной итерации, с (можно дробное: 0,5 или 0.5)");
        _courantBox = MakeInputBox("0,15", 90, "Число Куранта C = a*dt/h^2. Для явной 3D-схемы устойчиво C <= 1/6");
        _updatePeriodBox = MakeInputBox("1", 70, "Через сколько итераций обновлять интерфейс (1 — каждую итерацию)");

        _startButton = new Button
        {
            Content = "Старт расчёта",
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        _stopButton = new Button
        {
            Content = "Остановить",
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = false,
            VerticalAlignment = VerticalAlignment.Center
        };

        _resetButton = new Button
        {
            Content = "Сброс",
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        // Параметры расчёта — первой строкой, кнопки — второй (чтобы всё помещалось по ширине окна)
        var paramsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        paramsRow.Children.Add(MakeLabel("Итераций:"));
        paramsRow.Children.Add(_iterationsBox);
        paramsRow.Children.Add(MakeLabel("Шаг dt, с:"));
        paramsRow.Children.Add(_deltaTimeBox);
        paramsRow.Children.Add(MakeLabel("Число Куранта:"));
        paramsRow.Children.Add(_courantBox);
        paramsRow.Children.Add(MakeLabel("Обновлять каждые:"));
        paramsRow.Children.Add(_updatePeriodBox);
        paramsRow.Children.Add(MakeLabel("итераций"));

        var buttonsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0)
        };
        buttonsRow.Children.Add(_startButton);
        buttonsRow.Children.Add(_stopButton);
        buttonsRow.Children.Add(_resetButton);

        var settingsPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(10, 0, 10, 0)
        };
        settingsPanel.Children.Add(paramsRow);
        settingsPanel.Children.Add(buttonsRow);

        _statusText = new TextBlock
        {
            Text = "Готов к расчёту.",
            Foreground = Brushes.White,
            Margin = new Thickness(10, 6, 10, 0),
            TextWrapping = TextWrapping.Wrap
        };

        // График суммы модулей изменений температуры по ячейкам
        _chart = new SumDeltaChart
        {
            Height = 190,
            Margin = new Thickness(10, 6, 10, 8)
        };

        var bottomPanel = new StackPanel
        {
            Orientation = Orientation.Vertical
        };
        bottomPanel.Children.Add(new Border
        {
            BorderBrush = SeparatorBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 8, 0, 0),
            Child = settingsPanel
        });
        bottomPanel.Children.Add(_statusText);
        bottomPanel.Children.Add(_chart);

        // 5. Корневой контейнер: строка параметров, изображение среза (растягивается), нижняя панель
        var rootGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
        };
        Grid.SetRow(controlPanel, 0);
        Grid.SetRow(_sliceImageView, 1);
        Grid.SetRow(bottomPanel, 2);
        rootGrid.Children.Add(controlPanel);
        rootGrid.Children.Add(_sliceImageView);
        rootGrid.Children.Add(bottomPanel);

        Content = rootGrid;

        // 6. Обработчики пользовательского ввода. Расчёт здесь не запускается: окно только
        //    меняет своё состояние и сообщает о действии пользователя событиями.
        _axisComboBox.SelectionChanged += (s, e) => OnAxisChanged();
        _sliceSlider.ValueChanged += (s, e) => UpdateSliceView();
        _startButton.Click += (s, e) => OnStartClicked();
        _stopButton.Click += (s, e) => OnStopClicked();
        _resetButton.Click += (s, e) => OnResetClicked();
        Closed += (s, e) => _closed = true;

        // Отрисовка первого кадра
        UpdateSliceView();
        UpdateStatus($"Исходное состояние: {FormatTempRange(_colorMin, _colorMax)}, "
            + $"ячеек {_nx}x{_ny}x{_nz}, h = {_cellSize * 1000:0.###} мм.");
    }

    // ---------- Создание элементов оформления ----------

    /// <summary>Надпись белым шрифтом по центру строки.</summary>
    private static TextBlock MakeLabel(string text) => new()
    {
        Text = text,
        Foreground = Brushes.White,
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>Поле ввода в тёмном оформлении (чёрный фон, белый текст, светлая рамка).</summary>
    private static TextBox MakeInputBox(string text, double width, string tooltip)
    {
        var box = new TextBox
        {
            Text = text,
            Width = width,
            Background = InputBackgroundBrush,
            Foreground = Brushes.White,
            CaretBrush = Brushes.White,
            BorderBrush = InputBorderBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(box, tooltip);
        return box;
    }

    private void OnAxisChanged()
    {
        int maxIndex = _axisComboBox.SelectedIndex switch
        {
            0 => _nx - 1,
            1 => _ny - 1,
            _ => _nz - 1
        };

        _sliceSlider.Maximum = maxIndex;
        if (_sliceSlider.Value > maxIndex)
        {
            _sliceSlider.Value = maxIndex;
        }

        UpdateSliceView();
    }

    private void UpdateSliceView()
    {
        int axis = _axisComboBox.SelectedIndex;
        int maxIndex = axis switch { 0 => _nx - 1, 1 => _ny - 1, _ => _nz - 1 };
        int sliceIdx = Math.Clamp((int)Math.Round(_sliceSlider.Value), 0, maxIndex);

        int width, height;
        if (axis == 0)      { width = _ny; height = _nz; }
        else if (axis == 1) { width = _nx; height = _nz; }
        else                { width = _nx; height = _ny; }

        _sliceInfoText.Text = $"Срез {sliceIdx} / {_sliceSlider.Maximum}";

        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        double sliceMin = double.MaxValue, sliceMax = double.MinValue;

        using (var buffer = bitmap.Lock())
        {
            int rowBytes = buffer.RowBytes;
            // Обычный управляемый массив байтов в оперативной памяти C#
            byte[] rawBytes = new byte[rowBytes * height];

            for (int v = 0; v < height; v++)
            {
                // Смещение до начала строки (с разворотом оси снизу вверх)
                int rowOffset = (height - 1 - v) * rowBytes;

                for (int u = 0; u < width; u++)
                {
                    double temp = axis switch
                    {
                        0 => _temps[sliceIdx, u, v],
                        1 => _temps[u, sliceIdx, v],
                        _ => _temps[u, v, sliceIdx]
                    };

                    if (temp < sliceMin) sliceMin = temp;
                    if (temp > sliceMax) sliceMax = temp;

                    var (b, g, r) = TemperatureToRgb(temp, _colorMin, _colorMax);

                    // Смещение пикселя внутри строки (4 байта на пиксель в формате BGRA)
                    int pixelOffset = rowOffset + u * 4;
                    rawBytes[pixelOffset + 0] = b;     // Blue
                    rawBytes[pixelOffset + 1] = g;     // Green
                    rawBytes[pixelOffset + 2] = r;     // Red
                    rawBytes[pixelOffset + 3] = 255;   // Alpha (полная непрозрачность)
                }
            }

            // buffer.Address — это безопасный IntPtr.
            // Marshal.Copy сам переносит данные из управляемого массива в нативный буфер битмапа.
            Marshal.Copy(rawBytes, 0, buffer.Address, rawBytes.Length);
        }

        _sliceImageView.Source = bitmap;

        if (sliceMin <= sliceMax)
        {
            _sliceInfoText.Text = $"Срез {sliceIdx} / {_sliceSlider.Maximum}, {FormatTempRange(sliceMin, sliceMax)}";
        }
    }

    // ---------- Нажатия кнопок (только интерфейс) ----------

    /// <summary>
    /// Проверка введённых параметров расчёта. При ошибке пишет её в строку состояния
    /// и возвращает <c>false</c> — в этом случае событие «старт» не поднимается.
    /// </summary>
    private bool TryReadSettings(out CalcSettings settings)
    {
        settings = new CalcSettings(0, 0.0, 0.0, 1);

        if (!int.TryParse((_iterationsBox.Text ?? string.Empty).Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int iterations) || iterations < 0)
        {
            UpdateStatus("Ошибка: число итераций должно быть целым неотрицательным числом.");
            return false;
        }

        if (!TryParseDouble(_deltaTimeBox.Text, out double deltaTime) || deltaTime <= 0.0)
        {
            UpdateStatus("Ошибка: шаг по времени должен быть положительным дробным числом (например 0,5 или 0.5).");
            return false;
        }

        if (!TryParseDouble(_courantBox.Text, out double courant) || courant <= 0.0)
        {
            UpdateStatus("Ошибка: число Куранта должно быть положительным числом (например 0,15).");
            return false;
        }

        if (!int.TryParse((_updatePeriodBox.Text ?? string.Empty).Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int updatePeriod) || updatePeriod < 1)
        {
            UpdateStatus("Ошибка: интерфейс должен обновляться не реже, чем раз в 1 итерацию "
                + "(введите целое число 1, 2, 5 ...).");
            return false;
        }

        settings = new CalcSettings(iterations, deltaTime, courant, updatePeriod);
        return true;
    }

    /// <summary>
    /// «Старт расчёта»: проверить параметры, подготовить экран (кнопки, график, строка
    /// состояния) и сообщить о готовности считать. Сам расчёт запускает подписчик
    /// события <see cref="StartRequested"/> (Program.cs).
    /// </summary>
    private void OnStartClicked()
    {
        if (_running || !TryReadSettings(out CalcSettings settings))
        {
            return;
        }

        SetRunning(true);
        _chart.Clear();
        _lastCourant = settings.CourantNumber;
        _warning = settings.CourantNumber > StableCourant
            ? "  Внимание: C > 1/6 — явная схема может оказаться неустойчивой."
            : string.Empty;

        // Коэффициент температуропроводности: a = C·h²/dt (ядро считает его так же)
        double diffusivity = settings.CourantNumber * _cellSize * _cellSize / settings.DeltaTime;

        UpdateStatus($"Расчёт: {settings.Iterations} итераций, dt = {settings.DeltaTime:0.####} с, "
            + $"C = {settings.CourantNumber:0.####}, a = {diffusivity:0.###E+0} м^2/с, "
            + $"h = {_cellSize * 1000:0.###} мм, обновление интерфейса каждые "
            + $"{settings.UpdatePeriod} итер.{_warning}");

        StartRequested?.Invoke(this, settings);
    }

    /// <summary>
    /// «Остановить»: сообщить о запросе остановки. Расчёт завершит текущую итерацию
    /// и выйдет (флаг остановки проверяет Mesh.StartCalc).
    /// </summary>
    private void OnStopClicked()
    {
        if (!_running)
        {
            return;
        }

        _stopButton.IsEnabled = false;
        UpdateStatus("Останавливаем расчёт: завершаем текущую итерацию...");
        StopRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// «Сброс»: попросить вернуть начальные условия. Само поле перерисует ShowReset,
    /// когда вызывающий код присвоит сетке начальные температуры.
    /// </summary>
    private void OnResetClicked()
    {
        if (_running)
        {
            return;
        }

        ResetRequested?.Invoke(this, EventArgs.Empty);
    }

    // ---------- Приём данных из расчёта (вызывает Program.cs) ----------

    /// <summary>
    /// Новая итерация расчёта (вызывается в UI-потоке): срез, график и строка состояния.
    /// </summary>
    /// <param name="snapshot">Снимок температур ячеек [X, Y, Z], К.</param>
    /// <param name="iteration">Номер выполненной итерации.</param>
    /// <param name="time">Текущее время расчёта, с.</param>
    /// <param name="sumAbsDeltaT">Сумма модулей изменений температуры за итерацию, К.</param>
    public void ShowFrame(double[,,] snapshot, int iteration, double time, double sumAbsDeltaT)
    {
        if (_closed)
        {
            return;
        }

        CheckSnapshotShape(snapshot);
        _temps = snapshot;

        (double min, double max) = FindRange(snapshot);
        if (min < _colorMin) _colorMin = min;
        if (max > _colorMax) _colorMax = max;

        _chart.AddPoint(iteration, time, sumAbsDeltaT);
        UpdateSliceView();

        // Пока расчёт идёт, в строке состояния показываем прогресс.
        // После завершения (когда _running == false) итоговое сообщение не затирается
        // отложенными обновлениями.
        if (_running)
        {
            UpdateStatus($"Итерация {iteration}: t = {time:0.###} с, Σ|ΔT| = {FormatSigma(sumAbsDeltaT)} К, "
                + $"{FormatTempRange(min, max)}, C = {_lastCourant:0.####}.");
        }
    }

    /// <summary>Расчёт завершён (или остановлен): разблокировать кнопки и показать итоги.</summary>
    /// <param name="stopped">Расчёт был прерван кнопкой «Остановить».</param>
    /// <param name="iteration">Сколько итераций выполнено.</param>
    /// <param name="time">Время расчёта, с.</param>
    /// <param name="thermalDiffusivity">Коэффициент температуропроводности a, м²/с.</param>
    public void ShowFinished(bool stopped, int iteration, double time, double thermalDiffusivity)
    {
        SetRunning(false);

        string prefix = stopped
            ? $"Расчёт остановлен пользователем: {iteration} итераций, t = {time:0.###} с, "
            : $"Расчёт завершён: {iteration} итераций, t = {time:0.###} с, ";

        UpdateStatus(prefix + $"a = {thermalDiffusivity:0.###E+0} м^2/с.{_warning}");
    }

    /// <summary>Ошибка расчёта: разблокировать кнопки и показать текст ошибки.</summary>
    public void ShowError(string message)
    {
        SetRunning(false);
        UpdateStatus("Ошибка расчёта: " + message);
    }

    /// <summary>
    /// Возврат к начальным условиям (ответ на «Сброс»): новое поле температур,
    /// сброшенная цветовая шкала и пустой график.
    /// </summary>
    /// <param name="temps">Начальные температуры ячеек [X, Y, Z], К.</param>
    public void ShowReset(double[,,] temps)
    {
        CheckSnapshotShape(temps);
        _temps = temps;

        (_colorMin, _colorMax) = FindRange(_temps);
        if (_colorMax - _colorMin < 1e-9)
        {
            _colorMax = _colorMin + 1.0;
        }

        _chart.Clear();
        UpdateSliceView();
        UpdateStatus($"Начальные условия восстановлены: {FormatTempRange(_colorMin, _colorMax)}.");
    }

    /// <summary>Состояние «идёт расчёт»: состояния кнопок и признак для строки состояния.</summary>
    private void SetRunning(bool running)
    {
        _running = running;
        _startButton.IsEnabled = !running;
        _stopButton.IsEnabled = running;
        _resetButton.IsEnabled = !running;
    }

    /// <summary>Проверка, что снимок температур совпадает по размерам с сеткой окна.</summary>
    private void CheckSnapshotShape(double[,,] temps)
    {
        ArgumentNullException.ThrowIfNull(temps);

        if (temps.GetLength(0) != _nx || temps.GetLength(1) != _ny || temps.GetLength(2) != _nz)
        {
            throw new ArgumentException(
                $"снимок температур имеет размерность "
                + $"[{temps.GetLength(0)}, {temps.GetLength(1)}, {temps.GetLength(2)}], "
                + $"а ожидалась [{_nx}, {_ny}, {_nz}]", nameof(temps));
        }
    }

    private void UpdateStatus(string text)
    {
        if (_closed)
        {
            return;
        }
        _statusText.Text = text;
    }

    // ---------- Форматирование чисел для вывода ----------

    /// <summary>
    /// Температура с числом знаков после запятой, подобранным по ширине диапазона:
    /// значение выводится целиком, без обрезки и без «...».
    /// </summary>
    private static string FormatTemperature(double value, double range)
    {
        double abs = Math.Abs(value);
        if (abs >= 1e5 || (abs > 0.0 && abs < 1e-3))
        {
            return value.ToString("0.###E+0", CultureInfo.CurrentCulture);
        }

        int decimals = range switch
        {
            >= 1000.0 => 0,
            >= 100.0 => 1,
            >= 10.0 => 2,
            >= 1.0 => 3,
            >= 0.1 => 4,
            _ => 5
        };

        return value.ToString("F" + decimals.ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture);
    }

    /// <summary>Диапазон температур в виде «T от A до B К» с согласованным округлением концов.</summary>
    private static string FormatTempRange(double min, double max)
    {
        double range = Math.Max(0.0, max - min);
        return $"T от {FormatTemperature(min, range)} до {FormatTemperature(max, range)} К";
    }

    /// <summary>Сумма |ΔT|: чем больше величина, тем меньше знаков после запятой.</summary>
    private static string FormatSigma(double value)
    {
        double abs = Math.Abs(value);
        if (abs >= 1e5 || (abs > 0.0 && abs < 1e-3))
        {
            return value.ToString("0.###E+0", CultureInfo.CurrentCulture);
        }
        if (abs >= 100.0)
        {
            return value.ToString("0", CultureInfo.CurrentCulture);
        }
        if (abs >= 1.0)
        {
            return value.ToString("0.##", CultureInfo.CurrentCulture);
        }
        return value.ToString("0.####", CultureInfo.CurrentCulture);
    }

    private static bool TryParseDouble(string? text, out double value)
    {
        value = 0.0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string normalized = text.Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static (double Min, double Max) FindRange(double[,,] values)
    {
        double min = double.MaxValue;
        double max = double.MinValue;

        foreach (double value in values)
        {
            if (double.IsNaN(value))
            {
                continue;
            }
            if (value < min) min = value;
            if (value > max) max = value;
        }

        if (min > max)
        {
            return (0.0, 1.0);
        }
        return (min, max);
    }

    private static (byte B, byte G, byte R) TemperatureToRgb(double temp, double minTemp, double maxTemp)
    {
        double range = maxTemp - minTemp;
        double t = range <= 0 ? 0.0 : Math.Clamp((temp - minTemp) / range, 0.0, 1.0);

        byte r, g, b;
        if (t < 0.25)
        {
            r = 0;
            g = (byte)(t / 0.25 * 255);
            b = 255;
        }
        else if (t < 0.5)
        {
            r = 0;
            g = 255;
            b = (byte)((0.5 - t) / 0.25 * 255);
        }
        else if (t < 0.75)
        {
            r = (byte)((t - 0.5) / 0.25 * 255);
            g = 255;
            b = 0;
        }
        else
        {
            r = 255;
            g = (byte)((1.0 - t) / 0.25 * 255);
            b = 0;
        }

        return (b, g, r);
    }
}

/// <summary>
/// График суммы модулей изменений температуры в ячейках:
/// по оси X — номер итерации (и соответствующее время), по оси Y — сумма |dT| по всем ячейкам.
/// </summary>
public sealed class SumDeltaChart : Control
{
    private readonly List<(int Iteration, double Time, double SumAbs)> _points = new();

    // Тёмное оформление графика: чёрный фон, белые подписи, яркая кривая
    private static readonly IBrush BackBrush = new SolidColorBrush(Colors.Black);
    private static readonly IBrush PlotBrush = new SolidColorBrush(Colors.Black);
    private static readonly IBrush TextBrush = Brushes.White;
    private static readonly IBrush CurveBrush = new SolidColorBrush(Color.Parse("#FFFF8A00"));
    private static readonly IPen AxisPen = new Pen(new SolidColorBrush(Color.Parse("#FFBDBDBD")), 1.2);
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#FF303030")), 1.0);
    private static readonly IPen CurvePen = new Pen(CurveBrush, 2.0);

    public SumDeltaChart()
    {
        MinHeight = 140;
    }

    /// <summary>Добавить точку графика (вызывается из UI-потока).</summary>
    public void AddPoint(int iteration, double time, double sumAbsDeltaT)
    {
        _points.Add((iteration, time, sumAbsDeltaT));
        InvalidateVisual();
    }

    /// <summary>Очистить график.</summary>
    public void Clear()
    {
        _points.Clear();
        InvalidateVisual();
    }

    /// <summary>Число точек на графике (используется автотестом).</summary>
    public int PointCount => _points.Count;

    /// <summary>Значение Σ|ΔT| в последней точке (используется автотестом).</summary>
    public double LastValue => _points.Count > 0 ? _points[^1].SumAbs : 0.0;

    /// <summary>Номер итерации в последней точке (используется автотестом).</summary>
    public int LastIteration => _points.Count > 0 ? _points[^1].Iteration : -1;

    /// <summary>Значение Σ|ΔT| в точке с индексом index (используется автотестом).</summary>
    public double ValueAt(int index) => index >= 0 && index < _points.Count ? _points[index].SumAbs : double.NaN;

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w < 80 || h < 80)
        {
            return;
        }

        context.FillRectangle(BackBrush, new Rect(0, 0, w, h), 1f);

        const double left = 92, right = 18, top = 22, bottom = 62;
        double plotWidth = w - left - right;
        double plotHeight = h - top - bottom;
        if (plotWidth < 20 || plotHeight < 20)
        {
            return;
        }

        var plot = new Rect(left, top, plotWidth, plotHeight);
        context.FillRectangle(PlotBrush, plot, 1f);

        double maxIteration = _points.Count > 0 ? Math.Max(1, _points[^1].Iteration) : 1;
        double maxTime = _points.Count > 0 ? _points[^1].Time : 0.0;

        double maxSum = 0.0;
        foreach (var point in _points)
        {
            maxSum = Math.Max(maxSum, point.SumAbs);
        }
        if (!(maxSum > 0.0) || double.IsNaN(maxSum) || double.IsInfinity(maxSum))
        {
            maxSum = 1.0;
        }

        const int ticks = 5;

        // Сетка и подписи по оси Y (сумма модулей изменений температуры)
        for (int i = 0; i <= ticks; i++)
        {
            double frac = (double)i / ticks;
            double y = plot.Bottom - frac * plotHeight;
            context.DrawLine(GridPen, new Point(plot.X, y), new Point(plot.Right, y));
            DrawTextRightMiddle(context, FormatNumber(maxSum * frac), new Point(plot.X - 6, y));
        }

        // Сетка и подписи по оси X (номер итерации и общее время)
        int lastIterationLabel = -1;
        for (int i = 0; i <= ticks; i++)
        {
            double frac = (double)i / ticks;
            double x = plot.X + frac * plotWidth;
            context.DrawLine(GridPen, new Point(x, plot.Y), new Point(x, plot.Bottom));

            if (_points.Count == 0)
            {
                continue;
            }

            int iterationLabel = (int)Math.Round(maxIteration * frac);
            if (iterationLabel != lastIterationLabel)
            {
                lastIterationLabel = iterationLabel;
                DrawTextCenterTop(context, iterationLabel.ToString(CultureInfo.CurrentCulture),
                    new Point(x, plot.Bottom + 4));
            }

            if (maxTime > 0.0)
            {
                DrawTextCenterTop(context, FormatNumber(maxTime * frac) + " с", new Point(x, plot.Bottom + 20));
            }
        }

        // Оси
        context.DrawLine(AxisPen, new Point(plot.X, plot.Bottom), new Point(plot.X, plot.Y));
        context.DrawLine(AxisPen, new Point(plot.X, plot.Bottom), new Point(plot.Right, plot.Bottom));

        // Кривая
        if (_points.Count >= 2)
        {
            var geometry = new StreamGeometry();
            using (StreamGeometryContext gc = geometry.Open())
            {
                gc.BeginFigure(ToPoint(_points[0], plot, maxIteration, maxSum), false);
                for (int i = 1; i < _points.Count; i++)
                {
                    gc.LineTo(ToPoint(_points[i], plot, maxIteration, maxSum), true);
                }
                gc.EndFigure(false);
            }
            context.DrawGeometry(null, CurvePen, geometry);
        }
        else if (_points.Count == 1)
        {
            context.DrawEllipse(CurveBrush, null, ToPoint(_points[0], plot, maxIteration, maxSum), 2.5, 2.5);
        }

        // Подписи осей и текущее значение
        DrawTextLeftTop(context, "Σ|ΔT|, К", new Point(6, 4));
        DrawTextLeftTop(context, "номер итерации (снизу — время)", new Point(plot.X + 2, plot.Bottom + 38));

        if (_points.Count > 0)
        {
            var last = _points[^1];
            DrawTextRightTop(context,
                $"итерация {last.Iteration}, t = {last.Time:0.###} с, Σ|ΔT| = {FormatNumber(last.SumAbs)} К",
                new Point(plot.Right, 3));
        }
        else
        {
            FormattedText hint = CreateText("нет данных — нажмите «Старт расчёта»");
            context.DrawText(hint, new Point(
                plot.X + (plot.Width - hint.Width) / 2.0,
                plot.Y + (plot.Height - hint.Height) / 2.0));
        }
    }

    private static Point ToPoint((int Iteration, double Time, double SumAbs) point,
        Rect plot, double maxIteration, double maxSum)
    {
        double x = plot.X + point.Iteration / maxIteration * plot.Width;
        double y = plot.Bottom - Math.Clamp(point.SumAbs / maxSum, 0.0, 1.0) * plot.Height;
        return new Point(x, y);
    }

    private static FormattedText CreateText(string text)
    {
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11.0, TextBrush);
    }

    private static void DrawTextLeftTop(DrawingContext context, string text, Point anchor)
    {
        context.DrawText(CreateText(text), anchor);
    }

    private static void DrawTextRightTop(DrawingContext context, string text, Point anchor)
    {
        FormattedText ft = CreateText(text);
        context.DrawText(ft, new Point(anchor.X - ft.Width, anchor.Y));
    }

    private static void DrawTextRightMiddle(DrawingContext context, string text, Point anchor)
    {
        FormattedText ft = CreateText(text);
        context.DrawText(ft, new Point(anchor.X - ft.Width, anchor.Y - ft.Height / 2.0));
    }

    private static void DrawTextCenterTop(DrawingContext context, string text, Point anchor)
    {
        FormattedText ft = CreateText(text);
        context.DrawText(ft, new Point(anchor.X - ft.Width / 2.0, anchor.Y));
    }

    private static string FormatNumber(double value)
    {
        double abs = Math.Abs(value);
        if (abs >= 1e5 || (abs > 0.0 && abs < 0.001))
        {
            return value.ToString("0.0E+0", CultureInfo.CurrentCulture);
        }
        if (abs >= 100.0)
        {
            return value.ToString("0", CultureInfo.CurrentCulture);
        }
        return value.ToString("0.###", CultureInfo.CurrentCulture);
    }
}

public class App : Application
{
    public override void Initialize()
    {
        // Тёмное оформление всего приложения: чёрный фон, белые подписи и элементы
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }
}
