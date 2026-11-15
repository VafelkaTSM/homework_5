namespace ThermConductSim;

/// <summary>
/// Библиотека функций условий эксперимента и методов пересчёта ячеек.
///
/// Все функции хранятся в словарях под строковыми ключами. JSON-файл условий
/// (conditions.json) ссылается на функцию только ключом; сама функция берётся
/// из словаря (и копируется — например, метод расчёта копируется в каждую ячейку).
/// Чтобы добавить свою функцию, достаточно дописать её в нужный словарь под
/// новым ключом — код загрузчика и ядра менять не нужно.
///
/// Три словаря:
/// <list type="bullet">
/// <item><see cref="BoundaryConditions"/> — функции времени <c>t → значение</c> для
/// граничных условий (температура стенки, тепловой поток, температура среды, α);</item>
/// <item><see cref="InitialTemperatures"/> — начальное распределение температуры
/// <c>T(x, y, z)</c>, координаты — в метрах, начало координат в углу куба;</item>
/// <item><see cref="CellUpdateMethods"/> — методы подсчёта теплопередачи: делегат
/// <see cref="CellUpdateDelegate"/>, вызываемый для каждой ячейки на каждом шаге.</item>
/// </list>
/// </summary>
public static class ThermalFunctions
{
    /// <summary>
    /// Функции граничных условий: аргумент — время расчёта, с; результат — значение, К
    /// (температура стенки / температура среды) или Вт/м² (тепловой поток / α — Вт/(м²·К)).
    /// </summary>
    public static readonly Dictionary<string, Func<double, double>> BoundaryConditions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["const_0"] = _ => 0.0,
        ["const_300"] = _ => 300.0,
        ["const_400"] = _ => 400.0,
        ["const_500"] = _ => 500.0,
        ["linear_heating"] = t => 300.0 + 10.0 * t,                 // нагрев: +10 К каждую секунду
        ["linear_cooling"] = t => 400.0 - 10.0 * t,                 // охлаждение: −10 К каждую секунду
        ["sin_pulse"] = t => 350.0 + 50.0 * Math.Sin(2.0 * Math.PI * t / 10.0), // импульс 300…400 К, период 10 с
        ["step_400"] = t => t < 5.0 ? 300.0 : 400.0,                // скачок до 400 К на 5-й секунде
    };

    /// <summary>
    /// Начальные распределения температуры: аргументы — координаты центра ячейки, м;
    /// результат — температура, К. Начало координат — угол куба {x, y, z} = min.
    /// </summary>
    public static readonly Dictionary<string, InitialTemperatureDelegate> InitialTemperatures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["uniform_300"] = (x, y, z) => 300.0,
        ["linear_gradient_x"] = (x, y, z) => 300.0 + 10000.0 * x,   // градиент +10 К на каждый мм по X
        ["hot_center"] = (x, y, z) =>                                // разогретый центр куба 16 мм
            300.0 + 200.0 * Math.Exp(-((x - 0.008) * (x - 0.008) + (y - 0.008) * (y - 0.008) + (z - 0.008) * (z - 0.008))
                                     / (2.0 * 0.004 * 0.004)),
    };

    /// <summary>
    /// Методы подсчёта теплопередачи: делегат копируется в каждую ячейку сетки
    /// по ключу из conditions.json и вызывается на каждом шаге по времени.
    /// </summary>
    public static readonly Dictionary<string, CellUpdateDelegate> CellUpdateMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        ["explicit"] = ExplicitUpdate,             // явная схема FTCS (устойчива при C ≤ 1/6 на равномерной сетке)
        ["dufort-frankel"] = DufortFrankelUpdate,  // явная трёхслойная схема Дюфорта–Франкела (устойчива при любом шаге)
    };

    /// <summary>
    /// Найти функцию граничного условия по ключу; ошибка перечисляет все доступные ключи.
    /// </summary>
    public static Func<double, double> GetBoundaryFunction(string key)
    {
        if (BoundaryConditions.TryGetValue(key, out Func<double, double>? function))
        {
            return function;
        }
        throw new InvalidDataException(
            $"неизвестная функция граничного условия \"{key}\"; доступные: {ListKeys(BoundaryConditions)}");
    }

    /// <summary>
    /// Найти начальное распределение температуры по ключу; ошибка перечисляет все доступные ключи.
    /// </summary>
    public static InitialTemperatureDelegate GetInitialTemperature(string key)
    {
        if (InitialTemperatures.TryGetValue(key, out InitialTemperatureDelegate? function))
        {
            return function;
        }
        throw new InvalidDataException(
            $"неизвестная функция начального распределения \"{key}\"; доступные: {ListKeys(InitialTemperatures)}");
    }

    /// <summary>
    /// Найти метод расчёта по ключу; ошибка перечисляет все доступные ключи.
    /// </summary>
    public static CellUpdateDelegate GetCellUpdateMethod(string key)
    {
        if (CellUpdateMethods.TryGetValue(key, out CellUpdateDelegate? method))
        {
            return method;
        }
        throw new InvalidDataException(
            $"неизвестный метод расчёта \"{key}\"; доступные: {ListKeys(CellUpdateMethods)}");
    }

    /// <summary>
    /// Явный одношаговый метод (FTCS): ΔT = a·dt·Σ (SecDerT_next − SecDerT_past)
    /// по трём осям. Условие устойчивости: a·dt·(1/hx² + 1/hy² + 1/hz²) ≤ 1/2
    /// (равномерная сетка — число Куранта C ≤ 1/6).
    /// </summary>
    public static double ExplicitUpdate(Cell cell, in CellStepContext step, int stepIndex)
    {
        double sumSecDer = (cell.XNext.SecDerT - cell.XPast.SecDerT)
                         + (cell.YNext.SecDerT - cell.YPast.SecDerT)
                         + (cell.ZNext.SecDerT - cell.ZPast.SecDerT);

        double deltaTemp = step.ADt * sumSecDer;
        cell.Temp += deltaTemp;
        return deltaTemp;
    }

    /// <summary>
    /// Трёхслойная явная схема Дюфорта–Франкела: устойчива при любом шаге по времени
    /// (в обмен на точность). Использует температуру с предыдущего шага (см. <see cref="Cell.PrevTemp"/>);
    /// первый шаг каждого расчёта (stepIndex = 0) считается явным методом — разгонная итерация.
    /// </summary>
    public static double DufortFrankelUpdate(Cell cell, in CellStepContext step, int stepIndex)
    {
        double sumSecDer = (cell.XNext.SecDerT - cell.XPast.SecDerT)
                         + (cell.YNext.SecDerT - cell.YPast.SecDerT)
                         + (cell.ZNext.SecDerT - cell.ZPast.SecDerT);

        if (stepIndex == 0)
        {
            // Разгонный шаг: T^{n-1} ещё нет, считаем явным методом Эйлера.
            double firstDelta = step.ADt * sumSecDer;
            cell.PrevTemp = cell.Temp;
            cell.Temp += firstDelta;
            return firstDelta;
        }

        double sumCourant = step.ADt * (1.0 / (step.CellSizeX * step.CellSizeX)
                                      + 1.0 / (step.CellSizeY * step.CellSizeY)
                                      + 1.0 / (step.CellSizeZ * step.CellSizeZ));

        double nextTemp = (2.0 * step.ADt * sumSecDer
                         + 4.0 * sumCourant * cell.Temp
                         + (1.0 - 2.0 * sumCourant) * cell.PrevTemp)
                        / (1.0 + 2.0 * sumCourant);

        double deltaTemp = nextTemp - cell.Temp;
        cell.PrevTemp = cell.Temp;
        cell.Temp = nextTemp;
        return deltaTemp;
    }

    /// <summary>Список ключей словаря через запятую — для сообщений об ошибках.</summary>
    private static string ListKeys<TValue>(Dictionary<string, TValue> dictionary) =>
        string.Join(", ", dictionary.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase));
}
