import torch
from torch.utils.data import DataLoader
from torchvision import datasets
from torchvision.transforms import v2

from torchvision.datasets.vision import VisionDataset

def getMNIST() -> tuple[VisionDataset, VisionDataset, VisionDataset]:
    # Пайплайн аугментаций для ОБУЧЕНИЯ (train) через v2
    train_transform = v2.Compose([
        # Конвертируем PIL-изображение в формат Image (подкласс тензора в v2)
        v2.ToImage(),
        # Геометрические искажения: поворот ±15°, сдвиг до 10%, масштаб 90-110%
        v2.RandomAffine(degrees=(-15, 15), translate=(0.1, 0.1), scale=(0.9, 1.1)),
        # Небольшое перспективное искажение (имитация наклона листа) с вероятностью 30%
        v2.RandomPerspective(distortion_scale=0.2, p=0.3),
        # Переводим в float32 и масштабируем значения из [0, 255] в [0.0, 1.0] (замена ToTensor)
        v2.ToDtype(torch.float32, scale=True),
        # Случайное перекрытие части цифры (Cutout / Random Erasing)
        v2.RandomErasing(p=0.2, scale=(0.02, 0.12)),
        # Нормализация по среднему и стандартному отклонению MNIST
        v2.Normalize(mean=[0.1307], std=[0.3081]),
    ])

    # Пайплайн для ВАЛИДАЦИИ и ТЕСТА (без аугментаций!)
    eval_transform = v2.Compose([
        v2.ToImage(),
        v2.ToDtype(torch.float32, scale=True),
        v2.Normalize(mean=[0.1307], std=[0.3081]),
    ])

    # Загрузка обучающей выборки (60 000 картинок)
    train_dataset_aug = datasets.MNIST(
        root='./data', train=True, download=True, transform=train_transform
    )
    train_dataset = datasets.MNIST(
        root='./data', train=True, download=True, transform=eval_transform
    )
    # Тестовая выборка (10 000 картинок) задается флагом train=False
    test_dataset = datasets.MNIST(
        root='./data', train=False, download=True, transform=eval_transform
    )

    return (train_dataset_aug, train_dataset, test_dataset)

def toMNISTFormat() -> VisionDataset:
    transform = v2.Compose([
        v2.ToImage(),
        v2.ToDtype(torch.float32, scale=True),
        v2.Normalize(mean=[0.1307], std=[0.3081]),
    ])

    dataset = datasets.ImageFolder(root="./data/myImage", transform=transform)

    return dataset