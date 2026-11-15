using System.Text.Json;
using ThermConductSim;

namespace Program;

/// <summary>
/// Условия эксперимента, прочитанные из единого JSON-файла <c>conditions.json</c>.
///
/// Формат файла (разрешены комментарии <c>//</c>, <c>/* */</c> и висячие запятые):
/// <code>
/// {
///   "grid": {                        // сетка (обязательна, если нет массива initial.cells)
///     "cells": [16, 16, 16],         //   количество ячеек по осям X, Y, Z
///     "size": [0.016, 0.016, 0.016], //   линейные размеры области по осям, м (h = size/cells)
///     // либо вместо size: "cellSize": 0.001 (один на все оси) или [hx, hy, hz] — по осям, м
///   },
///   "thermalConductivity": 45.0,     // λ, Вт/(м·К) — для ГУ 2-го и 3-го рода
///   "method": "explicit",            // метод расчёта: ключ из ThermalFunctions.CellUpdateMethods
///   "initial": {                     // начальное условие (одно из):
///     "temperature": 300.0,          //   число — равномерное поле, К
///     // "function": "hot_center"    //   ключ из ThermalFunctions.InitialTemperatures
///     // "cells": [[[...]]]          //   массив [X][Y][Z] — своя температура каждой ячейки
///   },
///   "boundaries": {                  // ГУ шести граней; пропущенная грань — 300 К (1-й род)
///     "xPast": { "type": "dirichlet", "value": 400.0 },   // 1-й род, константа на всю грань
///     "xNext": { "type": "dirichlet", "function": "sin_pulse" }, // функция времени по ключу
///     "yPast": { "type": "neumann", "value": 0.0 },       // 2-й род: поток, Вт/м²
///     "yNext": { "type": "robin", "value": [300.0, 800.0] }, // 3-й род: [T среды, α], α — Вт/(м²·К)
///     "zPast": [[300, 300, ...], ...],                    // сокращённо: массив = 1-й род по ячейкам
///     // равномерно или по ячейкам — для константы ("value"/"values") и для функции
///     // ("function"/"functions"): "values"/"functions" — двумерный массив формы грани
///     // (x — [Y][Z], y — [X][Z], z — [X][Y]); для Робина элемент — пара [значение, значение]
///   }
/// }
/// </code>
///
/// Сами функции хранятся в словарях <see cref="ThermalFunctions"/> и ищутся по ключу;
/// неизвестный ключ — ошибка со списком доступных.
/// </summary>
public sealed class MeshConditions
{
    /// <summary>Температура по умолчанию, К: ею заполняется всё, что не задано в json.</summary>
    public const double DefaultTemperature = 300.0;

    /// <summary>Шаг сетки по умолчанию, м (если в json не задан ни size, ни cellSize).</summary>
    public const double DefaultCellSize = Mesh.DefaultCellSize;

    /// <summary>Теплопроводность по умолчанию, Вт/(м·К) (совпадает с ядром).</summary>
    public const double DefaultThermalConductivity = Mesh.DefaultThermalConductivity;

    /// <summary>Параметры чтения json: комментарии и запятая перед закрывающей скобкой разрешены.</summary>
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Начальные температуры ячеек, <c>[X, Y, Z]</c>, К.</summary>
    public double[,,] Initial { get; }

    /// <summary>Граничное условие грани <c>x = min</c>, <c>[Y, Z]</c>.</summary>
    public BoundaryCondition[,] XPast { get; }

    /// <summary>Граничное условие грани <c>x = max</c>, <c>[Y, Z]</c>.</summary>
    public BoundaryCondition[,] XNext { get; }

    /// <summary>Граничное условие грани <c>y = min</c>, <c>[X, Z]</c>.</summary>
    public BoundaryCondition[,] YPast { get; }

    /// <summary>Граничное условие грани <c>y = max</c>, <c>[X, Z]</c>.</summary>
    public BoundaryCondition[,] YNext { get; }

    /// <summary>Граничное условие грани <c>z = min</c>, <c>[X, Y]</c>.</summary>
    public BoundaryCondition[,] ZPast { get; }

    /// <summary>Граничное условие грани <c>z = max</c>, <c>[X, Y]</c>.</summary>
    public BoundaryCondition[,] ZNext { get; }

    /// <summary>Шаг сетки по оси X (расстояние между центрами соседних ячеек), м.</summary>
    public double CellSizeX { get; }

    /// <summary>Шаг сетки по оси Y, м.</summary>
    public double CellSizeY { get; }

    /// <summary>Шаг сетки по оси Z, м.</summary>
    public double CellSizeZ { get; }

    /// <summary>Метод подсчёта теплопередачи (делегат из словаря по ключу "method").</summary>
    public CellUpdateDelegate UpdateMethod { get; }

    /// <summary>Теплопроводность материала λ, Вт/(м·К), для ГУ 2-го и 3-го рода.</summary>
    public double ThermalConductivity { get; }

    private MeshConditions(double[,,] initial,
        BoundaryCondition[,] xPast, BoundaryCondition[,] xNext,
        BoundaryCondition[,] yPast, BoundaryCondition[,] yNext,
        BoundaryCondition[,] zPast, BoundaryCondition[,] zNext,
        double cellSizeX, double cellSizeY, double cellSizeZ,
        CellUpdateDelegate updateMethod, double thermalConductivity)
    {
        Initial = initial;
        XPast = xPast;
        XNext = xNext;
        YPast = yPast;
        YNext = yNext;
        ZPast = zPast;
        ZNext = zNext;
        CellSizeX = cellSizeX;
        CellSizeY = cellSizeY;
        CellSizeZ = cellSizeZ;
        UpdateMethod = updateMethod;
        ThermalConductivity = thermalConductivity;
    }

    /// <summary>
    /// Создать объект расчёта — сетку вместе с начальными и граничными условиями.
    /// Это единственное место, где условия передаются в расчётное ядро.
    /// </summary>
    public Mesh CreateMesh() => new(Initial,
        xPast: XPast, xNext: XNext,
        yPast: YPast, yNext: YNext,
        zPast: ZPast, zNext: ZNext,
        cellSizeX: CellSizeX, cellSizeY: CellSizeY, cellSizeZ: CellSizeZ,
        updateMethod: UpdateMethod,
        thermalConductivity: ThermalConductivity);

    /// <summary>
    /// Прочитать условия из файла <paramref name="path"/>. Относительные пути ищутся
    /// рядом с исполняемым файлом, затем вверх по каталогам от текущего каталога
    /// (чтобы работал и <c>dotnet run</c> из каталога проекта).
    /// </summary>
    public static MeshConditions Load(string path)
    {
        string file = ResolvePath(path);
        using JsonDocument document = ParseFile(file);
        JsonElement root = document.RootElement;

        bool hasGrid = root.TryGetProperty("grid", out JsonElement grid)
            && grid.ValueKind == JsonValueKind.Object;
        bool hasInitial = root.TryGetProperty("initial", out JsonElement initialElement)
            && initialElement.ValueKind == JsonValueKind.Object;

        // 1. Количество ячеек: из grid.cells, а если его нет — из массива начальных температур.
        int nx, ny, nz;
        if (hasGrid && TryGetProperty(grid, "cells", out JsonElement counts) && counts.ValueKind == JsonValueKind.Array)
        {
            ReadCounts(counts, file, out nx, out ny, out nz);
        }
        else if (hasInitial && TryGetProperty(initialElement, "cells", out JsonElement initCells)
            && initCells.ValueKind == JsonValueKind.Array)
        {
            (nx, ny, nz) = PeekCubeDims(initCells, file);
        }
        else
        {
            throw new InvalidDataException(
                $"{file}: задайте размеры сетки — \"grid\": {{ \"cells\": [X, Y, Z], ... }} "
                + "или массив \"initial\".\"cells\" с температурами всех ячеек");
        }

        // 2. Линейные размеры: из размеров области в метрах (h = size/cells) или напрямую.
        double hx = DefaultCellSize, hy = DefaultCellSize, hz = DefaultCellSize;
        if (hasGrid)
        {
            if (TryGetProperty(grid, "size", out JsonElement size) && size.ValueKind == JsonValueKind.Array)
            {
                double[] lengths = ReadTriple(size, "grid.size", file);
                hx = lengths[0] / nx;
                hy = lengths[1] / ny;
                hz = lengths[2] / nz;
            }
            else if (TryGetProperty(grid, "cellSize", out JsonElement step) && step.ValueKind != JsonValueKind.Null)
            {
                if (step.ValueKind == JsonValueKind.Number)
                {
                    hx = hy = hz = ReadPositiveDouble(step, "grid.cellSize", file);
                }
                else if (step.ValueKind == JsonValueKind.Array)
                {
                    double[] steps = ReadTriple(step, "grid.cellSize", file);
                    hx = steps[0];
                    hy = steps[1];
                    hz = steps[2];
                }
                else
                {
                    throw new InvalidDataException(
                        $"{file}: \"grid.cellSize\" должно быть числом или массивом из трёх чисел");
                }
            }
        }

        // 3. Начальное условие: число / функция по ключу / массив всех ячеек.
        double[,,] initial = ReadInitial(initialElement, hasInitial, nx, ny, nz, hx, hy, hz, file);

        // 4. Граничные условия шести граней.
        bool hasBoundaries = root.TryGetProperty("boundaries", out JsonElement boundaries)
            && boundaries.ValueKind == JsonValueKind.Object;
        BoundaryCondition[,] xPast = ReadFace(boundaries, hasBoundaries, "xPast", ny, nz, file);
        BoundaryCondition[,] xNext = ReadFace(boundaries, hasBoundaries, "xNext", ny, nz, file);
        BoundaryCondition[,] yPast = ReadFace(boundaries, hasBoundaries, "yPast", nx, nz, file);
        BoundaryCondition[,] yNext = ReadFace(boundaries, hasBoundaries, "yNext", nx, nz, file);
        BoundaryCondition[,] zPast = ReadFace(boundaries, hasBoundaries, "zPast", nx, ny, file);
        BoundaryCondition[,] zNext = ReadFace(boundaries, hasBoundaries, "zNext", nx, ny, file);

        // 5. Метод расчёта и теплопроводность материала.
        CellUpdateDelegate method = ReadMethod(root, file);
        double lambda = ReadNumber(root, "thermalConductivity", DefaultThermalConductivity, file);
        if (!(lambda > 0.0))
        {
            throw new InvalidDataException($"{file}: \"thermalConductivity\" должно быть > 0");
        }

        return new MeshConditions(initial,
            xPast, xNext, yPast, yNext, zPast, zNext,
            hx, hy, hz, method, lambda);
    }

    // ---------- Начальное условие ----------

    /// <summary>Начальное поле: массив ячеек, функция координат по ключу или равномерное число.</summary>
    private static double[,,] ReadInitial(JsonElement initial, bool present, int nx, int ny, int nz,
        double hx, double hy, double hz, string file)
    {
        if (present && TryGetProperty(initial, "cells", out JsonElement cells) && cells.ValueKind != JsonValueKind.Null)
        {
            double[,,] cube = ReadCube(cells, "initial.cells", file);
            if (cube.GetLength(0) != nx || cube.GetLength(1) != ny || cube.GetLength(2) != nz)
            {
                throw new InvalidDataException(
                    $"{file}: \"initial.cells\" имеет размеры [{cube.GetLength(0)}, {cube.GetLength(1)}, {cube.GetLength(2)}], "
                    + $"а сетка — [{nx}, {ny}, {nz}]");
            }
            return cube;
        }

        if (present && TryGetProperty(initial, "function", out JsonElement function) && function.ValueKind != JsonValueKind.Null)
        {
            if (function.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"{file}: \"initial.function\" должно быть строкой — ключом функции");
            }
            InitialTemperatureDelegate temperature = ThermalFunctions.GetInitialTemperature(function.GetString()!);

            double[,,] cube = new double[nx, ny, nz];
            for (int x = 0; x < nx; x++)
            {
                for (int y = 0; y < ny; y++)
                {
                    for (int z = 0; z < nz; z++)
                    {
                        // Координаты центра ячейки, м: начало координат — угол {x, y, z} = min.
                        cube[x, y, z] = temperature((x + 0.5) * hx, (y + 0.5) * hy, (z + 0.5) * hz);
                    }
                }
            }
            return cube;
        }

        double uniform = present ? ReadNumber(initial, "temperature", DefaultTemperature, file) : DefaultTemperature;
        return UniformCube(nx, ny, nz, uniform);
    }

    // ---------- Граничные условия ----------

    /// <summary>
    /// Плоскость ГУ грани: пропуск — 300 К (1-й род, единый на всю грань); сокращённые
    /// формы — число или двумерный массив чисел (1-й род); полная — объект с "type".
    /// </summary>
    private static BoundaryCondition[,] ReadFace(JsonElement boundaries, bool present,
        string name, int d0, int d1, string file)
    {
        if (!present || !TryGetProperty(boundaries, name, out JsonElement value))
        {
            return UniformPlane(d0, d1, BoundaryCondition.Dirichlet(Constant(DefaultTemperature)));
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => UniformPlane(d0, d1, BoundaryCondition.Dirichlet(Constant(value.GetDouble()))),
            JsonValueKind.Array => ReadSpecPlane(value, BoundaryKind.Dirichlet, name, d0, d1, file),
            JsonValueKind.Object => ReadFaceObject(value, name, d0, d1, file),
            _ => throw new InvalidDataException(
                $"{file}: поле \"{name}\" должно быть числом, массивом или объектом с \"type\"")
        };
    }

    /// <summary>Объект ГУ: вид ("type") плюс ровно одно поле значений: value / values / function / functions.</summary>
    private static BoundaryCondition[,] ReadFaceObject(JsonElement obj, string name, int d0, int d1, string file)
    {
        BoundaryKind kind = ReadKind(obj, name, file);

        int variants = 0;
        variants += Has(obj, "value") ? 1 : 0;
        variants += Has(obj, "values") ? 1 : 0;
        variants += Has(obj, "function") ? 1 : 0;
        variants += Has(obj, "functions") ? 1 : 0;
        if (variants != 1)
        {
            throw new InvalidDataException(
                $"{file}: в \"{name}\" нужно ровно одно из полей: value, values, function, functions");
        }

        if (Has(obj, "value"))
        {
            return UniformPlane(d0, d1, ReadSpec(obj.GetProperty("value"), kind, name, file));
        }
        if (Has(obj, "function"))
        {
            return UniformPlane(d0, d1, ReadSpec(obj.GetProperty("function"), kind, name, file));
        }

        JsonElement array = Has(obj, "values") ? obj.GetProperty("values") : obj.GetProperty("functions");
        return ReadSpecPlane(array, kind, name, d0, d1, file);
    }

    /// <summary>Вид ГУ из поля "type" (регистр не важен).</summary>
    private static BoundaryKind ReadKind(JsonElement obj, string name, string file)
    {
        if (!TryGetProperty(obj, "type", out JsonElement type) || type.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                $"{file}: в \"{name}\" нужно строковое поле \"type\": \"dirichlet\" | \"neumann\" | \"robin\"");
        }

        return type.GetString()!.Trim().ToLowerInvariant() switch
        {
            "dirichlet" => BoundaryKind.Dirichlet,
            "neumann" => BoundaryKind.Neumann,
            "robin" => BoundaryKind.Robin,
            var unknown => throw new InvalidDataException(
                $"{file}: неизвестный вид граничного условия \"{unknown}\" в \"{name}\" "
                + "(ожидалось: dirichlet | neumann | robin)")
        };
    }

    /// <summary>
    /// Одно ГУ: для Дирихле/Неймана элемент — число или ключ функции; для Робина —
    /// пара [значение, значение], где каждое — число или ключ функции.
    /// </summary>
    private static BoundaryCondition ReadSpec(JsonElement element, BoundaryKind kind, string name, string file)
    {
        switch (kind)
        {
            case BoundaryKind.Dirichlet:
                return BoundaryCondition.Dirichlet(ReadValueFunction(element, name, file));

            case BoundaryKind.Neumann:
                return BoundaryCondition.Neumann(ReadValueFunction(element, name, file));

            default:
            {
                if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 2)
                {
                    throw new InvalidDataException(
                        $"{file}: для ГУ 3-го рода (robin) в \"{name}\" нужна пара "
                        + "[температура среды, коэффициент теплоотдачи α]");
                }

                using JsonElement.ArrayEnumerator pair = element.EnumerateArray();
                pair.MoveNext();
                JsonElement first = pair.Current;
                pair.MoveNext();
                JsonElement second = pair.Current;
                return BoundaryCondition.Robin(
                    ReadValueFunction(first, name, file),
                    ReadValueFunction(second, name, file));
            }
        }
    }

    /// <summary>Константа (число) или функция времени по ключу из словаря ThermalFunctions.BoundaryConditions.</summary>
    private static Func<double, double> ReadValueFunction(JsonElement element, string name, string file)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return Constant(element.GetDouble());
        }
        if (element.ValueKind == JsonValueKind.String)
        {
            return ThermalFunctions.GetBoundaryFunction(element.GetString()!);
        }
        throw new InvalidDataException(
            $"{file}: в \"{name}\" ожидалось число или имя функции из словаря "
            + "ThermalFunctions.BoundaryConditions");
    }

    /// <summary>Двумерный массив ГУ <c>[d0][d1]</c>: своё условие у каждой ячейки грани.</summary>
    private static BoundaryCondition[,] ReadSpecPlane(JsonElement array, BoundaryKind kind,
        string name, int d0, int d1, string file)
    {
        int rows = array.GetArrayLength();
        if (rows != d0)
        {
            throw new InvalidDataException(
                $"{file}: в \"{name}\" {rows} строк, а ожидалось {d0}");
        }

        BoundaryCondition[,] plane = new BoundaryCondition[d0, d1];
        int i = 0;
        foreach (JsonElement row in array.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != d1)
            {
                throw new InvalidDataException(
                    $"{file}: в \"{name}\" строка {i} должна быть массивом из {d1} элементов");
            }

            int j = 0;
            foreach (JsonElement item in row.EnumerateArray())
            {
                plane[i, j++] = ReadSpec(item, kind, name, file);
            }
            i++;
        }

        return plane;
    }

    /// <summary>Плоскость, заполненная одним и тем же ГУ (единичный экземпляр на всю грань).</summary>
    private static BoundaryCondition[,] UniformPlane(int d0, int d1, BoundaryCondition condition)
    {
        BoundaryCondition[,] plane = new BoundaryCondition[d0, d1];
        for (int i = 0; i < d0; i++)
        {
            for (int j = 0; j < d1; j++)
            {
                plane[i, j] = condition;
            }
        }
        return plane;
    }

    // ---------- Метод расчёта ----------

    /// <summary>Метод подсчёта теплопередачи: ключ из словаря ThermalFunctions.CellUpdateMethods.</summary>
    private static CellUpdateDelegate ReadMethod(JsonElement root, string file)
    {
        if (!TryGetProperty(root, "method", out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return ThermalFunctions.GetCellUpdateMethod("explicit");
        }
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                $"{file}: \"method\" должно быть строкой — ключом из словаря ThermalFunctions.CellUpdateMethods");
        }

        try
        {
            return ThermalFunctions.GetCellUpdateMethod(value.GetString()!);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"{file}: {ex.Message}", ex);
        }
    }

    // ---------- Общие читатели значений ----------

    /// <summary>Открыть json-файл; ошибки разбора переделываются в понятное сообщение.</summary>
    private static JsonDocument ParseFile(string path)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllBytes(path), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path}: некорректный json — {ex.Message}", ex);
        }
    }

    /// <summary>Найти файл условий: у исполняемого файла или выше текущего каталога.</summary>
    private static string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return File.Exists(path)
                ? path
                : throw new FileNotFoundException($"не найден файл условий '{path}'", path);
        }

        string nearExe = Path.Combine(AppContext.BaseDirectory, path);
        if (File.Exists(nearExe))
        {
            return nearExe;
        }

        for (string? dir = Directory.GetCurrentDirectory(); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            string candidate = Path.Combine(dir, path);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"не найден файл условий '{path}' (искали рядом с программой: {nearExe})", path);
    }

    /// <summary>Свойство объекта, если оно есть и не null.</summary>
    private static bool TryGetProperty(JsonElement obj, string property, out JsonElement value) =>
        obj.TryGetProperty(property, out value) && value.ValueKind != JsonValueKind.Null;

    /// <summary>Свойство объекта, если оно есть и не null (для подсчёта вариантов).</summary>
    private static bool Has(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out JsonElement value) && value.ValueKind != JsonValueKind.Null;

    /// <summary>Число из поля объекта; если поля нет — значение по умолчанию.</summary>
    private static double ReadNumber(JsonElement root, string property, double fallback, string source)
    {
        if (!TryGetProperty(root, property, out JsonElement value))
        {
            return fallback;
        }
        if (value.ValueKind != JsonValueKind.Number)
        {
            throw new InvalidDataException($"{source}: поле \"{property}\" должно быть числом");
        }
        return value.GetDouble();
    }

    /// <summary>Массив из трёх положительных чисел (размеры области или шаги сетки по осям).</summary>
    private static double[] ReadTriple(JsonElement array, string property, string source)
    {
        if (array.GetArrayLength() != 3)
        {
            throw new InvalidDataException($"{source}: \"{property}\" должно быть массивом из трёх чисел");
        }

        double[] result = new double[3];
        int i = 0;
        foreach (JsonElement item in array.EnumerateArray())
        {
            result[i++] = ReadPositiveDouble(item, property, source);
        }
        return result;
    }

    /// <summary>Положительное дробное число (размер в метрах, шаг сетки).</summary>
    private static double ReadPositiveDouble(JsonElement value, string property, string source)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double result) || !(result > 0.0))
        {
            throw new InvalidDataException($"{source}: в \"{property}\" ожидалось положительное число");
        }
        return result;
    }

    /// <summary>Количество ячеек по осям: массив из трёх целых положительных чисел.</summary>
    private static void ReadCounts(JsonElement array, string source, out int nx, out int ny, out int nz)
    {
        if (array.GetArrayLength() != 3)
        {
            throw new InvalidDataException(
                $"{source}: \"grid.cells\" должно быть массивом из трёх целых чисел [X, Y, Z]");
        }

        int[] dims = new int[3];
        int i = 0;
        foreach (JsonElement item in array.EnumerateArray())
        {
            dims[i++] = ReadPositiveInt(item, "grid.cells", source);
        }

        nx = dims[0];
        ny = dims[1];
        nz = dims[2];
    }

    /// <summary>Целое положительное число (число ячеек).</summary>
    private static int ReadPositiveInt(JsonElement value, string property, string source)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result) || result <= 0)
        {
            throw new InvalidDataException(
                $"{source}: в \"{property}\" ожидалось целое положительное число");
        }
        return result;
    }

    /// <summary>Размеры массива <c>[X][Y][Z]</c> без чтения значений (проверка прямоугольности).</summary>
    private static (int Nx, int Ny, int Nz) PeekCubeDims(JsonElement array, string source)
    {
        int nx = array.GetArrayLength();
        if (nx == 0)
        {
            throw new InvalidDataException($"{source}: \"initial.cells\" не должен быть пустым");
        }

        int ny = -1, nz = -1;
        int x = 0;
        foreach (JsonElement plane in array.EnumerateArray())
        {
            if (plane.ValueKind != JsonValueKind.Array || plane.GetArrayLength() == 0)
            {
                throw new InvalidDataException(
                    $"{source}: в \"initial.cells\" слой {x} должен быть непустым двумерным массивом");
            }
            if (ny < 0)
            {
                ny = plane.GetArrayLength();
            }
            else if (plane.GetArrayLength() != ny)
            {
                throw new InvalidDataException(
                    $"{source}: \"initial.cells\" должен быть прямоугольным массивом [X][Y][Z]");
            }

            foreach (JsonElement row in plane.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() == 0)
                {
                    throw new InvalidDataException(
                        $"{source}: в \"initial.cells\" строки должны быть непустыми массивами");
                }
                if (nz < 0)
                {
                    nz = row.GetArrayLength();
                }
                else if (row.GetArrayLength() != nz)
                {
                    throw new InvalidDataException(
                        $"{source}: \"initial.cells\" должен быть прямоугольным массивом [X][Y][Z]");
                }
            }
            x++;
        }

        return (nx, ny, nz);
    }

    /// <summary>Трёхмерный массив <c>[X][Y][Z]</c> из json (с проверкой прямоугольности).</summary>
    private static double[,,] ReadCube(JsonElement array, string property, string source)
    {
        (int nx, int ny, int nz) = PeekCubeDims(array, source);

        double[,,] cube = new double[nx, ny, nz];
        int x = 0;
        foreach (JsonElement plane in array.EnumerateArray())
        {
            int y = 0;
            foreach (JsonElement row in plane.EnumerateArray())
            {
                int z = 0;
                foreach (JsonElement item in row.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Number)
                    {
                        throw new InvalidDataException(
                            $"{source}: в \"{property}\" ожидались числа (слой {x}, строка {y})");
                    }
                    cube[x, y, z++] = item.GetDouble();
                }
                y++;
            }
            x++;
        }

        return cube;
    }

    /// <summary>Равномерное поле: одна и та же температура во всех ячейках.</summary>
    private static double[,,] UniformCube(int d0, int d1, int d2, double value)
    {
        double[,,] cube = new double[d0, d1, d2];
        for (int x = 0; x < d0; x++)
        {
            for (int y = 0; y < d1; y++)
            {
                for (int z = 0; z < d2; z++)
                {
                    cube[x, y, z] = value;
                }
            }
        }
        return cube;
    }

    /// <summary>Константная функция времени (константное ГУ — частный случай функции).</summary>
    private static Func<double, double> Constant(double value) => _ => value;
}
