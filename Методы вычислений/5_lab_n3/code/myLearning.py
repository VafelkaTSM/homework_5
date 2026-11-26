import json
import logging
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt

import myDatasets
from myModuls import CNN_model, MLP_model, ViT_model

LOG_ROOT = Path("logs")

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
)
logger = logging.getLogger(__name__)

MODELS = (MLP_model, CNN_model, ViT_model)


def plot_loss_curves(history: dict, model_name: str, out_path: Path) -> None:
    """График лоссов на обучающей и валидационной выборках, наложенных друг на друга."""
    fig, ax = plt.subplots(figsize=(8, 5))
    epochs = range(1, len(history["train_loss"]) + 1)
    ax.plot(epochs, history["train_loss"], marker="o", label="train loss")
    ax.plot(epochs, history["val_loss"], marker="o", label="val loss")
    ax.set_xlabel("Epoch")
    ax.set_ylabel("Loss")
    ax.set_title(f"{model_name}: train vs val loss")
    ax.legend()
    ax.grid(alpha=0.3)
    fig.tight_layout()
    fig.savefig(out_path, dpi=150)
    plt.close(fig)


def main() -> None:
    LOG_ROOT.mkdir(parents=True, exist_ok=True)
    _, train_dataset, test_dataset = myDatasets.getMNIST()

    summary = {}
    for model_cls in MODELS:
        log_dir = LOG_ROOT / model_cls.__name__

        # Подбор гиперпараметров
        logger.info("=== %s: hyperparameter search ===", model_cls.__name__)
        best_params = model_cls.find_hyperparameters(
            train_dataset,
            test_dataset,
            log_dir=log_dir,
        )
        logger.info("%s best hyperparameters: %s", model_cls.__name__, best_params)

        # Обучение финальной версии модели
        logger.info("=== %s: final training ===", model_cls.__name__)
        model_cls.train_final(
            train_dataset,
            test_dataset,
            hyperparameters=best_params,
            log_dir=log_dir,
        )

        # Результаты на тестовой выборке — из логов финального обучения
        with open(log_dir / "test_metrics.json", encoding="utf-8") as f:
            metrics = json.load(f)
        summary[model_cls.__name__] = metrics
        logger.info("%s test results: %s", model_cls.__name__, metrics)

        # Графики лоссов на train/val из логов финального обучения
        with open(log_dir / "history.json", encoding="utf-8") as f:
            history = json.load(f)
        plot_path = log_dir / "loss_curves.png"
        plot_loss_curves(history, model_cls.__name__, plot_path)
        logger.info("%s loss curves saved to %s", model_cls.__name__, plot_path)

    with open(LOG_ROOT / "summary.json", "w", encoding="utf-8") as f:
        json.dump(summary, f, indent=2, ensure_ascii=False)
    logger.info("=== Summary === %s", summary)


if __name__ == "__main__":
    main()
