"""Методы оптимизации первого и второго порядка.

Базовый класс Optimizer реализует общую схему спуска с сохранением истории
приближений и метрик сходимости; конкретный метод задаётся через _move.
Поиск шага выполняется классом LineSearch (метод золотого сечения).
"""

from __future__ import annotations

import numpy as np

from objectives import GradFunc, HessFunc, TargetFunction, Vector


class LineSearch:
    """Одномерная минимизация шага методом золотого сечения.

    Интервал вдоль луча x + alpha * d сначала расширяется вдвое, пока
    значение функции убывает, затем сжимается золотым сечением с
    коэффициентом r = (3 - sqrt(5)) / 2.
    """

    ratio: float = (3.0 - np.sqrt(5.0)) / 2.0

    def __init__(self, max_zoom: int = 60, tol: float = 1e-8) -> None:
        self.max_zoom = max_zoom
        self.tol = tol

    def search(self, obj: TargetFunction, x: Vector, direction: Vector,
               alpha0: float = 1.0) -> float:
        """Приближённо оптимальный шаг alpha >= 0 вдоль направления direction."""
        x = np.asarray(x, dtype=float)
        direction = np.asarray(direction, dtype=float)

        def phi(alpha: float) -> float:
            return obj.f(x + alpha * direction)

        # Расширение интервала, пока функция вдоль луча убывает.
        lo, f_lo, hi = 0.0, phi(0.0), alpha0
        for _ in range(self.max_zoom):
            f_hi = phi(hi)
            if not np.isfinite(f_hi) or f_hi >= f_lo:
                break
            lo, f_lo, hi = hi, f_hi, 2.0 * hi
        else:
            return lo

        # Золотое сечение на [lo, hi]: c — правая, d — левая внутренние точки.
        r = self.ratio
        c, d = hi - r * (hi - lo), lo + r * (hi - lo)
        f_c, f_d = phi(c), phi(d)
        for _ in range(100):
            if hi - lo <= self.tol * max(1.0, abs(lo), abs(hi)):
                break
            if f_c < f_d:
                lo, d, f_d = d, c, f_c  # минимум в [d, hi]: правая точка становится левой
                c = hi - r * (hi - lo)
                f_c = phi(c)
            else:
                hi, c, f_c = c, d, f_d  # минимум в [lo, c]: левая точка становится правой
                d = lo + r * (hi - lo)
                f_d = phi(d)
        return 0.5 * (lo + hi)


class Optimizer:
    """Базовый класс метода градиентной оптимизации.

    Args:
        max_iter: ограничение числа итераций;
        tol: порог остановки по норме градиента.

    После run доступны история приближений history_, значения функции
    f_history_ и нормы градиента grad_norms_ по итерациям.
    """

    name: str = ""

    def __init__(self, max_iter: int = 1000, tol: float = 1e-6) -> None:
        self.max_iter = max_iter
        self.tol = tol
        self._line_search = LineSearch()

    def run(self, obj: TargetFunction, x0: Vector,
            grad: GradFunc | None = None) -> Vector:
        """Минимизация; возвращает траекторию (массив N x n)."""
        grad = grad or obj.grad
        x = np.asarray(x0, dtype=float)
        self._reset(x)
        self.history_: list[Vector] = [x.copy()]
        self.f_history_: list[float] = [obj.f(x)]
        self.grad_norms_: list[float] = []
        for _ in range(self.max_iter):
            g = np.asarray(grad(x), dtype=float)
            self.grad_norms_.append(float(np.linalg.norm(g)))
            if np.linalg.norm(g) < self.tol:
                break
            points, x = self._move(obj, grad, x, g)
            self.history_.extend(points)
            self.f_history_.extend(obj.f(p) for p in points)
        return np.array(self.history_)

    def _reset(self, x: Vector) -> None:
        """Сброс внутреннего состояния (моменты, аппроксимации гессиана)."""

    def _move(self, obj: TargetFunction, grad: GradFunc, x: Vector, g: Vector
              ) -> tuple[list[Vector], Vector]:
        """Одна итерация спуска из точки x с градиентом g.

        Returns:
            Пара (точки для истории, новая точка). Их число совпадает,
            кроме покомпонентного спуска, который пишет точку после
            обновления каждой координаты.
        """
        raise NotImplementedError


class SteepestDescent(Optimizer):
    """Наискорейший градиентный спуск с одномерным поиском шага (LineSearch)."""

    name = "Наискорейший спуск"

    def __init__(self, max_iter: int = 20000, tol: float = 1e-5) -> None:
        super().__init__(max_iter, tol)

    def _move(self, obj, grad, x, g):
        direction = -g
        alpha = self._line_search.search(obj, x, direction)
        point = x + alpha * direction
        return [point], point


class CoordinateDescent(Optimizer):
    """Покомпонентный спуск: координаты обновляются по одной, градиент
    пересчитывается после каждого шага компоненты, шаг — LineSearch.

    Args:
        shuffle: False — последовательный обход x -> y -> ..., True —
                 случайное перемешивание порядка координат на каждой итерации.
    """

    def __init__(self, shuffle: bool = False, max_iter: int = 20000,
                 tol: float = 1e-5, seed: int = 0) -> None:
        super().__init__(max_iter, tol)
        self.shuffle = shuffle
        self._rng = np.random.default_rng(seed)
        self.name = ("Покомпонентный (перемеш.)" if shuffle
                     else "Покомпонентный (x -> y)")

    def _move(self, obj, grad, x, g):
        points = []
        order = (self._rng.permutation(x.size) if self.shuffle
                 else np.arange(x.size))
        for i in order:
            g = np.asarray(grad(x), dtype=float)
            direction = np.zeros_like(x)
            direction[i] = -g[i]
            alpha = self._line_search.search(obj, x, direction)
            x = x + alpha * direction
            points.append(x)
        return points, x


class NesterovMomentum(Optimizer):
    """Градиентный спуск с моментом Нестерова.

    Схема: v <- mu * v - lr * grad(x + mu * v); x <- x + v.
    """

    name = "Момент Нестерова"

    def __init__(self, lr: float = 1e-3, mu: float = 0.95,
                 max_iter: int = 8000, tol: float = 1e-5) -> None:
        super().__init__(max_iter, tol)
        self.lr, self.mu = lr, mu

    def _reset(self, x: Vector) -> None:
        self._velocity = np.zeros_like(x)

    def _move(self, obj, grad, x, g):
        lookahead = x + self.mu * self._velocity
        self._velocity = self.mu * self._velocity - self.lr * grad(lookahead)
        point = x + self._velocity
        return [point], point


class Adam(Optimizer):
    """Метод Adam с поправками первого и второго моментов m_hat, V_hat."""

    name = "Adam"

    def __init__(self, lr: float = 0.05, beta1: float = 0.9,
                 beta2: float = 0.999, eps: float = 1e-8,
                 max_iter: int = 4000, tol: float = 1e-5) -> None:
        super().__init__(max_iter, tol)
        self.lr, self.beta1, self.beta2, self.eps = lr, beta1, beta2, eps

    def _reset(self, x: Vector) -> None:
        self._m = np.zeros_like(x)
        self._v = np.zeros_like(x)
        self._t = 0

    def _move(self, obj, grad, x, g):
        self._t += 1
        self._m = self.beta1 * self._m + (1.0 - self.beta1) * g
        self._v = self.beta2 * self._v + (1.0 - self.beta2) * g ** 2
        m_hat = self._m / (1.0 - self.beta1 ** self._t)
        v_hat = self._v / (1.0 - self.beta2 ** self._t)
        point = x - self.lr * m_hat / (np.sqrt(v_hat) + self.eps)
        return [point], point


class Newton(Optimizer):
    """Классический метод Ньютона: x <- x - H^{-1} grad f.

    Args:
        hessian: способ вычисления гессиана (аналитический или численный);
                 по умолчанию obj.hess. Если полный шаг не убывает,
                 выполняется одномерный поиск вдоль направления Ньютона.
    """

    name = "Ньютон"

    def __init__(self, hessian: HessFunc | None = None,
                 max_iter: int = 500, tol: float = 1e-6) -> None:
        super().__init__(max_iter, tol)
        self._hessian = hessian

    def _move(self, obj, grad, x, g):
        hessian = self._hessian or obj.hess
        H = np.asarray(hessian(x), dtype=float)
        p = np.linalg.lstsq(H, -g, rcond=None)[0]
        if g @ p >= 0 or not np.all(np.isfinite(p)):
            p = -g  # гессиан не положительно определён
        point = x + p
        if obj.f(point) > obj.f(x):
            alpha = self._line_search.search(obj, x, p)
            point = x + alpha * p
        return [point], point


class BFGS(Optimizer):
    """Квазиньютоновский метод BFGS.

    Итерационная аппроксимация обратного гессиана B_k через поправку
    ранга два; шаг вдоль направления спуска p = -B_k g подбирается
    одномерным поиском.
    """

    name = "BFGS"

    def __init__(self, max_iter: int = 500, tol: float = 1e-6) -> None:
        super().__init__(max_iter, tol)

    def _reset(self, x: Vector) -> None:
        self._H = np.eye(x.size)

    def _move(self, obj, grad, x, g):
        p = -self._H @ g
        if g @ p >= 0:
            p, self._H = -g, np.eye(x.size)  # страховка от потери положительной определённости
        alpha = self._line_search.search(obj, x, p)
        s = alpha * p
        point = x + s
        y = np.asarray(grad(point), dtype=float) - g
        ys = float(y @ s)
        if ys > 1e-12:
            rho = 1.0 / ys
            eye = np.eye(x.size)
            A = eye - rho * np.outer(s, y)
            B = eye - rho * np.outer(y, s)
            self._H = A @ self._H @ B + rho * np.outer(s, s)
        return [point], point
