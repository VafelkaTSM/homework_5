using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using SodaTest;

internal static class Smoke
{
    private static Exception? _lastError;

    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Console.WriteLine("main thread=" + Environment.CurrentManagedThreadId.ToString());
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (s, e) => _lastError = e.Exception;

        try
        {
            var form = new GUI();
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(40, 40);
            form.Size = new Size(1280, 820);
            form.Show();
            Application.DoEvents();

            // Как в реальном приложении (Application.Run): контекст синхронизации
            // нужен, чтобы Progress<SolutionState> доставлял снимки в поток интерфейса.
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

            Render(form, "gui_initial.png");

            var startButton = Field<Button>(form, "_startButton");
            var resetButton = Field<Button>(form, "_resetButton");
            var keyDown = typeof(GUI).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var progressLabel = Field<ToolStripStatusLabel>(form, "_progressLabel");
            var statusLabel = Field<ToolStripStatusLabel>(form, "_statusLabel");

            var history = new System.Collections.Generic.List<string>();
            progressLabel.TextChanged += (s, e) => history.Add("P: " + progressLabel.Text);
            statusLabel.TextChanged += (s, e) => history.Add("S: " + statusLabel.Text);

            // --- Сценарий 1: полный расчёт, запуск по F5 ---
            SetInput(form, "_iterationsInput", 2400);
            SetInput(form, "_cellsInput", 1000);
            SetInput(form, "_updateEveryInput", 50);

            PressKey(keyDown, form, Keys.F5);
            Console.WriteLine(WaitForIdle(form, 900) ? "run 1: finished" : "run 1: still running (timeout)");
            Pump(30);
            Console.WriteLine("run 1 status: " + statusLabel.Text);
            Console.WriteLine("run 1 progress: " + progressLabel.Text);
            Render(form, "gui_final.png");

            // --- Сценарий 2: остановка по Esc ---
            SetInput(form, "_iterationsInput", 20000);
            SetInput(form, "_updateEveryInput", 500);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            startButton.PerformClick();
            Pump(10);
            Console.WriteLine("run 2 running: " + IsRunning(form).ToString()
                + ", elapsed " + watch.ElapsedMilliseconds.ToString() + " ms");
            Console.WriteLine("run 2 mid status: " + statusLabel.Text);
            PressKey(keyDown, form, Keys.Escape);
            Console.WriteLine(WaitForIdle(form, 300) ? "run 2: stopped" : "run 2: still running (timeout)");
            Console.WriteLine("run 2 immediately: " + progressLabel.Text);
            Pump(5);
            Console.WriteLine("run 2 +100ms: " + progressLabel.Text);
            Pump(25);
            Console.WriteLine("run 2 status: " + statusLabel.Text);
            Console.WriteLine("run 2 progress: " + progressLabel.Text);

            // --- Сценарий 3: сброс к начальным условиям ---
            resetButton.PerformClick();
            Pump(5);
            Console.WriteLine("reset status: " + statusLabel.Text);
            Console.WriteLine("reset progress: " + progressLabel.Text);
            Render(form, "gui_reset.png");

            Console.WriteLine("label history (" + history.Count + "):");
            for (int i = 0; i < history.Count; i++)
                Console.WriteLine(i.ToString("000") + " " + history[i]);

            form.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAILED: " + ex);
        }

        Console.WriteLine(_lastError is null ? "no UI-thread exceptions" : "UI exception: " + _lastError);
    }

    private static T Field<T>(GUI form, string name) where T : class
    {
        return (T)typeof(GUI).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
    }

    private static bool IsRunning(GUI form)
    {
        return (bool)typeof(GUI).GetField("_isRunning", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
    }

    private static void SetInput(GUI form, string field, decimal value)
    {
        var input = (NumericUpDown)typeof(GUI).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        input.Value = value;
    }

    private static void PressKey(MethodInfo onKeyDown, GUI form, Keys key)
    {
        onKeyDown.Invoke(form, new object?[] { new KeyEventArgs(key) });
    }

    private static void Pump(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
    }

    private static bool WaitForIdle(GUI form, int maxTicks)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            if (!IsRunning(form)) return true;
            Application.DoEvents();
            Thread.Sleep(20);
        }

        return !IsRunning(form);
    }

    private static void Render(Form form, string file)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
        bitmap.Save(file);
        Console.WriteLine("saved " + file);
    }
}

