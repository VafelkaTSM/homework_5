"""Нелинейный метод наименьших квадратов: Гаусс–Ньютон и Левенберг–Марквардт."""

from __future__ import annotations

from collections.abc import Callable

import numpy as np

from objectives import Vector, jacobian


class LeastSquaresProblem:
    """Задача МНК: минимизация S(theta) = ||r(theta)||^2.

    r — вектор невязок; якобиан J задан аналитически либо вычисляется
    конечными разностями.
    """

    def __init__(self, residual: Callable[[Vector], Vector],
                 jac: Callable[[Vector], Vector] | None = None,
                 name: str = "") -> None:
        self._r, self._jac, self.name = residual, jac, name

    def r(self, theta: Vector) -> Vector:
        return np.asarray(self._r(np.asarray(theta, dtype=float)), dtype=float)

    def jacobian(self, theta: Vector) -> Vector:
        theta = np.asarray(theta, dtype=float)
        if self._jac is not None:
            return np.asarray(self._jac(theta), dtype=float)
        return jacobian(self._r, theta)

    def f(self, theta: Vector) -> float:
        r = self.r(theta)
        return float(r @ r)

    def grad(self, theta: Vector) -> Vector:
        return self.jacobian(theta).T @ self.r(theta)


class GaussNewton:
    """Метод Гаусса–Ньютона: шаг из (J^T J) h = -J^T r, при необходимости
    с дроблением шага до убывания невязки."""

    name = "Гаусс–Ньютон"

    def __init__(self, max_iter: int = 300, tol: float = 1e-8) -> None:
        self.max_iter, self.tol = max_iter, tol

    def run(self, problem: LeastSquaresProblem, theta0: Vector) -> Vector:
        theta = np.asarray(theta0, dtype=float)
        history = [theta.copy()]
        self.f_history_: list[float] = [problem.f(theta)]
        for _ in range(self.max_iter):
            r, J = problem.r(theta), problem.jacobian(theta)
            if np.linalg.norm(J.T @ r) < self.tol:
                break
            h = np.linalg.lstsq(J, -r, rcond=None)[0]
            alpha, f0 = 1.0, problem.f(theta)
            for _ in range(50):
                if problem.f(theta + alpha * h) < f0:
                    break
                alpha *= 0.5
            theta = theta + alpha * h
            history.append(theta.copy())
            self.f_history_.append(problem.f(theta))
        self.history_ = np.array(history)
        return self.history_


class LevenbergMarquardt:
    """Метод Левенберга–Марквардта.

    Демпфирующий параметр lambda уменьшается в nu раз при положительном
    выигрыше rho(h_lm) = фактическое уменьшение / предсказанное моделью
    и увеличивается в nu раз при отрицательном.
    """

    name = "Левенберг–Марквардт"

    def __init__(self, max_iter: int = 300, tol: float = 1e-8,
                 lam: float = 1e-2, nu: float = 10.0) -> None:
        self.max_iter, self.tol, self.lam, self.nu = max_iter, tol, lam, nu

    def run(self, problem: LeastSquaresProblem, theta0: Vector) -> Vector:
        theta = np.asarray(theta0, dtype=float)
        lam = self.lam
        history = [theta.copy()]
        self.f_history_: list[float] = [problem.f(theta)]
        for _ in range(self.max_iter):
            r, J = problem.r(theta), problem.jacobian(theta)
            g = J.T @ r
            if np.linalg.norm(g) < self.tol:
                break
            JTJ = J.T @ J
            damping = np.diag(np.maximum(np.diag(JTJ), 1e-12))
            f0 = problem.f(theta)
            for _ in range(80):
                h = np.linalg.lstsq(JTJ + lam * damping, -g, rcond=None)[0]
                f_new = problem.f(theta + h)
                predicted = h @ JTJ @ h + 2.0 * lam * (h @ damping @ h)
                rho = (f0 - f_new) / predicted if predicted > 0 else -np.inf
                if rho > 0.0:
                    lam = max(lam / self.nu, 1e-15)
                    break
                lam *= self.nu
            else:
                break  # параметр lambda вырос недопустимо
            theta = theta + h
            history.append(theta.copy())
            self.f_history_.append(problem.f(theta))
        self.history_ = np.array(history)
        return self.history_


def rosenbrock_problem() -> LeastSquaresProblem:
    """Функция Розенброка как задача МНК: r = (1 - t1, 10 (t2 - t1^2))."""

    def residual(t: Vector) -> Vector:
        return np.array([1.0 - t[0], 10.0 * (t[1] - t[0] ** 2)])

    def jac(t: Vector) -> Vector:
        return np.array([[-1.0, 0.0], [-20.0 * t[0], 10.0]])

    return LeastSquaresProblem(residual, jac, name="Розенброк (МНК)")


def fitting_problem(seed: int = 0, n_points: int = 40,
                    noise: float = 0.05):
    """Аппроксимация зашумлённых данных моделью с четырьмя параметрами:
    g_theta(x) = a * exp(-((x - b) / c)^2) + d * x.

    Returns:
        (problem, x_data, y_data, model): задача МНК, точки данных и модель.
    """
    rng = np.random.default_rng(seed)
    true_theta = np.array([2.0, 1.5, 0.8, 0.3])
    x_data = np.linspace(0.0, 4.0, n_points)

    def model(theta: Vector, x: Vector) -> Vector:
        return theta[0] * np.exp(-((x - theta[1]) / theta[2]) ** 2) + theta[3] * x

    y_data = model(true_theta, x_data) + noise * rng.normal(size=n_points)
    problem = LeastSquaresProblem(lambda t: model(t, x_data) - y_data,
                                  name="Аппроксимация данных")
    return problem, x_data, y_data, model
