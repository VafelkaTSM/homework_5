"""Лабораторная работа № 5.

Поиск минимума двумерных функций Розенброка и Химмельблау методами:
    * наискорейшего градиентного спуска;
    * покомпонентного (покоординатного) градиентного спуска;
    * градиентного спуска с моментом Нестерова;
    * Adam.

Градиент целевой функции вычисляется двумя способами:
    * аналитически (точные формулы);
    * численно (scipy.differentiate.derivative -- конечно-разностная
      формула 8-го порядка точности).

Для каждой функции строится рисунок: изолинии целевой функции и
траектории всех методов для обоих способов вычисления градиента.

Запуск: python main.py
"""

from __future__ import annotations

from typing import Callable

import matplotlib.pyplot as plt
import numpy as np
from scipy.differentiate import derivative

Vector = np.ndarray
GradFunc = Callable[[Vector], Vector]


class Objective:
    """Двумерная целевая функция с аналитическим и численным градиентом.

    Attributes:
        name: название функции;
        x0: начальная точка поиска;
        xlim: пределы построения графика по оси x;
        ylim: пределы построения графика по оси y;
        levels: уровни изолиний для визуализации.
    """

    name: str = ""
    x0: Vector = np.zeros(2)
    xlim: tuple[float, float] = (-1.0, 1.0)
    ylim: tuple[float, float] = (-1.0, 1.0)
    levels: Vector = np.geomspace(0.1, 1000.0, 30)

    def __init__(self, func: Callable[[float, float], float],
                 grad: Callable[[float, float], Vector]) -> None:
        """Инициализация по скалярной функции и её аналитическому градиенту.

        Args:
            func: функция двух переменных f(x1, x2) -> float;
            grad: аналитический градиент (x1, x2) -> массив из двух чисел.
        """
        self._func = func
        self._grad = grad

    def f(self, x: Vector) -> float:
        """Значение целевой функции в точке x = [x1, x2]."""
        return float(self._func(float(x[0]), float(x[1])))

    def grad_analytic(self, x: Vector) -> Vector:
        """Аналитический градиент в точке x."""
        return np.asarray(self._grad(float(x[0]), float(x[1])), dtype=float)

    def grad_numeric(self, x: Vector) -> Vector:
        """Численный градиент в точке x.

        Каждая частная производная находится функцией
        scipy.differentiate.derivative (формула 8-го порядка точности).
        """
        x = np.asarray(x, dtype=float)
        grad = np.empty_like(x)
        for i in range(x.size):
            partial = np.vectorize(self._partial(x, i))
            grad[i] = float(derivative(partial, float(x[i]), order=8).df)
        return grad

    def _partial(self, x: Vector, i: int) -> Callable[[float], float]:
        """Возвращает функцию одной переменной t -> f(x с заменой x[i] на t)."""
        def g(t: float) -> float:
            z = x.copy()
            z[i] = t
            return self.f(z)
        return g


class Rosenbrock(Objective):
    """Функция Розенброка f = (1 - x1)^2 + 100 * (x2 - x1^2)^2 (минимум в (1, 1))."""

    name = "Розенброк"
    x0 = np.array([-1.5, 2.0])
    xlim = (-2.0, 2.0)
    ylim = (-1.0, 3.0)
    levels = np.geomspace(0.1, 5000.0, 30)

    def __init__(self) -> None:
        super().__init__(self._func, self._grad)

    @staticmethod
    def _func(x1: float, x2: float) -> float:
        """Значение функции Розенброка."""
        return (1.0 - x1) ** 2 + 100.0 * (x2 - x1 ** 2) ** 2

    @staticmethod
    def _grad(x1: float, x2: float) -> Vector:
        """Аналитический градиент функции Розенброка."""
        dx1 = -2.0 * (1.0 - x1) - 400.0 * x1 * (x2 - x1 ** 2)
        dx2 = 200.0 * (x2 - x1 ** 2)
        return np.array([dx1, dx2])


class Himmelblau(Objective):
    """Функция Химмельблау f = (x1^2 + x2 - 11)^2 + (x1 + x2^2 - 7)^2."""

    name = "Химмельблау"
    x0 = np.array([-4.0, 4.0])
    xlim = (-6.0, 6.0)
    ylim = (-6.0, 6.0)
    levels = np.geomspace(0.1, 2000.0, 30)

    def __init__(self) -> None:
        super().__init__(self._func, self._grad)

    @staticmethod
    def _func(x1: float, x2: float) -> float:
        """Значение функции Химмельблау."""
        return (x1 ** 2 + x2 - 11.0) ** 2 + (x1 + x2 ** 2 - 7.0) ** 2

    @staticmethod
    def _grad(x1: float, x2: float) -> Vector:
        """Аналитический градиент функции Химмельблау."""
        dx1 = 4.0 * x1 * (x1 ** 2 + x2 - 11.0) + 2.0 * (x1 + x2 ** 2 - 7.0)
        dx2 = 2.0 * (x1 ** 2 + x2 - 11.0) + 4.0 * x2 * (x1 + x2 ** 2 - 7.0)
        return np.array([dx1, dx2])


class Optimizer:
    """Базовый класс метода градиентной оптимизации.

    Args:
        max_iter: максимальное число итераций;
        tol: порог остановки по норме градиента.
    """

    name: str = ""

    def __init__(self, max_iter: int = 2000, tol: float = 1e-5) -> None:
        """Задание параметров остановки метода."""
        self.max_iter = max_iter
        self.tol = tol

    def run(self, obj: Objective, grad: GradFunc, x0: Vector) -> Vector:
        """Минимизирует функцию и возвращает траекторию (массив N x 2).

        Args:
            obj: целевая функция;
            grad: функция вычисления градиента (аналитическая или численная);
            x0: начальная точка.
        """
        raise NotImplementedError

    @staticmethod
    def _line_search(obj: Objective, x: Vector, direction: Vector,
                     derivative_along: float, alpha0: float = 1.0) -> float:
        """Дробный шаг (условие Армихо) вдоль направления direction.

        Args:
            obj: целевая функция;
            x: текущая точка;
            direction: направление спуска;
            derivative_along: производная функции по направлению (g^T d);
            alpha0: начальная величина шага.

        Returns:
            Подобранная величина шага.
        """
        alpha = alpha0
        fx = obj.f(x)
        for _ in range(50):
            if obj.f(x + alpha * direction) <= fx + 1e-4 * alpha * derivative_along:
                return alpha
            alpha *= 0.5
        return alpha

    @staticmethod
    def _to_array(points: list[Vector]) -> Vector:
        """Преобразует список точек траектории в массив N x 2."""
        return np.array(points)


class SteepestDescent(Optimizer):
    """Наискорейший градиентный спуск с выбором шага по условию Армихо."""

    name = "Наискорейший спуск"

    def __init__(self, max_iter: int = 6000, tol: float = 1e-5,
                 alpha0: float = 1.0) -> None:
        """Задание параметров, включая начальный шаг alpha0."""
        super().__init__(max_iter, tol)
        self.alpha0 = alpha0

    def run(self, obj: Objective, grad: GradFunc, x0: Vector) -> Vector:
        """Спуск вдоль антиградиента с дробным шагом."""
        x = np.asarray(x0, dtype=float)
        points = [x.copy()]
        for _ in range(self.max_iter):
            g = grad(x)
            if np.linalg.norm(g) < self.tol:
                break
            direction = -g
            alpha = self._line_search(obj, x, direction, float(g @ direction), self.alpha0)
            x = x + alpha * direction
            points.append(x.copy())
        return self._to_array(points)


class CoordinateDescent(Optimizer):
    """Покомпонентный (покоординатный) градиентный спуск.

    На каждой итерации компоненты x1, x2 обновляются по очереди; шаг по
    каждой координате подбирается дробным методом (условие Армихо).
    """

    name = "Покомпонентный спуск"

    def __init__(self, max_iter: int = 1500, tol: float = 1e-5,
                 alpha0: float = 1.0) -> None:
        """Задание параметров, включая начальный шаг alpha0."""
        super().__init__(max_iter, tol)
        self.alpha0 = alpha0

    def run(self, obj: Objective, grad: GradFunc, x0: Vector) -> Vector:
        """Обновление координат по одной с выбором шага."""
        x = np.asarray(x0, dtype=float)
        points = [x.copy()]
        for _ in range(self.max_iter):
            if np.linalg.norm(grad(x)) < self.tol:
                break
            for i in range(x.size):
                g = grad(x)
                direction = np.zeros_like(x)
                direction[i] = -g[i]
                alpha = self._line_search(obj, x, direction,
                                          float(g @ direction), self.alpha0)
                x = x + alpha * direction
                points.append(x.copy())
        return self._to_array(points)


class NesterovMomentum(Optimizer):
    """Градиентный спуск с моментом Нестерова.

    Схема: v <- mu * v - lr * grad(x + mu * v); x <- x + v.
    """

    name = "Момент Нестерова"

    def __init__(self, lr: float = 1e-3, mu: float = 0.95,
                 max_iter: int = 4000, tol: float = 1e-5) -> None:
        """Задание скорости обучения lr и коэффициента момента mu."""
        super().__init__(max_iter, tol)
        self.lr = lr
        self.mu = mu

    def run(self, obj: Objective, grad: GradFunc, x0: Vector) -> Vector:
        """Спуск с накоплением скорости и «заглядыванием вперёд»."""
        x = np.asarray(x0, dtype=float)
        velocity = np.zeros_like(x)
        points = [x.copy()]
        for _ in range(self.max_iter):
            if np.linalg.norm(grad(x)) < self.tol:
                break
            velocity = self.mu * velocity - self.lr * grad(x + self.mu * velocity)
            x = x + velocity
            points.append(x.copy())
        return self._to_array(points)


class Adam(Optimizer):
    """Метод Adam (адаптивные оценки первого и второго моментов градиента)."""

    name = "Adam"

    def __init__(self, lr: float = 0.05, beta1: float = 0.9,
                 beta2: float = 0.999, eps: float = 1e-8,
                 max_iter: int = 4000, tol: float = 1e-5) -> None:
        """Задание скорости обучения и параметров сглаживания моментов."""
        super().__init__(max_iter, tol)
        self.lr = lr
        self.beta1 = beta1
        self.beta2 = beta2
        self.eps = eps

    def run(self, obj: Objective, grad: GradFunc, x0: Vector) -> Vector:
        """Итерации Adam со смещёнными оценками моментов."""
        x = np.asarray(x0, dtype=float)
        m = np.zeros_like(x)
        v = np.zeros_like(x)
        points = [x.copy()]
        for t in range(1, self.max_iter + 1):
            g = grad(x)
            if np.linalg.norm(g) < self.tol:
                break
            m = self.beta1 * m + (1.0 - self.beta1) * g
            v = self.beta2 * v + (1.0 - self.beta2) * g ** 2
            m_hat = m / (1.0 - self.beta1 ** t)
            v_hat = v / (1.0 - self.beta2 ** t)
            x = x - self.lr * m_hat / (np.sqrt(v_hat) + self.eps)
            points.append(x.copy())
        return self._to_array(points)


def visualize(obj: Objective) -> None:
    """Строит изолинии функции и траектории всех методов.

    Для каждого метода показаны две траектории: с аналитическим
    градиентом (сплошная линия) и с численным (штриховая линия).
    """
    methods = [
        SteepestDescent(),
        CoordinateDescent(),
        NesterovMomentum(),
        Adam(),
    ]
    gradients = [
        ("аналитический", obj.grad_analytic, "-"),
        ("численный", obj.grad_numeric, "--"),
    ]

    grid_x, grid_y = np.meshgrid(
        np.linspace(*obj.xlim, 400), np.linspace(*obj.ylim, 400)
    )
    grid_z = np.vectorize(lambda a, b: obj.f(np.array([a, b])))(grid_x, grid_y)

    fig, axes = plt.subplots(2, 2, figsize=(12, 10))
    x0_str = ", ".join(f"{value:g}" for value in obj.x0)
    fig.suptitle(f"Функция {obj.name}: траектории методов "
                 f"(начальная точка ({x0_str}))")

    for ax, method in zip(axes.ravel(), methods):
        ax.contourf(grid_x, grid_y, grid_z, levels=obj.levels,
                    cmap="viridis", extend="both")
        ax.contour(grid_x, grid_y, grid_z, levels=obj.levels,
                   colors="black", linewidths=0.3)
        for grad_name, grad, style in gradients:
            points = method.run(obj, grad, obj.x0)
            ax.plot(points[:, 0], points[:, 1], style, color="red",
                    linewidth=1.4, label=f"градиент: {grad_name}")
            print(f"  {obj.name:12s} | {method.name:22s} | {grad_name:14s} | "
                  f"итераций: {len(points) - 1:5d} | "
                  f"x* = ({points[-1, 0]: .6f}, {points[-1, 1]: .6f}) | "
                  f"f = {obj.f(points[-1]):.3e}")
        ax.plot(*obj.x0, "wo", markersize=5, label="старт")
        ax.set_xlim(obj.xlim)
        ax.set_ylim(obj.ylim)
        ax.set_title(method.name)
        ax.set_xlabel("x1")
        ax.set_ylabel("x2")
        ax.legend(loc="upper right", fontsize=8)

    fig.tight_layout()
    fig.savefig(f"{type(obj).__name__.lower()}.png", dpi=150)


def main() -> None:
    """Запускает оптимизацию для обеих функций и строит графики."""
    for obj in (Rosenbrock(), Himmelblau()):
        print(f"\n=== Функция {obj.name} ===")
        visualize(obj)
    plt.show()


if __name__ == "__main__":
    main()

