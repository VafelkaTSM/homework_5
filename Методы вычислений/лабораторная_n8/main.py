import numpy as np
from scipy.integrate import solve_ivp
from matplotlib import pyplot as plt
from typing import Callable
import warnings
import sys
warnings.filterwarnings('ignore', category=RuntimeWarning)

State = list[float]
System = list[Callable[[float, State], float]]

# ============================================================
# Методы с фиксированным шагом
# ============================================================

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


# ============================================================
# Методы с адаптивным шагом (для одного уравнения)
# ============================================================

# Для одномерных случаев функция принимает t и x (оба числа)
SingleEq = Callable[[float, float], float]

def Runge_Kutta_Merson(f: SingleEq, x0: float, a: float, b: float, h: float, eps: float = 1e-5) -> tuple[float, int]:
    """Метод Рунге-Кутты-Мерсона с адаптивным шагом"""
    x_cur = np.float64(x0)
    t = np.float64(a)
    h = np.float64(h)
    step_count = 0

    while t < b:
        if t + h > b:
            h = b - t
        
        if h <= 0 or np.isclose(t, b):
            break

        k1 = f(t, x_cur)
        k2 = f(t + h / 3.0, x_cur + h * k1 / 3.0)
        k3 = f(t + h / 3.0, x_cur + h * k1 / 6.0 + h * k2 / 6.0)
        k4 = f(t + h / 2.0, x_cur + h * k1 / 8.0 + 3.0 * h * k3 / 8.0)
        k5 = f(t + h,       x_cur + h * k1 / 2.0 - 3.0 * h * k3 / 2.0 + 2.0 * h * k4)

        delta = (h / 30.0) * abs(2.0 * k1 - 9.0 * k3 + 8.0 * k4 - k5)

        if delta > eps:
            h /= 2.0
            continue
        else:
            x_new = x_cur + (h / 6.0) * (k1 + 4.0 * k4 + k5)
            t += h
            x_cur = x_new
            step_count += 1
            
            if delta < eps / 32.0:
                h *= 2.0

    return x_cur, step_count

def Euler_mod_adaptive(f: SingleEq, x0: float, a: float, b: float, h: float, eps: float = 1e-5) -> tuple[float, int]:
    """Адаптивный модифицированный метод Эйлера"""
    t = np.float64(a)
    x_cur = np.float64(x0)
    h = np.float64(h)
    step_count = 0

    while t < b:
        if t + h > b:
            h = b - t
            if h < 1e-12: break

        f_cur = f(t, x_cur)
        x_pred = x_cur + h * f_cur
        x_step_full = x_cur + (h / 2.0) * (f_cur + f(t + h, x_pred))

        h2 = h / 2.0
        x_pred_half1 = x_cur + h2 * f_cur
        x_half1 = x_cur + (h2 / 2.0) * (f_cur + f(t + h2, x_pred_half1))
        
        f_half1 = f(t + h2, x_half1)
        x_pred_half2 = x_half1 + h2 * f_half1
        x_step_half = x_half1 + (h2 / 2.0) * (f_half1 + f(t + h, x_pred_half2))

        error = abs(x_step_full - x_step_half) / 3.0

        if error <= eps:
            t += h
            x_cur = x_step_half
            step_count += 1
            
            if error < eps / 10.0:
                h *= 2.0
        else:
            h /= 2.0

    return x_cur, step_count

# ============================================================
# Методы с адаптивным шагом для систем
# ============================================================

def Runge_Kutta_45(f: System, x0: State, a: float, b: float, h: float, tol: float = 1e-6) -> tuple[list[float], int]:
    """Метод Рунге-Кутты-Фельберга 4-5 порядка"""
    x_cur = np.array(x0, dtype=np.float64)
    t = np.float64(a)
    step_count = 0

    b4 = np.array([25/216, 0, 1408/2565, 2197/4104, -1/5, 0])
    b5 = np.array([16/135, 0, 6656/12825, 28561/56430, -9/50, 2/55])
    
    h_min = 1e-12
    
    while t < b:
        step_count += 1
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
            
    return x_cur, step_count

# ============================================================
# Параметры задач
# ============================================================

# Задача 1
eq_1 = [
    lambda t, x: x[0] * np.cos(t) + np.sin(2 * t)
]
x0_1 = 1.0
a_1 = 0.0
b_1 = 10.0

# Задача 2
eq_2 = [
    lambda t, x: (2 - 2.5 * x[1]) * x[0],
    lambda t, x: (x[0] - 2) * (x[1] ** 2)
]
x0_2 = [1.0, 1.0]
a_2 = 0.0
b_2 = 10.0


# ============================================================
# ВЫЧИСЛИТЕЛЬНЫЙ БЛОК
# ============================================================

# Вспомогательные функции
def _sys_to_single(f_sys: System) -> SingleEq:
    """Конвертирует System из 1 уравнения в SingleEq"""
    def single(t, x):
        return f_sys[0](t, [x])
    return single


# ====================================================================
# ЗАДАЧА 1: ОДУ y' = y·cos(t) + sin(2t), y(0) = 1, t ∈ [0, 10]
# ====================================================================

print("=" * 70)
print("ЗАДАЧА 1: ОДУ  y' = y·cos(t) + sin(2t),  y(0) = 1,  t ∈ [0, 10]")
print("=" * 70)

# ---------- 1. Эталонное решение (solve_ivp) ----------
sol_1 = solve_ivp(
    lambda t, y: eq_1[0](t, [y]),
    [a_1, b_1], [x0_1],
    method='RK45', max_step=0.001, rtol=1e-9, atol=1e-12
)
y_ref_1 = sol_1.y[0, -1]
print(f"\n1. Эталонное решение (solve_ivp):")
print(f"   y({b_1}) = {y_ref_1:.12f},  число шагов: {len(sol_1.t)}")

# ---------- 2. Методы с фиксированным шагом ----------
fixed_methods_1 = [
    ("Эйлер 1-го порядка",            Euler_1),
    ("Модиф. Эйлер 2-го порядка",     Euler_2),
    ("Рунге-Кутта 4-го порядка",      Runge_Kutta_4),
    ("Явный Адамс 2-го порядка",      Adams_Explicit),
    ("Предиктор-корректор Адамса",    Adams_Modified),
]

h_start = 0.1
eps_fixed = 1e-4

print(f"\n2. Методы с фиксированным шагом (ошибка ≤ {eps_fixed}):")
print(f"   {'Метод':<30s} {'y(10)':>14s} {'шаг':>10s} {'ошибка':>10s} {'шагов':>8s}")
print("   " + "-" * 72)

results_fixed_1 = []
for name, method in fixed_methods_1:
    h_cur = h_start
    best_res = None
    best_h = None
    best_err = None
    
    # подбираем шаг, начиная с h_start, уменьшая вдвое
    for _ in range(40):  # до 2^-40 ~ 9e-13
        res = method(eq_1, [x0_1], a_1, b_1, h_cur)
        err = abs(res[0] - y_ref_1)
        steps = int(np.ceil((b_1 - a_1) / h_cur))
        if err <= eps_fixed:
            best_res = res
            best_h = h_cur
            best_err = err
            break
        h_cur /= 2.0
    
    if best_res is not None:
        n_steps = int(np.ceil((b_1 - a_1) / best_h))
        print(f"   {name:<30s} {best_res[0]:>14.10f} {best_h:>10.2e} {best_err:>10.2e} {n_steps:>8d}")
        results_fixed_1.append((name, best_res[0], best_h, best_err, n_steps))
    else:
        print(f"   {name:<30s} {'не удалось подобрать шаг':>42s}")

# ---------- 3. Адаптивные методы ----------
h0 = 0.1
f1_single = _sys_to_single(eq_1)

adaptive_methods_1 = [
    ("Рунге-Кутта-Мерсон",         lambda eps: Runge_Kutta_Merson(f1_single, x0_1, a_1, b_1, h0, eps=eps)),
    ("Адаптивный модиф. Эйлер",    lambda eps: Euler_mod_adaptive(f1_single, x0_1, a_1, b_1, h0, eps=eps)),
    ("РК-Фельберг 45 (сист.)",     lambda eps: Runge_Kutta_45(eq_1, [x0_1], a_1, b_1, h0, tol=eps)),
]

eps_target = 1e-4

print(f"\n3. Методы с адаптивным шагом (ошибка ≤ {eps_target}):")
print(f"   {'Метод':<30s} {'y(10)':>14s} {'eps':>10s} {'ошибка':>10s} {'шагов':>8s}")
print("   " + "-" * 72)

results_adaptive_1 = []
for name, method_fn in adaptive_methods_1:
    eps_cur = eps_target
    for _ in range(30):
        res, n_steps = method_fn(eps_cur)
        err = abs(res - y_ref_1) if isinstance(res, (int, float)) else abs(res[0] - y_ref_1)
        if isinstance(res, (int, float)):
            y_val = res
        else:
            y_val = res[0]
        if abs(err - eps_target) / eps_target < 0.5 or eps_cur < 1e-10:
            # проверим, что ошибка ≤ eps_target
            if err <= eps_target:
                print(f"   {name:<30s} {y_val:>14.10f} {eps_cur:>10.2e} {err:>10.2e} {n_steps:>8d}")
                results_adaptive_1.append((name, y_val, eps_cur, err, n_steps))
                break
        eps_cur /= 2.0

# ---------- 4. Сводка по количеству шагов ----------
print(f"\n4. Сравнение количества шагов (Задача 1):")
print(f"   {'Метод':<40s} {'шагов':>8s}")
print("   " + "-" * 48)

for name, y, h_or_eps, err, steps in results_fixed_1:
    print(f"   {name:<40s} {steps:>8d}")
for name, y, eps_val, err, steps in results_adaptive_1:
    print(f"   {name + ' (адапт.)':<40s} {steps:>8d}")
print(f"   {'solve_ivp (эталон)':<40s} {len(sol_1.t):>8d}")


# ====================================================================
# ЗАДАЧА 2: Система ОДУ
#   x' = (2 - 2.5y)·x,  y' = (x - 2)·y²
#   x(0) = y(0) = 1,  t ∈ [0, 10]
# ====================================================================

print("\n" + "=" * 70)
print("ЗАДАЧА 2: Система ОДУ")
print("  x' = (2 - 2.5·y)·x")
print("  y' = (x - 2)·y²")
print("  x(0) = y(0) = 1,  t ∈ [0, 10]")
print("=" * 70)

# ---------- 1. Эталонное решение (solve_ivp) ----------
sol_2 = solve_ivp(
    lambda t, xy: [eq_2[0](t, xy), eq_2[1](t, xy)],
    [a_2, b_2], x0_2,
    method='RK45', max_step=0.001, rtol=1e-9, atol=1e-12
)
x_ref_2, y_ref_2 = sol_2.y[0, -1], sol_2.y[1, -1]
print(f"\n1. Эталонное решение (solve_ivp):")
print(f"   x({b_2}) = {x_ref_2:.12f}")
print(f"   y({b_2}) = {y_ref_2:.12f}")
print(f"   число шагов: {len(sol_2.t)}")

# ---------- 2. Методы с фиксированным шагом ----------
fixed_methods_2 = [
    ("Эйлер 1-го порядка",        Euler_1),
    ("Модиф. Эйлер 2-го порядка", Euler_2),
    ("Рунге-Кутта 4-го порядка",  Runge_Kutta_4),
    ("Явный Адамс 2-го порядка",  Adams_Explicit),
    ("Модиф. Адамс (пред.-кор.)", Adams_Modified),
]

h_start_2 = 0.1

print(f"\n2. Методы с фиксированным шагом (ошибка ≤ {eps_fixed}):")
print(f"   {'Метод':<30s} {'x(10)':>14s} {'y(10)':>14s} {'шаг':>10s} {'ош-ка':>10s} {'шагов':>8s}")
print("   " + "-" * 86)

results_fixed_2 = []
for name, method in fixed_methods_2:
    h_cur = h_start_2
    best_res = None
    best_h = None
    best_err = None
    
    for _ in range(40):
        res = method(eq_2, x0_2, a_2, b_2, h_cur)
        # Проверка на NaN/Inf
        if any(np.isnan(r) or np.isinf(r) for r in res):
            h_cur /= 2.0
            continue
        err = np.linalg.norm(np.array(res) - np.array([x_ref_2, y_ref_2]))
        if err <= eps_fixed:
            best_res = res
            best_h = h_cur
            best_err = err
            break
        h_cur /= 2.0
    
    if best_res is not None:
        n_steps = int(np.ceil((b_2 - a_2) / best_h))
        print(f"   {name:<30s} {best_res[0]:>14.10f} {best_res[1]:>14.10f} {best_h:>10.2e} {best_err:>10.2e} {n_steps:>8d}")
        results_fixed_2.append((name, best_res, best_h, best_err, n_steps))
    else:
        print(f"   {name:<30s} {'не удалось подобрать шаг':>56s}")

# ---------- 3. Адаптивные методы ----------
print(f"\n3. Адаптивный метод (РК-Фельберг 45) с подбором tol:")
print(f"   {'tol':>12s} {'x(10)':>14s} {'y(10)':>14s} {'ошибка':>10s} {'шагов':>8s}")
print("   " + "-" * 58)

for tol in [1e-4, 1e-5, 1e-6, 1e-7]:
    res, n_steps = Runge_Kutta_45(eq_2, x0_2, a_2, b_2, 0.1, tol=tol)
    err = np.linalg.norm(np.array(res) - np.array([x_ref_2, y_ref_2]))
    print(f"   {tol:>12.0e} {res[0]:>14.10f} {res[1]:>14.10f} {err:>10.2e} {n_steps:>8d}")

# ---------- 4. Построение графиков ----------
print(f"\n4. Построение графиков...")

# Функция: вычислить кривую методом РК4 с мелким шагом для построения
def _rk4_plot(eq, x0, a, b, step):
    t_vals = np.arange(a, b + step, step)
    x_vals, y_vals = [], []
    x_cur = np.array(x0, dtype=np.float64)
    for t_i in t_vals[:-1]:
        s = min(step, t_vals[-1] - t_i)
        if s <= 0: break
        k1 = np.array([func(t_i, x_cur) for func in eq])
        k2 = np.array([func(t_i + s/2, x_cur + s/2*k1) for func in eq])
        k3 = np.array([func(t_i + s/2, x_cur + s/2*k2) for func in eq])
        k4 = np.array([func(t_i + s, x_cur + s*k3) for func in eq])
        x_cur = x_cur + (s/6)*(k1 + 2*k2 + 2*k3 + k4)
        x_vals.append(x_cur[0])
        y_vals.append(x_cur[1])
    return t_vals[:-1], np.array(x_vals), np.array(y_vals)

colors = ['r', 'g', 'b', 'm', 'c', 'orange']
h_plot = 0.01

# а) Графики x(t) и y(t) — отдельный файл
plt.figure(figsize=(10, 6))

# Эталон
plt.plot(sol_2.t, sol_2.y[0], 'k-', linewidth=2, label='x(t) эталон')
plt.plot(sol_2.t, sol_2.y[1], 'k--', linewidth=2, label='y(t) эталон')

# Численные решения
for idx, (name, method) in enumerate(fixed_methods_2):
    t_vals, x_vals, y_vals = _rk4_plot(eq_2, x0_2, a_2, b_2, h_plot)
    c = colors[idx % len(colors)]
    plt.plot(t_vals, x_vals, f'{c}-', linewidth=0.8, alpha=0.7, label=f'{name} x(t)')
    plt.plot(t_vals, y_vals, f'{c}--', linewidth=0.8, alpha=0.7, label=f'{name} y(t)')

plt.xlabel('t')
plt.ylabel('x, y')
plt.title('Решение системы ОДУ: x(t) и y(t)')
plt.legend(fontsize=7, loc='upper right')
plt.grid(True)
plt.tight_layout()
plt.savefig('Методы вычислений/лабораторная_n8/task2_xt_yt.png', dpi=150)
print("   График x(t), y(t) сохранён в 'task2_xt_yt.png'")
plt.close()

# б) Фазовая траектория y(x) — отдельный файл
plt.figure(figsize=(8, 6))

plt.plot(sol_2.y[0], sol_2.y[1], 'k-', linewidth=2, label='эталон')

for idx, (name, method) in enumerate(fixed_methods_2):
    t_vals, x_vals, y_vals = _rk4_plot(eq_2, x0_2, a_2, b_2, h_plot)
    c = colors[idx % len(colors)]
    plt.plot(x_vals, y_vals, f'{c}-', linewidth=0.8, alpha=0.7, label=name)

plt.xlabel('x')
plt.ylabel('y')
plt.title('Фазовая траектория y(x)')
plt.legend(fontsize=7, loc='lower right')
plt.grid(True)
plt.tight_layout()
plt.savefig('Методы вычислений/лабораторная_n8/task2_phase.png', dpi=150)
print("   Фазовая траектория сохранена в 'task2_phase.png'")
plt.close()

# ---------- 5. Сводка по количеству шагов ----------
print(f"\n5. Сводка по методам (Задача 2):")
print(f"   {'Метод':<35s} {'шагов':>8s}")
print("   " + "-" * 43)

for name, res, h_or_tol, err, steps in results_fixed_2:
    print(f"   {name:<35s} {steps:>8d}")
print(f"   {'РК-Фельберг 45 (tol=1e-6)':<35s} {Runge_Kutta_45(eq_2, x0_2, a_2, b_2, 0.1, tol=1e-6)[1]:>8d}")
print(f"   {'solve_ivp (эталон)':<35s} {len(sol_2.t):>8d}")

print("\n" + "=" * 70)
print("ВЫЧИСЛЕНИЯ ЗАВЕРШЕНЫ")
print("=" * 70)