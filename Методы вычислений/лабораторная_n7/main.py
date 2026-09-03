import numpy as np
from scipy.optimize import fsolve
from matplotlib import pyplot as plt
from typing import Callable

def Newton(f:list[Callable[[list[float]], float]], a:list[float], b:list[float], e:float, h:list[float], f_yakobi:list[list[Callable[[list[float]], float]]] = None, iter:int = 100) -> list[float]:
    a = np.array(a, np.float64)
    b = np.array(b, np.float64)
    x = np.array(a, np.float64)
    l = len(x)
    fx = np.zeros([l], np.float64)
    yakobi = np.zeros([l, l], np.float64)
    for count in range(iter):
        for i in range(l):
            fx[i] = f[i](x)
        if min(x - a) < 0 or max(x - b) > 0:
            return None, count, 'err'
        if np.linalg.norm(fx) < e:
            return x, count, 'suc'
        if f_yakobi:
            for i in range(l):
                for j in range(l):
                    yakobi[i][j] = f_yakobi[i][j](*x)
        else:
            for i in range(l):
                for j in range(l):
                    dfp = np.array(x, np.float64)
                    dfp[j] += h[j]
                    dfm = np.array(x, np.float64)
                    dfm[j] -= h[j]
                    yakobi[i][j] = (f[i](dfp) - f[i](dfm)) / (2 * h[j])
        x = x + np.linalg.solve(yakobi, (-1) * fx)
    return x, iter, 'max'

def chord(f:Callable[[float], float], a:float, b:float, e:float, iter:int = 100) -> float:
    for count in range(iter):
        x = a - (b - a) / (f(b) - f(a)) * f(a)
        if abs(f(x)) < e:
            return x, count, 'suc'
        if f(a) * f(x) > 0:
            a = x
        else:
            b = x
    return x, iter, 'max'

def in_half(f:Callable[[float], float], a:float, b:float, e:float, iter:int = 100) -> float:
    for count in range(iter):
        x = (a + b) / 2
        fx = f(x)
        if abs(fx) < e:
            return x, count, 'suc'
        if f(a) * fx > 0:
            a = x
        else:
            b = x
    return x, iter, 'max'
        
def chord_Newton(f:Callable[[float], float], a:float, b:float, e:float, iter:int = 100) -> float:
    x_old = a
    x_cur = a - (b - a) / (f(b) - f(a)) * f(a) 
    for count in range(iter):
        x_new = x_cur - (x_cur - x_old) / (f(x_cur) - f(x_old)) * f(x_cur)
        if min(x - a) < 0 or max(x - b) > 0:
            return None, count, 'err'
        fx_new = f(x_new)
        if abs(fx_new) < e:
            return x_new, count, 'suc'
        x_old = x_cur
        x_cur = x_new
    return x_cur, iter, 'max'

def Halley(f:Callable[[float], float], a:float, b:float, e:float, h:float, iter:int = 100) -> float:
    x = a
    for count in range(iter):
        if x < a or x > b:
            return None, count, 'err'
        fx = f(x)
        if abs(fx) < e:
            return x, count, 'suc'
        fir_der = (f(x + h) - f(x - h)) / (2 * h)
        sec_der = (f(x - h) - 2 * f(x) + f(x + h)) / (h ** 2)
        x = x - fx * fir_der / (fir_der ** 2 - 0.5 * fx * sec_der)
    return x, iter, 'max'

def simple_iter(f:list[Callable[[list[float]], float]], a:list[float], b:list[float], e:float, w:list[list[float]], iter:int = 100) -> list[float]:
    a = np.array(a, np.float64)
    b = np.array(b, np.float64)
    w = np.array(w, np.float64)
    x = np.array(a, np.float64)
    l = len(x)
    fx = np.zeros([l], np.float64)

    for count in range(iter):
        for i in range(l):
            fx[i] = f[i](x)
        if min(x - a) < 0 or max(x - b) > 0:
            return None, count, 'err'
        if np.linalg.norm(fx) < e:
            return x, count, 'suc'
        x = x + w @ fx
    return x, iter, 'max'
    
def Zeidel(f:list[Callable[[list[float]], float]], a:list[float], b:list[float], e:float, w:list[list[float]], iter:int = 100) -> list[float]:
    a = np.array(a, np.float64)
    b = np.array(b, np.float64)
    w = np.array(w, np.float64)
    x = np.array(a, np.float64)
    l = len(x)
    fx = np.zeros([l], np.float64)
    for i in range(l):
        fx[i] = f[i](x)
    for count in range(iter):
        if min(x - a) < 0 or max(x - b) > 0:
            return None, count, 'err'
        if np.linalg.norm(fx) < e:
            return x, count, 'suc'
        for i in range(l):
            fx[i] = f[i](x)
            x[i] = x[i] + w[i] @ fx
    return x, iter, 'max'

f_1 = [lambda x: np.log(x[0]) - np.tan(x[0])]

f_2 = [lambda x: np.sin(x[0] + x[1]) - x[1] - 1.2,
       lambda x: 2 * x[0] + np.cos(x[1]) - 2]

f_yakobi = [[lambda x, y: np.cos(x + y), lambda x, y: np.cos(x + y) - 1], 
            [lambda x, y: 2, lambda x, y: -np.sin(y)]]

f1_single = lambda x: np.log(x) - np.tan(x)

# Задание 1. Отделение корней
x = np.linspace(0, 10, 10000)
f1x = f1_single(x)
f1x[abs(f1x) > 100] = np.nan
plt.plot(x, f1x)
plt.grid()
plt.show()

h = 0.001
intervals = [[4, 4.2], [7.3, 7.5]]
eps = [1e-3, 1e-6, 1e-9, 1e-12, 1e-15]

def run_method(method, a, b, e):
    if method == Newton:
        x, it, st = Newton(f_1, [a], [b], e, [h])
        return (x[0], it, st) if x is not None else (None, it, st)
    elif method == in_half:
        return in_half(f1_single, a, b, e)
    elif method == Halley:
        return Halley(f1_single, a, b, e, h)

print("Задача 1.1")
e = 1e-6
for a, b in intervals:
    print(f'В интервале [{a:.1f}, {b:.1f}]')
    for method in [Newton, in_half, Halley]:
        x, it, st = run_method(method, a, b, e)
        print(f'  {method.__name__}: x = {x:.15f}, итераций = {it}, статус: {st}')

print("\nЗадача 1.2 (максимальный корень [7.3, 7.5])")
print(f'{"ε":<12} | {"Ньютон":<18} | {"in_half":<18} | {"Halley":<18}')
print('-' * 72)
for e in eps:
    line = f'{e:<12} |'
    for method in [Newton, in_half, Halley]:
        x, it, st = run_method(method, 7.3, 7.5, e)
        line += f' {it:>3} итер ({st:<3})  |'
    print(line)

print("\nЗадача 1.3 (Метод простой итерации)")
for a, b in intervals:
    print(f'Интервал [{a}, {b}]:')
    for w in [0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7]:
        x, it, st = simple_iter(f_1, [a], [b], 1e-6, [[w]])
        if st == 'suc' and x is not None:
            print(f'  ω = {w:.1f}: x = {x[0]:.10f}, итераций = {it}')
        else:
            print(f'  ω = {w:.1f}: не сошёлся ({st})')

print("\nЗадача 1.4 (fsolve)")
for a, b in intervals:
    x0 = (a + b) / 2
    x_sol = fsolve(f1_single, x0)[0]
    print(f'Корень в [{a}, {b}]: x = {x_sol:.15f}')

print("\nЗадача 2")
def f2_system(v):
    return [f_2[0](v), f_2[1](v)]

a2, b2 = [1, -2], [1.3, -1.7]
print("Метод Ньютона:")
for e in eps:
    x, it, st = Newton(f_2, a2, b2, e, [h, h])
    if x is not None:
        print(f'  ε = {e:<8}: x = ({x[0]:.10f}, {x[1]:.10f}), итераций = {it}, статус: {st}')

x0 = [(a2[0] + b2[0]) / 2, (a2[1] + b2[1]) / 2]
x_fs = fsolve(f2_system, x0)
print(f"fsolve: x = ({x_fs[0]:.10f}, {x_fs[1]:.10f})")

print("\nЗадача 2.3 (Метод Ньютона с аналитической матрицей Якоби)")
print(f'{"ε":<12} | {"Численно (итер.)":<18} | {"Аналитически (итер.)":<18} | {"x (аналит.)":<35}')
print('-' * 88)
for e in eps:
    x_num, it_num, st_num = Newton(f_2, a2, b2, e, [h, h])
    x_an, it_an, st_an = Newton(f_2, a2, b2, e, [h, h], f_yakobi)
    line = f'{e:<12} |'
    if x_num is not None:
        line += f' {it_num:>3} итер ({st_num:<3})       |'
    else:
        line += f' {"—":>13}       |'
    if x_an is not None:
        line += f' {it_an:>3} итер ({st_an:<3})       |'
        line += f' ({x_an[0]:.10f}, {x_an[1]:.10f})'
    else:
        line += f' {"—":>13}       |'
        line += f' {"—":>35}'
    print(line)

# Задание 2. Визуализация
rng = np.random.default_rng(seed=42)
xy = rng.uniform(low=[1, -2], high=[1.3, -1.7], size=(1000, 2))
f21xy = np.array([f_2[0](pt) for pt in xy])
f22xy = np.array([f_2[1](pt) for pt in xy])

fig = plt.figure()
ax = fig.add_subplot(111, projection='3d')
surf1 = ax.plot_trisurf(xy[:, 0], xy[:, 1], f21xy, cmap='viridis', alpha=0.6, edgecolor='none')
surf2 = ax.plot_trisurf(xy[:, 0], xy[:, 1], f22xy, cmap='plasma', alpha=0.5, edgecolor='none')
ax.tricontour(xy[:, 0], xy[:, 1], f22xy - f21xy, levels=[0], colors='red', linewidths=3, zorder=10)
ax.set_xlabel('X')
ax.set_ylabel('Y')
ax.set_zlabel('Z')
plt.show()
