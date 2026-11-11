"""Лабораторная работа № 2 «Методы численной оптимизации».

Блок 1. Градиентные методы первого порядка (Розенброк, Химмельблау):
    наискорейший спуск, градиентный и покомпонентный спуски с фиксированным
    шагом, покомпонентный спуск (последовательный и со случайным
    перемешиванием), момент Нестерова, Adam, Adagrad, RMSProp; градиент —
    аналитический и численный (конечные разности).
Блок 2. Методы второго порядка: Ньютон и BFGS; гессиан — аналитический
    и численный.
Блок 3. Методы нулевого порядка: Нелдер–Мид (2D и 6D Розенброк),
    рой частиц PSO с адаптивными коэффициентами.
Блок 4. Нелинейные наименьшие квадраты: Гаусс–Ньютон, Левенберг–Марквардт
    (минимум Розенброка 2D и аппроксимация зашумлённых данных).
Блок 5. Распределение времени на сессию: проекция на симплекс и
    квадратичный штраф; мультизапуск, кластеризация, итоговые таблицы.

Запуск: python main.py (или uv run python main.py)
"""

from __future__ import annotations

import os
from pathlib import Path

import matplotlib

if not os.environ.get("DISPLAY"):
    matplotlib.use("Agg")

import matplotlib.pyplot as plt
import numpy as np

from least_squares import GaussNewton, LevenbergMarquardt, fitting_problem, rosenbrock_problem
from objectives import Himmelblau, Rosenbrock, TargetFunction, Vector
from optimizers import (
    BFGS,
    Adagrad,
    Adam,
    CoordinateDescent,
    NesterovMomentum,
    Newton,
    Optimizer,
    RMSProp,
    SteepestDescent,
)
from session_task import run_session_block
from zeroth_order import PSO, NelderMead

RESULTS_DIR = Path(__file__).resolve().parent / "results"
RESULTS_DIR.mkdir(exist_ok=True)

FIRST_ORDER: list[Optimizer] = [
    SteepestDescent(),
    SteepestDescent(lr=2e-3),
    CoordinateDescent(),
    CoordinateDescent(shuffle=True),
    CoordinateDescent(lr=2e-3),
    NesterovMomentum(),
    Adam(),
    Adagrad(),
    RMSProp(),
]


def _grid(obj: TargetFunction) -> tuple[Vector, Vector, Vector]:
    """Регулярная сетка и значения функции на ней для построения изолиний."""
    xs, ys = np.meshgrid(np.linspace(*obj.xlim, 400), np.linspace(*obj.ylim, 400))
    zs = np.vectorize(lambda a, b: obj.f(np.array([a, b])))(xs, ys)
    return xs, ys, zs


def _contours(ax: plt.Axes, obj: TargetFunction,
              grid: tuple[Vector, Vector, Vector]) -> None:
    ax.contourf(*grid, levels=obj.levels, cmap="viridis", extend="both")
    ax.contour(*grid, levels=obj.levels, colors="black", linewidths=0.3)


def _report(name: str, variant: str, points: Vector, f: float) -> None:
    """Печать сводки по запуску: число итераций, точка окончания, f."""
    coord = ", ".join(f"{v:g}" for v in points[-1])
    print(f"  {name:28s} | {variant:16s} | итераций: {len(points) - 1:5d} | "
          f"x* = ({coord}) | f = {f:.3e}")


def _plot_first_order(ax: plt.Axes, obj: TargetFunction, method: Optimizer,
                      grid: tuple[Vector, Vector, Vector]) -> None:
    """Изолинии и траектории метода с обоими способами градиента."""
    gradients = [("аналитический", "grad", "-"),
                 ("численный", "grad_numeric", "--")]
    _contours(ax, obj, grid)
    for grad_name, attr, style in gradients:
        points = method.run(obj, obj.x0, grad=getattr(obj, attr))
        ax.plot(points[:, 0], points[:, 1], style, color="red",
                linewidth=1.2, label=f"градиент: {grad_name}")
        _report(method.name, grad_name, points, obj.f(points[-1]))
    ax.plot(*obj.x0, "wo", markersize=5, label="старт")
    ax.set(xlim=obj.xlim, ylim=obj.ylim, title=method.name)
    ax.legend(loc="upper right", fontsize=8)


def block1() -> None:
    """Блок 1: первый порядок, изолинии с траекториями (рис. 3 x 3)."""
    for obj in (Rosenbrock(), Himmelblau()):
        print(f"\n=== Блок 1. Первый порядок: {obj.name} ===")
        grid = _grid(obj)
        fig, axes = plt.subplots(3, 3, figsize=(15, 13))
        fig.suptitle(f"{obj.name}: градиентные методы первого порядка")
        for ax, method in zip(axes.ravel(), FIRST_ORDER):
            _plot_first_order(ax, obj, method, grid)
        fig.tight_layout()
        fig.savefig(RESULTS_DIR / f"{type(obj).__name__.lower()}.png", dpi=150)


def block2() -> None:
    """Блок 2: Ньютон (аналит./числ. гессиан) и BFGS, рис. 2 x 3."""
    print("\n=== Блок 2. Второй порядок ===")
    fig, axes = plt.subplots(2, 3, figsize=(15, 9))
    fig.suptitle("Методы второго порядка")
    for row, obj in enumerate((Rosenbrock(), Himmelblau())):
        grid = _grid(obj)
        cells = [(Newton(hessian=obj.hess), "Ньютон (аналит. H)"),
                 (Newton(hessian=obj.hess_numeric), "Ньютон (числ. H)"),
                 (BFGS(), "BFGS")]
        print(f"\n--- {obj.name} ---")
        for col, (method, title) in enumerate(cells):
            points = method.run(obj, obj.x0)
            ax = axes[row, col]
            _contours(ax, obj, grid)
            ax.plot(points[:, 0], points[:, 1], "r-", linewidth=1.2)
            ax.plot(*obj.x0, "wo", markersize=5)
            ax.set(xlim=obj.xlim, ylim=obj.ylim, title=f"{obj.name}: {title}")
            _report(title, "аналит. градиент", points, obj.f(points[-1]))
    fig.tight_layout()
    fig.savefig(RESULTS_DIR / "second_order.png", dpi=150)


def block3() -> None:
    """Блок 3: Нелдер–Мид (2D — траектория симплекса, 6D — сходимость)
    и PSO с визуализацией частиц и скоростей."""
    print("\n=== Блок 3. Нулевой порядок ===")
    obj = Rosenbrock()
    grid = _grid(obj)

    nm = NelderMead()
    centroids = nm.run(obj, obj.x0)
    print("\n--- Нелдер–Мид, 2D ---")
    _report(nm.name, "2D Розенброк", centroids, obj.f(centroids[-1]))
    fig, ax = plt.subplots(figsize=(8, 6))
    _contours(ax, obj, grid)
    ax.plot(centroids[:, 0], centroids[:, 1], "r-", linewidth=1.2,
            label="центроид симплекса")
    for k in np.linspace(0, len(nm.simplex_history_) - 1, 6).astype(int):
        simplex = np.vstack([nm.simplex_history_[k], nm.simplex_history_[k][0]])
        ax.plot(simplex[:, 0], simplex[:, 1], color="white", linewidth=0.7,
                alpha=0.7)
    ax.plot(*obj.x0, "wo", markersize=5, label="старт")
    ax.set(xlim=obj.xlim, ylim=obj.ylim,
           title="Нелдер–Мид: траектория симплекса (2D Розенброк)")
    ax.legend(loc="upper right", fontsize=8)
    fig.tight_layout()
    fig.savefig(RESULTS_DIR / "nelder_mead_2d.png", dpi=150)

    obj6 = Rosenbrock(n=6)
    nm6 = NelderMead(max_iter=5000, tol=1e-7)
    nm6.run(obj6, obj6.x0)
    print("\n--- Нелдер–Мид, 6D ---")
    print(f"  итераций: {len(nm6.f_history_) - 1:5d} | "
          f"f* = {nm6.f_history_[-1]:.3e}")
    fig, ax = plt.subplots(figsize=(8, 5))
    ax.semilogy(nm6.f_history_, linewidth=1.5)
    ax.set(xlabel="номер итерации k", ylabel="f(theta*_k)",
           title="Нелдер–Мид: сходимость на 6D функции Розенброка")
    ax.grid(alpha=0.3)
    fig.tight_layout()
    fig.savefig(RESULTS_DIR / "nelder_mead_6d.png", dpi=150)

    pso = PSO(n_particles=30, max_iter=150, seed=1)
    best = pso.run(obj, bounds=np.array([obj.xlim, obj.ylim]))
    print("\n--- Рой частиц (PSO), 2D Розенброк ---")
    print(f"  итераций: {pso.max_iter:5d} | x* = ({best[0]:g}, {best[1]:g}) | "
          f"f = {obj.f(best):.3e}")
    fig, axes = plt.subplots(2, 2, figsize=(11, 9))
    fig.suptitle("PSO: частицы и скорости на изолиниях Розенброка")
    idxs = np.linspace(0, len(pso.snapshots_) - 1, 4).astype(int)
    for ax, k in zip(axes.ravel(), idxs):
        x, v, g = pso.snapshots_[k]
        _contours(ax, obj, grid)
        ax.quiver(x[:, 0], x[:, 1], v[:, 0], v[:, 1], color="yellow",
                  width=0.004)
        ax.scatter(x[:, 0], x[:, 1], c="red", s=15)
        ax.plot(*g, "w*", markersize=12)
        ax.set(xlim=obj.xlim, ylim=obj.ylim, title=f"итерация {k}")
    fig.tight_layout()
    fig.savefig(RESULTS_DIR / "pso.png", dpi=150)


def block4() -> None:
    """Блок 4: МНК — Розенброк 2D (траектории) и аппроксимация данных."""
    print("\n=== Блок 4. Нелинейные наименьшие квадраты ===")
    obj = Rosenbrock()
    grid = _grid(obj)
    problem = rosenbrock_problem()
    gn, lm = GaussNewton(), LevenbergMarquardt()

    fig, axes = plt.subplots(2, 2, figsize=(12, 9))
    for ax, method in zip(axes[0], (gn, lm)):
        points = method.run(problem, obj.x0)
        _contours(ax, obj, grid)
        ax.plot(points[:, 0], points[:, 1], "r.-", linewidth=1.2, markersize=3)
        ax.plot(*obj.x0, "wo", markersize=5)
        ax.set(xlim=obj.xlim, ylim=obj.ylim,
               title=f"Розенброк 2D: {method.name}")
        _report(method.name, "Розенброк 2D", points, problem.f(points[-1]))

    fit, x_data, y_data, model = fitting_problem()
    theta0 = np.array([1.0, 1.0, 1.0, 0.0])
    gn.run(fit, theta0)
    traj_lm = lm.run(fit, theta0)
    _report(gn.name, "аппроксимация", gn.history_, fit.f(gn.history_[-1]))
    _report(lm.name, "аппроксимация", traj_lm, fit.f(traj_lm[-1]))

    ax = axes[1, 0]
    ax.scatter(x_data, y_data, c="black", s=15, label="данные")
    xs = np.linspace(x_data.min(), x_data.max(), 300)
    for k in np.unique(np.linspace(0, len(traj_lm) - 1, 5).astype(int)):
        ax.plot(xs, model(traj_lm[k], xs), linewidth=1.2,
                label=f"итерация {k}")
    ax.set(title=f"Эволюция кривой g_theta(x): {lm.name}", xlabel="x", ylabel="y")
    ax.legend(fontsize=8)

    ax = axes[1, 1]
    ax.semilogy(gn.f_history_, linewidth=1.4, label=gn.name)
    ax.semilogy(lm.f_history_, linewidth=1.4, label=lm.name)
    ax.set(xlabel="номер итерации k", ylabel="S(theta_k)",
           title="Сходимость невязки")
    ax.grid(alpha=0.3)
    ax.legend(fontsize=8)

    fig.suptitle("Методы нелинейных наименьших квадратов")
    fig.tight_layout()
    fig.savefig(RESULTS_DIR / "least_squares.png", dpi=150)


def main() -> None:
    """Запускает все блоки лабораторной работы."""
    block1()
    block2()
    block3()
    block4()
    run_session_block()  # печатает сводные таблицы
    plt.show()


if __name__ == "__main__":
    main()
