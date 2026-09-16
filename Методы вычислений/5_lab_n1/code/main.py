import numpy as np
import matplotlib.pyplot as plt
from pathlib import Path
from scipy.integrate import solve_ivp, ODESolution
from typing import List, Sequence

def robertson_sys(t: float, y: Sequence[float]) -> List[float]:
    """Правая часть жесткой системы дифференциальных уравнений Робертсона."""
    k1, k2, k3 = 0.04, 3e7, 1e4
    y1, y2, y3 = y
    
    dy1 = -k1 * y1 + k3 * y2 * y3
    dy2 = k1 * y1 - k3 * y2 * y3 - k2 * y2 ** 2
    dy3 = k2 * y2 ** 2  # Исправлена опечатка в условии (было y2')
    return [dy1, dy2, dy3]

y0 = [1.0, 0.0, 0.0]
t_span = (0, 1e11)  # В системе Робертсона обычно берут большой интервал времён

sol1 = solve_ivp(robertson_sys, t_span, y0, method='BDF', dense_output=True)

def e5_sys(t: float, y: Sequence[float]) -> List[float]:
    """Правая часть системы химической кинетики E5."""
    A, B, C, M = 7.89e-10, 1.1e7, 1.13e3, 1e6
    y1, y2, y3, y4 = y
    
    dy1 = -A*y1 - B*y1*y3
    dy2 = A*y1 - M*C*y2*y3
    dy3 = A*y1 - B*y1*y3 - M*C*y2*y3 + C*y4
    dy4 = B*y1*y3 - C*y4
    return [dy1, dy2, dy3, dy4]

y0 = [1.76e-3, 0.0, 0.0, 0.0]
t_span = (0, 1e13)

sol2 = solve_ivp(e5_sys, t_span, y0, method='BDF', dense_output=True)

def bio_sys(t: float, y: Sequence[float]) -> List[float]:
    """Правая часть биологической системы дифференциальных уравнений."""
    y1, y2, y3, y4, y5, y6, y7, y8 = y
    
    dy1 = -1.71 * y1 + 0.43 * y2 + 8.23 * y3 + 0.0007
    dy2 = 1.71 * y1 - 8.75 * y2
    dy3 = -10.03 * y3 + 0.43 * y4 + 0.035 * y5
    dy4 = 8.32 * y2 + 1.71 * y3 - 1.12 * y4
    dy5 = -1.745 * y5 + 0.43 * y6 + 0.43 * y7

    dy7 = 280 * y6 * y8 - 1.87 * y7
    dy6 = -280 * y6 * y8 + 0.69 * y4 + 1.71 * y5 - 0.43 * y6 + 0.69 * y7
    dy8 = -dy7
    
    return [dy1, dy2, dy3, dy4, dy5, dy6, dy7, dy8]

y0 = [1.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0057]
t_span = (0, 421.8122)
sol3 = solve_ivp(bio_sys, t_span, y0, method='Radau', dense_output=True)

# ГРАФИКИ
# Равномерная по логарифму сетка t (начинаем с маленького t, т.к. log(0) не определён)

PICTURES_DIR = Path(__file__).resolve().parent.parent / 'pictures'
PICTURES_DIR.mkdir(exist_ok=True)

def plot_system(sol: ODESolution, t_max: float, filename: str) -> None:
    """Строит и сохраняет в логарифмическом масштабе графики компонент решения системы ОДУ."""
    t = np.logspace(-8, np.log10(t_max), 500)
    y = sol.sol(t)
    n = len(y)
    nrows = int(np.ceil(n / 2))
    fig, axes = plt.subplots(nrows, 2, figsize=(8, 3 * nrows))
    axes = np.atleast_1d(axes).ravel()
    for i, ax in enumerate(axes[:n]):
        ax.loglog(t, y[i])
        ax.set_ylabel(f'$y_{i + 1}$')
        ax.set_xlabel('t')
        ax.grid(True, which='both', ls='--', alpha=0.5)
    # Удаляем пустые оси, если число переменных нечётное
    for ax in axes[n:]:
        fig.delaxes(ax)
    fig.tight_layout()
    fig.savefig(PICTURES_DIR / filename, dpi=150)

plot_system(sol1, 1e11, 'robertson.png')
plot_system(sol2, 1e13, 'e5.png')
plot_system(sol3, 421.8122, 'bio.png')