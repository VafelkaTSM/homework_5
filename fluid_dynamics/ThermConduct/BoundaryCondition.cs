namespace ThermConductSim;

/// <summary>
/// Вид граничного условия на внешней грани куба.
/// </summary>
public enum BoundaryKind
{
    /// <summary>1-й род (Дирихле): задана температура стенки.</summary>
    Dirichlet = 1,

    /// <summary>2-й род (Нейман): задан тепловой поток в тело, Вт/м² (0 — теплоизоляция).</summary>
    Neumann = 2,

    /// <summary>3-й род (Робин): теплообмен со средой, q = α·(T_среды − T_стенки).</summary>
    Robin = 3,
}

/// <summary>
/// Граничное условие одной внешней грани (или одной ячейки грани).
/// Хранит вид условия и одну или две функции времени: значения могут меняться
/// во время расчёта (функции — из словаря <see cref="ThermalFunctions.BoundaryConditions"/>,
/// константа — функция, возвращающая одно число).
///
/// Размерности значений:
/// <list type="bullet">
/// <item>Дирихле — температура стенки, К;</item>
/// <item>Нейман — тепловой поток в тело, Вт/м²;</item>
/// <item>Робин — температура среды, К, и коэффициент теплоотдачи α, Вт/(м²·К).</item>
/// </list>
///
/// Физика всех трёх видов сводится к температуре стенки (см. <see cref="WallTemperature"/>):
/// внешняя грань ядра восстанавливает по ней фиктивную ячейку-зеркало
/// T_ghost = 2·T_стенки − T_ячейки (подробности — в README).
/// </summary>
public sealed class BoundaryCondition
{
    /// <summary>Функции времени: 1 для Дирихле/Неймана, 2 для Робина (T_среды, α).</summary>
    private readonly Func<double, double>[] _values;

    /// <summary>Вид граничного условия.</summary>
    public BoundaryKind Kind { get; }

    private BoundaryCondition(BoundaryKind kind, params Func<double, double>[] values)
    {
        Kind = kind;
        _values = values;
    }

    /// <summary>ГУ 1-го рода: задана температура стенки, К (может зависеть от времени).</summary>
    public static BoundaryCondition Dirichlet(Func<double, double> temperature) =>
        new(BoundaryKind.Dirichlet, temperature);

    /// <summary>ГУ 2-го рода: задан тепловой поток в тело, Вт/м² (может зависеть от времени).</summary>
    public static BoundaryCondition Neumann(Func<double, double> heatFlux) =>
        new(BoundaryKind.Neumann, heatFlux);

    /// <summary>
    /// ГУ 3-го рода: теплообмен со средой — температура среды, К, и коэффициент
    /// теплоотдачи α, Вт/(м²·К) (оба могут зависеть от времени).
    /// </summary>
    public static BoundaryCondition Robin(Func<double, double> environmentTemperature,
        Func<double, double> heatTransferCoefficient) =>
        new(BoundaryKind.Robin, environmentTemperature, heatTransferCoefficient);

    /// <summary>
    /// Температура стенки при текущем состоянии: по ней грань строит фиктивную ячейку.
    /// <list type="bullet">
    /// <item>Дирихле: T_стенки задана напрямую;</item>
    /// <item>Нейман: поток q даёт скачок температуры «стенка ↔ центр ячейки» на h/2:
    /// T_стенки = T_ячейки + q·h/(2λ);</item>
    /// <item>Робин: из условия α(T_среды − T_стенки) = 2λ(T_стенки − T_ячейки)/h
    /// (поток в среду равен потоку к центру ячейки):
    /// T_стенки = (α·T_среды + (2λ/h)·T_ячейки) / (α + 2λ/h). При α → ∞ вырождается
    /// в Дирихле (T_стенки → T_среды), при α = 0 — в нулевой поток.</item>
    /// </list>
    /// </summary>
    /// <param name="cellTemp">Температура пристеночной ячейки, К.</param>
    /// <param name="time">Текущее время расчёта, с.</param>
    /// <param name="thermalConductivity">Теплопроводность материала λ, Вт/(м·К).</param>
    /// <param name="cellSize">Шаг сетки по оси грани h, м (центр ячейки на расстоянии h/2 от стенки).</param>
    public double WallTemperature(double cellTemp, double time, double thermalConductivity, double cellSize)
    {
        switch (Kind)
        {
            case BoundaryKind.Dirichlet:
                return _values[0](time);

            case BoundaryKind.Neumann:
                return cellTemp + _values[0](time) * cellSize / (2.0 * thermalConductivity);

            case BoundaryKind.Robin:
            {
                double environment = _values[0](time);
                double alpha = _values[1](time);
                double conduction = 2.0 * thermalConductivity / cellSize;
                return (alpha * environment + conduction * cellTemp) / (alpha + conduction);
            }

            default:
                throw new InvalidOperationException($"неизвестный вид граничного условия: {Kind}");
        }
    }
}
