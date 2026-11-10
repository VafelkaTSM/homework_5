"""Целевые функции и базовые средства численного дифференцирования."""

from __future__ import annotations

from typing import Callable

import numpy as np

Vector = np.ndarray
GradFunc = Callable[[Vector], Vector]
HessFunc = Callable[[Vector], Vector]
VectorField = Callable[[Vector], Vector]


def jacobian(func: VectorField, x: Vector, h: float = 1e-6) -> Vector:
    """Якобиан векторной функции (центральные конечные разности)."""
    x = np.asarray(x, dtype=float)
    fx = np.asarray(func(x), dtype=float)
    jac = np.empty((fx.size, x.size))
    for i in range(x.size):
        e = np.zeros_like(x)
        e[i] = h
        jac[:, i] = (np.asarray(func(x + e)) - np.asarray(func(x - e))) / (2.0 * h)
    return jac


class TargetFunction:
    """Скалярная целевая функция f: R^n -> R.

    Аналитические градиент и гессиан задаются при создании; при их отсутствии
    методы grad/hess автоматически используют численные оценки.

    Attributes:
        name: название функции;
        x0: начальная точка поиска;
        xlim, ylim, levels: параметры изолиний для двумерных функций.
    """

    name: str = ""
    x0: Vector = np.zeros(2)
    xlim: tuple[float, float] = (-2.0, 2.0)
    ylim: tuple[float, float] = (-2.0, 2.0)
    levels: Vector = np.geomspace(0.1, 1000.0, 25)

    def __init__(self, func: Callable[[Vector], float],
                 grad: GradFunc | None = None,
                 hess: HessFunc | None = None) -> None:
        self._func, self._grad, self._hess = func, grad, hess

    def f(self, x: Vector) -> float:
        """Значение функции в точке x."""
        return float(self._func(np.asarray(x, dtype=float)))

    def grad(self, x: Vector) -> Vector:
        """Аналитический градиент (при отсутствии — численный)."""
        if self._grad is not None:
            return np.asarray(self._grad(np.asarray(x, dtype=float)), dtype=float)
        return self.grad_numeric(x)

    def grad_numeric(self, x: Vector, h: float = 1e-5) -> Vector:
        """Градиент центральными конечными разностями."""
        x = np.asarray(x, dtype=float)
        grad = np.empty_like(x)
        for i in range(x.size):
            e = np.zeros_like(x)
            e[i] = h
            grad[i] = (self.f(x + e) - self.f(x - e)) / (2.0 * h)
        return grad

    def hess(self, x: Vector) -> Vector:
        """Аналитический гессиан (при отсутствии — численный)."""
        if self._hess is not None:
            return np.asarray(self._hess(np.asarray(x, dtype=float)), dtype=float)
        return self.hess_numeric(x)

    def hess_numeric(self, x: Vector, h: float = 1e-4) -> Vector:
        """Гессиан — якобиан численного градиента."""
        return jacobian(self.grad_numeric, np.asarray(x, dtype=float), h)


class Rosenbrock(TargetFunction):
    """f = sum [100 (x_{i+1} - x_i^2)^2 + (1 - x_i)^2], минимум в (1, ..., 1)."""

    name = "Розенброк"
    x0 = np.array([-1.5, 2.0])
    xlim = (-2.0, 2.0)
    ylim = (-1.0, 3.0)
    levels = np.geomspace(0.1, 5000.0, 25)

    def __init__(self, n: int = 2) -> None:
        if n != 2:
            self.x0 = np.full(n, -1.0)
        super().__init__(self._func, self._grad, self._hess)

    @staticmethod
    def _func(x: Vector) -> float:
        return float(np.sum(100.0 * (x[1:] - x[:-1] ** 2) ** 2
                            + (1.0 - x[:-1]) ** 2))

    @staticmethod
    def _grad(x: Vector) -> Vector:
        grad = np.zeros_like(x)
        grad[:-1] += 400.0 * x[:-1] * (x[:-1] ** 2 - x[1:]) + 2.0 * (x[:-1] - 1.0)
        grad[1:] += 200.0 * (x[1:] - x[:-1] ** 2)
        return grad

    @staticmethod
    def _hess(x: Vector) -> Vector:
        n = x.size
        hess = np.zeros((n, n))
        idx = np.arange(n - 1)
        hess[idx, idx] += 1200.0 * x[:-1] ** 2 - 400.0 * x[1:] + 2.0
        hess[idx + 1, idx + 1] += 200.0
        off = -400.0 * x[:-1]
        hess[idx, idx + 1] += off
        hess[idx + 1, idx] += off
        return hess


class Himmelblau(TargetFunction):
    """f = (x1^2 + x2 - 11)^2 + (x1 + x2^2 - 7)^2 (четыре минимума)."""

    name = "Химмельблау"
    x0 = np.array([-4.0, 4.0])
    xlim = (-6.0, 6.0)
    ylim = (-6.0, 6.0)
    levels = np.geomspace(0.1, 2000.0, 25)

    def __init__(self) -> None:
        super().__init__(self._func, self._grad, self._hess)

    @staticmethod
    def _func(x: Vector) -> float:
        return float((x[0] ** 2 + x[1] - 11.0) ** 2
                     + (x[0] + x[1] ** 2 - 7.0) ** 2)

    @staticmethod
    def _grad(x: Vector) -> Vector:
        u = x[0] ** 2 + x[1] - 11.0
        v = x[0] + x[1] ** 2 - 7.0
        return np.array([4.0 * x[0] * u + 2.0 * v,
                         2.0 * u + 4.0 * x[1] * v])

    @staticmethod
    def _hess(x: Vector) -> Vector:
        u = x[0] ** 2 + x[1] - 11.0
        v = x[0] + x[1] ** 2 - 7.0
        return np.array([[4.0 * u + 8.0 * x[0] ** 2 + 2.0, 4.0 * (x[0] + x[1])],
                         [4.0 * (x[0] + x[1]), 4.0 * v + 8.0 * x[1] ** 2 + 2.0]])
