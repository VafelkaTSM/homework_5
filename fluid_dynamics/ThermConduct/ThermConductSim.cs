namespace ThermConductSim;

/// <summary>
/// Параметры одного шага по времени: всё, что методу подсчёта теплопередачи нужно
/// знать про текущий шаг. Структура собирается один раз на расчёт и передаётся
/// в каждый вызов метода по ссылке (<c>in</c>) — без копирования в горячем цикле.
/// </summary>
public readonly struct CellStepContext
{
    /// <summary>Шаг по времени Δt одной итерации, с.</summary>
    public double DeltaTime { get; }

    /// <summary>Коэффициент температуропроводности a, м²/с.</summary>
    public double ThermalDiffusivity { get; }

    /// <summary>Шаг сетки по оси X, м.</summary>
    public double CellSizeX { get; }

    /// <summary>Шаг сетки по оси Y, м.</summary>
    public double CellSizeY { get; }

    /// <summary>Шаг сетки по оси Z, м.</summary>
    public double CellSizeZ { get; }

    /// <summary>Произведение a·Δt, м²: множитель при сумме вторых производных.</summary>
    public double ADt => ThermalDiffusivity * DeltaTime;

    public CellStepContext(double deltaTime, double thermalDiffusivity,
        double cellSizeX, double cellSizeY, double cellSizeZ)
    {
        DeltaTime = deltaTime;
        ThermalDiffusivity = thermalDiffusivity;
        CellSizeX = cellSizeX;
        CellSizeY = cellSizeY;
        CellSizeZ = cellSizeZ;
    }
}

/// <summary>
/// Метод подсчёта теплопередачи: один шаг по времени для одной ячейки.
/// Метод выбирается ключом из conditions.json и копируется в каждую ячейку
/// (словарь <see cref="ThermalFunctions.CellUpdateMethods"/>).
/// </summary>
/// <param name="cell">Ячейка, чью температуру нужно пересчитать (меняется прямо в делегате).</param>
/// <param name="step">Параметры шага: Δt, a и шаги сетки по осям.</param>
/// <param name="stepIndex">Номер шага с нуля в текущем расчёте (многошаговые методы используют его для разгона).</param>
/// <returns>Приращение температуры ячейки на этом шаге, К.</returns>
public delegate double CellUpdateDelegate(Cell cell, in CellStepContext step, int stepIndex);

/// <summary>
/// Начальное распределение температуры: функция координат центра ячейки, м (результат — К).
/// Выбирается ключом из conditions.json (словарь <see cref="ThermalFunctions.InitialTemperatures"/>).
/// </summary>
public delegate double InitialTemperatureDelegate(double x, double y, double z);

/// <summary>
/// Ячейка сетки: хранит свою настоящую температуру, температуру с предыдущего шага
/// (для многошаговых методов) и шесть ограничивающих её граней.
/// Грани лежат отдельными полями, а не в массиве: в горячем цикле это прямая
/// загрузка поля по фиксированному смещению — без промежуточного объекта, без проверки
/// границ и без индексной арифметики. Имена полей повторяют роли, которые знает
/// Face.PastCell/NextCell.
/// </summary>
public class Cell
{
    /// <summary>Температура в центре ячейки, К.</summary>
    public double Temp;

    /// <summary>Температура на предыдущем шаге по времени, К (используется многошаговыми методами).</summary>
    public double PrevTemp;

    /// <summary>
    /// Метод подсчёта теплопередачи — копия делегата из словаря ThermalFunctions.CellUpdateMethods
    /// по ключу из conditions.json. Сетка присваивает его каждой ячейке при создании.
    /// </summary>
    public CellUpdateDelegate UpdateMethod { get; set; } =
        ThermalFunctions.CellUpdateMethods["explicit"];

    /// <summary>
    /// Грань со стороны «past» по оси X (−нормаль). У ячейки x = 0 это внешняя грань куба,
    /// на которой работает граничное условие.
    /// </summary>
    public Face XPast { get; internal set; } = null!;

    /// <summary>Грань со стороны «next» по оси X (+нормаль); у ячейки x = X−1 — внешняя.</summary>
    public Face XNext { get; internal set; } = null!;

    /// <summary>Грань со стороны «past» по оси Y (−нормаль); у ячейки y = 0 — внешняя.</summary>
    public Face YPast { get; internal set; } = null!;

    /// <summary>Грань со стороны «next» по оси Y (+нормаль); у ячейки y = Y−1 — внешняя.</summary>
    public Face YNext { get; internal set; } = null!;

    /// <summary>Грань со стороны «past» по оси Z (−нормаль); у ячейки z = 0 — внешняя.</summary>
    public Face ZPast { get; internal set; } = null!;

    /// <summary>Грань со стороны «next» по оси Z (+нормаль); у ячейки z = Z−1 — внешняя.</summary>
    public Face ZNext { get; internal set; } = null!;

    /// <summary>
    /// Ячейка создаётся с одной температурой и без граней: грань хранит ссылки на соседние
    /// ячейки, поэтому создаётся позже и вписывается в поля сеткой (Mesh.CreateFaces).
    /// </summary>
    /// <param name="temp">Начальная температура ячейки, К.</param>
    public Cell(double temp = 0.0)
    {
        Temp = temp;
        PrevTemp = temp;
    }

    /// <summary>
    /// Один шаг по времени для ячейки: работу делает делегат <see cref="UpdateMethod"/>
    /// (метод подсчёта теплопередачи выбирается ключом из conditions.json).
    /// </summary>
    /// <param name="step">Параметры шага: Δt, a и шаги сетки по осям.</param>
    /// <param name="stepIndex">Номер шага с нуля в текущем расчёте.</param>
    /// <returns>Приращение температуры на этом шаге, К.</returns>
    public double UpdateT(in CellStepContext step, int stepIndex) =>
        UpdateMethod(this, in step, stepIndex);
}

/// <summary>
/// Грань ячейки. Хранит ссылки на две соседние ячейки (или на одну, если грань внешняя),
/// поэтому сама пересчитывает вторую производную температуры по нормали:
/// SecDerT = (T_next − T_past) / h², где h — шаг сетки по оси грани (хранится в грани).
/// У внешней грани куба недостающая температура восстанавливается из граничного условия
/// <see cref="Boundary"/> (1–3-го рода, возможно зависящего от времени): по температуре
/// стенки строится фиктивная ячейка-зеркало T_ghost = 2·T_стенки − T_ячейки.
/// </summary>
public class Face
{
    /// <summary>Ячейка со стороны «past» (−нормаль грани); null у внешней грани {x, y, z} = min.</summary>
    public readonly Cell? PastCell;

    /// <summary>Ячейка со стороны «next» (+нормаль грани); null у внешней грани {x, y, z} = max.</summary>
    public readonly Cell? NextCell;

    /// <summary>
    /// Граничное условие внешней грани (1–3-го рода). Осмысленно только на внешних
    /// гранях куба; у внутренних граней — null.
    /// </summary>
    public readonly BoundaryCondition? Boundary;

    /// <summary>(T_{i+1} − T_i) / h² вдоль нормали грани, К/м².</summary>
    public double SecDerT;

    /// <summary>Шаг сетки по оси грани h, м.</summary>
    private readonly double _cellSize;

    /// <summary>
    /// Внутренняя грань между двумя соседями. Обе ячейки уже должны существовать:
    /// грань ссылается на них и берёт их температуры при пересчёте SecDerT.
    /// </summary>
    /// <param name="pastCell">Ячейка со стороны «past» (−нормаль грани).</param>
    /// <param name="nextCell">Ячейка со стороны «next» (+нормаль грани).</param>
    /// <param name="cellSize">Шаг сетки по оси грани h, м.</param>
    public Face(Cell pastCell, Cell nextCell, double cellSize)
        : this(pastCell, nextCell, null, cellSize)
    {
    }

    /// <summary>
    /// Грань с граничным условием: одна из соседних ячеек отсутствует, а вместо её
    /// температуры при пересчёте SecDerT используется Boundary (возможно, зависящее от времени).
    /// </summary>
    /// <param name="pastCell">Ячейка со стороны «past»; null у внешней грани {x, y, z} = min.</param>
    /// <param name="nextCell">Ячейка со стороны «next»; null у внешней грани {x, y, z} = max.</param>
    /// <param name="boundary">Граничное условие (вид и функции значений).</param>
    /// <param name="cellSize">Шаг сетки по оси грани h, м.</param>
    public Face(Cell? pastCell, Cell? nextCell, BoundaryCondition? boundary, double cellSize)
    {
        if (pastCell == null && nextCell == null)
        {
            throw new ArgumentException("у грани должна быть хотя бы одна соседняя ячейка", nameof(pastCell));
        }
        if (!(cellSize > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), "cellSize <= 0");
        }

        PastCell = pastCell;
        NextCell = nextCell;
        Boundary = boundary;
        _cellSize = cellSize;
    }

    /// <summary>Грань внешняя: вместо одной из соседних ячеек работает граничное условие.</summary>
    public bool IsBoundary => Boundary != null;

    /// <summary>
    /// Пересчёт (T_{i+1} − T_i) / h² по текущим температурам соседних ячеек.
    /// На внешней грани температура соседней (фиктивной) ячейки восстанавливается как
    /// T_ghost = 2·T_стенки − T_cell, то есть SecDerT = 2·(T_cell − T_стенки) / h²
    /// со стороны min и SecDerT = 2·(T_стенки − T_cell) / h² со стороны max.
    /// Температура стенки берётся из граничного условия (1–3-го рода) по текущему времени.
    /// </summary>
    /// <param name="time">Текущее время расчёта, с (для ГУ, зависящих от времени).</param>
    /// <param name="thermalConductivity">Теплопроводность материала λ, Вт/(м·К) (нужна ГУ 2-го и 3-го рода).</param>
    public void UpdateSecDerT(double time, double thermalConductivity)
    {
        double h2 = _cellSize * _cellSize;

        if (PastCell != null && NextCell != null)
        {
            // Внутренняя грань: разность температур двух соседних ячеек.
            SecDerT = (NextCell.Temp - PastCell.Temp) / h2;
            return;
        }

        // Внешняя грань: температура стенки из ГУ → фиктивная ячейка-зеркало.
        Cell cell = (PastCell ?? NextCell)!;
        double wallTemp = Boundary!.WallTemperature(cell.Temp, time, thermalConductivity, _cellSize);

        if (PastCell != null)
        {
            // Внешняя грань {x, y, z} = max: соседняя ячейка есть только со стороны «past».
            SecDerT = 2.0 * (wallTemp - cell.Temp) / h2;
        }
        else
        {
            // Внешняя грань {x, y, z} = min: соседняя ячейка есть только со стороны «next».
            SecDerT = 2.0 * (cell.Temp - wallTemp) / h2;
        }
    }
}

/// <summary>
/// Обратный вызов для передачи данных из расчёта в графический интерфейс (AvaloniaUI).
/// Вызывается на том потоке, в котором выполняется расчёт, поэтому внутри обработчика
/// нужно сделать снимок данных (см. Mesh.TempSnapshot) и передать его в UI-поток.
/// </summary>
/// <param name="mesh">Сетка, по которой идёт расчёт.</param>
/// <param name="iteration">Номер выполненной итерации.</param>
/// <param name="time">Текущее время расчёта, с.</param>
/// <param name="laplacianSum">
/// Сумма по всем ячейкам сумм вторых производных температуры по осям (центральные
/// разности второго порядка), К/м²: отклонение процесса в данный момент от
/// установившегося (в стационаре ∇²T = 0 в каждой ячейке).
/// </param>
public delegate void CalcUpdateHandler(Mesh mesh, int iteration, double time, double laplacianSum);

/// <summary>
/// Куб из ячеек с гранями (ячейки — центры, грани — плоскости между ними).
/// Начальные и граничные условия задаются один раз при создании сетки: поле температур
/// массивом [X, Y, Z], шесть плоскостей граничных условий (1–3-го рода, константы или
/// функции времени), шаги сетки по осям, метод подсчёта теплопередачи — делегатом.
/// </summary>
public class Mesh
{
    /// <summary>Шаг сетки по умолчанию, м (если в conditions.json не задан ни size, ни cellSize).</summary>
    public const double DefaultCellSize = 0.001;

    /// <summary>Теплопроводность материала по умолчанию, Вт/(м·К) — ориентировочно сталь.</summary>
    public const double DefaultThermalConductivity = 45.0;

    readonly int _nx, _ny, _nz;
    readonly Cell[,,] _cellList;
    readonly Face[,,] _faceXList, _faceYList, _faceZList;

    /// <summary>Шаг сетки по оси X (расстояние между центрами соседних ячеек), м.</summary>
    public double CellSizeX { get; }

    /// <summary>Шаг сетки по оси Y, м.</summary>
    public double CellSizeY { get; }

    /// <summary>Шаг сетки по оси Z, м.</summary>
    public double CellSizeZ { get; }

    /// <summary>Наименьший из шагов сетки, м (используется для определения числа Куранта).</summary>
    public double MinCellSize { get; }

    /// <summary>Шаги сетки по осям [hx, hy, hz], м (копия, менять можно свободно).</summary>
    public double[] CellSizes => new[] { CellSizeX, CellSizeY, CellSizeZ };

    /// <summary>Теплопроводность материала λ, Вт/(м·К): нужна граничным условиям 2-го и 3-го рода.</summary>
    public double ThermalConductivity { get; }

    /// <summary>Текущее время расчёта, с.</summary>
    public double Time { get; private set; }

    /// <summary>Номер последней выполненной итерации.</summary>
    public int Iteration { get; private set; }

    /// <summary>Коэффициент температуропроводности a последнего расчёта, м²/с.</summary>
    public double ThermalDiffusivity { get; private set; }

    // Флаг остановки расчёта (меняется из другого потока — кнопка «Остановить» в интерфейсе)
    private volatile bool _stopRequested;
    private volatile bool _wasStopped;

    /// <summary>Последний расчёт был прерван вызовом StopCalc().</summary>
    public bool WasStopped => _wasStopped;

    /// <summary>
    /// Запросить остановку текущего расчёта. Безопасно вызывать из другого потока:
    /// цикл в StartCalc проверяет флаг перед каждой итерацией и выходит,
    /// сохранив уже посчитанные температуры, номер итерации и время.
    /// </summary>
    public void StopCalc()
    {
        _stopRequested = true;
    }

    /// <summary>
    /// Создание сетки X на Y на Z ячеек вместе с начальными и граничными условиями.
    /// Размерность сетки берётся из массива начальных температур; шаги сетки могут
    /// отличаться по осям (h = размер области / число ячеек, оба задаются в conditions.json).
    /// </summary>
    /// <param name="initTemp">Начальные температуры всех ячеек, размерность [X, Y, Z], К.</param>
    /// <param name="xPast">ГУ грани x = min для каждой ячейки плоскости, размерность [Y, Z].</param>
    /// <param name="xNext">ГУ грани x = max, размерность [Y, Z].</param>
    /// <param name="yPast">ГУ грани y = min, размерность [X, Z].</param>
    /// <param name="yNext">ГУ грани y = max, размерность [X, Z].</param>
    /// <param name="zPast">ГУ грани z = min, размерность [X, Y].</param>
    /// <param name="zNext">ГУ грани z = max, размерность [X, Y].</param>
    /// <param name="cellSizeX">Шаг сетки по оси X, м.</param>
    /// <param name="cellSizeY">Шаг сетки по оси Y, м.</param>
    /// <param name="cellSizeZ">Шаг сетки по оси Z, м.</param>
    /// <param name="updateMethod">Метод подсчёта теплопередачи: копируется в каждую ячейку.</param>
    /// <param name="thermalConductivity">Теплопроводность материала λ, Вт/(м·К), для ГУ 2-го и 3-го рода.</param>
    public Mesh(double[,,] initTemp,
        BoundaryCondition[,] xPast, BoundaryCondition[,] xNext,
        BoundaryCondition[,] yPast, BoundaryCondition[,] yNext,
        BoundaryCondition[,] zPast, BoundaryCondition[,] zNext,
        double cellSizeX, double cellSizeY, double cellSizeZ,
        CellUpdateDelegate updateMethod,
        double thermalConductivity = DefaultThermalConductivity)
    {
        if (initTemp == null)
        {
            throw new ArgumentNullException(nameof(initTemp));
        }

        _nx = initTemp.GetLength(0);
        _ny = initTemp.GetLength(1);
        _nz = initTemp.GetLength(2);

        if (_nx <= 0 || _ny <= 0 || _nz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initTemp), "initTemp задан не для всех осей сетки");
        }
        if (!(cellSizeX > 0.0) || !(cellSizeY > 0.0) || !(cellSizeZ > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(cellSizeX), "все шаги сетки должны быть > 0");
        }
        if (updateMethod == null)
        {
            throw new ArgumentNullException(nameof(updateMethod));
        }
        if (!(thermalConductivity > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(thermalConductivity), "thermalConductivity <= 0");
        }

        CheckShape(xPast, nameof(xPast), _ny, _nz);
        CheckShape(xNext, nameof(xNext), _ny, _nz);
        CheckShape(yPast, nameof(yPast), _nx, _nz);
        CheckShape(yNext, nameof(yNext), _nx, _nz);
        CheckShape(zPast, nameof(zPast), _nx, _ny);
        CheckShape(zNext, nameof(zNext), _nx, _ny);

        CellSizeX = cellSizeX;
        CellSizeY = cellSizeY;
        CellSizeZ = cellSizeZ;
        MinCellSize = Math.Min(cellSizeX, Math.Min(cellSizeY, cellSizeZ));
        ThermalConductivity = thermalConductivity;
        _cellList = new Cell[_nx, _ny, _nz];
        _faceXList = new Face[_nx + 1, _ny, _nz];
        _faceYList = new Face[_nx, _ny + 1, _nz];
        _faceZList = new Face[_nx, _ny, _nz + 1];

        // Ячейки создаются первыми: грань хранит ссылки на своих соседей
        // (Face.PastCell / Face.NextCell), поэтому её можно создать только после ячеек.
        CreateCells(initTemp, updateMethod);
        CreateFaces(xPast, xNext, yPast, yNext, zPast, zNext);
    }

    /// <summary>
    /// Создание всех граней сетки. Каждая грань сразу получает ссылки на соседние ячейки
    /// и попадает в их поля (XPast/XNext, YPast/YNext, ZPast/ZNext); у шести внешних
    /// плоскостей куба вместо одной из ячеек стоит граничное условие из соответствующей
    /// двумерной плоскости (в единичном экземпляре на всю грань или своё у каждой ячейки).
    /// </summary>
    private void CreateFaces(
        BoundaryCondition[,] xPast, BoundaryCondition[,] xNext,
        BoundaryCondition[,] yPast, BoundaryCondition[,] yNext,
        BoundaryCondition[,] zPast, BoundaryCondition[,] zNext)
    {
        for (int y = 0; y < _ny; y++)
        {
            for (int z = 0; z < _nz; z++)
            {
                // Внешние грани x = min / x = max: соседняя ячейка только с одной стороны.
                _faceXList[0, y, z] = CreateFace(null, _cellList[0, y, z], xPast[y, z], CellSizeX, 0);

                for (int x = 1; x < _nx; x++)
                {
                    _faceXList[x, y, z] = CreateFace(_cellList[x - 1, y, z], _cellList[x, y, z], null, CellSizeX, 0);
                }

                _faceXList[_nx, y, z] = CreateFace(_cellList[_nx - 1, y, z], null, xNext[y, z], CellSizeX, 0);
            }
        }

        for (int x = 0; x < _nx; x++)
        {
            for (int z = 0; z < _nz; z++)
            {
                _faceYList[x, 0, z] = CreateFace(null, _cellList[x, 0, z], yPast[x, z], CellSizeY, 1);

                for (int y = 1; y < _ny; y++)
                {
                    _faceYList[x, y, z] = CreateFace(_cellList[x, y - 1, z], _cellList[x, y, z], null, CellSizeY, 1);
                }

                _faceYList[x, _ny, z] = CreateFace(_cellList[x, _ny - 1, z], null, yNext[x, z], CellSizeY, 1);
            }
        }

        for (int x = 0; x < _nx; x++)
        {
            for (int y = 0; y < _ny; y++)
            {
                _faceZList[x, y, 0] = CreateFace(null, _cellList[x, y, 0], zPast[x, y], CellSizeZ, 2);

                for (int z = 1; z < _nz; z++)
                {
                    _faceZList[x, y, z] = CreateFace(_cellList[x, y, z - 1], _cellList[x, y, z], null, CellSizeZ, 2);
                }

                _faceZList[x, y, _nz] = CreateFace(_cellList[x, y, _nz - 1], null, zNext[x, y], CellSizeZ, 2);
            }
        }
    }

    /// <summary>
    /// Создание одной грани и её привязка к обеим ячейкам: грань становится «next»-гранью
    /// ячейки со стороны «past» и «past»-гранью ячейки со стороны «next».
    /// </summary>
    /// <param name="pastCell">Ячейка со стороны «past»; null у внешней грани {x, y, z} = min.</param>
    /// <param name="nextCell">Ячейка со стороны «next»; null у внешней грани {x, y, z} = max.</param>
    /// <param name="boundary">ГУ внешней грани (у внутренних — null).</param>
    /// <param name="cellSize">Шаг сетки по оси грани h, м.</param>
    /// <param name="axis">Ось грани: 0 — X, 1 — Y, 2 — Z.</param>
    /// <returns>Созданная грань.</returns>
    private static Face CreateFace(Cell? pastCell, Cell? nextCell, BoundaryCondition? boundary, double cellSize, int axis)
    {
        Face face = pastCell != null && nextCell != null
            ? new Face(pastCell, nextCell, cellSize)
            : new Face(pastCell, nextCell, boundary, cellSize);

        switch (axis)
        {
            case 0:
                if (pastCell != null)
                {
                    pastCell.XNext = face;
                }
                if (nextCell != null)
                {
                    nextCell.XPast = face;
                }
                break;

            case 1:
                if (pastCell != null)
                {
                    pastCell.YNext = face;
                }
                if (nextCell != null)
                {
                    nextCell.YPast = face;
                }
                break;

            case 2:
                if (pastCell != null)
                {
                    pastCell.ZNext = face;
                }
                if (nextCell != null)
                {
                    nextCell.ZPast = face;
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(axis), "ось должна быть 0 (X), 1 (Y) или 2 (Z)");
        }

        return face;
    }

    /// <summary>
    /// Создание ячеек: температура из начального условия и копия метода подсчёта
    /// теплопередачи (делегата из словаря по ключу из conditions.json).
    /// Грани вписывает CreateFaces — грань ссылается на соседние ячейки,
    /// поэтому ячейки должны существовать раньше граней.
    /// </summary>
    private void CreateCells(double[,,] initTemp, CellUpdateDelegate updateMethod)
    {
        for (int x = 0; x < _nx; x++)
        {
            for (int y = 0; y < _ny; y++)
            {
                for (int z = 0; z < _nz; z++)
                {
                    _cellList[x, y, z] = new Cell(initTemp[x, y, z]) { UpdateMethod = updateMethod };
                }
            }
        }
    }

    /// <summary>Размерность сетки {X, Y, Z} (копия, менять можно свободно).</summary>
    public int[] Shape() => new[] { _nx, _ny, _nz };

    /// <summary>
    /// Присвоение новых температур всем ячейкам (кнопка «Сброс» в интерфейсе).
    /// Граничные условия при этом не меняются — они заданы при создании сетки.
    /// </summary>
    /// <param name="temp">Массив температур размером ровно X на Y на Z.</param>
    public void SetTemps(double[,,] temp)
    {
        CheckShape(temp, nameof(temp), _nx, _ny, _nz);

        for (int x = 0; x < _nx; x++)
        {
            for (int y = 0; y < _ny; y++)
            {
                for (int z = 0; z < _nz; z++)
                {
                    _cellList[x, y, z].Temp = temp[x, y, z];
                    _cellList[x, y, z].PrevTemp = temp[x, y, z];
                }
            }
        }
    }

    /// <summary>Снимок температур всех ячеек (безопасно передавать в другой поток).</summary>
    public double[,,] TempSnapshot()
    {
        double[,,] copy = new double[_nx, _ny, _nz];
        for (int x = 0; x < _nx; x++)
        {
            for (int y = 0; y < _ny; y++)
            {
                for (int z = 0; z < _nz; z++)
                {
                    copy[x, y, z] = _cellList[x, y, z].Temp;
                }
            }
        }
        return copy;
    }

    /// <summary>
    /// Запуск расчёта: температуры ячеек на каждом шаге пересчитываются методом подсчёта
    /// теплопередачи (делегатом из conditions.json) по вторым производным на гранях,
    /// после чего грани обновляются по новым температурам. Граничные условия
    /// перевычисляются каждый шаг по текущему времени, поэтому могут зависеть от времени.
    /// </summary>
    /// <param name="numberIter">Количество итераций (шагов по времени).</param>
    /// <param name="deltaTime">Шаг по времени одной итерации, с.</param>
    /// <param name="thermalDiffusivity">Коэффициент температуропроводности a, м²/с
    /// (устойчивость явной схемы: a·Δt·(1/hx² + 1/hy² + 1/hz²) ≤ 1/2).</param>
    /// <param name="updateHandler">
    /// Ссылка на метод, через который в графический интерфейс (AvaloniaUI) передаются данные
    /// и обновляется отображение процесса в реальном времени. Можно передать null, если
    /// промежуточные данные не нужны.
    /// </param>
    /// <param name="updatePeriod">Через сколько итераций вызывать updateHandler (обновлять интерфейс).</param>
    public void StartCalc(int numberIter, double deltaTime, double thermalDiffusivity,
        CalcUpdateHandler? updateHandler, int updatePeriod)
    {
        if (numberIter < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numberIter), "numberIter < 0");
        }
        if (!(deltaTime > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(deltaTime), "deltaTime <= 0");
        }
        if (!(thermalDiffusivity > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(thermalDiffusivity), "thermalDiffusivity <= 0");
        }
        if (updatePeriod < 1)
        {
            updatePeriod = 1;
        }

        _stopRequested = false;
        _wasStopped = false;
        ThermalDiffusivity = thermalDiffusivity;
        Time = 0.0;
        Iteration = 0;

        var step = new CellStepContext(deltaTime, thermalDiffusivity, CellSizeX, CellSizeY, CellSizeZ);

        // Начальное состояние: вторые производные на гранях по начальным температурам.
        // Делегату передаётся Σ∇²T начального поля — отклонение от установившегося процесса.
        UpdateFaces();

        updateHandler?.Invoke(this, 0, Time, SumLaplacian());

        for (int iteration = 1; iteration <= numberIter; iteration++)
        {
            // Проверка запроса на остановку (кнопка «Остановить» в интерфейсе)
            if (_stopRequested)
            {
                _wasStopped = true;
                break;
            }

            // Ячейки считаются по граням состояния iteration − 1 (схема остаётся явной:
            // SecDerT граней за время обхода ячеек не меняются).
            UpdateCells(step, iteration - 1);

            Time = iteration * deltaTime;
            Iteration = iteration;

            // Грани по новым температурам: Σ∇²T ниже считается уже по тому состоянию,
            // которое в этот момент показывает интерфейс.
            UpdateFaces();

            if (updateHandler != null && (iteration % updatePeriod == 0 || iteration == numberIter))
            {
                updateHandler(this, Iteration, Time, SumLaplacian());
            }
        }
    }

    /// <summary>
    /// Пересчёт (T_{i+1} − T_i) / h² на всех гранях по текущим температурам ячеек
    /// и текущему времени (для ГУ, зависящих от времени). Каждая грань знает своих
    /// соседей (см. Face.UpdateSecDerT), поэтому достаточно пройти по всем трём
    /// массивам граней; на внешних гранях куба недостающая температура восстанавливается
    /// через граничное условие (фиктивная ячейка).
    /// </summary>
    private void UpdateFaces()
    {
        UpdateFaces(_faceXList);
        UpdateFaces(_faceYList);
        UpdateFaces(_faceZList);
    }

    /// <summary>Пересчёт SecDerT на всех гранях одной оси.</summary>
    private void UpdateFaces(Face[,,] faces)
    {
        foreach (Face face in faces)
        {
            face.UpdateSecDerT(Time, ThermalConductivity);
        }
    }

    /// <summary>Обновление температур всех ячеек выбранным методом (по SecDerT своих граней).</summary>
    private void UpdateCells(in CellStepContext step, int stepIndex)
    {
        foreach (Cell cell in _cellList)
        {
            cell.UpdateT(step, stepIndex);
        }
    }

    /// <summary>
    /// Сумма по всем ячейкам сумм вторых производных температуры по координатам, К/м².
    /// Каждая вторя производная — трёхточечная центральная разность второго порядка:
    /// XNext.SecDerT − XPast.SecDerT = (T_{i+1} − 2·T_i + T_{i−1}) / h² (то же по Y и Z
    /// со своими шагами); на внешних гранях недостающая температура восстанавливается
    /// из граничного условия через фиктивную ячейку-зеркало. В установившемся процессе
    /// ∇²T = 0 в каждой ячейке, поэтому сумма стремится к нулю — это мера отклонения
    /// текущего состояния от стационарного.
    /// </summary>
    private double SumLaplacian()
    {
        double sum = 0.0;
        foreach (Cell cell in _cellList)
        {
            sum += (cell.XNext.SecDerT - cell.XPast.SecDerT)
                 + (cell.YNext.SecDerT - cell.YPast.SecDerT)
                 + (cell.ZNext.SecDerT - cell.ZPast.SecDerT);
        }
        return sum;
    }

    /// <summary>Проверка, что двумерный массив совпадает по размерам с плоскостью сетки.</summary>
    private static void CheckShape(BoundaryCondition[,] plane, string name, int d0, int d1)
    {
        if (plane == null)
        {
            throw new ArgumentNullException(name);
        }
        if (plane.GetLength(0) != d0 || plane.GetLength(1) != d1)
        {
            throw new ArgumentException(
                $"{name} имеет размерность [{plane.GetLength(0)}, {plane.GetLength(1)}], " +
                $"а ожидалась [{d0}, {d1}]", name);
        }
    }

    /// <summary>Проверка, что массив температур совпадает по размерам с сеткой.</summary>
    private static void CheckShape(double[,,] temp, string name, int d0, int d1, int d2)
    {
        if (temp == null)
        {
            throw new ArgumentNullException(name);
        }
        if (temp.GetLength(0) != d0 || temp.GetLength(1) != d1 || temp.GetLength(2) != d2)
        {
            throw new ArgumentException(
                $"{name} имеет размерность [{temp.GetLength(0)}, {temp.GetLength(1)}, {temp.GetLength(2)}], " +
                $"а ожидалась [{d0}, {d1}, {d2}]", name);
        }
    }
}
