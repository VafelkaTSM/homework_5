namespace ThermConductSim;

/// <summary>
/// Ячейка сетки: хранит температуру и шесть ограничивающих её граней.
/// </summary>
public class Cell
{
    /// <summary>Грани ячейки в порядке {x_past, x_next, y_past, y_next, z_past, z_next}.</summary>
    public Face[] Faces = new Face[6];

    /// <summary>Температура в центре ячейки, К.</summary>
    public double Temp;

    /// <summary>Расстояние между центрами соседних ячеек, м (выставляется сеткой).</summary>
    public double CellSize = Mesh.DefaultCellSize;

    /// <summary>Число Куранта C = a * deltaTime / h^2 (выставляется при старте расчёта).</summary>
    public double CourantNumber = 0.1;

    /// <summary>Приращение температуры, полученное на последнем шаге по времени, К.</summary>
    public double DeltaTemp;

    public Cell(Face[]? faces = null, double temp = 0)
    {
        if (faces != null && faces.Length != Faces.Length)
        {
            throw new ArgumentOutOfRangeException($"faces is not null and don't has a length equal to {Faces.Length}");
        }

        if (faces != null)
        {
            System.Array.Copy(faces, Faces, Faces.Length);
        }
        Temp = temp;
    }

    /// <summary>
    /// Один явный шаг по времени для уравнения dT/dt = a * (d2T/dx2 + d2T/dy2 + d2T/dz2).
    /// Так как C = a * dt / h^2, то dT = C * h^2 * ((SecDerT_x_next - SecDerT_x_past) + ... по y и z).
    /// </summary>
    /// <returns>Приращение температуры на этом шаге, К.</returns>
    public double UpdateT()
    {
        double sumSecDer = 0.0;

        for (int axis = 0; axis < 3; axis++)
        {
            Face past = Faces[axis * 2];
            Face next = Faces[axis * 2 + 1];
            sumSecDer += next.SecDerT - past.SecDerT;
        }

        DeltaTemp = CourantNumber * CellSize * CellSize * sumSecDer;
        Temp += DeltaTemp;
        return DeltaTemp;
    }
}

/// <summary>
/// Грань ячейки: хранит вторую производную температуры по нормали к грани.
/// </summary>
public class Face
{
    /// <summary>Соседние ячейки в порядке {past, next}. У граничной грани одна из них равна null.</summary>
    public Cell[] Cells = new Cell[2]; // {past, next}

    /// <summary>Признак граничной грани (грань лежит на внешней поверхности куба).</summary>
    public bool End;

    /// <summary>d^2T/dx^2 вдоль нормали грани (ось x, y или z — смотря как ориентирована грань), К/м^2.</summary>
    public double SecDerT;

    /// <summary>Температура на грани (для граничных граней задаётся граничным условием), К.</summary>
    public double BoundaryTemp;

    /// <summary>Граничное условие: время t [с] -> температура на грани [К].</summary>
    public Func<double, double>? BoundaryFunc;

    /// <summary>Расстояние между центрами соседних ячеек, м.</summary>
    public double CellSize = Mesh.DefaultCellSize;

    public Face(Cell[]? cells = null, bool end = false)
    {
        if (cells != null && cells.Length != 2)
        {
            throw new ArgumentOutOfRangeException($"cells is not null and don't has a length equal to {Cells.Length}");
        }

        if (cells != null)
        {
            System.Array.Copy(cells, Cells, Cells.Length);
        }
        End = end;
    }

    /// <summary>
    /// Пересчёт второй производной по нормали грани SecDerT по текущим температурам ячеек.
    /// На граничной грани недостающая температура восстанавливается через «призрачную» ячейку:
    /// T_ghost = 2 * T_boundary - T_cell (температура на грани — среднее между центром ячейки и призрачной).
    /// </summary>
    /// <returns>Новое значение SecDerT.</returns>
    public double updateSecDerT()
    {
        Cell? past = Cells[0];
        Cell? next = Cells[1];

        if (past == null && next == null)
        {
            SecDerT = 0.0;
            return SecDerT;
        }

        double tempPast = past != null ? past.Temp : 2.0 * BoundaryTemp - next!.Temp;
        double tempNext = next != null ? next.Temp : 2.0 * BoundaryTemp - past!.Temp;

        SecDerT = (tempNext - tempPast) / (CellSize * CellSize);
        return SecDerT;
    }

    /// <summary>Обновление температуры грани по граничному условию в момент времени time.</summary>
    public void UpdateBoundaryTemp(double time)
    {
        if (End && BoundaryFunc != null)
        {
            BoundaryTemp = BoundaryFunc(time);
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
/// </summary>
public class Mesh
{
    /// <summary>Расстояние между центрами соседних ячеек по умолчанию, м.</summary>
    public const double DefaultCellSize = 0.001;

    int[] _shape;
    Cell[,,] _cellList;
    Face[,,] _faceXList, _faceYList, _faceZList;
    readonly List<Face> _boundaryFaces = new();

    /// <summary>Расстояние между центрами соседних ячеек, м.</summary>
    public double CellSize = DefaultCellSize;

    /// <summary>Текущее время расчёта, с.</summary>
    public double Time { get; private set; }

    /// <summary>Номер последней выполненной итерации.</summary>
    public int Iteration { get; private set; }

    /// <summary>Коэффициент температуропроводности a = C * h^2 / dt последнего расчёта, м^2/с.</summary>
    public double ThermalDiffusivity { get; private set; }

    // Флаги остановки расчёта (меняются из другого потока — кнопка «Остановить» в интерфейсе)
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

    public int[] Shape()
    {
        int[] copy = new int[_shape.Length];
        System.Array.Copy(_shape, copy, _shape.Length);
        return copy;
    }
    public Cell[,,] CellList()
    {
        return (Cell[,,])_cellList.Clone();
    }
    public Face[,,] FaceXList()
    {
        return (Face[,,])_faceXList.Clone();
    }
    public Face[,,] FaceYList()
    {
        return (Face[,,])_faceYList.Clone();
    }
    public Face[,,] FaceZList()
    {
        return (Face[,,])_faceZList.Clone();
    }

    /// <summary>Снимок температур всех ячеек (безопасно передавать в другой поток).</summary>
    public double[,,] TempSnapshot()
    {
        double[,,] copy = new double[_shape[0], _shape[1], _shape[2]];
        for (int x = 0; x < _shape[0]; x++)
        {
            for (int y = 0; y < _shape[1]; y++)
            {
                for (int z = 0; z < _shape[2]; z++)
                {
                    copy[x, y, z] = _cellList[x, y, z].Temp;
                }
            }
        }
        return copy;
    }

    /// <summary>Сумма модулей приращений температуры по всем ячейкам от последней итерации, К.</summary>
    public double SumAbsDeltaT()
    {
        double sum = 0.0;
        foreach (Cell cell in _cellList)
        {
            sum += Math.Abs(cell.DeltaTemp);
        }
        return sum;
    }

    /// <summary>
    /// Создание кубической (прямоугольной) сетки X на Y на Z ячеек.
    /// </summary>
    /// <param name="X">Число ячеек вдоль оси X.</param>
    /// <param name="Y">Число ячеек вдоль оси Y.</param>
    /// <param name="Z">Число ячеек вдоль оси Z.</param>
    /// <param name="cellSize">Расстояние между центрами соседних ячеек, м.</param>
    public Mesh(int X, int Y, int Z, double cellSize = DefaultCellSize)
    {
        if (X <= 0 || Y <= 0 || Z <= 0)
        {
            throw new ArgumentOutOfRangeException("X or Y or Z <= 0");
        }
        if (cellSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), "cellSize <= 0");
        }

        CellSize = cellSize;
        _shape = new int[] { X, Y, Z };
        _cellList = new Cell[X, Y, Z];
        _faceXList = new Face[X + 1, Y, Z];
        _faceYList = new Face[X, Y + 1, Z];
        _faceZList = new Face[X, Y, Z + 1];

        for (int y = 0; y < Y; y++)
        {
            for (int z = 0; z < Z; z++)
            {
                _faceXList[0, y, z] = new Face(null, true);
            }
        }
        for (int x = 0; x < X; x++)
        {
            for (int z = 0; z < Z; z++)
            {
                _faceYList[x, 0, z] = new Face(null, true);
            }
        }
        for (int x = 0; x < X; x++)
        {
            for (int y = 0; y < Y; y++)
            {
                _faceZList[x, y, 0] = new Face(null, true);
            }
        }


        for (int x = 0; x < X; x++)
        {
            for (int y = 0; y < Y; y++)
            {
                for (int z = 0; z < Z; z++)
                {
                    if (x != 0 && y != 0 && z != 0)
                    {
                        continue;
                    }

                    Face faceX = (x == 0) ? _faceXList[x, y, z] : CreateFaceX(x, y, z);
                    Face faceY = (y == 0) ? _faceYList[x, y, z] : CreateFaceY(x, y, z);
                    Face faceZ = (z == 0) ? _faceZList[x, y, z] : CreateFaceZ(x, y, z);

                    CreateCell(x, y, z, faceX, faceY, faceZ);
                }
            }
        }

        for (int x = 1; x < X; x++)
        {
            for (int y = 1; y < Y; y++)
            {
                for (int z = 1; z < Z; z++)
                {
                    CreateCell(x, y, z, CreateFaceX(x, y, z), CreateFaceY(x, y, z), CreateFaceZ(x, y, z));
                }
            }
        }

        for (int y = 0; y < Y; y++)
        {
            for (int z = 0; z < Z; z++)
            {
                _faceXList[X, y, z] = CreateFaceX(X, y, z, true);
            }
        }
        for (int x = 0; x < X; x++)
        {
            for (int z = 0; z < Z; z++)
            {
                _faceYList[x, Y, z] = CreateFaceY(x, Y, z, true);
            }
        }
        for (int x = 0; x < X; x++)
        {
            for (int y = 0; y < Y; y++)
            {
                _faceZList[x, y, Z] = CreateFaceZ(x, y, Z, true);
            }
        }

        CollectBoundaryFaces();
        ApplyCellSize();
    }

    /// <summary>
    /// Инициализация начальных условий: одинаковая температура во всех ячейках.
    /// </summary>
    /// <param name="temp">Температура, К.</param>
    public void InitCond(double temp)
    {
        for (int x = 0; x < _shape[0]; x++)
        {
            for (int y = 0; y < _shape[1]; y++)
            {
                for (int z = 0; z < _shape[2]; z++)
                {
                    _cellList[x, y, z].Temp = temp;
                }
            }
        }
    }

    /// <summary>
    /// Инициализация начальных условий из массива температур.
    /// </summary>
    /// <param name="temp">Массив температур размером ровно X на Y на Z.</param>
    public void InitCond(double[,,] temp)
    {
        CheckShape(temp, nameof(temp));

        for (int x = 0; x < _shape[0]; x++)
        {
            for (int y = 0; y < _shape[1]; y++)
            {
                for (int z = 0; z < _shape[2]; z++)
                {
                    _cellList[x, y, z].Temp = temp[x, y, z];
                }
            }
        }
    }

    /// <summary>
    /// Инициализация граничных условий одной функцией на каждую грань куба.
    /// Функция получает текущее время расчёта t [с] и возвращает температуру грани [К],
    /// одна и та же функция применяется ко всей плоскости грани.
    /// </summary>
    public void BoundaryCond(
        Func<double, double> xPast, Func<double, double> xNext,
        Func<double, double> yPast, Func<double, double> yNext,
        Func<double, double> zPast, Func<double, double> zNext)
    {
        int X = _shape[0], Y = _shape[1], Z = _shape[2];

        for (int y = 0; y < Y; y++)
        {
            for (int z = 0; z < Z; z++)
            {
                _faceXList[0, y, z].BoundaryFunc = xPast;
                _faceXList[X, y, z].BoundaryFunc = xNext;
            }
        }
        for (int x = 0; x < X; x++)
        {
            for (int z = 0; z < Z; z++)
            {
                _faceYList[x, 0, z].BoundaryFunc = yPast;
                _faceYList[x, Y, z].BoundaryFunc = yNext;
            }
        }
        for (int x = 0; x < X; x++)
        {
            for (int y = 0; y < Y; y++)
            {
                _faceZList[x, y, 0].BoundaryFunc = zPast;
                _faceZList[x, y, Z].BoundaryFunc = zNext;
            }
        }
    }

    /// <summary>
    /// Инициализация граничных условий массивами функций: своя функция для каждой грани каждой ячейки плоскости.
    /// Размерности массивов: xPast и xNext — [Y, Z], yPast и yNext — [X, Z], zPast и zNext — [X, Y].
    /// Элемент массива может быть null — тогда у соответствующей грани остаётся прежнее условие.
    /// </summary>
    public void BoundaryCond(
        Func<double, double>[,] xPast, Func<double, double>[,] xNext,
        Func<double, double>[,] yPast, Func<double, double>[,] yNext,
        Func<double, double>[,] zPast, Func<double, double>[,] zNext)
    {
        int X = _shape[0], Y = _shape[1], Z = _shape[2];

        CheckShape(xPast, nameof(xPast), Y, Z);
        CheckShape(xNext, nameof(xNext), Y, Z);
        CheckShape(yPast, nameof(yPast), X, Z);
        CheckShape(yNext, nameof(yNext), X, Z);
        CheckShape(zPast, nameof(zPast), X, Y);
        CheckShape(zNext, nameof(zNext), X, Y);

        for (int y = 0; y < Y; y++)
        {
            for (int z = 0; z < Z; z++)
            {
                _faceXList[0, y, z].BoundaryFunc = xPast[y, z] ?? _faceXList[0, y, z].BoundaryFunc;
                _faceXList[X, y, z].BoundaryFunc = xNext[y, z] ?? _faceXList[X, y, z].BoundaryFunc;
            }
        }
        for (int x = 0; x < X; x++)
        {
            for (int z = 0; z < Z; z++)
            {
                _faceYList[x, 0, z].BoundaryFunc = yPast[x, z] ?? _faceYList[x, 0, z].BoundaryFunc;
                _faceYList[x, Y, z].BoundaryFunc = yNext[x, z] ?? _faceYList[x, Y, z].BoundaryFunc;
            }
        }
        for (int x = 0; x < X; x++)
        {
            for (int y = 0; y < Y; y++)
            {
                _faceZList[x, y, 0].BoundaryFunc = zPast[x, y] ?? _faceZList[x, y, 0].BoundaryFunc;
                _faceZList[x, y, Z].BoundaryFunc = zNext[x, y] ?? _faceZList[x, y, Z].BoundaryFunc;
            }
        }
    }

    /// <summary>
    /// Запуск расчёта: на каждом шаге сначала обновляются границы, затем вторые производные на гранях,
    /// затем температуры ячеек простым явным одношаговым методом
    /// dT/dt = a * (d2T/dx2 + d2T/dy2 + d2T/dz2).
    /// </summary>
    /// <param name="numberIter">Количество итераций (шагов по времени).</param>
    /// <param name="deltaTime">Шаг по времени одной итерации, с.</param>
    /// <param name="CourantNumber">Число Куранта C = a * deltaTime / h^2 (настраиваемое, устойчиво при C &lt;= 1/6).</param>
    /// <param name="updateHandler">
    /// Ссылка на метод, через который в графический интерфейс (AvaloniaUI) передаются данные
    /// и обновляется отображение процесса в реальном времени. Можно передать null, если
    /// промежуточные данные не нужны.
    /// </param>
    /// <param name="UpdatePeriod">Через сколько итераций вызывать updateHandler (обновлять интерфейс).</param>
    public void StartCalc(int numberIter, double deltaTime, double CourantNumber,
        CalcUpdateHandler? updateHandler, int UpdatePeriod)
    {
        if (numberIter < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numberIter), "numberIter < 0");
        }
        if (!(deltaTime > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(deltaTime), "deltaTime <= 0");
        }
        if (!(CourantNumber > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(CourantNumber), "CourantNumber <= 0");
        }
        if (UpdatePeriod < 1)
        {
            UpdatePeriod = 1;
        }

        ApplyCellSize();
        ResetDeltaTemp();

        _stopRequested = false;
        _wasStopped = false;

        foreach (Cell cell in _cellList)
        {
            cell.CourantNumber = CourantNumber;
        }

        ThermalDiffusivity = CourantNumber * CellSize * CellSize / deltaTime;
        Time = 0.0;
        Iteration = 0;

        // Начальное состояние: границы на момент времени t = 0 и вторые производные на гранях
        UpdateBoundaries(Time);
        UpdateFaces();

        updateHandler?.Invoke(this, 0, Time, 0.0);

        for (int iter = 1; iter <= numberIter; iter++)
        {
            // Проверка запроса на остановку (кнопка «Остановить» в интерфейсе)
            if (_stopRequested)
            {
                _wasStopped = true;
                break;
            }

            Time = iter * deltaTime;

            // 1. Обновление границ (граничные условия могут зависеть от времени)
            UpdateBoundaries(Time);

            // 2. Обновление SecDerT на всех гранях по текущим температурам ячеек
            UpdateFaces();

            // 3. Обновление температур ячеек (все ячейки используют один и тот же набор SecDerT)
            double sumAbsDeltaT = UpdateCells();

            Iteration = iter;

            if (updateHandler != null && (iter % UpdatePeriod == 0 || iter == numberIter))
            {
                updateHandler(this, iter, Time, sumAbsDeltaT);
            }
        }
    }

    /// <summary>Обновление температур всех граничных граней по их граничным условиям.</summary>
    private void UpdateBoundaries(double time)
    {
        for (int i = 0; i < _boundaryFaces.Count; i++)
        {
            _boundaryFaces[i].UpdateBoundaryTemp(time);
        }
    }

    /// <summary>Пересчёт второй производной по нормали на всех гранях сетки.</summary>
    private void UpdateFaces()
    {
        foreach (Face face in _faceXList)
        {
            face.updateSecDerT();
        }
        foreach (Face face in _faceYList)
        {
            face.updateSecDerT();
        }
        foreach (Face face in _faceZList)
        {
            face.updateSecDerT();
        }
    }

    /// <summary>Обновление температур всех ячеек. Возвращает сумму модулей приращений, К.</summary>
    private double UpdateCells()
    {
        double sumAbsDeltaT = 0.0;
        foreach (Cell cell in _cellList)
        {
            sumAbsDeltaT += Math.Abs(cell.UpdateT());
        }
        return sumAbsDeltaT;
    }

    /// <summary>Обнуление приращений температуры во всех ячейках.</summary>
    private void ResetDeltaTemp()
    {
        foreach (Cell cell in _cellList)
        {
            cell.DeltaTemp = 0.0;
        }
    }

    /// <summary>Передача расстояния между центрами ячеек во все ячейки и грани.</summary>
    private void ApplyCellSize()
    {
        foreach (Cell cell in _cellList)
        {
            cell.CellSize = CellSize;
        }
        foreach (Face face in _faceXList)
        {
            face.CellSize = CellSize;
        }
        foreach (Face face in _faceYList)
        {
            face.CellSize = CellSize;
        }
        foreach (Face face in _faceZList)
        {
            face.CellSize = CellSize;
        }
    }

    /// <summary>Сбор списка граничных граней (End == true).</summary>
    private void CollectBoundaryFaces()
    {
        _boundaryFaces.Clear();
        foreach (Face face in _faceXList)
        {
            if (face.End) _boundaryFaces.Add(face);
        }
        foreach (Face face in _faceYList)
        {
            if (face.End) _boundaryFaces.Add(face);
        }
        foreach (Face face in _faceZList)
        {
            if (face.End) _boundaryFaces.Add(face);
        }
    }

    /// <summary>Проверка, что массив температур совпадает по размерам с сеткой.</summary>
    private void CheckShape(double[,,] temp, string name)
    {
        if (temp == null)
        {
            throw new ArgumentNullException(name);
        }
        if (temp.GetLength(0) != _shape[0] || temp.GetLength(1) != _shape[1] || temp.GetLength(2) != _shape[2])
        {
            throw new ArgumentException(
                $"{name} имеет размерность [{temp.GetLength(0)}, {temp.GetLength(1)}, {temp.GetLength(2)}], " +
                $"а ожидалась [{_shape[0]}, {_shape[1]}, {_shape[2]}]", name);
        }
    }

    /// <summary>Проверка размерности массива функций граничного условия для плоскости.</summary>
    private static void CheckShape(Func<double, double>[,] funcs, string name, int d0, int d1)
    {
        if (funcs == null)
        {
            throw new ArgumentNullException(name);
        }
        if (funcs.GetLength(0) != d0 || funcs.GetLength(1) != d1)
        {
            throw new ArgumentException(
                $"{name} имеет размерность [{funcs.GetLength(0)}, {funcs.GetLength(1)}], " +
                $"а ожидалась [{d0}, {d1}]", name);
        }
    }

    /// <summary>
    /// Тестовое распределение температуры: горячее пятно в центре куба (0..100 К).
    /// </summary>
    public void TestTempDistr()
    {
        double cx = _shape[0] / 2.0;
        double cy = _shape[1] / 2.0;
        double cz = _shape[2] / 2.0;

        double[,,] temp = new double[_shape[0], _shape[1], _shape[2]];

        for (int x = 0; x < _shape[0]; x++)
        {
            for (int y = 0; y < _shape[1]; y++)
            {
                for (int z = 0; z < _shape[2]; z++)
                {
                    double rad = Math.Sqrt(Math.Pow(x - cx, 2) + Math.Pow(y - cy, 2) + Math.Pow(z - cz, 2));
                    temp[x, y, z] = Math.Max(0.0, 100.0 - rad * 3.5);
                }
            }
        }

        InitCond(temp);
    }

    private Face CreateFaceX(int x, int y, int z, bool end = false)
    {
        Face face = new Face(null, end);
        _faceXList[x, y, z] = face;
        face.Cells[0] = _cellList[x - 1, y, z];
        _cellList[x - 1, y, z].Faces[1] = face;
        return face;
    }

    private Face CreateFaceY(int x, int y, int z, bool end = false)
    {
        Face face = new Face(null, end);
        _faceYList[x, y, z] = face;
        face.Cells[0] = _cellList[x, y - 1, z];
        _cellList[x, y - 1, z].Faces[3] = face;
        return face;
    }

    private Face CreateFaceZ(int x, int y, int z, bool end = false)
    {
        Face face = new Face(null, end);
        _faceZList[x, y, z] = face;
        face.Cells[0] = _cellList[x, y, z - 1];
        _cellList[x, y, z - 1].Faces[5] = face;
        return face;
    }

    private Cell CreateCell(int x, int y, int z, Face faceX, Face faceY, Face faceZ)
    {
        Cell cell = new Cell();
        _cellList[x, y, z] = cell;
        faceX.Cells[1] = cell;
        faceY.Cells[1] = cell;
        faceZ.Cells[1] = cell;
        cell.Faces[0] = faceX;
        cell.Faces[2] = faceY;
        cell.Faces[4] = faceZ;
        return cell;
    }
}
