"""Методы оптимизации нулевого порядка: Нелдер–Мид и рой частиц (PSO)."""

from __future__ import annotations

import numpy as np

from objectives import TargetFunction, Vector


class NelderMead:
    """Метод Нелдера–Мида.

    Симплекс из n + 1 вершины; операции отражения, растяжения, сжатия
    и редукции. Остановка — по разбросу расстояний вершин до центра тяжести.

    После run доступны history_ (центроиды симплекса), f_history_
    (лучшее значение f по итерациям) и simplex_history_ (вершины).
    """

    name = "Нелдер–Мид"
    alpha, gamma, rho, sigma = 1.0, 2.0, 0.5, 0.5

    def __init__(self, max_iter: int = 2000, tol: float = 1e-8,
                 step: float = 1.0) -> None:
        self.max_iter, self.tol, self.step = max_iter, tol, step

    def run(self, obj: TargetFunction, x0: Vector) -> Vector:
        x0 = np.asarray(x0, dtype=float)
        n = x0.size
        simplex = np.vstack([x0, x0 + self.step * np.eye(n)])
        fvals = np.array([obj.f(v) for v in simplex])
        self.history_: list[Vector] = []
        self.f_history_: list[float] = []
        self.simplex_history_: list[Vector] = []
        for _ in range(self.max_iter):
            order = np.argsort(fvals)
            simplex, fvals = simplex[order], fvals[order]
            centroid = simplex.mean(axis=0)
            self.history_.append(centroid.copy())
            self.f_history_.append(float(fvals[0]))
            self.simplex_history_.append(simplex.copy())
            spread = max(np.linalg.norm(v - centroid) for v in simplex)
            if spread < self.tol:
                break
            # Отражение наихудшей вершины через центроид лучших n.
            top = simplex[:-1].mean(axis=0)
            xr = top + self.alpha * (top - simplex[-1])
            fr = obj.f(xr)
            if fvals[0] <= fr < fvals[-2]:
                simplex[-1], fvals[-1] = xr, fr
            elif fr < fvals[0]:
                xe = top + self.gamma * (xr - top)
                fe = obj.f(xe)
                simplex[-1], fvals[-1] = (xe, fe) if fe < fr else (xr, fr)
            else:
                if fr < fvals[-1]:
                    xc = top + self.rho * (xr - top)  # внешнее сжатие
                    fc = obj.f(xc)
                    if fc <= fr:
                        simplex[-1], fvals[-1] = xc, fc
                        continue
                else:
                    xc = top + self.rho * (simplex[-1] - top)  # внутреннее сжатие
                    fc = obj.f(xc)
                    if fc < fvals[-1]:
                        simplex[-1], fvals[-1] = xc, fc
                        continue
                simplex[1:] = simplex[0] + self.sigma * (simplex[1:] - simplex[0])
                fvals[1:] = [obj.f(v) for v in simplex[1:]]
        return np.array(self.history_)


class PSO:
    """Метод роя частиц с адаптивными коэффициентами.

    Инeрция omega_k линейно убывает от omega_max до omega_min. Коэффициенты
    c1k (когнитивный) и c2k (социальный) каждой частицы зависят от отставания
    её локального рекорда F^LB от глобального F^GB: чем дальше частица от
    глобального лидера, тем сильнее опора на собственный рекорд (c1k растёт),
    чем ближе — тем сильнее социальная тяга (c2k растёт).

    После run доступны snapshots_ — позиции, скорости и лидер роя
    на каждой итерации (для визуализации).
    """

    name = "Рой частиц (PSO)"

    def __init__(self, n_particles: int = 30, max_iter: int = 150,
                 omega: tuple[float, float] = (0.9, 0.4),
                 c: tuple[float, float] = (0.5, 2.5), seed: int = 0) -> None:
        self.n_particles, self.max_iter = n_particles, max_iter
        self.omega_max, self.omega_min = omega
        self.c_min, self.c_max = c
        self._rng = np.random.default_rng(seed)

    def run(self, obj: TargetFunction, bounds: Vector) -> Vector:
        """Минимизация в прямоугольной области bounds (массив n x 2)."""
        rng = self._rng
        low, high = bounds[:, 0], bounds[:, 1]
        span = high - low
        x = rng.uniform(low, high, size=(self.n_particles, low.size))
        v = 0.1 * span * rng.uniform(-1.0, 1.0, size=x.shape)
        fvals = np.array([obj.f(p) for p in x])
        pbest, f_pb = x.copy(), fvals.copy()
        gbest, f_gb = x[int(np.argmin(fvals))].copy(), float(fvals.min())
        self.history_: list[Vector] = [gbest.copy()]
        self.f_history_: list[float] = [f_gb]
        self.snapshots_: list[tuple[Vector, Vector, Vector]] = [(x.copy(), v.copy(), gbest.copy())]
        for k in range(1, self.max_iter + 1):
            omega = self.omega_max - (self.omega_max - self.omega_min) * k / self.max_iter
            gap = (f_pb - f_gb) / (np.max(f_pb - f_gb) + 1e-12)
            c1 = self.c_min + (self.c_max - self.c_min) * gap
            c2 = self.c_max - (self.c_max - self.c_min) * gap
            r1 = rng.uniform(size=x.shape)
            r2 = rng.uniform(size=x.shape)
            v = omega * v + c1[:, None] * r1 * (pbest - x) + c2[:, None] * r2 * (gbest - x)
            x = x + v
            fvals = np.array([obj.f(p) for p in x])
            improved = fvals < f_pb
            pbest[improved], f_pb[improved] = x[improved], fvals[improved]
            if f_pb.min() < f_gb:
                gbest, f_gb = pbest[int(np.argmin(f_pb))].copy(), float(f_pb.min())
            self.history_.append(gbest.copy())
            self.f_history_.append(f_gb)
            self.snapshots_.append((x.copy(), v.copy(), gbest.copy()))
        return gbest
