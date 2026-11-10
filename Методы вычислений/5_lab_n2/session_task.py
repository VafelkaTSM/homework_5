"""Блок 5. Прикладная задача оптимизации: распределение времени на сессию.

Максимизация суммарной вероятности сдачи N = 3 экзаменов при бюджете
времени M = 8 ч: сумма t_i <= M, t_i > 0. Целевая функция на минимум:
f(t) = -sum P_i(t_i), где P_1, P_2 — логистические кривые, P_3 — функция
типа Гаусса.

Ограничения учитываются двумя способами:
    * проекция — градиентный спуск с LineSearch и евклидовой проекцией
      на симплекс бюджета;
    * штраф — BFGS с квадратичным штрафом за нарушение ограничений.

Далее серия из 100 запусков со случайных начальных приближений,
кластеризация сошедшихся решений и итоговые таблицы.
"""

from __future__ import annotations

import numpy as np

from objectives import TargetFunction, Vector
from optimizers import BFGS, Optimizer

BUDGET = 8.0  # суммарный бюджет времени M > 5 часов
N_EXAMS = 3


class ExamSession(TargetFunction):
    """f(t) = -(P_1(t_1) + P_2(t_2) + P_3(t_3)) — задача на минимум."""

    name = "Сессия"
    x0 = np.full(N_EXAMS, 1.0)

    def __init__(self) -> None:
        super().__init__(self._func, self._grad)

    @staticmethod
    def _sigmoid(t: float, k: float, t0: float) -> float:
        return 1.0 / (1.0 + np.exp(-k * (t - t0)))

    def _probs(self, t: Vector) -> Vector:
        return np.array([
            self._sigmoid(t[0], 1.5, 3.0),
            self._sigmoid(t[1], 2.0, 4.5),
            np.exp(-((t[2] - 2.0) ** 2) / 1.5),
        ])

    def _func(self, t: Vector) -> float:
        return float(-np.sum(self._probs(t)))

    def _grad(self, t: Vector) -> Vector:
        p = self._probs(t)
        dp = np.array([
            1.5 * p[0] * (1.0 - p[0]),
            2.0 * p[1] * (1.0 - p[1]),
            -2.0 * (t[2] - 2.0) / 1.5 * p[2],
        ])
        return -dp


def project_simplex(v: Vector, budget: float = BUDGET) -> Vector:
    """Евклидова проекция точки v на симплекс {t >= 0: sum t = budget}."""
    v = np.asarray(v, dtype=float)
    u = np.sort(v)[::-1]
    css = np.cumsum(u)
    rho = np.nonzero(u * np.arange(1, v.size + 1) > css - budget)[0][-1]
    theta = (css[rho] - budget) / (rho + 1.0)
    return np.clip(v - theta, 0.0, None)


class ProjectedGradient(Optimizer):
    """Градиентный спуск с LineSearch и проекцией на симплекс ограничений.

    Остановка — по невязке условий Куна–Таккера ||x - Proj(x - grad)||.
    """

    name = "Проекция на симплекс"

    def __init__(self, budget: float = BUDGET, max_iter: int = 500,
                 tol: float = 1e-7) -> None:
        super().__init__(max_iter, tol)
        self.budget = budget

    def run(self, obj: TargetFunction, x0: Vector,
            grad=None) -> Vector:
        grad = grad or obj.grad
        x = project_simplex(np.asarray(x0, dtype=float), self.budget)
        self.history_: list[Vector] = [x.copy()]
        self.f_history_: list[float] = [obj.f(x)]
        for _ in range(self.max_iter):
            g = np.asarray(grad(x), dtype=float)
            if np.linalg.norm(x - project_simplex(x - g, self.budget)) < self.tol:
                break  # условия Куна–Таккера выполнены
            alpha = self._line_search.search(obj, x, -g)
            point = project_simplex(x - alpha * g, self.budget)
            for _ in range(40):
                if obj.f(point) <= obj.f(x):
                    break
                alpha *= 0.5
                point = project_simplex(x - alpha * g, self.budget)
            if obj.f(point) > obj.f(x):
                break  # не удалось уменьшить f
            self.history_.append(point.copy())
            self.f_history_.append(obj.f(point))
            x = point
        return np.array(self.history_)


class QuadraticPenalty:
    """Штрафная функция F_mu(t) = f(t) + mu * h(t),
    h(t) = sum max^2(0, -t_i) + max^2(0, sum t_i - M)."""

    def __init__(self, base: TargetFunction, budget: float = BUDGET,
                 mu: float = 100.0) -> None:
        self.base, self.budget, self.mu = base, budget, mu

    def h(self, t: Vector) -> float:
        t = np.asarray(t, dtype=float)
        return float(np.sum(np.clip(-t, 0.0, None) ** 2)
                     + max(0.0, t.sum() - self.budget) ** 2)

    def f(self, t: Vector) -> float:
        return self.base.f(t) + self.mu * self.h(t)

    def grad(self, t: Vector) -> Vector:
        t = np.asarray(t, dtype=float)
        grad = self.base.grad(t).copy()
        grad -= 2.0 * self.mu * np.clip(-t, 0.0, None)
        grad += 2.0 * self.mu * max(0.0, t.sum() - self.budget)
        return grad


def _clusterize(finals: list[Vector], obj: TargetFunction,
                ndigits: int = 2) -> list[tuple[Vector, int, float]]:
    """Группировка точек, округлённых до ndigits знаков, с подсчётом траекторий."""
    clusters: dict[tuple[float, ...], list[Vector]] = {}
    for point in finals:
        clusters.setdefault(tuple(np.round(point, ndigits)), []).append(point)
    rows = [(np.clip(np.mean(ps, axis=0), 0.0, None), len(ps),
             obj.f(np.clip(np.mean(ps, axis=0), 0.0, None)))
            for ps in clusters.values()]
    return sorted(rows, key=lambda row: -row[1])


def _print_table(title: str, rows: list[tuple[Vector, int, float]]) -> None:
    print(f"\n--- {title} ---")
    print(f" {'(t1, t2, t3)':>20s} | Траекторий |   F(t)")
    for mean, count, f in rows:
        t_str = "(" + ", ".join(f"{v:5.2f}" for v in mean) + ")"
        print(f" {t_str:>20s} | {count:10d} | {f:6.3f}")


def run_session_block(n_runs: int = 100, seed: int = 42) -> None:
    """Мультизапуск из n_runs случайных начальных точек, кластеризация
    сошедшихся решений и итоговые сводные таблицы."""
    session = ExamSession()
    rng = np.random.default_rng(seed)
    starts = rng.uniform(0.1, BUDGET, size=(n_runs, N_EXAMS))

    print(f"\n=== Блок 5. Распределение времени на сессию (M = {BUDGET:g} ч) ===")

    projector = ProjectedGradient()
    finals = [projector.run(session, s)[-1] for s in starts]
    print(f"\nМетод проекции: запусков {n_runs}")
    _print_table("Проекция на симплекс", _clusterize(finals, session))

    penalty = QuadraticPenalty(session)
    bfgs = BFGS(max_iter=1000, tol=1e-8)
    finals = []
    for s in starts:
        theta = s.copy()
        for mu in (10.0, 100.0, 1000.0):
            penalty.mu = mu
            theta = bfgs.run(penalty, theta, grad=penalty.grad)[-1]
        finals.append(theta)
    print(f"\nМетод штрафных функций (BFGS, mu = 10 -> 100 -> 1000): запусков {n_runs}")
    _print_table("Квадратичный штраф", _clusterize(finals, session))
