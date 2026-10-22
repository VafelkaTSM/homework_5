using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using System.Runtime.InteropServices;
using ThermConductSim;

namespace ThermSliceViewer;

public class MainWindow : Window
{
    private readonly Mesh _mesh;
    private readonly Cell[,,] _cells;
    private readonly int _nx, _ny, _nz;

    // Компоненты интерфейса
    private readonly ComboBox _axisComboBox;
    private readonly Slider _sliceSlider;
    private readonly TextBlock _sliceInfoText;
    private readonly Image _sliceImageView;

    public MainWindow(Mesh mesh)
    {
        Title = "Визуализация срезов сетки теплопередачи";
        Width = 800;
        Height = 650;

        // 1. Инициализация сетки и распределения температуры
        _mesh = mesh;
        _cells = _mesh.CellList();
        int[] shape = _mesh.Shape();
        _nx = shape[0];
        _ny = shape[1];
        _nz = shape[2];

        // 2. Создание панели управления срезами
        var controlPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(10)
        };

        controlPanel.Children.Add(new TextBlock
        {
            Text = "Нормаль среза:",
            VerticalAlignment = VerticalAlignment.Center
        });

        // Выпадающий список для выбора направления нормали
        _axisComboBox = new ComboBox
        {
            ItemsSource = new[] { "Ось X (плоскость YZ)", "Ось Y (плоскость XZ)", "Ось Z (плоскость XY)" },
            SelectedIndex = 2,
            VerticalAlignment = VerticalAlignment.Center
        };
        controlPanel.Children.Add(_axisComboBox);

        controlPanel.Children.Add(new TextBlock
        {
            Text = "Индекс слоя:",
            VerticalAlignment = VerticalAlignment.Center
        });

        // Ползунок выбора номера среза вдоль выбранной оси
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

        _sliceInfoText = new TextBlock
        {
            Text = $"Срез {_sliceSlider.Value} / {_nz - 1}",
            VerticalAlignment = VerticalAlignment.Center
        };
        controlPanel.Children.Add(_sliceInfoText);

        // 3. Элемент Image для вывода пиксельной карты среза
        _sliceImageView = new Image
        {
            Stretch = Stretch.Uniform,
            Margin = new Thickness(10)
        };
        RenderOptions.SetBitmapInterpolationMode(_sliceImageView, BitmapInterpolationMode.None);

        // 4. Корневой контейнер компоновки DockPanel
        var rootDock = new DockPanel();
        DockPanel.SetDock(controlPanel, Dock.Top);
        rootDock.Children.Add(controlPanel);
        rootDock.Children.Add(_sliceImageView);

        Content = rootDock;

        // 5. Подписка на события пользовательского ввода
        _axisComboBox.SelectionChanged += (s, e) => OnAxisChanged();
        _sliceSlider.ValueChanged += (s, e) => UpdateSliceView();

        // Отрисовка первого кадра
        UpdateSliceView();
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
        int sliceIdx = (int)Math.Round(_sliceSlider.Value);
        int axis = _axisComboBox.SelectedIndex;

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
                        0 => _cells[sliceIdx, u, v].Temp,
                        1 => _cells[u, sliceIdx, v].Temp,
                        _ => _cells[u, v, sliceIdx].Temp
                    };

                    var (b, g, r) = TemperatureToRgb(temp, 0.0, 100.0);

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
    }

    private static (byte B, byte G, byte R) TemperatureToRgb(double temp, double minTemp, double maxTemp)
    {
        double t = Math.Clamp((temp - minTemp) / (maxTemp - minTemp), 0.0, 1.0);

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

public class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
}