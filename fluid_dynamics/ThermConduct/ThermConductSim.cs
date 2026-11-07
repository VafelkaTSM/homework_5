namespace ThermConductSim;

/// <summary>
/// Ячейка сетки: хранит только свою настоящую температуру и шесть ограничивающих её граней.
/// Грани лежат отдельными полями, а не в массиве: в горячем цикле (Cell.UpdateT) это прямая
/// загрузка поля по фиксированному смещению — без промежуточного объекта, без проверки границ
/// и без индексной арифметики. Имена полей повторяют роли, которые знает Face.PastCell/NextCell.
/// </summary>
public class Cell
{
    /// <summary>Температура в центре ячейки, К.</summary>
    public double Temp;

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
    }

    /// <summary>
    /// Один явный шаг по времени для уравнения dT/dt = a * (d2T/dx2 + d2T/dy2 + d2T/dz2).
    /// Так как C = a * dt / h^2, то dT = C * h^2 * ((SecDerT_x_next - SecDerT_x_past) + ... по y и z).
    /// Ячейка должна быть собрана сеткой: все шесть граней не null (это гарантирует Mesh).
    /// </summary>
    /// <param name="courantNumber">Число Куранта C = a * deltaTime / h^2.</param>
    /// <param name="cellSize">Расстояние между центрами соседних ячеек h, м.</param>
    /// <returns>Приращение температуры на этом шаге, К.</returns>
    public double UpdateT(double courantNumber, double cellSize)
    {
        double sumSecDer = (XNext.SecDerT - XPast.SecDerT)
                         + (YNext.SecDerT - YPast.SecDerT)
                         + (ZNext.SecDerT - ZPast.SecDerT);

        double deltaTemp = courantNumber * cellSize * cellSize * sumSecDer;
        Temp += deltaTemp;
        return deltaTemp;
    }
}

/// <summary>
/// Грань ячейки. Хранит ссылки на две соседние ячейки (или на одну, если грань внешняя),
/// поэтому сама пересчитывает вторую производную температуры по нормали:
/// SecDerT = (T_next - T_past) / h^2. У внешней грани куба недостающая температура
/// восстанавливается из граничного условия BoundaryTemp — числа, заданного при создании сетки.
/// </summary>
public class Face
{
    /// <summary>Ячейка со стороны «past» (−нормаль грани); null у внешней грани {x, y, z} = min.</summary>
    public readonly Cell? PastCell;

    /// <summary>Ячейка со стороны «next» (+нормаль грани); null у внешней грани {x, y, z} = max.</summary>
    public readonly Cell? NextCell;

    /// <summary>
    /// Температура грани из граничного условия, К. Осмысленна только на внешних гранях куба
    /// (у внутренних остаётся 0).
    /// </summary>
    public readonly double BoundaryTemp;

    /// <summary>(T_{i+1} - T_i) / h^2 вдоль нормали грани, К/м^2.</summary>
    public double SecDerT;

    /// <summary>
    /// Внутренняя грань между двумя соседями. Обе ячейки уже должны существовать:
    /// грань ссылается на них и берёт их температуры при пересчёте SecDerT.
    /// </summary>
    /// <param name="pastCell">Ячейка со стороны «past» (−нормаль грани).</param>
    /// <param name="nextCell">Ячейка со стороны «next» (+нормаль грани).</param>
    public Face(Cell pastCell, Cell nextCell) : this(pastCell, nextCell, 0.0)
    {
    }

    /// <summary>
    /// Грань с граничным условием: одна из соседних ячеек отсутствует, а вместо её
    /// температуры при пересчёте SecDerT берётся BoundaryTemp.
    /// </summary>
    /// <param name="pastCell">Ячейка со стороны «past»; null у внешней грани {x, y, z} = min.</param>
    /// <param name="nextCell">Ячейка со стороны «next»; null у внешней грани {x, y, z} = max.</param>
    /// <param name="boundaryTemp">Температура грани из граничного условия, К.</param>
    public Face(Cell? pastCell, Cell? nextCell, double boundaryTemp)
    {
        if (pastCell == null && nextCell == null)
        {
            throw new ArgumentException("у грани должна быть хотя бы одна соседняя ячейка", nameof(pastCell));
        }

        PastCell = pastCell;
        NextCell = nextCell;
        BoundaryTemp = boundaryTemp;
    }

    /// <summary>Грань внешняя: одной из соседних ячеек нет, вместо неё работает граничное условие.</summary>
    public bool IsBoundary => PastCell == null || NextCell == null;

    /// <summary>
    /// Пересчёт (T_{i+1} - T_i) / h^2 по текущим температурам соседних ячеек.
    /// На внешней грани температура соседней (фиктивной) ячейки восстанавливается как
    /// T_ghost = 2 * T_boundary - T_cell, то есть SecDerT = 2 * (T_cell - T_boundary) / h^2
    /// со стороны min и SecDerT = 2 * (T_boundary - T_cell) / h^2 со стороны max.
    /// </summary>
    /// <param name="cellSize">Расстояние между центрами соседних ячеек h, м.</param>
    public void UpdateSecDerT(double cellSize)
    {
        if (!(cellSize > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), "cellSize <= 0");
        }

        double h2 = cellSize * cellSize;

        if (PastCell != null && NextCell != null)
        {
            // Внутренняя грань: разность температур двух соседних ячеек.
            SecDerT = (NextCell.Temp - PastCell.Temp) / h2;
        }
        else if (PastCell != null)
        {
            // Внешняя грань {x, y, z} = max: соседняя ячейка есть только со стороны «past».
            SecDerT = 2.0 * (BoundaryTemp - PastCell.Temp) / h2;
        }
        else
        {
            // Внешняя грань {x, y, z} = min: соседняя ячейка есть только со стороны «next».
            SecDerT = 2.0 * (NextCell!.Temp - BoundaryTemp) / h2;
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
/// <param name="sumAbsDeltaT">Сумма модулей изменений температуры по всем ячейкам за итерацию, К.</param>
public delegate void CalcUpdateHandler(Mesh mesh, int iteration, double time, double sumAbsDeltaT);

/// <summary>
/// Куб из ячеек с гранями (ячейки — центры, грани — плоскости между ними).
/// Начальные и граничные условия задаются один раз при создании сетки двумерными и
/// трёхмерным массивами температур.
/// </summary>
public class Mesh
{
    /// <summary>Расстояние между центрами соседних ячеек по умолчанию, м.</summary>
    public const double DefaultCellSize = 0.001;

    readonly int _nx, _ny, _nz;
    readonly Cell[,,] _cellList;
    readonly Face[,,] _faceXList, _faceYList, _faceZList;

    /// <summary>Расстояние между центрами соседних ячеек, м.</summary>
    public double CellSize { get; }

    /// <summary>Текущее время расчёта, с.</summary>
    public double Time { get; private set; }

    /// <summary>Номер последней выполненной итерации.</summary>
    public int Iteration { get; private set; }

    /// <summary>Коэффициент температуропроводности a = C * h^2 / dt последнего расчёта, м^2/с.</summary>
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
    /// Размерность сетки берётся из массива начальных температур.
    /// </summary>
    /// <param name="initTemp">Начальные температуры всех ячеек, размерность [X, Y, Z], К.</param>
    /// <param name="xPast">Температура грани x = min для каждой ячейки плоскости, размерность [Y, Z], К.</param>
    /// <param name="xNext">Температура грани x = max, размерность [Y, Z], К.</param>
    /// <param name="yPast">Температура грани y = min, размерность [X, Z], К.</param>
    /// <param name="yNext">Температура грани y = max, размерность [X, Z], К.</param>
    /// <param name="zPast">Температура грани z = min, размерность [X, Y], К.</param>
    /// <param name="zNext">Температура грани z = max, размерность [X, Y], К.</param>
    /// <param name="cellSize">Расстояние между центрами соседних ячеек, м.</param>
    public Mesh(double[,,] initTemp,
        double[,] xPast, double[,] xNext,
        double[,] yPast, double[,] yNext,
        double[,] zPast, double[,] zNext,
        double cellSize = DefaultCellSize)
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
        if (cellSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), "cellSize <= 0");
        }

        CheckShape(xPast, nameof(xPast), _ny, _nz);
        CheckShape(xNext, nameof(xNext), _ny, _nz);
        CheckShape(yPast, nameof(yPast), _nx, _nz);
        CheckShape(yNext, nameof(yNext), _nx, _nz);
        CheckShape(zPast, nameof(zPast), _nx, _ny);
        CheckShape(zNext, nameof(zNext), _nx, _ny);

        CellSize = cellSize;
        _cellList = new Cell[_nx, _ny, _nz];
        _faceXList = new Face[_nx + 1, _ny, _nz];
        _faceYList = new Face[_nx, _ny + 1, _nz];
        _faceZList = new Face[_nx, _ny, _nz + 1];

        // Ячейки создаются первыми: грань хранит ссылки на своих соседей
        // (Face.PastCell / Face.NextCell), поэтому её можно создать только после ячеек.
        CreateCells(initTemp);
        CreateFaces(xPast, xNext, yPast, yNext, zPast, zNext);
    }

    /// <summary>
    /// Создание всех граней сетки. Каждая грань сразу получает ссылки на соседние ячейки
    /// и попадает в их поля (XPast/XNext, YPast/YNext, ZPast/ZNext); у шести внешних
    /// плоскостей куба вместо одной из ячеек стоит граничное условие — число из
    /// соответствующего двумерного массива.
    /// </summary>
    private void CreateFaces(
        double[,] xPast, double[,] xNext,
        double[,] yPast, double[,] yNext,
        double[,] zPast, double[,] zNext)
    {
        for (int y = 0; y < _ny; y++)
        {
            for (int z = 0; z < _nz; z++)
            {
                // Внешние грани x = min / x = max: соседняя ячейка только с одной стороны.
                _faceXList[0, y, z] = CreateFace(null, _cellList[0, y, z], xPast[y, z], 0);

                for (int x = 1; x < _nx; x++)
                {
                    _faceXList[x, y, z] = CreateFace(_cellList[x - 1, y, z], _cellList[x, y, z], 0.0, 0);
                }

                _faceXList[_nx, y, z] = CreateFace(_cellList[_nx - 1, y, z], null, xNext[y, z], 0);
            }
        }

        for (int x = 0; x < _nx; x++)
        {
            for (int z = 0; z < _nz; z++)
            {
                _faceYList[x, 0, z] = CreateFace(null, _cellList[x, 0, z], yPast[x, z], 1);

                for (int y = 1; y < _ny; y++)
                {
                    _faceYList[x, y, z] = CreateFace(_cellList[x, y - 1, z], _cellList[x, y, z], 0.0, 1);
                }

                _faceYList[x, _ny, z] = CreateFace(_cellList[x, _ny - 1, z], null, yNext[x, z], 1);
            }
        }

        for (int x = 0; x < _nx; x++)
        {
            for (int y = 0; y < _ny; y++)
            {
                _faceZList[x, y, 0] = CreateFace(null, _cellList[x, y, 0], zPast[x, y], 2);

                for (int z = 1; z < _nz; z++)
                {
                    _faceZList[x, y, z] = CreateFace(_cellList[x, y, z - 1], _cellList[x, y, z], 0.0, 2);
                }

                _faceZList[x, y, _nz] = CreateFace(_cellList[x, y, _nz - 1], null, zNext[x, y], 2);
            }
        }
    }

    /// <summary>
    /// Создание одной грани и её привязка к обеим ячейкам: грань становится «next»-гранью
    /// ячейки со стороны «past» и «past»-гранью ячейки со стороны «next».
    /// </summary>
    /// <param name="pastCell">Ячейка со стороны «past»; null у внешней грани {x, y, z} = min.</param>
    /// <param name="nextCell">Ячейка со стороны «next»; null у внешней грани {x, y, z} = max.</param>
    /// <param name="boundaryTemp">Температура грани из ГУ, К (у внутренних граней не используется).</param>
    /// <param name="axis">Ось грани: 0 — X, 1 — Y, 2 — Z.</param>
    /// <returns>Созданная грань.</returns>
    private static Face CreateFace(Cell? pastCell, Cell? nextCell, double boundaryTemp, int axis)
    {
        Face face = pastCell != null && nextCell != null
            ? new Face(pastCell, nextCell)
            : new Face(pastCell, nextCell, boundaryTemp);

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
    /// Создание ячеек: только температура из начального условия. Грани вписывает CreateFaces —
    /// грань ссылается на соседние ячейки, поэтому ячейки должны существовать раньше граней.
    /// </summary>
    private void CreateCells(double[,,] initTemp)
    {
        for (int x = 0; x < _nx; x++)
        {
            for (int y = 0; y < _ny; y++)
            {
                for (int z = 0; z < _nz; z++)
                {
                    _cellList[x, y, z] = new Cell(initTemp[x, y, z]);
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
    /// Запуск расчёта: на каждом шаге по текущим температурам ячеек пересчитываются
    /// вторые производные на гранях, затем температуры ячеек простым явным одношаговым
    /// методом dT/dt = a * (d2T/dx2 + d2T/dy2 + d2T/dz2). Граничные условия постоянны
    /// (заданы числами при создании сетки), поэтому от времени они не зависят.
    /// </summary>
    /// <param name="numberIter">Количество итераций (шагов по времени).</param>
    /// <param name="deltaTime">Шаг по времени одной итерации, с.</param>
    /// <param name="courantNumber">Число Куранта C = a * deltaTime / h^2 (устойчиво при C &lt;= 1/6).</param>
    /// <param name="updateHandler">
    /// Ссылка на метод, через который в графический интерфейс (AvaloniaUI) передаются данные
    /// и обновляется отображение процесса в реальном времени. Можно передать null, если
    /// промежуточные данные не нужны.
    /// </param>
    /// <param name="updatePeriod">Через сколько итераций вызывать updateHandler (обновлять интерфейс).</param>
    public void StartCalc(int numberIter, double deltaTime, double courantNumber,
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
        if (!(courantNumber > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(courantNumber), "courantNumber <= 0");
        }
        if (updatePeriod < 1)
        {
            updatePeriod = 1;
        }

        _stopRequested = false;
        _wasStopped = false;
        ThermalDiffusivity = courantNumber * CellSize * CellSize / deltaTime;
        Time = 0.0;
        Iteration = 0;

        // Начальное состояние: вторые производные на гранях по начальным температурам
        UpdateFaces();

        updateHandler?.Invoke(this, 0, Time, 0.0);

        double time = deltaTime;
        double maxTime = deltaTime * numberIter;
        while (time <= maxTime)
        {
            // Проверка запроса на остановку (кнопка «Остановить» в интерфейсе)
            if (_stopRequested)
            {
                _wasStopped = true;
                break;
            }

            UpdateFaces();
            double sumAbsDeltaT = UpdateCells(courantNumber);

            Time = time;
            Iteration = (int)(time / deltaTime);

            if (updateHandler != null && (Iteration % updatePeriod == 0 || Iteration == numberIter))
            {
                updateHandler(this, Iteration, Time, sumAbsDeltaT);
            }

            time += deltaTime;
        }
    }

    /// <summary>
    /// Пересчёт (T_{i+1} - T_i) / h^2 на всех гранях по текущим температурам ячеек.
    /// Каждая грань знает своих соседей (см. Face.UpdateSecDerT), поэтому достаточно
    /// пройти по всем трём массивам граней; на внешних гранях куба недостающая температура
    /// восстанавливается через граничное условие (фиктивная ячейка).
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
            face.UpdateSecDerT(CellSize);
        }
    }

    /// <summary>Обновление температур всех ячеек. Возвращает сумму модулей приращений, К.</summary>
    private double UpdateCells(double courantNumber)
    {
        double sumAbsDeltaT = 0.0;
        foreach (Cell cell in _cellList)
        {
            sumAbsDeltaT += Math.Abs(cell.UpdateT(courantNumber, CellSize));
        }
        return sumAbsDeltaT;
    }

    /// <summary>Проверка, что двумерный массив совпадает по размерам с плоскостью сетки.</summary>
    private static void CheckShape(double[,] plane, string name, int d0, int d1)
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

