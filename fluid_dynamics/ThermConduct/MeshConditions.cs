using System.Text.Json;
using ThermConductSim;

namespace Program;

/// <summary>
/// Начальные и граничные условия эксперимента, прочитанные из двух JSON-файлов.
///
/// <b>initial_conditions.json</b> — начальные условия:
/// <list type="bullet">
/// <item><c>shape</c> — размеры сетки <c>[X, Y, Z]</c> в ячейках (нужны, если поле задано числом);</item>
/// <item><c>cellSize</c> — шаг сетки <c>h</c>, м (по умолчанию <see cref="Mesh.DefaultCellSize"/>);</item>
/// <item><c>temperature</c> — одно число на всё поле: равномерное начальное распределение, К
/// (по умолчанию <see cref="DefaultTemperature"/>);</item>
/// <item><c>cells</c> — необязательный трёхмерный массив <c>[X][Y][Z]</c> «своя температура каждой
/// ячейки»; если он задан, <c>temperature</c> не используется, а размеры берутся из массива.</item>
/// </list>
///
/// <b>boundary_conditions.json</b> — граничные условия: шесть полей
/// <c>xPast, xNext, yPast, yNext, zPast, zNext</c>, каждое либо одно число (равномерная
/// температура всей грани, К), либо двумерный массив, повторяющий форму грани
/// (x — <c>[Y][Z]</c>, y — <c>[X][Z]</c>, z — <c>[X][Y]</c>). Пропущенная грань получает
/// <see cref="DefaultTemperature"/>.
///
/// В обоих файлах разрешены комментарии <c>//</c>, <c>/* */</c> и висячие запятые.
/// </summary>
public sealed class MeshConditions
{
    /// <summary>Температура по умолчанию, К: ею заполняется всё, что не задано в json.</summary>
    public const double DefaultTemperature = 300.0;

    /// <summary>Шаг сетки по умолчанию, м (совпадает с шагом сетки расчётного ядра).</summary>
    public const double DefaultCellSize = Mesh.DefaultCellSize;

    /// <summary>Параметры чтения json: комментарии и запятая перед закрывающей скобкой разрешены.</summary>
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Начальные температуры ячеек, <c>[X, Y, Z]</c>, К.</summary>
    public double[,,] Initial { get; }

    /// <summary>Граничное условие на грани <c>x = min</c>, <c>[Y, Z]</c>, К.</summary>
    public double[,] XPast { get; }

    /// <summary>Граничное условие на грани <c>x = max</c>, <c>[Y, Z]</c>, К.</summary>
    public double[,] XNext { get; }

    /// <summary>Граничное условие на грани <c>y = min</c>, <c>[X, Z]</c>, К.</summary>
    public double[,] YPast { get; }

    /// <summary>Граничное условие на грани <c>y = max</c>, <c>[X, Z]</c>, К.</summary>
    public double[,] YNext { get; }

    /// <summary>Граничное условие на грани <c>z = min</c>, <c>[X, Y]</c>, К.</summary>
    public double[,] ZPast { get; }

    /// <summary>Граничное условие на грани <c>z = max</c>, <c>[X, Y]</c>, К.</summary>
    public double[,] ZNext { get; }

    /// <summary>Шаг сетки <c>h</c>, м.</summary>
    public double CellSize { get; }

    private MeshConditions(double[,,] initial,
        double[,] xPast, double[,] xNext,
        double[,] yPast, double[,] yNext,
        double[,] zPast, double[,] zNext,
        double cellSize)
    {
        Initial = initial;
        XPast = xPast;
        XNext = xNext;
        YPast = yPast;
        YNext = yNext;
        ZPast = zPast;
        ZNext = zNext;
        CellSize = cellSize;
    }

    /// <summary>
    /// Создать объект расчёта — сетку вместе с начальными и граничными условиями.
    /// Это единственное место, где условия передаются в расчётное ядро.
    /// </summary>
    public Mesh CreateMesh() => new(Initial,
        xPast: XPast, xNext: XNext,
        yPast: YPast, yNext: YNext,
        zPast: ZPast, zNext: ZNext,
        cellSize: CellSize);

    /// <summary>
    /// Прочитать условия из двух файлов: начальные — <paramref name="initialPath"/>,
    /// граничные — <paramref name="boundaryPath"/>. Относительные пути ищутся рядом
    /// с исполняемым файлом, затем вверх по каталогам от текущего каталога
    /// (чтобы работал и <c>dotnet run</c> из каталога проекта).
    /// </summary>
    public static MeshConditions Load(string initialPath, string boundaryPath)
    {
        string initialFile = ResolvePath(initialPath);

        double[,,] initial;
        double cellSize;
        int nx, ny, nz;

        // 1. Начальное условие: либо равномерное поле из числа, либо массив всех ячеек.
        using (JsonDocument document = ParseFile(initialFile))
        {
            JsonElement root = document.RootElement;
            cellSize = ReadNumber(root, "cellSize", DefaultCellSize, initialFile);

            if (root.TryGetProperty("cells", out JsonElement cells) && cells.ValueKind != JsonValueKind.Null)
            {
                // Поле задано массивом — размеры берутся из него, shape (если есть) сверяется.
                initial = ReadCube(cells, "cells", initialFile);
                nx = initial.GetLength(0);
                ny = initial.GetLength(1);
                nz = initial.GetLength(2);

                if (TryReadShape(root, initialFile, out int sx, out int sy, out int sz)
                    && (sx != nx || sy != ny || sz != nz))
                {
                    throw new InvalidDataException(
                        $"{initialFile}: массив \"cells\" имеет размеры [{nx}, {ny}, {nz}], "
                        + $"а \"shape\" задаёт [{sx}, {sy}, {sz}]");
                }
            }
            else
            {
                ReadShape(root, initialFile, out nx, out ny, out nz);
                initial = UniformCube(nx, ny, nz,
                    ReadNumber(root, "temperature", DefaultTemperature, initialFile));
            }
        }

        // 2. Граничные условия: по плоскости на каждую из шести граней.
        string boundaryFile = ResolvePath(boundaryPath);
        using JsonDocument boundary = ParseFile(boundaryFile);
        JsonElement b = boundary.RootElement;

        return new MeshConditions(initial,
            ReadPlane(b, "xPast", ny, nz, boundaryFile),
            ReadPlane(b, "xNext", ny, nz, boundaryFile),
            ReadPlane(b, "yPast", nx, nz, boundaryFile),
            ReadPlane(b, "yNext", nx, nz, boundaryFile),
            ReadPlane(b, "zPast", nx, ny, boundaryFile),
            ReadPlane(b, "zNext", nx, ny, boundaryFile),
            cellSize);
    }

    // ---------- Чтение отдельных значений ----------

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

    /// <summary>Число из поля объекта; если поля нет — значение по умолчанию.</summary>
    private static double ReadNumber(JsonElement root, string property, double fallback, string source)
    {
        if (!root.TryGetProperty(property, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return fallback;
        }
        if (value.ValueKind != JsonValueKind.Number)
        {
            throw new InvalidDataException($"{source}: поле \"{property}\" должно быть числом");
        }
        return value.GetDouble();
    }

    /// <summary>Обязательные размеры сетки из поля <c>shape: [X, Y, Z]</c>.</summary>
    private static void ReadShape(JsonElement root, string source, out int nx, out int ny, out int nz)
    {
        if (!root.TryGetProperty("shape", out JsonElement shape) || shape.ValueKind != JsonValueKind.Array
            || shape.GetArrayLength() != 3)
        {
            throw new InvalidDataException(
                $"{source}: нужен массив \"shape\": [X, Y, Z] (размеры сетки в ячейках) "
                + "или массив \"cells\" с температурами всех ячеек");
        }

        int[] dims = new int[3];
        int i = 0;
        foreach (JsonElement item in shape.EnumerateArray())
        {
            dims[i++] = ReadPositiveInt(item, "shape", source);
        }

        nx = dims[0];
        ny = dims[1];
        nz = dims[2];
    }

    /// <summary>Необязательные размеры сетки: <c>false</c>, если поля <c>shape</c> нет.</summary>
    private static bool TryReadShape(JsonElement root, string source, out int nx, out int ny, out int nz)
    {
        nx = ny = nz = 0;
        if (!root.TryGetProperty("shape", out JsonElement shape) || shape.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        ReadShape(root, source, out nx, out ny, out nz);
        return true;
    }

    /// <summary>Целое положительное число (размер сетки).</summary>
    private static int ReadPositiveInt(JsonElement value, string property, string source)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result) || result <= 0)
        {
            throw new InvalidDataException($"{source}: в \"{property}\" ожидалось целое положительное число");
        }
        return result;
    }

    // ---------- Плоскости граничных условий ----------

    /// <summary>
    /// Плоскость грани <c>[d0, d1]</c>: одно число — равномерная температура всей грани,
    /// массив — своя температура у каждого участка грани, отсутствие поля — 300 К.
    /// </summary>
    private static double[,] ReadPlane(JsonElement root, string property, int d0, int d1, string source)
    {
        if (!root.TryGetProperty(property, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return UniformPlane(d0, d1, DefaultTemperature);
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => UniformPlane(d0, d1, value.GetDouble()),
            JsonValueKind.Array => ReadPlaneArray(value, property, d0, d1, source),
            _ => throw new InvalidDataException(
                $"{source}: поле \"{property}\" должно быть числом или двумерным массивом")
        };
    }

    /// <summary>Двумерный массив грани <c>[d0][d1]</c>.</summary>
    private static double[,] ReadPlaneArray(JsonElement array, string property, int d0, int d1, string source)
    {
        int rows = array.GetArrayLength();
        if (rows != d0)
        {
            throw new InvalidDataException(
                $"{source}: в \"{property}\" {rows} строк, а ожидалось {d0}");
        }

        double[,] plane = new double[d0, d1];
        int i = 0;
        foreach (JsonElement row in array.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != d1)
            {
                throw new InvalidDataException(
                    $"{source}: в \"{property}\" строка {i} должна быть массивом из {d1} чисел");
            }

            int j = 0;
            foreach (JsonElement item in row.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number)
                {
                    throw new InvalidDataException(
                        $"{source}: в \"{property}\" ожидались числа (строка {i})");
                }
                plane[i, j++] = item.GetDouble();
            }
            i++;
        }

        return plane;
    }

    /// <summary>Плоскость, заполненная одной температурой.</summary>
    private static double[,] UniformPlane(int d0, int d1, double value)
    {
        double[,] plane = new double[d0, d1];
        for (int i = 0; i < d0; i++)
        {
            for (int j = 0; j < d1; j++)
            {
                plane[i, j] = value;
            }
        }
        return plane;
    }

    // ---------- Начальное поле температур ----------

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

    /// <summary>Трёхмерный массив <c>[X][Y][Z]</c> из json (с проверкой прямоугольности).</summary>
    private static double[,,] ReadCube(JsonElement array, string property, string source)
    {
        int nx = array.GetArrayLength();
        if (nx == 0)
        {
            throw new InvalidDataException($"{source}: \"{property}\" не должен быть пустым");
        }

        double[,,]? cube = null;
        int x = 0;
        foreach (JsonElement plane in array.EnumerateArray())
        {
            if (plane.ValueKind != JsonValueKind.Array || plane.GetArrayLength() == 0)
            {
                throw new InvalidDataException(
                    $"{source}: в \"{property}\" слой {x} должен быть непустым двумерным массивом");
            }

            int ny = plane.GetArrayLength();
            int y = 0;
            foreach (JsonElement row in plane.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() == 0)
                {
                    throw new InvalidDataException(
                        $"{source}: в \"{property}\" строка {y} слоя {x} должна быть непустым массивом");
                }

                int nz = row.GetArrayLength();
                cube ??= new double[nx, ny, nz];
                if (cube.GetLength(1) != ny || cube.GetLength(2) != nz)
                {
                    throw new InvalidDataException(
                        $"{source}: \"{property}\" должен быть прямоугольным массивом [X][Y][Z]");
                }

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

        return cube!;
    }
}
