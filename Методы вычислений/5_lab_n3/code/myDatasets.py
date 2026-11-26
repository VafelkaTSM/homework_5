import torch
from torch.utils.data import DataLoader
from torchvision import datasets
from torchvision.transforms import v2

from torchvision.datasets.vision import VisionDataset

def getMNIST() -> tuple[VisionDataset, VisionDataset, VisionDataset]:
    # Загрузка обучающей выборки (60 000 картинок)
    train_dataset_aug = datasets.MNIST(
        root='./data', train=True, download=True
    )
    train_dataset = datasets.MNIST(
        root='./data', train=True, download=True
    )
    # Тестовая выборка (10 000 картинок) задается флагом train=False
    test_dataset = datasets.MNIST(
        root='./data', train=False, download=True
    )

    return (train_dataset_aug, train_dataset, test_dataset)

def toMNISTFormat() -> VisionDataset:
    transform = v2.Compose([
        v2.ToImage(),
        v2.ToDtype(torch.float32, scale=True),
        v2.Normalize(mean=[0.1307], std=[0.3081]),
    ])

    dataset = datasets.ImageFolder(root="./data/myImage")

    return dataset