import pandas as pd
import matplotlib.pyplot as plt

df = pd.read_csv("fluid_dynamics/pendulum/pendulum_calculation/pendulum_results.csv")

print(df.info())

def create_and_save_plot(x, y, xlabel, ylabel, label, filename):
    """
    Создает график, сохраняет его в файл и возвращает объекты fig и ax.
    """
    fig, ax = plt.subplots()
    ax.plot(x, y, label=label)
    ax.set_xlabel(xlabel)
    ax.set_ylabel(ylabel)
    ax.legend()
    fig.savefig(filename, dpi=300, bbox_inches='tight')
    return fig, ax

# 1. tY без затухания
fig1, ax1 = create_and_save_plot(
    df.iloc[:, 0], df.iloc[:, 2], "t", "Y", "tY без затухания", "fluid_dynamics/pendulum/pictures/ty_no_damping.png"
)

# 2. XY без затухания
fig2, ax2 = create_and_save_plot(
    df.iloc[:, 1], df.iloc[:, 2], "X", "Y", "XY без затухания", "fluid_dynamics/pendulum/pictures/xy_no_damping.png"
)

# 3. tY с затуханием
fig3, ax3 = create_and_save_plot(
    df.iloc[:, 0], df.iloc[:, 4], "t", "Y", "tY с затуханием", "fluid_dynamics/pendulum/pictures/ty_damping.png"
)

# 4. XY с затуханием
fig4, ax4 = create_and_save_plot(
    df.iloc[:, 3], df.iloc[:, 4], "X", "Y", "XY с затуханием", "fluid_dynamics/pendulum/pictures/xy_damping.png"
)