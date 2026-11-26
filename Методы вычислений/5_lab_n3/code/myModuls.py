import copy
import json
import logging
import random
from collections.abc import Generator
from pathlib import Path
from typing import ClassVar, Literal

import numpy as np
import optuna
import torch
from torch import nn, optim
from torch.utils.data import DataLoader, TensorDataset
from torchvision.datasets.vision import VisionDataset
from torchvision.transforms import v2

# Фиксация seed для воспроизводимости
SEED = 42
torch.manual_seed(SEED)
np.random.seed(SEED)
random.seed(SEED)

DEVICE = torch.device("cuda" if torch.cuda.is_available() else "cpu")

# Автотюнинг cuDNN под фиксированный размер входа
torch.backends.cudnn.benchmark = True

# Константы обучения
N_FOLDS = 6            # число фолдов кросс-валидации
MAX_EPOCHS = 50        # максимум эпох на одну модель
PATIENCE = 5           # ранняя остановка: эпох без улучшения val loss
WARMUP_EPOCHS = 3      # разогрев lr для AdamW
N_TRIALS = 50          # число итераций Optuna
MIN_DELTA = 1e-4       # минимальное улучшение val loss для сброса patience
EVAL_BATCH_SIZE = 1024 # размер батча при оценке (val/test)

# Параллелизм
N_PARALLEL_TRIALS = 1  # сколько итераций Optuna обучается одновременно (потоки)

CRITERION = nn.CrossEntropyLoss()

# Аугментации и нормализация выполняются на GPU батчами (transforms v2
# поддерживают батчевые CUDA-тензоры), CPU-конвейер с DataLoader не нужен
TRAIN_TRANSFORM = v2.Compose([
    # Переводим в float32 и масштабируем значения из [0, 255] в [0.0, 1.0] (замена ToTensor)
    v2.ToDtype(torch.float32, scale=True),
    # Геометрические искажения: поворот ±15°, сдвиг до 10%, масштаб 90-110%
    v2.RandomAffine(degrees=(-15, 15), translate=(0.1, 0.1), scale=(0.9, 1.1)),
    # Небольшое перспективное искажение (имитация наклона листа) с вероятностью 30%
    v2.RandomPerspective(distortion_scale=0.2, p=0.3),
    # Случайное перекрытие части цифры (Cutout / Random Erasing)
    v2.RandomErasing(p=0.2, scale=(0.02, 0.12)),
    # Нормализация по среднему и стандартному отклонению MNIST
    v2.Normalize(mean=[0.1307], std=[0.3081]),
])

EVAL_TRANSFORM = v2.Compose([
    v2.ToDtype(torch.float32, scale=True),
    v2.Normalize(mean=[0.1307], std=[0.3081]),
])


def get_activation(activation_function: Literal["ReLU", "SiLU", "GELU"]) -> nn.ReLU | nn.SiLU | nn.GELU:
    match activation_function:
        case "ReLU":
            return nn.ReLU()
        case "SiLU":
            return nn.SiLU()
        case "GELU":
            return nn.GELU()
        case _:
            raise ValueError(f"Unknown activation function: {activation_function}")


def k_fold_generator(n_samples: int, k: int = 5, shuffle: bool = True, seed: int = SEED) -> Generator[tuple[list[int], list[int]]]:
    indices = list(range(n_samples))

    if shuffle:
        rng = random.Random(seed)
        rng.shuffle(indices)

    fold_sizes = [n_samples // k] * k
    for i in range(n_samples % k):
        fold_sizes[i] += 1

    current = 0
    for fold_size in fold_sizes:
        start, stop = current, current + fold_size

        val_idx = indices[start:stop]
        train_idx = indices[:start] + indices[stop:]

        yield train_idx, val_idx
        current = stop


def layer_sizes(n_layers: int, a: int) -> list[int]:
    """Массив размеров длины n_layers + 1: [2^a, 2^(a-1), ..., 2^(a-n)]."""
    return [2 ** (a - i) for i in range(n_layers + 1)]


def to_gpu_tensors(dataset: VisionDataset) -> tuple[torch.Tensor, torch.Tensor]:
    """MNIST целиком в видеопамять: картинки (N, 1, 28, 28) uint8 и метки (N,)."""
    images = dataset.data.unsqueeze(1).to(DEVICE)
    labels = dataset.targets.to(DEVICE)
    return images, labels


def dataset_meta(images: torch.Tensor, labels: torch.Tensor) -> dict:
    """Размерности входа/выхода модели, выведенные из данных."""
    return {
        "in_features": images[0].numel(),
        "img_size": images.shape[-1],
        "in_channels": images.shape[1],
        "num_classes": int(labels.max().item()) + 1,
    }


def make_loader(
    images: torch.Tensor,
    labels: torch.Tensor,
    batch_size: int,
    shuffle: bool,
    drop_last: bool = False,
) -> DataLoader:
    """DataLoader поверх CUDA-тензоров: батчи нарезаются прямо в видеопамяти.

    Воркеры и pin_memory не нужны — данные уже на GPU, num_workers=0.
    drop_last=True: BatchNorm1d падает на батче из одного объекта.
    """
    return DataLoader(
        TensorDataset(images, labels),
        batch_size=batch_size,
        shuffle=shuffle,
        drop_last=drop_last,
        num_workers=0,
    )


def evaluate_loss(model: nn.Module, loader: DataLoader) -> float:
    model.eval()
    loss_sum, n_samples = 0.0, 0
    with torch.no_grad():
        for batch_x, batch_y in loader:
            out = model(EVAL_TRANSFORM(batch_x))
            loss_sum += CRITERION(out, batch_y).item() * batch_y.size(0)
            n_samples += batch_y.size(0)
    return loss_sum / n_samples


def evaluate_accuracy(model: nn.Module, loader: DataLoader) -> float:
    model.eval()
    correct, total = 0, 0
    with torch.no_grad():
        for batch_x, batch_y in loader:
            preds = model(EVAL_TRANSFORM(batch_x)).argmax(dim=1)
            correct += (preds == batch_y).sum().item()
            total += batch_y.size(0)
    return correct / total


def make_optimizer(
    model: nn.Module, optimizer_name: str, lr: float, weight_decay: float = 0.0
) -> tuple[optim.Optimizer, optim.lr_scheduler.LRScheduler | None]:
    if optimizer_name == "AdamW_warmup":
        optimizer = optim.AdamW(model.parameters(), lr=lr, weight_decay=weight_decay)
        # Линейный разогрев lr в первые WARMUP_EPOCHS эпох
        scheduler = optim.lr_scheduler.LambdaLR(
            optimizer, lambda epoch: min(1.0, (epoch + 1) / WARMUP_EPOCHS)
        )
    elif optimizer_name == "SGD_nesterov":
        optimizer = optim.SGD(model.parameters(), lr=lr, momentum=0.9, nesterov=True)
        scheduler = None
    else:
        raise ValueError(f"Unknown optimizer: {optimizer_name}")
    return optimizer, scheduler


def build_optimizer(model: nn.Module, params: dict) -> tuple[optim.Optimizer, optim.lr_scheduler.LRScheduler | None]:
    if params["optimizer"] == "AdamW_warmup":
        return make_optimizer(model, "AdamW_warmup", params["adamw_lr"], params["adamw_weight_decay"])
    return make_optimizer(model, "SGD_nesterov", params["sgd_lr"])


def train_one_model(
    model: nn.Module,
    train_loader: DataLoader,
    val_loader: DataLoader,
    optimizer: optim.Optimizer,
    scheduler: optim.lr_scheduler.LRScheduler | None,
    max_epochs: int | None = None,
    patience: int | None = None,
) -> dict:
    """Обучение с ранней остановкой по лоссу на валидационной выборке.

    Возвращает веса лучшей эпохи, лоссы на ней и историю обучения.
    """
    if max_epochs is None:
        max_epochs = MAX_EPOCHS
    if patience is None:
        patience = PATIENCE
    best_val_loss = float("inf")
    best_train_loss = float("inf")
    best_state = None
    epochs_without_improvement = 0
    history = {"train_loss": [], "val_loss": []}

    for epoch in range(max_epochs):
        model.train()
        train_loss_sum, n_train = 0.0, 0
        for batch_x, batch_y in train_loader:
            batch_x = TRAIN_TRANSFORM(batch_x)
            optimizer.zero_grad()
            out = model(batch_x)
            loss = CRITERION(out, batch_y)
            loss.backward()
            optimizer.step()
            train_loss_sum += loss.item() * batch_y.size(0)
            n_train += batch_y.size(0)
        train_loss = train_loss_sum / n_train

        val_loss = evaluate_loss(model, val_loader)
        history["train_loss"].append(train_loss)
        history["val_loss"].append(val_loss)

        if scheduler is not None:
            scheduler.step()

        if val_loss < best_val_loss - MIN_DELTA:
            best_val_loss = val_loss
            best_train_loss = train_loss
            best_state = copy.deepcopy(model.state_dict())
            epochs_without_improvement = 0
        else:
            epochs_without_improvement += 1
            if epochs_without_improvement >= patience:
                break

    if best_state is not None:
        model.load_state_dict(best_state)

    return {
        "best_train_loss": best_train_loss,
        "best_val_loss": best_val_loss,
        "history": history,
        "stopped_epoch": len(history["train_loss"]),
    }


def _setup_logger(name: str, log_dir: str | Path, filename: str, verbose: bool) -> logging.Logger:
    """Логгер с записью в файл log_dir/filename и, при verbose=True, в терминал."""
    log_path = Path(log_dir)
    log_path.mkdir(parents=True, exist_ok=True)

    logger = logging.getLogger(name)
    logger.setLevel(logging.INFO)
    logger.propagate = False
    logger.handlers.clear()  # при повторных вызовах не плодить хендлеры

    formatter = logging.Formatter("%(asctime)s [%(levelname)s] %(message)s")
    file_handler = logging.FileHandler(log_path / filename, encoding="utf-8")
    file_handler.setFormatter(formatter)
    logger.addHandler(file_handler)
    if verbose:
        stream_handler = logging.StreamHandler()
        stream_handler.setFormatter(formatter)
        logger.addHandler(stream_handler)
    return logger


class SearchableModelMixin:
    """Подбор гиперпараметров (Optuna, кросс-валидация) и финальное обучение.

    Класс модели задаёт DEFAULT_PARAM_RANGES, _sample_params и _build_model;
    общий конвейер (фолды, ранняя остановка, логирование, артефакты) — здесь.
    """

    DEFAULT_PARAM_RANGES: ClassVar[dict] = {}

    @classmethod
    def _sample_params(cls, trial: optuna.Trial, param_ranges: dict) -> dict:
        raise NotImplementedError

    @classmethod
    def _build_model(cls, params: dict, meta: dict) -> nn.Module:
        raise NotImplementedError

    @classmethod
    def _trial_user_attrs(cls, params: dict) -> dict:
        """Дополнительные атрибуты итерации Optuna (архитектура и т.п.)."""
        return {}

    @classmethod
    def _objective(
        cls,
        trial: optuna.Trial,
        train_images: torch.Tensor,
        train_labels: torch.Tensor,
        test_images: torch.Tensor | None,
        test_labels: torch.Tensor | None,
        param_ranges: dict,
        meta: dict,
        max_epochs: int,
        logger: logging.Logger,
    ) -> float:
        params = cls._sample_params(trial, param_ranges)

        test_loader = (
            make_loader(test_images, test_labels, batch_size=EVAL_BATCH_SIZE, shuffle=False)
            if test_images is not None else None
        )

        fold_train_losses, fold_val_losses, fold_test_losses = [], [], []

        for fold, (train_idx, val_idx) in enumerate(k_fold_generator(train_images.size(0), k=N_FOLDS)):
            train_loader = make_loader(
                train_images[train_idx], train_labels[train_idx],
                batch_size=params["batch_size"], shuffle=True, drop_last=True,
            )
            val_loader = make_loader(
                train_images[val_idx], train_labels[val_idx],
                batch_size=EVAL_BATCH_SIZE, shuffle=False,
            )

            # Модель и оптимизатор инициализируются заново для каждого фолда
            fold_model = cls._build_model(params, meta).to(DEVICE)
            fold_optimizer, fold_scheduler = build_optimizer(fold_model, params)

            result = train_one_model(fold_model, train_loader, val_loader, fold_optimizer, fold_scheduler, max_epochs=max_epochs)

            fold_train_losses.append(result["best_train_loss"])
            fold_val_losses.append(result["best_val_loss"])

            # Лосс на тесте логируется, но НЕ участвует в отборе гиперпараметров
            if test_loader is not None:
                test_loss = evaluate_loss(fold_model, test_loader)
                fold_test_losses.append(test_loss)
                logger.info(
                    "Trial %d fold %d/%d: train_loss=%.4f val_loss=%.4f test_loss=%.4f (epochs=%d)",
                    trial.number, fold + 1, N_FOLDS,
                    result["best_train_loss"], result["best_val_loss"], test_loss,
                    result["stopped_epoch"],
                )
            else:
                logger.info(
                    "Trial %d fold %d/%d: train_loss=%.4f val_loss=%.4f (epochs=%d)",
                    trial.number, fold + 1, N_FOLDS,
                    result["best_train_loss"], result["best_val_loss"],
                    result["stopped_epoch"],
                )

            # Отсечение (pruning) неудачных испытаний по мере прохождения фолдов
            trial.report(np.mean(fold_val_losses), step=fold)
            if trial.should_prune():
                raise optuna.exceptions.TrialPruned()

        # Логирование итерации: гиперпараметры и лоссы по фолдам
        for attr_name, attr_value in cls._trial_user_attrs(params).items():
            trial.set_user_attr(attr_name, attr_value)
        trial.set_user_attr("fold_train_losses", fold_train_losses)
        trial.set_user_attr("fold_val_losses", fold_val_losses)
        trial.set_user_attr("mean_train_loss", float(np.mean(fold_train_losses)))
        if fold_test_losses:
            trial.set_user_attr("fold_test_losses", fold_test_losses)
            trial.set_user_attr("mean_test_loss", float(np.mean(fold_test_losses)))

        mean_test = f" mean_test_loss={np.mean(fold_test_losses):.4f}" if fold_test_losses else ""
        logger.info(
            "Trial %d finished: params=%s | mean_train_loss=%.4f mean_val_loss=%.4f%s",
            trial.number, trial.params,
            np.mean(fold_train_losses), np.mean(fold_val_losses), mean_test,
        )

        # Целевая метрика — средний лосс на валидационных фолдах (тест не используется)
        return float(np.mean(fold_val_losses))

    @classmethod
    def _log_best_trial(cls, study: optuna.Study, logger: logging.Logger) -> None:
        trial = study.best_trial
        logger.info("=== Best trial #%d ===", trial.number)
        logger.info("Hyperparameters: %s", trial.params)
        for attr_name, attr_value in trial.user_attrs.items():
            logger.info("%s: %s", attr_name, attr_value)
        logger.info("Mean val loss: %.4f", trial.value)

    @classmethod
    def find_hyperparameters(
        cls,
        train_dataset: VisionDataset,
        test_dataset: VisionDataset | None = None,
        param_ranges: dict | None = None,
        *,
        log_dir: str | Path,
        best_params: dict | None = None,
        verbose: bool = True,
        n_trials: int = N_TRIALS,
        max_epochs: int = MAX_EPOCHS,
    ) -> dict:
        """Подбор гиперпараметров кросс-валидацией и Optuna.

        Аргументы:
            train_dataset — обучающий датасет;
            test_dataset — тестовый датасет (необязательно; его лосс только
                логируется и не участвует в отборе);
            param_ranges — диапазоны подбора (по умолчанию DEFAULT_PARAM_RANGES
                класса модели);
            log_dir — папка для файлов логов (hyperparameter_search.log,
                best_params.json);
            best_params — уже известные лучшие гиперпараметры: добавляются
                в study как стартовая итерация;
            verbose — дублировать логи в терминал;
            n_trials, max_epochs — бюджет подбора.

        Возвращает словарь подобранных гиперпараметров.
        """
        logger = _setup_logger(f"{cls.__name__}.search", log_dir, "hyperparameter_search.log", verbose)
        ranges = dict(cls.DEFAULT_PARAM_RANGES if param_ranges is None else param_ranges)

        train_images, train_labels = to_gpu_tensors(train_dataset)
        test_images = test_labels = None
        if test_dataset is not None:
            test_images, test_labels = to_gpu_tensors(test_dataset)
        meta = dataset_meta(train_images, train_labels)

        study = optuna.create_study(
            direction="minimize",
            sampler=optuna.samplers.TPESampler(seed=SEED),
        )
        if best_params is not None:
            study.enqueue_trial(best_params)
            logger.info("Study seeded with known best params: %s", best_params)

        study.optimize(
            lambda trial: cls._objective(
                trial, train_images, train_labels, test_images, test_labels,
                ranges, meta, max_epochs, logger,
            ),
            n_trials=n_trials,
            n_jobs=N_PARALLEL_TRIALS,
        )

        cls._log_best_trial(study, logger)

        found_params = dict(study.best_trial.params)
        params_path = Path(log_dir) / "best_params.json"
        with open(params_path, "w", encoding="utf-8") as f:
            json.dump(found_params, f, indent=2, ensure_ascii=False)
        logger.info("Best hyperparameters saved to %s", params_path)
        return found_params

    @classmethod
    def train_final(
        cls,
        train_dataset: VisionDataset,
        test_dataset: VisionDataset | None = None,
        *,
        hyperparameters: dict,
        log_dir: str | Path,
        max_epochs: int = MAX_EPOCHS,
        patience: int = PATIENCE,
        verbose: bool = True,
    ) -> None:
        """Финальное обучение модели на подобранных гиперпараметрах.

        Выборка делится 9:1, ранняя остановка — по лоссу на малой
        (валидационной) части. Тестовая выборка используется один раз
        для финальной оценки.
        Ничего не возвращает: веса (final_model.pt), гиперпараметры
        (final_model_params.json), история лоссов (history.json) и метрики
        на тесте (test_metrics.json) сохраняются в log_dir.
        """
        logger = _setup_logger(f"{cls.__name__}.final", log_dir, "final_training.log", verbose)
        log_path = Path(log_dir)

        train_images, train_labels = to_gpu_tensors(train_dataset)
        meta = dataset_meta(train_images, train_labels)

        n_samples = train_images.size(0)
        indices = list(range(n_samples))
        rng = random.Random(SEED)
        rng.shuffle(indices)

        split = int(0.9 * n_samples)
        train_idx, val_idx = indices[:split], indices[split:]

        train_loader = make_loader(
            train_images[train_idx], train_labels[train_idx],
            batch_size=hyperparameters["batch_size"], shuffle=True, drop_last=True,
        )
        val_loader = make_loader(
            train_images[val_idx], train_labels[val_idx],
            batch_size=EVAL_BATCH_SIZE, shuffle=False,
        )

        test_loader = None
        if test_dataset is not None:
            test_images, test_labels = to_gpu_tensors(test_dataset)
            test_loader = make_loader(test_images, test_labels, batch_size=EVAL_BATCH_SIZE, shuffle=False)

        model = cls._build_model(hyperparameters, meta).to(DEVICE)
        optimizer, scheduler = build_optimizer(model, hyperparameters)

        result = train_one_model(
            model, train_loader, val_loader, optimizer, scheduler,
            max_epochs=max_epochs, patience=patience,
        )
        logger.info(
            "Final model trained %d epochs: train_loss=%.4f val_loss=%.4f",
            result["stopped_epoch"], result["best_train_loss"], result["best_val_loss"],
        )

        # Тест — один раз на лучших весах, после всего подбора и обучения
        metrics = {
            "train_loss": result["best_train_loss"],
            "val_loss": result["best_val_loss"],
            "epochs": result["stopped_epoch"],
        }
        if test_loader is not None:
            test_loss = evaluate_loss(model, test_loader)
            test_acc = evaluate_accuracy(model, test_loader)
            logger.info("Final model on TEST: loss=%.4f accuracy=%.4f", test_loss, test_acc)
            metrics["test_loss"] = test_loss
            metrics["test_accuracy"] = test_acc

        # Сохранение весов, гиперпараметров, истории лоссов и метрик
        torch.save(model.state_dict(), log_path / "final_model.pt")
        with open(log_path / "final_model_params.json", "w", encoding="utf-8") as f:
            json.dump(hyperparameters, f, indent=2, ensure_ascii=False)
        with open(log_path / "history.json", "w", encoding="utf-8") as f:
            json.dump(result["history"], f, indent=2)
        with open(log_path / "test_metrics.json", "w", encoding="utf-8") as f:
            json.dump(metrics, f, indent=2, ensure_ascii=False)
        logger.info("Artifacts saved to %s", log_path)


class MLP_model(SearchableModelMixin, nn.Module):
    DEFAULT_PARAM_RANGES: ClassVar[dict] = {
        "n_layers": (1, 4),
        # a >= n_layers + 1, чтобы наименьший скрытый слой имел размер >= 2
        "hidden_size_exp": (4, 11),
        "activation": ("ReLU", "SiLU", "GELU"),
        "dropout": (0.0, 0.5),
        "batch_size": (32, 64, 128, 256, 512, 1024),
        "optimizer": ("AdamW_warmup", "SGD_nesterov"),
        "adamw_lr": (1e-4, 1e-2),
        "adamw_weight_decay": (1e-5, 1e-2),
        "sgd_lr": (1e-3, 1e-1),
    }

    def __init__(self, in_features: int, num_classes: int, hiddenSize: list[int], nLayers: int, activationFunction: Literal["ReLU", "SiLU", "GELU"], dropout: float):
        super().__init__()

        if len(hiddenSize) != nLayers + 1:
            raise ValueError("hiddenSize length must be equal to nLayers + 1")

        self.linearSequential = nn.Sequential(
            nn.Flatten(),
            nn.Linear(in_features, hiddenSize[0]),
            nn.BatchNorm1d(hiddenSize[0]),
            get_activation(activationFunction),
            nn.Dropout(dropout),
        )

        for i in range(nLayers):
            self.linearSequential += nn.Sequential(
                nn.Linear(hiddenSize[i], hiddenSize[i + 1]),
                nn.BatchNorm1d(hiddenSize[i + 1]),
                get_activation(activationFunction),
                nn.Dropout(dropout),
            )

        self.linearSequential += nn.Sequential(
            nn.Linear(hiddenSize[-1], num_classes)
        )

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        return self.linearSequential(x)

    @classmethod
    def _sample_params(cls, trial: optuna.Trial, param_ranges: dict) -> dict:
        """Сэмплирование гиперпараметров одной итерации Optuna."""
        r = param_ranges
        n_layers = trial.suggest_int("n_layers", *r["n_layers"])
        params = {
            "n_layers": n_layers,
            # a >= n_layers + 1, чтобы наименьший скрытый слой имел размер >= 2
            "hidden_size_exp": trial.suggest_int("hidden_size_exp", max(r["hidden_size_exp"][0], n_layers + 1), r["hidden_size_exp"][1]),
            "activation": trial.suggest_categorical("activation", list(r["activation"])),
            "dropout": trial.suggest_float("dropout", *r["dropout"]),
            "batch_size": trial.suggest_categorical("batch_size", list(r["batch_size"])),
            "optimizer": trial.suggest_categorical("optimizer", list(r["optimizer"])),
        }
        if params["optimizer"] == "AdamW_warmup":
            params["adamw_lr"] = trial.suggest_float("adamw_lr", *r["adamw_lr"], log=True)
            params["adamw_weight_decay"] = trial.suggest_float("adamw_weight_decay", *r["adamw_weight_decay"], log=True)
        else:
            params["sgd_lr"] = trial.suggest_float("sgd_lr", *r["sgd_lr"], log=True)
        return params

    @classmethod
    def _build_model(cls, params: dict, meta: dict) -> nn.Module:
        n_layers = params["n_layers"]
        sizes = layer_sizes(n_layers, params["hidden_size_exp"])
        return cls(meta["in_features"], meta["num_classes"], sizes, n_layers, params["activation"], params["dropout"])

    @classmethod
    def _trial_user_attrs(cls, params: dict) -> dict:
        return {"layer_sizes": layer_sizes(params["n_layers"], params["hidden_size_exp"])}


class CNN_model(SearchableModelMixin, nn.Module):
    DEFAULT_PARAM_RANGES: ClassVar[dict] = {
        "double_blocks": (False, True),
        "kernel_size": (3, 5),
        # число каналов: 2^exp; в doubleBlocks второй блок удваивает каналы ещё раз
        "conv_channels_exp": (4, 6),
        "linear_size_exp": (7, 10),
        "activation": ("ReLU", "SiLU", "GELU"),
        "bias": (False, True),
        "dropout_conv": (0.0, 0.3),
        "dropout_linear": (0.0, 0.5),
        "batch_size": (32, 64, 128, 256, 512, 1024),
        "optimizer": ("AdamW_warmup", "SGD_nesterov"),
        "adamw_lr": (1e-4, 1e-2),
        "adamw_weight_decay": (1e-5, 1e-2),
        "sgd_lr": (1e-3, 1e-1),
    }

    def __init__(self, convHiddenSize: list[int], linearHiddenSize: int, doubleBlocks: bool, kernelSize: int, activationFunction: Literal["ReLU", "SiLU", "GELU"], bias: bool, dropout: list[float]):
        super().__init__()

        if not((not doubleBlocks and len(convHiddenSize) == 2) or (doubleBlocks and len(convHiddenSize) == 4)):
            raise ValueError("convHiddenSize length must be equal 4 if doubleBlocks, else equal 2")
        if not((not doubleBlocks and len(dropout) == 2) or (doubleBlocks and len(dropout) == 3)):
            raise ValueError("dropout length must be equal 3 if doubleBlocks, else equal 2")

        padding = kernelSize // 2

        self.convBlock = nn.Sequential(
            nn.Conv2d(1, convHiddenSize[0], kernel_size=kernelSize, padding=padding, bias=bias),
            nn.BatchNorm2d(convHiddenSize[0]),
            get_activation(activationFunction),
            nn.Conv2d(convHiddenSize[0], convHiddenSize[1], kernel_size=kernelSize, padding=padding, bias=False),
            nn.BatchNorm2d(convHiddenSize[1]),
            get_activation(activationFunction),
            nn.MaxPool2d(2),
            nn.Dropout2d(dropout[0]),
        )

        if doubleBlocks:
            self.convBlock += nn.Sequential(
                nn.Conv2d(convHiddenSize[1], convHiddenSize[2], kernel_size=kernelSize, padding=padding, bias=bias),
                nn.BatchNorm2d(convHiddenSize[2]),
                get_activation(activationFunction),
                nn.Conv2d(convHiddenSize[2], convHiddenSize[3], kernel_size=kernelSize, padding=padding, bias=False),
                nn.BatchNorm2d(convHiddenSize[3]),
                get_activation(activationFunction),
                nn.MaxPool2d(2),
                nn.Dropout2d(dropout[1]),
            )
            self.linearBlock = nn.Sequential(
                nn.Flatten(),
                nn.Linear(convHiddenSize[-1] * 7 * 7, linearHiddenSize, bias=False),
                nn.BatchNorm1d(linearHiddenSize),
                get_activation(activationFunction),
                nn.Dropout(dropout[-1]),
                nn.Linear(linearHiddenSize, 10)
            )
        else:
            self.linearBlock = nn.Sequential(
                nn.Flatten(),
                nn.Linear(convHiddenSize[-1] * 14 * 14, linearHiddenSize, bias=False),
                nn.BatchNorm1d(linearHiddenSize),
                get_activation(activationFunction),
                nn.Dropout(dropout[-1]),
                nn.Linear(linearHiddenSize, 10)
            )

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        return self.linearBlock(self.convBlock(x))

    @classmethod
    def _arch(cls, params: dict) -> tuple[list[int], int, list[float]]:
        """Размеры свёрток/линейного слоя и dropout'ы из гиперпараметров."""
        exp = params["conv_channels_exp"]
        if params["double_blocks"]:
            conv_sizes = [2 ** exp, 2 ** (exp + 1), 2 ** (exp + 1), 2 ** (exp + 2)]
            dropout = [params["dropout_conv1"], params["dropout_conv2"], params["dropout_linear"]]
        else:
            conv_sizes = [2 ** exp, 2 ** (exp + 1)]
            dropout = [params["dropout_conv1"], params["dropout_linear"]]
        return conv_sizes, 2 ** params["linear_size_exp"], dropout

    @classmethod
    def _sample_params(cls, trial: optuna.Trial, param_ranges: dict) -> dict:
        r = param_ranges
        params = {
            "double_blocks": trial.suggest_categorical("double_blocks", list(r["double_blocks"])),
            "conv_channels_exp": trial.suggest_int("conv_channels_exp", *r["conv_channels_exp"]),
            "linear_size_exp": trial.suggest_int("linear_size_exp", *r["linear_size_exp"]),
            "kernel_size": trial.suggest_categorical("kernel_size", list(r["kernel_size"])),
            "activation": trial.suggest_categorical("activation", list(r["activation"])),
            "bias": trial.suggest_categorical("bias", list(r["bias"])),
            "dropout_conv1": trial.suggest_float("dropout_conv1", *r["dropout_conv"]),
            "dropout_linear": trial.suggest_float("dropout_linear", *r["dropout_linear"]),
            "batch_size": trial.suggest_categorical("batch_size", list(r["batch_size"])),
            "optimizer": trial.suggest_categorical("optimizer", list(r["optimizer"])),
        }
        if params["double_blocks"]:
            params["dropout_conv2"] = trial.suggest_float("dropout_conv2", *r["dropout_conv"])
        if params["optimizer"] == "AdamW_warmup":
            params["adamw_lr"] = trial.suggest_float("adamw_lr", *r["adamw_lr"], log=True)
            params["adamw_weight_decay"] = trial.suggest_float("adamw_weight_decay", *r["adamw_weight_decay"], log=True)
        else:
            params["sgd_lr"] = trial.suggest_float("sgd_lr", *r["sgd_lr"], log=True)
        return params

    @classmethod
    def _build_model(cls, params: dict, meta: dict) -> nn.Module:
        conv_sizes, linear_size, dropout = cls._arch(params)
        return cls(conv_sizes, linear_size, params["double_blocks"], params["kernel_size"], params["activation"], params["bias"], dropout)

    @classmethod
    def _trial_user_attrs(cls, params: dict) -> dict:
        conv_sizes, linear_size, _ = cls._arch(params)
        return {"conv_hidden_size": conv_sizes, "linear_hidden_size": linear_size}


class myMultiheadAttention(nn.Module):
    def __init__(
        self,
        embed_dim: int,
        num_heads: int,
        dropout: float,
        bias: bool = True,
    ):
        super().__init__()

        if embed_dim % num_heads != 0:
            raise ValueError("embed_dim должен делиться на num_heads нацело")

        self.embed_dim = embed_dim
        self.num_heads = num_heads
        self.head_dim = embed_dim // num_heads
        self.dropout_p = dropout

        # Объединенная проекция для Query, Key и Value
        self.qkv_proj = nn.Linear(embed_dim, 3 * embed_dim, bias=bias)
        # Финальная проекция после конкатенации голов
        self.out_proj = nn.Linear(embed_dim, embed_dim, bias=bias)
        self.out_dropout = nn.Dropout(dropout)

    def forward(self, x: torch.Tensor, attn_mask: torch.Tensor | None = None) -> torch.Tensor:
        B, N, D = x.shape

        # Получаем Q, K, V: (B, N, 3 * D) -> (B, N, 3, num_heads, head_dim)
        qkv = self.qkv_proj(x).reshape(B, N, 3, self.num_heads, self.head_dim)

        # Переставляем оси в (3, B, num_heads, N, head_dim) и распаковываем
        qkv = qkv.permute(2, 0, 3, 1, 4)
        q, k, v = qkv.unbind(0)  # Каждый тензор формы (B, num_heads, N, head_dim)

        # Вычисляем внимание (FlashAttention / Memory-Efficient kernel)
        # Dropout применяется только в режиме обучения (self.training)
        attn_out = nn.functional.scaled_dot_product_attention(
            q, k, v,
            attn_mask=attn_mask,
            dropout_p=self.dropout_p if self.training else 0.0,
        )  # (B, num_heads, N, head_dim)

        # Склеиваем головы: (B, num_heads, N, head_dim) -> (B, N, num_heads, head_dim) -> (B, N, D)
        attn_out = attn_out.transpose(1, 2).contiguous().reshape(B, N, D)

        # Выходная проекция
        return self.out_dropout(self.out_proj(attn_out))

class EncodingBlock(nn.Module):
    def __init__(self, embed_dim: int, num_heads: int, mlp_dim: int, dropout: float):
        super().__init__()
        self.norm1 = nn.LayerNorm(embed_dim)
        self.attn = myMultiheadAttention(embed_dim, num_heads, dropout=dropout)

        self.norm2 = nn.LayerNorm(embed_dim)
        self.nn = nn.Sequential(
            nn.Linear(embed_dim, mlp_dim),
            nn.GELU(),
            nn.Dropout(dropout),
            nn.Linear(mlp_dim, embed_dim),
            nn.Dropout(dropout),
        )

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        x = x + self.attn(self.norm1(x))
        x = x + self.nn(self.norm2(x))
        return x

class ViT_model(SearchableModelMixin, nn.Module):
    DEFAULT_PARAM_RANGES: ClassVar[dict] = {
        # 28 делится на 2, 4 и 7 без остатка
        "patch_size": (2, 4, 7),
        "embed_dim": (64, 128, 192, 256),
        "depth": (1, 6),
        "num_heads": (2, 4, 8),
        # скрытый размер MLP-блока = mlp_ratio * embed_dim
        "mlp_ratio": (2, 3, 4),
        "dropout": (0.0, 0.3),
        "batch_size": (32, 64, 128, 256, 512, 1024),
        "optimizer": ("AdamW_warmup", "SGD_nesterov"),
        # ViT чувствителен к lr: ниже, чем у MLP/CNN, и регуляризация сильнее
        "adamw_lr": (1e-5, 1e-3),
        "adamw_weight_decay": (1e-4, 1e-1),
        "sgd_lr": (1e-3, 1e-1),
    }

    def __init__(
        self,
        img_size: int,
        patch_size: int,
        in_channels: int,
        num_classes: int,
        embed_dim: int,
        depth: int,
        num_heads: int,
        mlp_dim: int,
        dropout: float,
    ):
        super().__init__()

        if img_size % patch_size != 0:
            raise ValueError("img_size должен делиться на patch_size без остатка")

        num_patches = (img_size // patch_size) ** 2

        # 1. Input Image -> Image Tokens
        self.patch_embed = nn.Conv2d(
            in_channels=in_channels,
            out_channels=embed_dim,
            kernel_size=patch_size,
            stride=patch_size,
        )

        # 2. Prediction Token (инициализируем нулями как на схеме, но делаем обучаемым nn.Parameter)
        self.cls_token = nn.Parameter(torch.zeros(1, 1, embed_dim))

        # 3. Position Embedding (для всех токенов: num_patches + 1)
        self.pos_embed = nn.Parameter(torch.randn(1, num_patches + 1, embed_dim) * 0.02)
        self.pos_dropout = nn.Dropout(dropout)

        # 4. Стек Encoding Blocks (глубина depth — гиперпараметр)
        self.blocks = nn.Sequential(*[
            EncodingBlock(embed_dim, num_heads, mlp_dim, dropout)
            for _ in range(depth)
        ])

        # 5. Финальная нормализация перед разделением (Norm на схеме)
        self.norm = nn.LayerNorm(embed_dim)

        # 6. Классификационная голова (Linear Layer -> Prediction)
        self.head = nn.Linear(embed_dim, num_classes)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        B = x.shape[0]

        # Разбиение на патчи: (B, C, H, W) -> (B, embed_dim, H/P, W/P) -> (B, num_patches, embed_dim)
        x = self.patch_embed(x)
        x = x.flatten(2).transpose(1, 2)

        # Concatenate Prediction Token в начало последовательности
        cls_tokens = self.cls_token.expand(B, -1, -1)  # (B, 1, embed_dim)
        x = torch.cat((cls_tokens, x), dim=1)          # (B, num_patches + 1, embed_dim)

        # Добавление Position Embedding
        x = self.pos_dropout(x + self.pos_embed)

        # Проход через стек Encoding Blocks
        x = self.blocks(x)

        # Финальный Norm
        x = self.norm(x)

        # Separate: берем только первый токен (Prediction Token), Image Tokens отбрасываем
        cls_out = x[:, 0]  # (B, embed_dim)

        # Linear Layer -> логиты классов
        logits = self.head(cls_out)  # (B, num_classes)
        return logits

    @classmethod
    def _sample_params(cls, trial: optuna.Trial, param_ranges: dict) -> dict:
        r = param_ranges
        embed_dim = trial.suggest_categorical("embed_dim", list(r["embed_dim"]))
        params = {
            "patch_size": trial.suggest_categorical("patch_size", list(r["patch_size"])),
            "embed_dim": embed_dim,
            "depth": trial.suggest_int("depth", *r["depth"]),
            # embed_dim должен делиться на num_heads нацело
            "num_heads": trial.suggest_categorical("num_heads", [h for h in r["num_heads"] if embed_dim % h == 0]),
            "mlp_ratio": trial.suggest_categorical("mlp_ratio", list(r["mlp_ratio"])),
            "dropout": trial.suggest_float("dropout", *r["dropout"]),
            "batch_size": trial.suggest_categorical("batch_size", list(r["batch_size"])),
            "optimizer": trial.suggest_categorical("optimizer", list(r["optimizer"])),
        }
        if params["optimizer"] == "AdamW_warmup":
            params["adamw_lr"] = trial.suggest_float("adamw_lr", *r["adamw_lr"], log=True)
            params["adamw_weight_decay"] = trial.suggest_float("adamw_weight_decay", *r["adamw_weight_decay"], log=True)
        else:
            params["sgd_lr"] = trial.suggest_float("sgd_lr", *r["sgd_lr"], log=True)
        return params

    @classmethod
    def _build_model(cls, params: dict, meta: dict) -> nn.Module:
        return cls(
            img_size=meta["img_size"],
            patch_size=params["patch_size"],
            in_channels=meta["in_channels"],
            num_classes=meta["num_classes"],
            embed_dim=params["embed_dim"],
            depth=params["depth"],
            num_heads=params["num_heads"],
            mlp_dim=params["embed_dim"] * params["mlp_ratio"],
            dropout=params["dropout"],
        )

    @classmethod
    def _trial_user_attrs(cls, params: dict) -> dict:
        return {"mlp_dim": params["embed_dim"] * params["mlp_ratio"]}
