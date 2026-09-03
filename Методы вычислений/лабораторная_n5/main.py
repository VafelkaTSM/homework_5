import numpy as np
import matplotlib.pyplot as plt
from typing import Callable

def first_dev_y_2(y_func:Callable[[float], float], h:float, a:float, b:float, n:int) -> np.ndarray:
    '''
    Функция считает первую производную второго порядка точности
    для указанной функции, с шагом h, в диапозоне от a до b, с кол-ом точек n.
    '''
    dev_arr = np.array([], np.float64)
    for x in np.linspace(a, b, n):
        dev_y = (y_func(x + h) - y_func(x - h)) / (2 * h)
        dev_arr = np.append(dev_arr, dev_y)
    return dev_arr

def second_dev_y_2(y_func:Callable[[float], float], h:float, a:float, b:float, n:int) -> np.ndarray:
    '''
    Функция считает вторую производную второго порядка точности
    для указанной функции, с шагом h, в диапозоне от a до b, с кол-ом точек n.
    '''
    dev_arr = np.array([], np.float64)
    for x in np.linspace(a, b, n):
        dev_y = (y_func(x + h) - 2 * y_func(x) + y_func(x - h)) / (h ** 2)
        dev_arr = np.append(dev_arr, dev_y)
    return dev_arr

def first_dev_y_4(y_func:Callable[[float], float], h:float, a:float, b:float, n:int) -> np.ndarray:
    '''
    Функция считает первую производную четвертого порядка точности
    для указанной функции, с шагом h, в диапозоне от a до b, с кол-ом точек n.
    '''
    dev_arr = np.array([], np.float64)
    for x in np.linspace(a, b, n):
        dev_y = (y_func(x - 2 * h) - 8 * y_func(x - h) + 8 * 
                 y_func(x + h) - y_func(x + 2 * h)) / (12 * h)
        dev_arr = np.append(dev_arr, dev_y)
    return dev_arr

def second_dev_y_4(y_func:Callable[[float], float], h:float, a:float, b:float, n:int) -> np.ndarray:
    '''
    Функция считает вторую производную четвертого порядка точности
    для указанной функции, с шагом h, в диапозоне от a до b, с кол-ом точек n.
    '''
    dev_arr = np.array([], np.float64)
    for x in np.linspace(a, b, n):
        dev_y = (- y_func(x - 2 * h) + 16 * y_func(x - h) - 30 * y_func(x) +
                 16 * y_func(x + h) - y_func(x + 2 * h)) / (12 * (h ** 2))
        dev_arr = np.append(dev_arr, dev_y)
    return dev_arr

def f_var10(x:float) -> float:
    '''
    Функция для расчёта значения функции 10 варианта в точке.
    '''
    return np.exp(-x / np.pi) * ((np.sin(x) + np.pi) ** 2)

def first_dev_f_var10(x:float) -> float:
    '''
    Первая аналитическая производная для функциии 10 варианта.
    '''
    return (np.exp(-x / np.pi) * (np.sin(x) + np.pi) *
           (- (np.sin(x) + np.pi) / np.pi + 2 * np.cos(x)))

def second_dev_f_var10(x:float) -> float:
    '''
    Вторая аналитическая производная для функции 10 варианта.
    '''
    return ((-1 /np.pi) * np.exp(-x / np.pi) * (np.sin(x) + np.pi) * 
           (- (np.sin(x) + np.pi) / np.pi + 2 * np.cos(x)) +
           np.exp(-x / np.pi) * np.cos(x) * 
           (- (np.sin(x) + np.pi) / np.pi + 2 * np.cos(x)) +
           np.exp(-x / np.pi) * (np.sin(x) + np.pi) * 
           (- np.cos(x) / np.pi - 2 * np.sin(x)))

def right_teilor_1(y_func:Callable[[float], float], h:float, a:float, b:float, n:int) -> float:
    '''
    Функция для расчёта 1-ой производной 2-го порядка точности по Тейлору.
    '''
    dev_arr = np.array([], np.float64)
    for x in np.linspace(a, b, n):
        dev_y = (-3 * y_func(x) + 4 * y_func(x + h) - y_func(x + 2 * h)) / (2 * h)
        dev_arr = np.append(dev_arr, dev_y)
    return dev_arr

def right_teilor_2(y_func:Callable[[float], float], h:float, a:float, b:float, n:int) -> float:
    '''
    Функция для расчёта 2-ой производной 2-го порядка точности по Тейлору.
    '''
    dev_arr = np.array([], np.float64)
    for x in np.linspace(a, b, n):
        dev_y = (y_func(x) - 2 * y_func(x + h) + y_func(x + 2 * h)) / (h**2)
        dev_arr = np.append(dev_arr, dev_y)
    return dev_arr

def func_1(s:str):
    plt.xlabel('X')
    plt.ylabel('Y')
    plt.grid()
    plt.legend()
    plt.savefig(s)
    plt.clf()

# Задание 1.1.
x_ar = np.arange(0, 10 + 0.01, 0.1)
y_ar = f_var10(x_ar)
plt.plot(x_ar, y_ar, label = 'Функция варианта 10')
func_1('pictures/figure_1_1.png')

# Задание 1.2.
x_ar = np.linspace(0, 10, 100)
def plot_1(num, ord, task, h = 0.01):
    if num == 1:
        if ord == 2:
            plt.plot(x_ar, first_dev_y_2(f_var10, h, 0, 10, 100), 
                     label = f'{num}-ая производная {ord}-ой точности', color = 'blue', linewidth = 3)
        elif ord == 4:
            plt.plot(x_ar, first_dev_y_4(f_var10, h, 0, 10, 100), 
                     label = f'{num}-ая производная {ord}-ой точности', color = 'blue', linewidth = 3)
        plt.plot(x_ar, first_dev_f_var10(x_ar), 
                 label = f'{num}-ая производная аналитическая', color = 'red', linewidth = 1.5)
    elif num == 2:
        if ord == 2:
            plt.plot(x_ar, second_dev_y_2(f_var10, h, 0, 10, 100), 
                     label = f'{num}-ая производная {ord}-ой точности', color = 'blue', linewidth = 3)
        elif ord == 4:
            plt.plot(x_ar, second_dev_y_4(f_var10, h, 0, 10, 100), 
                     label = f'{num}-ая производная {ord}-ой точности', color = 'blue', linewidth = 3)
        plt.plot(x_ar, second_dev_f_var10(x_ar), 
                 label = f'{num}-ая производная аналитическая', color = 'red', linewidth = 1.5)
    func_1(f'pictures/figure_1_2_{task}.png')

plot_1(1, 2, 1)
plot_1(2, 2, 2)
plot_1(1, 4, 3)
plot_1(2, 4, 4)
# Дополнительно Тейлор для правых 1-ой и 2-ой производных
plt.plot(x_ar, right_teilor_1(f_var10, 0.01, 0, 10, 100), 
         label = '1-ая правая производная Тейлора', color = 'blue', linewidth = 3)
plt.plot(x_ar, first_dev_f_var10(x_ar), 
         label = '1-ая производная аналитическая', color = 'red', linewidth = 1.5)
func_1('pictures/figure_1_2_Teilor_1.png')
plt.plot(x_ar, right_teilor_2(f_var10, 0.01, 0, 10, 100), 
         label = '2-ая правая производная Тейлора', color = 'blue', linewidth = 3)
plt.plot(x_ar, second_dev_f_var10(x_ar), 
         label = '2-ая производная аналитическая', color = 'red', linewidth = 1.5)
func_1('pictures/figure_1_2_Teilor_2.png')

# Задание 1.3.
def plot_2(num, ord, task, h = 0.1):
    x_ar = np.linspace(0, 10, 100)
    if num == 1:
        if ord == 2:
            plt.plot(x_ar, first_dev_f_var10(x_ar) - first_dev_y_2(f_var10, h, 0, 10, 100), 
                     label = f'Разность {num}-ой производной \n{ord}-ой точности с аналитической', 
                     color = 'blue')
        elif ord == 4:
            plt.plot(x_ar, first_dev_f_var10(x_ar) - first_dev_y_4(f_var10, h, 0, 10, 100), 
                     label = f'Разность {num}-ой производной \n{ord}-ой точности с аналитической', 
                     color = 'blue')
    elif num == 2:
        if ord == 2:
            plt.plot(x_ar, second_dev_f_var10(x_ar) - second_dev_y_2(f_var10, h, 0, 10, 100),
                     label = f'Разность {num}-ой производной \n{ord}-ой точности с аналитической', 
                     color = 'blue')
        elif ord == 4:
            plt.plot(x_ar, second_dev_f_var10(x_ar) - second_dev_y_4(f_var10, h, 0, 10, 100), 
                     label = f'Разность {num}-ой производной \n{ord}-ой точности с аналитической', 
                     color = 'blue')
    func_1(f'pictures/figure_1_3_{task}.png')

plot_2(1, 2, 1)
plot_2(2, 2, 2)
plot_2(1, 4, 3)
plot_2(2, 4, 4)
# Дополнительно Тейлор для правых 1-ой и 2-ой производных
plt.plot(x_ar, right_teilor_1(f_var10, 0.01, 0, 10, 100) - first_dev_f_var10(x_ar), 
         label = 'Разность 1-ой правой производной Тейлора с аналитической', color = 'blue')
func_1('pictures/figure_1_3_Teilor_1.png')
plt.plot(x_ar, right_teilor_2(f_var10, 0.01, 0, 10, 100) - second_dev_f_var10(x_ar), 
         label = 'Разность 2-ой правой производной Тейлора с аналитической', color = 'blue')
func_1('pictures/figure_1_3_Teilor_2.png')

# Задание 1.4.
x_ar = np.linspace(0, 10, 100)
def acc_dev(num, ord, h):
    if num == 1:
        if ord == 2:
            return np.abs(first_dev_f_var10(x_ar) - first_dev_y_2(f_var10, h, 0, 10, 100))
        elif ord == 4:
            return np.abs(first_dev_f_var10(x_ar) - first_dev_y_4(f_var10, h, 0, 10, 100))
    elif num == 2:
        if ord == 2:
            return np.abs(second_dev_f_var10(x_ar) - second_dev_y_2(f_var10, h, 0, 10, 100))
        elif ord == 4:
            return np.abs(second_dev_f_var10(x_ar) - second_dev_y_4(f_var10, h, 0, 10, 100))
        
        
acc_1 = np.mean(acc_dev(1, 2, 1))
acc_2 = np.mean(acc_dev(2, 2, 1))
acc_3 = np.mean(acc_dev(1, 4, 1))
acc_4 = np.mean(acc_dev(2, 4, 1))
acc_1 = np.array([], np.float64)
acc_2 = np.array([], np.float64)
acc_3 = np.array([], np.float64)
acc_4 = np.array([], np.float64)
h_ar_log = np.logspace(-1, -8, 50)
for h in h_ar_log:
    acc = np.mean(acc_dev(1, 2, h))
    acc_1 = np.append(acc_1, acc)
    acc = np.mean(acc_dev(2, 2, h))
    acc_2 = np.append(acc_2, acc)
    acc = np.mean(acc_dev(1, 4, h))
    acc_3 = np.append(acc_3, acc)
    acc = np.mean(acc_dev(2, 4, h))
    acc_4 = np.append(acc_4, acc)
plt.xscale('log')
plt.yscale('log')
plt.plot(h_ar_log, acc_1, label = 'Точность 1-ой производной 2-ой точности от шага h', color = 'blue')
func_1('pictures/figure_1_4_1.png')
plt.xscale('log')
plt.yscale('log')
plt.plot(h_ar_log, acc_2, label = 'Точность 2-ой производной 2-ой точности от шага h', color = 'blue')
func_1('pictures/figure_1_4_2.png')
plt.xscale('log')
plt.yscale('log')
plt.plot(h_ar_log, acc_3, label = 'Точность 1-ой производной 4-ой точности от шага h', color = 'blue')
func_1('pictures/figure_1_4_3.png')
plt.xscale('log')
plt.yscale('log')
plt.plot(h_ar_log, acc_4, label = 'Точность 2-ой производной 4-ой точности от шага h', color = 'blue')
func_1('pictures/figure_1_4_4.png')
i = acc_1.argmin()
print(h_ar_log[i], ':', acc_1[i])
i = acc_2.argmin()
print(h_ar_log[i], ':', acc_2[i])
i = acc_3.argmin()
print(h_ar_log[i], ':', acc_3[i])
i = acc_4.argmin()
print(h_ar_log[i], ':', acc_4[i])

eps = 11.56769 * (10 ** (-16))
h_1 = (3 * eps) ** (1 / 3)
h_2 = (48 * eps) ** (1 / 4)
h_3 = (45 / 4 * eps) ** (1 / 5)
h_4 = (240 * eps) ** (1 / 6)
acc_1_t = np.mean(acc_dev(1, 2, h_1))
acc_2_t = np.mean(acc_dev(2, 2, h_2))
acc_3_t = np.mean(acc_dev(1, 4, h_3))
acc_4_t = np.mean(acc_dev(2, 4, h_4))
print(h_1, ':', acc_1_t, ':', acc_1.min() - acc_1_t)
print(h_2, ':', acc_2_t, ':', acc_2.min() - acc_2_t)
print(h_3, ':', acc_3_t, ':', acc_3.min() - acc_3_t)
print(h_4, ':', acc_4_t, ':', acc_4.min() - acc_4_t)

# Задание 2.1. 
from scipy.interpolate import lagrange
x_ar_to_pol = np.linspace(0, 10, 20)
x_ar_to_dev = np.linspace(0, 10, 100)
y_ar_to_pol = f_var10(x_ar_to_pol)
polynom = lagrange(x_ar_to_pol, y_ar_to_pol)
#polynom = np.vectorize(polynom)

# Задание 2.2.
plt.plot(x_ar_to_dev, polynom(x_ar_to_dev), 
         label = f'Интерполяционный полином Лагранжа', 
         color = 'blue')
plt.scatter(x_ar_to_pol, y_ar_to_pol, 
            label = f'Значения изначальной функции', 
            color = 'green', 
            s = 10)
func_1('pictures/figure_2_2.png')

# Задание 2.3.
plt.plot(x_ar_to_dev, first_dev_y_2(f_var10, 0.001, 0, 10, 100), 
         label = f'2-ая производная 2-ой точности для полинома', color = 'blue', linewidth = 3)
plt.plot(x_ar_to_dev, first_dev_f_var10(x_ar_to_dev), 
         label = f'2-ая производная аналитическая для функции', color = 'green', linewidth = 1.5)
func_1('pictures/figure_2_3_2.png')

plt.plot(x_ar_to_dev, second_dev_y_2(f_var10, 0.001, 0, 10, 100), 
         label = f'1-ая производная 2-ой точности для полинома', color = 'blue', linewidth = 3)
plt.plot(x_ar_to_dev, second_dev_f_var10(x_ar_to_dev), 
         label = f'1-ая производная аналитическая для функции', color = 'green', linewidth = 1.5)
func_1('pictures/figure_2_3_1.png')

# Задание 2.4.
plt.plot(x_ar_to_dev, first_dev_f_var10(x_ar_to_dev) - first_dev_y_2(f_var10, 0.001, 0, 10, 100), 
         label = f'Разность аналит. и двухстор. от Лагранжа\n 2-го порядка первых произв.', 
         color = 'blue', linewidth = 2)
func_1('pictures/figure_2_4_1')

plt.plot(x_ar_to_dev, second_dev_f_var10(x_ar_to_dev) - second_dev_y_2(f_var10, 0.001, 0, 10, 100), 
         label = f'Разность аналит. и двухстор. от Лагранжа\n 2-го порядка вторых произв.', 
         color = 'blue', linewidth = 2)
func_1('pictures/figure_2_4_2')