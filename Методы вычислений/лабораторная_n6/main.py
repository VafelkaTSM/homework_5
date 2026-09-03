import numpy as np
from scipy.integrate import quad
from typing import Callable

np.random.seed(42)

def f_var(x:float) -> float:
    '''
    Функция варианта 10.
    '''
    return np.exp(-x / np.pi) * ((np.sin(x) + np.pi) ** 2)


def analytical_var(a:float, b:float) -> float:
    '''
    Аналитический метод вычисления интеграла.
    '''
    antider = lambda x: (np.exp(-x / np.pi) * (
                         - np.pi * (np.pi**2 + 0.5) 
                         - (2 * np.pi**2 * (np.sin(x) + np.pi * np.cos(x))) / (1 + np.pi**2) 
                         - (np.pi * (2 * np.pi * np.sin(2 * x) - np.cos(2 * x))) / (2 * (1 + 4 * np.pi**2))))
    return antider(b) - antider(a)

def right_rect(f:Callable[[float], float], a:float, b:float, n:int) -> float:
    '''
    Вычисление интеграла методом правого прямоугольника.
    '''
    h = (b - a) / n
    ar_x = np.linspace(a + h, b, n)
    integr = 0
    for x in ar_x:
        integr += f(x)
    integr *= h
    return integr

def left_rect(f:Callable[[float], float], a:float, b:float, n:int) -> float:
    '''
    Вычисление интеграла методом левого прямоугольника.
    '''
    h = (b - a) / n
    ar_x = np.linspace(a, b - h, n)
    integr = 0
    for x in ar_x:
        integr += f(x)
    integr *= h
    return integr

def centre_rect(f:Callable[[float], float], a:float, b:float, n:int) -> float:
    '''
    Вычисление интеграла методом центрального прямоугольника.
    '''
    h = (b - a) / n
    ar_x = np.linspace(a + h / 2, b - h / 2, n)
    integr = 0
    for x in ar_x:
        integr += f(x)
    integr *= h
    return integr

def trapezoid(f:Callable[[float], float], a:float, b:float, n:int) -> float:
    '''
    Вычисление интеграла методом трапеции.
    '''
    h = (b - a) / n
    ar_x = np.linspace(a, b, n + 1)
    integr = f(ar_x[0]) / 2 + f(ar_x[n]) / 2
    for i in range(1, n):
        integr += f(ar_x[i])
    integr *= h
    return integr

def Simpson(f:Callable[[float], float], a:float, b:float, n:int) -> float:
    '''
    Вычисление интеграла методом Симпсона.
    '''
    if n % 2 != 0:
        n += 1
    h = (b - a) / n
    ar_x = np.linspace(a, b, n + 1)
    integr = f(ar_x[0]) + f(ar_x[n])
    integr_term = 0
    for i in range(1, n, 2):
        integr_term += f(ar_x[i])
    integr_term *= 4
    integr += integr_term
    integr_term = 0
    for i in range(2, n - 1, 2):
        integr_term += f(ar_x[i])
    integr_term *= 2
    integr += integr_term
    integr *= h / 3
    return integr

def Gauss(f: Callable[[float], float], a: float, b: float, m: int) -> float:
    """
    Вычисление интеграла методом Гаусса.
    """
    nodes = np.zeros(m)
    weights = np.zeros(m)
    
    half_m = (m + 1) // 2
    eps = 1e-15  # Заданная точность для метода Ньютона.
    
    for i in range(half_m):
        z = np.cos(np.pi * (i + 0.75) / (m + 0.5))
        
        # Метод Ньютона для получения весов.
        while True:
            p1 = 1.0
            p2 = 0.0
            
            for j in range(1, m + 1):
                p3 = p2
                p2 = p1
                p1 = ((2.0 * j - 1.0) * z * p2 - (j - 1.0) * p3) / j
            
            pp = m * (z * p1 - p2) / (z * z - 1.0)
            
            z_old = z
            z = z_old - p1 / pp
            
            if abs(z - z_old) < eps:
                break
        
        nodes[i] = -z
        nodes[m - 1 - i] = z
        
        w = 2.0 / ((1.0 - z * z) * pp * pp)
        weights[i] = w
        weights[m - 1 - i] = w

    integr = 0.0
    for i in range(m):
        x_i = 0.5 * (b - a) * nodes[i] + 0.5 * (a + b)
        integr += weights[i] * f(x_i)
        
    integr *= 0.5 * (b - a)
    
    return integr
    
def Monte_Karlo(func:Callable[[list[float]], float], ar_a:list[float], ar_b:list[float], n:int) -> float:
    """
    Интегрирование Монте-Карло для n-мерного прямоугольника.
    """
    l = len(ar_a)
    ar_a = np.array(ar_a)
    ar_b = np.array(ar_b)
    matr_x = np.random.uniform(ar_a, ar_b, size=(n, l))
    ar_y = np.array([], np.float64)
    for ar_x in matr_x:
        ar_y = np.append(ar_y, func(ar_x))
    v = np.prod(ar_b - ar_a)
    integr = v * np.mean(ar_y)
    return integr

# Задание 1
print('Задание 1: Использование методов для расчётов\n')
print('Правый прямоугольник: ', f'{right_rect(f_var, 0, 10, 1000):.4f}')
print('Левый прямоугольник: ', f'{left_rect(f_var, 0, 10, 1000):.4f}')
print('Центральный прямоугльник: ', f'{centre_rect(f_var, 0, 10, 1000):.4f}')
print('Трапеция: ', f'{trapezoid(f_var, 0, 10, 1000):.4f}')
print('Симпсон: ', f'{Simpson(f_var, 0, 10, 1000):.4f}')
print('Гаусс: ', f'{Gauss(f_var, 0, 10, 7):.4f}')
print('Монте-Карло: ', f'{Monte_Karlo(lambda ar_x: [f_var(x) for x in ar_x], [0], [10], 1000):.4f}')
print('Метод из scipi.integrate: ', f'{quad(f_var, 0, 10)[0]:.4f}')
print('Аналитический: ', f'{analytical_var(0, 10):.4f}')

# Задание 2
print('\nЗадание 2: Сравнение методов с разными N\n')

analytical_result = analytical_var(0, 10)
print(f'Аналитическое решение: {analytical_result:.4f}')

ar_n = [100, 1000]
ar_func_integr = [
    (right_rect, 'Right rectangle'),
    (left_rect, 'Left rectangle'),
    (centre_rect, 'Centre rectangle'),
    (trapezoid, 'Trapezoid'),
    (Simpson, 'Simpson'),
    (Gauss, 'Gauss'),
]

print('\nN = 100 и N = 1000')
for method, name in ar_func_integr:
    for n in ar_n:
        if name == 'Gauss':
            result = method(f_var, 0, 10, 7)
        else:
            result = method(f_var, 0, 10, n)
        error = abs(analytical_result - result)
        print(f'N = {n}, {name}: результат = {result:.4f}, абсолютная ошибка = {error:.4e}')

# Метод Монте-Карло для N = {1000, 10000, 100000}
n_values_monte_carlo = [1000, 10000, 100000]
print('\nMonte-Karlo: N = 1000, 10000, 100000')
for n in n_values_monte_carlo:
    result = Monte_Karlo(lambda ar_x: [f_var(x) for x in ar_x], [0], [10], n)
    error = abs(analytical_result - result)
    print(f'N = {n}, Монте-Карло: результат = {result:.4f}, абсолютная ошибка = {error:.4e}')

# Задание 3
# a)
def ball_n(meth:Callable[[Callable[[float], float], float, float, int], float], name:str, n:int) -> float:
    '''
    Вычисляет интеграл n мерного шара используя переданный метод, для a).
    '''
    if name == 'Gauss':
        f = lambda *args: 1
        for i in range(n - 1, -1, -1):
            def f_new(*args, i = i, f = f):
                b = max(0, 1 - np.sum(np.array(args[:i]) ** 2)) ** (1 / 2)
                return meth(lambda x: f(*(args[:i]), x), -b, b, 7)
            f = f_new
        return f()
    else:
        f = lambda *args: 1
        for i in range(n - 1, -1, -1):
            def f_new(*args, i = i, f = f):
                b = max(0, 1 - np.sum(np.array(args[:i]) ** 2)) ** (1 / 2)
                j = int(60 * b)
                if j != 0:
                    return meth(lambda x: f(*(args[:i]), x), -b, b, j)
                else:
                    return 0
            f = f_new
        return f()

# b)
def double_parob_part(meth:Callable[[Callable[[float], float], float, float, int], float], name:str) -> float:
    '''
    Вычисляет интеграл части параболоида ограниченного параболами используя переданный метод, для b).
    '''
    if name == 'Gauss':
        def f_y(x):
            b = max(0, 1 - x ** 2)
            return meth(lambda y: x ** 2 + y ** 2, -b, b, 7)
        return meth(f_y, -1, 1, 7)
    else:
        def f_y(x):
            b = max(0, 1 - x ** 2)
            j = int(500 * b)
            if j != 0:
                return meth(lambda y: x ** 2 + y ** 2, -b, b, j)
            else:
                return 0
        return meth(f_y, -1, 1, 500)
    
# c)
def lin_cone_part(meth:Callable[[Callable[[float], float], float, float, int], float], name:str) -> float:
    '''
    Вычисляет интеграл для конуса ограниченного плоскостями используя переданный метод, для c).
    '''
    if name == 'Gauss':
        return meth(lambda z: np.pi * (z ** 2) * np.tan(np.pi / 6), 0, 2, 7)
    else:
        return meth(lambda z: np.pi * (z ** 2) * np.tan(np.pi / 6), 0, 2, 500)
    
# d)
def cone_part_cos(meth:Callable[[Callable[[float], float], float, float, int], float], name:str) -> float:
    '''
    Вычисляет интеграл для конуса ограниченного плоскостями используя переданный метод, для d).
    '''
    if name == 'Gauss':
        def f_y(z, x):
            b = max(0, (max(0, z / (3 ** (1 / 2)) - x ** 2)) ** (1 / 2))
            return meth(lambda y: np.cos(z) * np.cos(x) * np.cos(y), -b, b, 7)
        def f_x(z):
            b = max(0, z / (3 ** (1 / 4)))
            return meth(lambda x: f_y(z, x), -b, b, 7)
        return meth(f_x, 0, 2, 7)
    else:
        def f_y(z, x):
            b = max(0, (max(0,z / (3 ** (1 / 2)) - x ** 2)) ** (1 / 2))
            j = int(500 * b)
            if j != 0:
                return meth(lambda y: np.cos(z) * np.cos(x) * np.cos(y), -b, b, j)
            else:
                return 0 
        def f_x(z):
            b = max(0, z / (3 ** (1 / 4)))
            j = int(500 * b)
            if j != 0:
                return meth(lambda x: f_y(z, x), -b, b, j)
            else:
                return 0
        return meth(f_x, 0, 2, 500)
    
# Всё кроме Монте-Карло
ar_integr = [
    (double_parob_part, 'b)', 1.1429),
    (lin_cone_part, 'c)', 4.8368)
]
ar_func_integr = [
    (right_rect, 'Right rectangle'),
    (left_rect, 'Left rectangle'),
    (centre_rect, 'Centre rectangle'),
    (trapezoid, 'Trapezoid'),
    (Simpson, 'Simpson'),
    (Gauss, 'Gauss'),
]

print('\nЗадание 3: Вычисление многомерных интегралов')
for n, r in [(3, 4.1888), (5, 5.2638)]:
    print(f'\nВычисление интеграла из a) в {n}-мерном случае: ')
    print(f'Аналитически полученное решение: {r}')
    for f, na in ar_func_integr:
        r_new = ball_n(f, na, n)
        print(f'Результат полученный {na}: {r_new:.4f}, абсалютная погрешность: {abs(r_new - r):.4e}')

for i, na_1, r in ar_integr:
    print(f'\nВычисление интеграла из {na_1} в многомерном случае: ')
    print(f'Аналитически полученное решение: {r}')
    for f, na_2 in ar_func_integr:
        r_new = i(f, na_2)
        print(f'Результат полученный {na_2}: {r_new:.4f}, абсалютная погрешность: {abs(r_new - r):.4e}')

print(f'\nВычисление интеграла из d) в многомерном случае: ')
for f, na_2 in ar_func_integr:
    r_new = cone_part_cos(f, na_2)
    print(f'Полученный результат {na_2}: {r_new:.4f}')

# Монте-Карло
def der_ball_n(*args:float) -> float:
    args = np.array(args)
    rad = np.sum(args ** 2)
    if rad < 1:
        return 2 * (1 - rad) ** (1 / 2)
    else:
        return 0
    
def der_double_parob_part(ar:list[float]) -> float:
    x = ar[0]
    y = ar[1]
    if y >= x ** 2 - 1 and y <= -x ** 2 + 1:
        return y ** 2 + x ** 2
    else:
        return 0

def der_lin_cone_part(ar:list[float]) -> float:
    x = ar[0]
    y = ar[1]
    z = ar[2]
    if x ** 2 + y ** 2 <= (z ** 2) * np.tan(np.pi / 6) and z <= 2 and z >= 0:
        return 1
    else: 
        return 0
    
def der_cone_part_cos(ar:list[float]) -> float:
    x = ar[0]
    y = ar[1]
    z = ar[2]
    if x ** 2 + y ** 2 <= (z ** 2) * np.tan(np.pi / 6) and z <= 2 and z >= 0:
        return np.cos(x) * np.cos(y) * np.cos(z)
    else:
        return 0
    
ar_der = [
    (der_ball_n, 'a) (при 3 измерениях)', (-1, -1), (1, 1), 4.1888),
    (der_ball_n, 'a) (при 5 измерениях)', (-1, -1, -1, -1), (1, 1, 1, 1), 5.2638),
    (der_double_parob_part, 'b)', (-1, -1), (1, 1), 1.1429),
    (der_lin_cone_part, 'c)', (-2 * (np.tan(np.pi / 6) ** (1 / 2)), -2 * (np.tan(np.pi / 6) ** (1 / 2)), 0), 
     (2 * (np.tan(np.pi / 6) ** (1 / 2)), 2 * (np.tan(np.pi / 6) ** (1 / 2)), 2), 4.8368),
    (der_cone_part_cos, 'd)', (-2 * (np.tan(np.pi / 6) ** (1 / 2)), -2 * (np.tan(np.pi / 6) ** (1 / 2)), 0), 
     (2 * (np.tan(np.pi / 6) ** (1 / 2)), 2 * (np.tan(np.pi / 6) ** (1 / 2)), 2), None)
]

print()
for d, na, a, b, r in ar_der:
    r_new = Monte_Karlo(d, a, b, 10000)
    if r == None:
        print(f'Вычисление интеграла из {na} методом Монте-Карло: {r_new:.4f}')
    else:
        print(f'Вычисление интеграла из {na} методом Монте-Карло: {r_new:.4f}, абсалютная погрешность: {abs(r_new - r):.4e}')
