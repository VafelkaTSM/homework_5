# ThermConduct — нестационарная теплопроводность в параллелепипеде (3D)

Учебный проект: расчёт распределения температуры в параллелепипеде из `X × Y × Z`
ячеек и визуализация результата в окне AvaloniaUI — срез по выбранной оси и график
отклонения процесса от установившегося: суммы по ячейкам сумм вторых производных
температуры по осям `Σ∇²T` (центральные разности второго порядка) по итерациям.

Все условия эксперимента задаются **одним** JSON-файлом `conditions.json` (загрузчик —
`MeshConditions.cs`): сетка (число ячеек и линейные размеры в метрах), метод расчёта,
начальное поле и граничные условия **1–3-го рода** — константами или функциями.
Сами функции и методы хранятся в словарях `ThermalFunctions.cs` и в JSON передаются
**по ключу** — чтобы добавить свою функцию, достаточно дописать её в словарь.

---

## 1. Математическая модель

Решается трёхмерное уравнение теплопроводности (диффузии) без источников:

```
∂T/∂t = a · (∂²T/∂x² + ∂²T/∂y² + ∂²T/∂z²)
```

где `a` — коэффициент температуропроводности, м²/с.

### Сетка

| Объект | Что это | Размер массива |
|---|---|---|
| `Cell` | центр ячейки, хранит температуру `T` | `[X, Y, Z]` |
| `Face` по X | плоскость между ячейками `x−1` и `x` | `[X+1, Y, Z]` |
| `Face` по Y | плоскость между ячейками `y−1` и `y` | `[X, Y+1, Z]` |
| `Face` по Z | плоскость между ячейками `z−1` и `z` | `[X, Y, Z+1]` |

Число ячеек по каждой оси и линейные размеры задаются в `conditions.json`
(`grid.cells` и `grid.size` в метрах, либо сразу `grid.cellSize`). Шаги сетки
по осям `hx, hy, hz` могут различаться; грань хранит шаг своей оси.

### Вторая производная на грани

Из физических величин грань хранит **только** вторую производную температуры по нормали:

```
SecDerT = (T_{i+1} − T_i) / h²        // h — шаг сетки по оси грани, К/м²
```

Для внутренней грани — это просто разность температур двух соседних ячеек.

### Шаг по времени

Температура каждой ячейки за один шаг пересчитывается методом подсчёта теплопередачи —
делегатом `CellUpdateDelegate` из словаря `ThermalFunctions.CellUpdateMethods`
(ключ задаётся в conditions.json, делегат копируется в каждую ячейку):

* **"explicit"** — явная схема FTCS (одношаговая):

```
ΔT = a·Δt · [ (SecDerT_x_next − SecDerT_x_past)
            + (SecDerT_y_next − SecDerT_y_past)
            + (SecDerT_z_next − SecDerT_z_past) ]

T_new = T + ΔT
```

Условие устойчивости: `a·Δt·(1/hx² + 1/hy² + 1/hz²) ≤ 1/2` (равномерная сетка —
число Куранта `C = a·Δt/h² ≤ 1/6`; интерфейс предупреждает, но не блокирует расчёт).

* **"dufort-frankel"** — трёхслойная явная схема Дюфорта–Франкела: устойчива при любом
  шаге по времени (в обмен на точность); использует `Cell.PrevTemp`, первый шаг каждого
  расчёта — разгонный явный.

### Граничные условия 1–3-го рода

На внешней грани куба задано граничное условие — объект `BoundaryCondition`
(вид + одна или две функции времени). Все три вида сводятся к **температуре стенки**
`T_стенки`, после чего недостающая температура за границей («фиктивная» ячейка-зеркало)
восстанавливается как `T_ghost = 2·T_стенки − T_cell`:

| Вид | Задаётся | Температура стенки |
|---|---|---|
| 1-й род (Дирихле) | температура стенки, К | `T_стенки = T_заданная(t)` |
| 2-й род (Нейман) | тепловой поток в тело `q`, Вт/м² | `T_стенки = T_cell + q·h/(2λ)` |
| 3-й род (Робин) | среда: `T_среды`, К, и `α`, Вт/(м²·К) | `α(T_среды − T_стенки) = 2λ(T_стенки − T_cell)/h` |

Для грани со стороны `x = min` это даёт `SecDerT = 2·(T_cell − T_стенки)/h²`,
для грани со стороны `x = max` — `SecDerT = 2·(T_стенки − T_cell)/h²`.

Пределы 3-го рода: `α → ∞` вырождается в 1-й род (`T_стенки → T_среды`),
`α = 0` — в нулевой поток. Значения могут быть константами **или функциями времени**
(по ключу из словаря), заданными на всю грань **или у каждой ячейки грани**.

**Откуда множитель 2 и формулы стенки.** Ячейка — это центр, отстоящий от стенки на
`h/2`, а `SecDerT` по определению делится на `h²` (полный шаг). Поэтому разность
температур «центр ↔ стенка» делится на реальное расстояние `h/2`, а не на `h`:

```
SecDerT = (T_cell − T_стенки) / (h/2) / h = 2·(T_cell − T_стенки) / h²
```

Для 2-го рода тот же скачок температуры даёт поток: `q = 2λ(T_стенки − T_cell)/h`.
То же самое в терминах фиктивной ячейки: `T_ghost` ставится в точку-зеркало `x = −h/2`,
и линейная интерполяция по стенке даёт `T_ghost = 2·T_стенки − T_cell`.
Аппроксимация первого порядка по `h`, но линейное поле воспроизводит точно.

---

## 2. Требования и запуск

* .NET SDK **10.0** (проект `net10.0`);
* пакеты NuGet: `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent` версии `12.1.4`;
* Linux/Windows/macOS с графической сессией (нужно окно; для Linux — `DISPLAY`).

```bash
cd fluid_dynamics/ThermConduct
dotnet restore
dotnet build
dotnet run
```

В репозиторий не попадают каталоги `obj/` и `bin/` (см. `.gitignore`).
Условия эксперимента задаются в `conditions.json` (файл копируется рядом с исполняемым
файлом при сборке, см. `ThermConduct.csproj`).

---

## 3. Структура проекта

| Файл | Строк | Назначение |
|---|---:|---|
| `Program.cs` | 103 | точка входа: условия из conditions.json → объект сетки → окно → запуск расчёта с делегатом в интерфейс |
| `MeshConditions.cs` | 684 | чтение conditions.json (сетка, метод, начальные и граничные условия), `CreateMesh()` (namespace `Program`) |
| `ThermConductSim.cs` | 715 | расчётное ядро: `Cell`, `Face`, `Mesh`, `BoundaryKind`+`BoundaryCondition` рядом, делегаты, `CellStepContext` (namespace `ThermConductSim`) |
| `ThermalFunctions.cs` | 156 | библиотека функций и методов: словари `BoundaryConditions`, `InitialTemperatures`, `CellUpdateMethods` — вызов по ключу (namespace `ThermConductSim`) |
| `BoundaryCondition.cs` | 103 | ГУ 1–3-го рода: вид, функции времени, температура стенки (namespace `ThermConductSim`) |
| `ThermSliceViewer.cs` | 993 | только интерфейс: `MainWindow`, `LaplaceSumChart`, `App`, `CalcSettings` (namespace `ThermSliceViewer`) |
| `conditions.json` | 84 | единый файл условий: сетка, метод, начальное поле, граничные условия |
| `ThermConduct.csproj` | — | целевая платформа, ссылки на Avalonia, копирование conditions.json в каталог сборки |

Зависимость односторонняя: `Program` → {`ThermConductSim`, `ThermSliceViewer`},
`ThermConductSim` ничего не знает про UI (связь только через делегат `CalcUpdateHandler`),
`ThermSliceViewer` ничего не знает про `Mesh` — общается только событиями и методами `Show…`.

---

## 4. Расчётное ядро — `namespace ThermConductSim`

### 4.1 Функции и методы — `ThermalFunctions`

Все функции условий эксперимента хранятся в словарях и вызываются (копируются)
по строковому ключу, который задаётся в `conditions.json`:

| Словарь | Тип значения | Ключ в json | Назначение |
|---|---|---|---|
| `BoundaryConditions` | `Func<double, double>` | поля ГУ | значение ГУ от времени, с: температура стенки/среды, К, поток, Вт/м², α, Вт/(м²·К) |
| `InitialTemperatures` | `InitialTemperatureDelegate` | `initial.function` | начальная температура `T(x, y, z)` от координат центра ячейки, м |
| `CellUpdateMethods` | `CellUpdateDelegate` | `method` | метод подсчёта теплопередачи: один шаг по времени для ячейки |

Неизвестный ключ — `InvalidDataException` со списком доступных ключей. Константное ГУ —
частный случай: функция, возвращающая одно число (загрузчик оборачивает сам).

Делегаты (в `ThermConductSim.cs`):

```csharp
public delegate double CellUpdateDelegate(Cell cell, in CellStepContext step, int stepIndex);
public delegate double InitialTemperatureDelegate(double x, double y, double z);
```

`CellStepContext` — параметры шага (Δt, a, hx, hy, hz, свойство `ADt = a·Δt`),
передаётся в горячем цикле по ссылке (`in`).

### 4.2 `BoundaryCondition` — граничное условие (файл `BoundaryCondition.cs`)

| Член | Назначение |
|---|---|
| `Kind` | вид: `BoundaryKind.Dirichlet` (1), `Neumann` (2), `Robin` (3) |
| `Dirichlet(Func<double,double>)` | 1-й род: температура стенки, К |
| `Neumann(Func<double,double>)` | 2-й род: тепловой поток в тело, Вт/м² |
| `Robin(Func<double,double>, Func<double,double>)` | 3-й род: температура среды, К, и α, Вт/(м²·К) |
| `WallTemperature(cellTemp, time, lambda, h)` | температура стенки по текущему состоянию (формулы — в п. 1); её ядро кладёт в фиктивную ячейку-зеркало |

Значения — функции времени (константа — вырожденный случай). Один экземпляр может
обслуживать всю грань (равномерное ГУ), а может — одну ячейку грани (поэлементная
плоскость, см. `MeshConditions.ReadSpecPlane`).

### 4.3 `Cell` — ячейка сетки

Хранит **только** свою температуру, температуру с предыдущего шага (для
многошаговых методов) и шесть ограничивающих её граней; `C`, `h`, `Δt` внутрь не
копируются — методам они передаются через `CellStepContext`.

| Член | Тип | Назначение |
|---|---|---|
| `Temp` | `public double` | температура в центре ячейки, К |
| `PrevTemp` | `public double` | температура с предыдущего шага, К (Дюфорт–Франкел) |
| `UpdateMethod` | `public CellUpdateDelegate { get; set; }` | **копия метода подсчёта теплопередачи из словаря по ключу из json**; сетка присваивает её каждой ячейке |
| `XPast` | `public Face { get; internal set; }` | грань со стороны −X; у ячейки `x = 0` — внешняя грань куба |
| `XNext` | `public Face { get; internal set; }` | грань со стороны +X; у ячейки `x = X−1` — внешняя |
| `YPast`, `YNext` | `public Face { get; internal set; }` | то же по оси Y |
| `ZPast`, `ZNext` | `public Face { get; internal set; }` | то же по оси Z |
| `Cell(double temp = 0.0)` | конструктор | создаёт ячейку с температурой; грани вписывает сетка |
| `UpdateT(in CellStepContext, int stepIndex)` | `public double` | один шаг по времени через делегат `UpdateMethod`; возвращает приращение `ΔT` |

Грани — **отдельные поля, а не массив** (та же логика, что `Face.PastCell`/`Face.NextCell`):

* ссылки лежат прямо в объекте ячейки, поэтому нет ни второго объекта `Face[6]`, ни его
  заголовка: на `32³` это на **2,5 МБ и 32 768 объектов меньше**, чем с массивом граней
  (измерено при первой версии; с тех пор у `Cell` добавились только `PrevTemp` и ссылка
  на метод расчёта);
* в горячем цикле обращение к грани — прямая загрузка поля по фиксированному
  смещению: не нужно вычислять индекс `2*axis + …` и проверять границу массива;
* имена полей несут смысл («past»/«next» и ось), а не индекс — перепутать грань сложно;
* `internal set` — записывать грани может только ядро (`Mesh.CreateFaces`), снаружи набор
  граней неизменяем. Ячейка остаётся «глупой»: она только читает `SecDerT` своих граней
  и отдаёт себя делегату `UpdateMethod`.

### 4.4 `Face` — грань между двумя центрами ячеек

Грань — общая для двух соседних ячеек, поэтому она хранит **ссылки на них** и сама
пересчитывает вторую производную температуры по нормали.

| Член | Тип | Назначение |
|---|---|---|
| `PastCell` | `public readonly Cell?` | ячейка со стороны «past» (−нормаль); `null` у внешней грани `{x, y, z} = min` |
| `NextCell` | `public readonly Cell?` | ячейка со стороны «next» (+нормаль); `null` у внешней грани `{x, y, z} = max` |
| `Boundary` | `public readonly BoundaryCondition?` | ГУ внешней грани (1–3-го рода, возможно зависящее от времени); `null` у внутренних |
| `SecDerT` | `public double` | `(T_{i+1} − T_i) / h²` вдоль нормали грани, К/м² |
| `IsBoundary` | `public bool { get; }` | `true`, если грань внешняя (работает ГУ) |
| `Face(Cell, Cell, double cellSize)` | конструктор | внутренняя грань; `Boundary` = null |
| `Face(Cell?, Cell?, BoundaryCondition?, double cellSize)` | конструктор | грань с ГУ; `ArgumentException`, если ячеек нет вовсе |
| `UpdateSecDerT(double time, double lambda)` | метод | пересчёт `SecDerT` по текущим температурам, времени (для ГУ-функций) и λ |

Грань хранит шаг сетки **своей оси** (`cellSize` в конструкторе) — шаги по осям
могут различаться.

```csharp
public void UpdateSecDerT(double time, double thermalConductivity)
{
    double h2 = _cellSize * _cellSize;

    if (PastCell != null && NextCell != null)
    {
        SecDerT = (NextCell.Temp - PastCell.Temp) / h2;             // внутренняя грань
        return;
    }

    Cell cell = (PastCell ?? NextCell)!;
    double wallTemp = Boundary!.WallTemperature(cell.Temp, time, thermalConductivity, _cellSize);

    if (PastCell != null)
        SecDerT = 2.0 * (wallTemp - cell.Temp) / h2;                // внешняя грань max
    else
        SecDerT = 2.0 * (cell.Temp - wallTemp) / h2;                // внешняя грань min
}
```

### 4.5 `CalcUpdateHandler` — делегат обратного вызова

```csharp
public delegate void CalcUpdateHandler(Mesh mesh, int iteration, double time, double laplacianSum);
```

`laplacianSum` — сумма по всем ячейкам сумм вторых производных температуры по осям
(каждая — трёхточечная центральная разность `(T_{i+1} − 2T_i + T_{i−1}) / h²` второго
порядка точности), К/м². В установившемся процессе `∇²T = 0` в каждой ячейке, поэтому
сумма стремится к нулю — это мера отклонения текущего состояния от стационарного.
Вызывается **на том потоке, где идёт расчёт**, поэтому в обработчике нужно сделать копию
данных (`mesh.TempSnapshot()`) и передать её в UI-поток (`Dispatcher.UIThread.Post`).

### 4.6 `Mesh` — параллелепипед из ячеек и граней

#### Поля и свойства

| Член | Тип | Назначение |
|---|---|---|
| `DefaultCellSize` | `const double = 0.001` | шаг сетки по умолчанию, м |
| `DefaultThermalConductivity` | `const double = 45.0` | λ по умолчанию, Вт/(м·К) |
| `CellSizeX`, `CellSizeY`, `CellSizeZ` | `double { get; }` | шаги сетки по осям, м |
| `MinCellSize` | `double { get; }` | наименьший шаг (для числа Куранта) |
| `CellSizes` | `double[] { get; }` | `[hx, hy, hz]`, м (копия) |
| `ThermalConductivity` | `double { get; }` | λ, Вт/(м·К) — для ГУ 2-го и 3-го рода |
| `Time` | `double { get; private set; }` | текущее время расчёта, с |
| `Iteration` | `int { get; private set; }` | номер последней выполненной итерации |
| `ThermalDiffusivity` | `double { get; private set; }` | `a` последнего расчёта, м²/с |
| `WasStopped` | `bool { get; }` | последний расчёт был прерван вызовом `StopCalc()` |
| `_nx, _ny, _nz` | `int` | размеры сетки по осям |
| `_cellList` | `Cell[X, Y, Z]` | все ячейки |
| `_faceXList`, `_faceYList`, `_faceZList` | `Face[X+1,Y,Z]`, `Face[X,Y+1,Z]`, `Face[X,Y,Z+1]` | все грани |
| `_stopRequested`, `_wasStopped` | `volatile bool` | флаги остановки (меняются из другого потока) |

Ячейки и грани наружу не выдаются — снаружи доступны только `Shape()`, `TempSnapshot()`
и `SetTemps(...)`, поэтому нельзя испортить структуру сетки извне.

#### Конструктор — единственное место, где задаются начальные и граничные условия

```csharp
public Mesh(double[,,] initTemp,
            BoundaryCondition[,] xPast, BoundaryCondition[,] xNext,
            BoundaryCondition[,] yPast, BoundaryCondition[,] yNext,
            BoundaryCondition[,] zPast, BoundaryCondition[,] zNext,
            double cellSizeX, double cellSizeY, double cellSizeZ,
            CellUpdateDelegate updateMethod,
            double thermalConductivity = DefaultThermalConductivity)
```

| Параметр | Размерность | Смысл |
|---|---|---|
| `initTemp` | `[X, Y, Z]` | начальная температура каждой ячейки, К |
| `xPast` | `[Y, Z]` | ГУ грани `x = min` (у каждой ячейки плоскости — свой экземпляр) |
| `xNext` | `[Y, Z]` | ГУ грани `x = max` |
| `yPast` | `[X, Z]` | ГУ грани `y = min` |
| `yNext` | `[X, Z]` | ГУ грани `y = max` |
| `zPast` | `[X, Y]` | ГУ грани `z = min` |
| `zNext` | `[X, Y]` | ГУ грани `z = max` |
| `cellSizeX/Y/Z` | — | шаги сетки по осям, м; должны быть `> 0` |
| `updateMethod` | — | метод подсчёта теплопередачи: **копируется в каждую ячейку** |
| `thermalConductivity` | — | λ, Вт/(м·К); должна быть `> 0` |

Размеры сетки берутся из `initTemp`, размерности всех шести плоскостей ГУ проверяются.
Исключения: `ArgumentNullException` (массив/делегат `null`),
`ArgumentOutOfRangeException` (пустая сетка, неположительные шаги или λ),
`ArgumentException` (несовпадение размерности).

Внутри конструктор делает две вещи (именно в таком порядке):

1. создаёт ячейки (`CreateCells`) — температура и копия `updateMethod` в каждую ячейку;
2. создаёт грани (`CreateFaces`) — каждая грань получает ссылки на соседние ячейки
   (у шести внешних плоскостей одна из ссылок `null`, а вместо неё — `BoundaryCondition`
   из соответствующей плоскости) и записывается в поля `XNext`/`XPast`, `YNext`/`YPast`
   или `ZNext`/`ZPast` обеих ячеек.

Порядок важен: грань ссылается на ячейки, поэтому ячейки создаются первыми.
Значения `SecDerT` до первого `StartCalc()` не нужны: они пересчитываются в начале
расчёта и затем на каждой итерации.

#### Публичные методы

| Метод | Что делает |
|---|---|
| `int[] Shape()` | размеры сетки `{X, Y, Z}` (новый массив — можно менять свободно) |
| `void SetTemps(double[,,] temp)` | присвоить новые температуры всем ячейкам (кнопка «Сброс»); размерность проверяется, `PrevTemp` синхронизируется, ГУ не меняются |
| `double[,,] TempSnapshot()` | копия температур всех ячеек — безопасно передавать в другой поток |
| `void StartCalc(int numberIter, double deltaTime, double thermalDiffusivity, CalcUpdateHandler? updateHandler, int updatePeriod)` | выполнить расчёт (см. 4.7); `updateHandler` может быть `null` |
| `void StopCalc()` | попросить остановиться; безопасно из другого потока (кнопка «Остановить») |

`StartCalc` проверяет аргументы: `numberIter >= 0`, `deltaTime > 0`,
`thermalDiffusivity > 0` (иначе `ArgumentOutOfRangeException`),
`updatePeriod < 1` молча заменяется на `1`.

#### Приватные методы

| Метод | Что делает |
|---|---|
| `CreateCells(initTemp, updateMethod)` | создаёт ячейки — температура и копия метода расчёта |
| `CreateFaces(xPast, …, zNext)` | создаёт грани со ссылками на соседние ячейки; у внешних плоскостей вместо одной из ячеек — ГУ |
| `CreateFace(pastCell, nextCell, boundary, cellSize, axis)` | создаёт одну грань и присваивает её в поля ячеек по оси |
| `UpdateFaces()` | пересчитывает `SecDerT` на всех гранях по текущему времени и λ |
| `UpdateCells(in step, stepIndex)` | вызывает `Cell.UpdateT` у всех ячеек |
| `SumLaplacian()` | сумма по ячейкам сумм вторых производных по осям `(SecDerT_next − SecDerT_past)` — отклонение от установившегося процесса, К/м² |
| `CheckShape(BoundaryCondition[,] …)` / `CheckShape(double[,,] …)` | проверка размерности плоскостей ГУ и температур |

### 4.7 Индексация: какая грань какой ячейке принадлежит

* `_faceXList[x, y, z]` — плоскость **между** ячейками `x−1` и `x`; `_faceXList[0, …]` —
  внешняя грань `x = min`, `_faceXList[X, …]` — внешняя грань `x = max` (аналогично Y и Z);
* внутри сетки `SecDerT = (T[x, y, z] − T[x−1, y, z]) / hx²`;
* ячейка `(x, y, z)` держит шесть ссылок на эти плоскости:
  `XPast = FaceX[x]`, `XNext = FaceX[x+1]`, `YPast = FaceY[y]`, `YNext = FaceY[y+1]`,
  `ZPast = FaceZ[z]`, `ZNext = FaceZ[z+1]`;
* поэтому `XNext.SecDerT − XPast.SecDerT = (T_{i+1} − 2T_i + T_{i−1}) / hx²` —
  обычная трёхточечная аппроксимация второй производной (то же по Y и Z со своими h);
* грань — **общий объект** для двух ячеек: одна и та же `Face` лежит в `Next`-поле
  «past»-ячейки и в `Past`-поле «next»-ячейки (это делает `Mesh.CreateFace`), поэтому
  `SecDerT` считается один раз на итерацию и обе ячейки видят одно и то же значение;
* у внешней грани одна из ссылок `Face.PastCell` / `Face.NextCell` равна `null`, и вторая
  производная считается через граничное условие (фиктивная ячейка-зеркало):
  `SecDerT = 2·(T_стенки − T_cell) / h²` со стороны `max` и
  `SecDerT = 2·(T_cell − T_стенки) / h²` со стороны `min`.

### 4.8 Что происходит за одну итерацию (`StartCalc`)

1. обнуляются `Time` и `Iteration`, запоминается `ThermalDiffusivity = a`,
   сбрасываются флаги остановки;
2. `UpdateFaces()` по начальным температурам;
3. вызов `updateHandler(this, 0, 0, SumLaplacian())` — «нулевая» итерация для отрисовки
   исходного поля вместе с его отклонением от установившегося процесса;
4. цикл `iter = 1…numberIter`:
   1. если пришёл `StopCalc()` — выставить `WasStopped` и выйти из цикла;
   2. `UpdateCells(step, iter − 1)` — метод подсчёта теплопередачи для всех ячеек
      (по `SecDerT` граней, посчитанным на предыдущем шаге);
   3. `Time = iter·Δt`, `Iteration = iter`;
   4. `UpdateFaces()` — вторые производные уже по новым температурам и текущему
      времени (ГУ-функции времени перевычисляются каждый шаг);
   5. если `iter % updatePeriod == 0` или это последняя итерация — вызвать
      `updateHandler` со значением `SumLaplacian()` этого состояния.

Порядок «сначала все ячейки, потом все грани» важен: температуры ячеек читаются только
методами подсчёта теплопередачи, а грани пересчитываются после того, как все ячейки
обновлены, поэтому схема остаётся явной и не зависит от направления обхода. Число
`UpdateFaces()` за расчёт то же, что в варианте «грани в начале шага», но `Σ∇²T`
передаётся уже по тому состоянию, которое показывает интерфейс (отклонение «в данный
момент»).

---

## 5. Пример использования

```csharp
using ThermConductSim;

const int nx = 20, ny = 20, nz = 20;

// Вспомогательная функция: плоскость, заполненная одним ГУ
static BoundaryCondition[,] Fill(int d0, int d1, BoundaryCondition condition)
{
    var a = new BoundaryCondition[d0, d1];
    for (int i = 0; i < d0; i++)
        for (int j = 0; j < d1; j++)
            a[i, j] = condition;
    return a;
}

// 1. Начальные условия: температура каждой ячейки
double[,,] temps = new double[nx, ny, nz];
for (int x = 0; x < nx; x++)
    for (int y = 0; y < ny; y++)
        for (int z = 0; z < nz; z++)
            temps[x, y, z] = 300.0;

// 2. Граничные условия (размерности см. в 4.6)
var hotYZ = Fill(ny, nz, BoundaryCondition.Dirichlet(_ => 400.0));   // x = min — нагрев
var coldYZ = Fill(ny, nz, BoundaryCondition.Dirichlet(t => 300 + 10 * t)); // …или функция времени
var insulXZ = Fill(nx, nz, BoundaryCondition.Neumann(_ => 0.0));     // y — теплоизоляция
var airXY = Fill(nx, ny, BoundaryCondition.Robin(_ => 300.0, _ => 800.0)); // z — теплообмен с воздухом

// 3. Метод расчёта — из словаря по ключу (как в conditions.json)
CellUpdateDelegate method = ThermalFunctions.GetCellUpdateMethod("explicit");

// 4. Сетка создаётся вместе с условиями, h = 0.001 м по всем осям
Mesh mesh = new(temps,
    xPast: hotYZ, xNext: coldYZ,
    yPast: insulXZ, yNext: insulXZ,
    zPast: airXY, zNext: airXY,
    cellSizeX: 0.001, cellSizeY: 0.001, cellSizeZ: 0.001,
    updateMethod: method,
    thermalConductivity: 45.0);

// 5. Расчёт: 1000 итераций, Δt = 1 с, a = 1.5e-5 м²/с, интерфейс — раз в 10 шагов
mesh.StartCalc(1000, 1.0, 1.5e-5, (m, iter, time, laplacianSum) =>
{
    double[,,] slice = m.TempSnapshot();   // копия — можно отдавать в другой поток
    Console.WriteLine($"iter={iter} t={time} Σ∇²T={laplacianSum:E3}");
}, updatePeriod: 10);

// 6. Результат и повторный запуск
double[,,] result = mesh.TempSnapshot();
mesh.SetTemps(temps);                     // вернуть исходное поле
```

Проверка модели на простых случаях (использовалась при отладке ядра):

| Сценарий | Ожидание | Результат |
|---|---|---|
| однородное поле, все ГУ равны ему же | поле не меняется | `max\|ΔT\| = 0` |
| Нейман q=0 на всех гранях | поле не меняется | `max\|ΔT\| = 0` |
| Нейман ±q на противных гранях | линейный профиль, перепад `q·L/λ` | совпало (~3 К) |
| Робин с α→∞ | совпадает с Дирихле `T_среды` | совпало |
| линейное поле, ГУ из той же линейной функции | стационарно во всех ячейках, включая пристеночные | `max\|ΔT\| = 0` (точно) |
| Дирихле, зависящее от времени (функция `step_400`) | до 5 с поле не меняется, после — греется | совпало |

---

## 6. Интерфейс — `namespace ThermSliceViewer`

### 6.1 `MainWindow : Window`

Окно — только интерфейс: создаётся по начальному полю температур и шагам сетки
(`new MainWindow(double[,,] initialTemps, double[] cellSizes)`), о расчётном ядре ничего
не знает и само ничего не считает. О нажатиях кнопок оно сообщает событиями `StartRequested`
(с проверенными параметрами `CalcSettings`), `StopRequested`, `ResetRequested`, а данные
расчёта принимает методами `ShowFrame`, `ShowReset`, `ShowFinished`, `ShowError`.

**Что на экране** (сверху вниз, `Grid` со строками `Auto, *, Auto`):

1. **Панель срезов**: `ComboBox` «Нормаль среза» (`Ось X (плоскость YZ)`, `Ось Y (плоскость XZ)`,
   `Ось Z (плоскость XY)`, по умолчанию Z), `Slider` «Индекс слоя» (0…N-1, шаг 1),
   подпись «Срез i / N-1, T_min…T_max».
2. **`Image`** — карта среза, растягивается по окну, интерполяция отключена
   (`BitmapInterpolationMode.None`), чтобы пиксели оставались «квадратиками».
3. **Нижняя панель**: строка параметров (`Итераций`, `Шаг dt`, `Число Куранта`,
   `Обновлять каждые … итераций`), строка кнопок (`Старт расчёта`, `Остановить`, `Сброс`),
   строка состояния и график `LaplaceSumChart` высотой 190 px.

Число Куранта вводится по наименьшему шагу сетки: `C = a·Δt/h²`, `h = min(hx, hy, hz)`.
Предел устойчивости явной схемы в этих терминах: `C ≤ 1 / (2·h²·(1/hx²+1/hy²+1/hz²))`
(равномерная сетка — `C ≤ 1/6`); предупреждение выводится в строку состояния.

**Поля**

| Поле | Назначение |
|---|---|
| `_nx, _ny, _nz` | размеры сетки — из формы начального поля температур |
| `_cellSizes` | шаги сетки по осям [hx, hy, hz], м — для подписей и вычисления `a = C·h²/Δt` |
| `_hMin` | наименьший шаг сетки, м |
| `_stableCourant` | предел устойчивости явной схемы по введённому C |
| `_temps` | текущий снимок температур `[X, Y, Z]`, по нему рисуется срез |
| `_colorMin, _colorMax` | диапазон палитры; при росте/падении температуры только расширяется (цвета не «прыгают») |
| `_axisComboBox, _sliceSlider, _sliceInfoText, _sliceImageView` | элементы панели срезов |
| `_iterationsBox, _deltaTimeBox, _courantBox, _updatePeriodBox` | поля ввода параметров (по умолчанию `200`, `1,0`, `0,15`, `1`) |
| `_startButton, _stopButton, _resetButton, _statusText, _chart` | кнопки, строка состояния, график |
| `_running`, `_closed`, `_lastCourant`, `_warning` | идёт ли расчёт / закрыто ли окно / последнее `C` / предупреждение о неустойчивости схемы |

**Методы** — `OnAxisChanged`, `UpdateSliceView`, `TryReadSettings`, `OnStartClicked`,
`OnStopClicked`, `OnResetClicked`, `ShowFrame`, `ShowReset`, `ShowFinished`, `ShowError`,
`SetRunning`, `CheckSnapshotShape`, `UpdateStatus`, `MakeLabel`, `MakeInputBox`,
`FormatTemperature`, `FormatTempRange`, `FormatSigma`, `FormatCellSizes` («h = 1×1×1 мм»),
`SumInverseSquares`, `TryParseDouble`, `FindRange`, `TemperatureToRgb` — логика прежняя,
изменены только шаги сетки (массив вместо одного h) и критерий устойчивости.

**Многопоточность.** Расчёт запускает `Program.cs` в `Task.Run`; делегат `CalcUpdateHandler`
вызывается на потоке расчёта, поэтому снимок `TempSnapshot()` делается там же
(`Program.OnMeshUpdated`), а вся работа с визуальными элементами — только в `ShowFrame`
на UI-потоке через `Dispatcher.UIThread.Post`. После `ShowFinished` (`_running == false`)
отложенные кадры больше не затирают итоговое сообщение в строке состояния.

### 6.2 `LaplaceSumChart : Control`

Собственный элемент отрисовки (без сторонних библиотек графиков).

| Член | Назначение |
|---|---|
| `AddPoint(int iteration, double time, double laplacianSum)` | добавить точку и вызвать `InvalidateVisual()` |
| `Clear()` | очистить график |
| `PointCount`, `LastValue`, `LastIteration`, `ValueAt(int)` | доступ к данным (используется автотестом) |
| `Render(DrawingContext)` | фон, оси, сетка 5×5, подписи (по X — номер итерации и время, по Y — `Σ∇²T`, К/м²), ломаная по точкам и текущее значение справа сверху; без данных — подсказка «нет данных — нажмите «Старт расчёта»». Шкала по Y строится от минимума до максимума (значения могут быть отрицательными — сумма без модулей), горизонтальная ось рисуется на уровне нуля — установившегося процесса |

### 6.3 `App : Application`

`Initialize()` подключает тёмную тему `FluentTheme`; главное окно создаётся в
`Program.BuildAvaloniaApp()`.

---

## 7. `Program.cs` — условия, объект расчёта и интерфейса

`Main` состоит из трёх шагов: условия из conditions.json, объект сетки, запуск Avalonia.

```csharp
MeshConditions conditions = MeshConditions.Load("conditions.json");
Mesh mesh = conditions.CreateMesh();
BuildAvaloniaApp(conditions, mesh).StartWithClassicDesktopLifetime(args);
```

В `AfterSetup` создаётся объект интерфейса `new MainWindow(conditions.Initial, mesh.CellSizes)`
и события окна связываются с расчётным ядром:

| Событие окна | Что делает `Program.cs` |
|---|---|
| `StartRequested(settings)` | считает `a = C·h²_min/Δt` и выполняет `Task.Run(() => mesh.StartCalc(...))` с делегатом `OnMeshUpdated` и параметрами `CalcSettings` |
| `StopRequested` | `mesh.StopCalc()` |
| `ResetRequested` | `mesh.SetTemps(conditions.Initial)`, затем `window.ShowReset(mesh.TempSnapshot())` |

Делегат `OnMeshUpdated(window, mesh, iteration, time, laplacianSum)` (тип `CalcUpdateHandler`)
вызывается на потоке расчёта: делает `mesh.TempSnapshot()` и через `Dispatcher.UIThread.Post`
передаёт снимок в `window.ShowFrame(...)`. После завершения `Task.Run` вызывается
`window.ShowFinished(...)`, при исключении — `window.ShowError(...)`.

### 7.1 Формат `conditions.json`

Читает `MeshConditions.Load`. Разрешены комментарии `//`, `/* */` и висячие запятые;
все размеры массивов сверяются с сеткой, ошибки сообщаются с именем файла и поля;
функции ищутся по ключу в словарях `ThermalFunctions` (неизвестный ключ — ошибка
со списком доступных).

| Поле | Смысл |
|---|---|
| `grid.cells` | `[X, Y, Z]` — количество ячеек по осям (обязательно, если нет `initial.cells`) |
| `grid.size` | `[Lx, Ly, Lz]` — линейные размеры области по осям, м → `h = size/cells` |
| `grid.cellSize` | альтернатива `size`: число (один шаг на все оси) или `[hx, hy, hz]`, м |
| `thermalConductivity` | λ, Вт/(м·К), для ГУ 2-го и 3-го рода (по умолчанию `45`) |
| `method` | ключ метода расчёта из `ThermalFunctions.CellUpdateMethods` (по умолчанию `"explicit"`) |
| `initial.temperature` | одно число — **равномерное** начальное поле, К (по умолчанию `300`) |
| `initial.function` | ключ начального распределения `T(x, y, z)` из `ThermalFunctions.InitialTemperatures` |
| `initial.cells` | массив `[X][Y][Z]`: своя температура у каждой ячейки (заменяет `temperature`) |
| `boundaries.xPast … zNext` | по одному ГУ на каждую из шести граней; пропуск — `300` К (1-й род) |

Граница — число, массив или объект:

```
300.0                          // 1-й род, константа на всю грань
[[300, 300], [300, 300]]       // 1-й род, своя константа каждой ячейке грани
{ "type": "dirichlet", "value": 400.0 }                     // 1-й род, константа
{ "type": "dirichlet", "function": "sin_pulse" }            // 1-й род, функция времени
{ "type": "neumann",   "value": 0.0 }                       // 2-й род, поток Вт/м²
{ "type": "robin",     "value": [300.0, 800.0] }            // 3-й род, [T среды, α]
{ "type": "dirichlet", "values":   [[...], [...]] }         // своё число каждой ячейке
{ "type": "dirichlet", "functions": [["k1", "k2"], [...]] } // своя функция каждой ячейке
```

* `type` — `dirichlet` | `neumann` | `robin` (регистр не важен);
* ровно одно поле значений: `value` / `values` / `function` / `functions`;
* элемент — число или ключ из `ThermalFunctions.BoundaryConditions`; у Робина элемент —
  пара `[T_среды, α]` (числа или ключи);
* размерность `values`/`functions` — форма грани: x — `[Y][Z]`, y — `[X][Z]`, z — `[X][Y]`.

Демонстрационные значения (см. `conditions.json`): сетка `16³`, область `16` мм
(`h = 1` мм), начальное поле равномерное `300` К, `λ = 45` Вт/(м·К), метод `"explicit"`;
грань `x = min` — циклическая функция времени (Дирихле `sin_pulse`: импульс `300…400` К,
период `10` с), грань `x = max` — постоянный градиент температуры
`∂T/∂x = −10` К/мм (Нейман, `q = λ·∂T/∂x = −450000` Вт/м²), грани `y` и `z` — нулевой
градиент (Нейман `0`, теплоизоляция). Чтобы сменить условия эксперимента, достаточно
поправить `conditions.json` (и, при нужде, словари `ThermalFunctions`) — расчётное ядро
и интерфейс менять не нужно.

---

## 8. Ограничения и особенности реализации

1. **ГУ 1–3-го рода, константы или функции времени** — но не функции координат вдоль
   грани: пространственное изменение задаётся поклеточно (`values`/`functions`).
2. **Аппроксимация на внешней грани — первого порядка** по `h` (фиктивная ячейка-зеркало,
   множитель 2 — учёт расстояния `h/2` до стенки). Для линейного поля она **точна**
   (включая пристеночные ячейки), на гладких профилях даёт у стенки ошибку `O(h)`,
   тогда как внутри — `O(h²)`.
3. **Шаги сетки могут различаться по осям**; число Куранта в интерфейсе — одно, по
   наименьшему шагу.
4. **Схема "explicit" явная**: при `a·Δt·(1/hx²+1/hy²+1/hz²) > 1/2` решение может
   «разлететься» (интерфейс предупреждает, но не запрещает расчёт). Схема
   "dufort-frankel" устойчива при любом шаге.
5. **Память**: `X·Y·Z` ячеек + `(X+1)YZ + X(Y+1)Z + XY(Z+1)` граней; каждый вызов
   `TempSnapshot()` копирует весь массив температур (для `32³` это ~260 КБ).
   Промежуточных массивов нет: все связи — прямые ссылки в полях (`Cell.XNext`,
   `Face.PastCell`). По сравнению с первой версией у `Cell` добавились `PrevTemp`
   (8 Б) и ссылка на метод расчёта, у `Face` — ссылка на `BoundaryCondition` и шаг
   своей оси: на `32³` добавка — порядка сотен килобайт, несмотря на миллион граней.
6. **Один расчёт за раз**: `StartCalc` синхронный и занимает вызвавший его поток,
   поэтому в UI он запускается через `Task.Run`.
7. **Условия читаются из JSON** (`conditions.json`); результаты расчёта (срезы, график)
   не сохраняются — только показываются в окне.

## 9. Возможные направления развития

* ГУ-функции координат и времени вместе: `Func<(x, y, z, t), double>` (сейчас — время
  или поклеточный перебор);
* аппроксимация внешней грани второго порядка точности;
* неявная схема (потребует решения СЛАУ) и автоматический подбор `Δt` из условия устойчивости;
* экспорт срезов в PNG/CSV и сохранение истории `Σ∇²T`;
* распараллеливание `UpdateFaces`/`UpdateCells` (`Parallel.For`) — сейчас всё однопоточное.
