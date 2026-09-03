import numpy as np
from typing import Callable

State = list[float]
System = list[Callable[[float, State], float]]

def Euler_1(f: System, x0: State, a: float, b: float, h: float) -> list[float]:
    """Метод Эйлера 1-го порядка (явный)"""
    x_cur = np.array(x0, dtype=np.float64)
    t = np.float64(a)

    while t < b:
        step = min(h, b - t)
        if step <= 0: break

        f_val = np.array([func(t, x_cur) for func in f])
        x_cur = x_cur + f_val * step
        t += step

    return x_cur.tolist()

def Euler_2(f: System, x0: State, a: float, b: float, h: float) -> list[float]:
    """Модифицированный метод Эйлера 2-го порядка"""
    x_cur = np.array(x0, dtype=np.float64)
    t = np.float64(a)
    
    while t < b:
        step = min(h, b - t)
        if step <= 0: break
            
        f_cur = np.array([func(t, x_cur) for func in f])
        x_pred = x_cur + f_cur * step
        
        f_pred = np.array([func(t + step, x_pred) for func in f])
        x_cur = x_cur + (step / 2.0) * (f_cur + f_pred)
            
        t += step
        
    return x_cur.tolist()

def Runge_Kutta_4(f: System, x0: State, a: float, b: float, h: float) -> list[float]:
    """Метод Рунге-Кутты 4-го порядка"""
    x_cur = np.array(x0, dtype=np.float64)
    t = np.float64(a)

    while t < b:
        step = min(h, b - t)
        if step <= 0: break

        k1 = np.array([func(t, x_cur) for func in f])
        k2 = np.array([func(t + step / 2.0, x_cur + (step / 2.0) * k1) for func in f])
        k3 = np.array([func(t + step / 2.0, x_cur + (step / 2.0) * k2) for func in f])
        k4 = np.array([func(t + step, x_cur + step * k3) for func in f])

        x_cur = x_cur + (step / 6.0) * (k1 + 2.0 * k2 + 2.0 * k3 + k4)
        t += step

    return x_cur.tolist()

def Adams_Explicit(f: System, x0: State, a: float, b: float, h: float) -> list[float]:
    """Явный метод Адамса 2-го порядка"""
    x_cur = np.array(x0, dtype=np.float64)
    t = np.float64(a)
    
    f_prev = np.array([func(t, x_cur) for func in f])

    if a + h >= b:
        return Runge_Kutta_4(f, x0, a, b, b - a)

    x_cur = np.array(Runge_Kutta_4(f, x_cur, t, t + h, h))
    t += h

    while t < b:
        step = min(h, b - t)
        if step <= 0: break
            
        if step == h:
            f_cur = np.array([func(t, x_cur) for func in f])
            x_cur = x_cur + (f_cur * 3 / 2 - f_prev / 2) * step
            f_prev = f_cur
            t += step
        else:
            x_cur = np.array(Runge_Kutta_4(f, x_cur, t, b, step))
            t += step

    return x_cur.tolist()

def Adams_Modified(f: System, x0: State, a: float, b: float, h: float) -> list[float]:
    """Модифицированный метод Адамса (предиктор-корректор 4-го порядка)"""
    x_cur = np.array(x0, dtype=np.float64)
    t = np.float64(a)
    l = len(x0)
    
    f_hist = np.zeros((4, l), dtype=np.float64)

    for i in range(3, -1, -1):
        if t >= b: break
        step = min(h, b - t)
        
        f_hist[i] = np.array([func(t, x_cur) for func in f])
        x_cur = np.array(Runge_Kutta_4(f, x_cur, t, t + step, step))
        t += step
    
    while t < b:
        step = min(h, b - t)
        if step <= 0: break

        if step == h:
            x_pred = x_cur + (step / 24.0) * (55 * f_hist[0] - 59 * f_hist[1] + 37 * f_hist[2] - 9 * f_hist[3])
            
            f_pred = np.array([func(t + step, x_pred) for func in f])

            x_cur = x_cur + (step / 24.0) * (9 * f_pred + 19 * f_hist[0] - 5 * f_hist[1] + f_hist[2])

            f_hist = np.roll(f_hist, shift=1, axis=0)
            f_hist[0] = np.array([func(t + step, x_cur) for func in f])

            t += step  
        else:
            x_cur = np.array(Runge_Kutta_4(f, x_cur, t, b, step))
            t += step

    return x_cur.tolist()

def Runge_Kutta_45(f: System, x0: State, a: float, b: float, h: float, tol: float = 1e-6) -> list[float]:
    """Метод Рунге-Кутты-Фельберга 4-5 порядка"""
    x_cur = np.array(x0, dtype=np.float64)
    t = np.float64(a)

    b4 = np.array([25/216, 0, 1408/2565, 2197/4104, -1/5, 0])
    b5 = np.array([16/135, 0, 6656/12825, 28561/56430, -9/50, 2/55])
    
    h_min = 1e-12
    
    while t < b:
        if t + h > b:
            h = b - t
            
        k1 = np.array([func(t, x_cur) for func in f])
        
        x_tmp = x_cur + h * (1/4 * k1)
        k2 = np.array([func(t + h * 1/4, x_tmp) for func in f])
        
        x_tmp = x_cur + h * (3/32 * k1 + 9/32 * k2)
        k3 = np.array([func(t + h * 3/8, x_tmp) for func in f])
        
        x_tmp = x_cur + h * (1932/2197 * k1 - 7200/2197 * k2 + 7296/2197 * k3)
        k4 = np.array([func(t + h * 12/13, x_tmp) for func in f])
        
        x_tmp = x_cur + h * (439/216 * k1 - 8 * k2 + 3680/513 * k3 - 845/4104 * k4)
        k5 = np.array([func(t + h, x_tmp) for func in f])
        
        x_tmp = x_cur + h * (-8/27 * k1 + 2 * k2 - 3544/2565 * k3 + 1859/4104 * k4 - 11/40 * k5)
        k6 = np.array([func(t + h * 1/2, x_tmp) for func in f])
        
        K = np.vstack([k1, k2, k3, k4, k5, k6])
        
        x_pred = x_cur + h * np.dot(b4, K)
        x_new  = x_cur + h * np.dot(b5, K)
        
        error = np.max(np.abs(x_new - x_pred))
        
        if error <= tol or h < h_min:
            t += h
            x_cur = np.copy(x_new)

        if error == 0:
            h *= 2.0
        else:
            s = 0.84 * (tol / error)**0.25
            h *= min(max(s, 0.1), 4.0)
            
    return x_cur