import pandas as pd
import matplotlib.pyplot as plt

df = pd.read_csv("fluid_dynamics/pendulum/pendulum_calculation/pendulum_results.csv")

print(df.info())

plt.plot(df.iloc[:, 0], df.iloc[:, 2], label = "tY без затухания")
plt.xlabel = "t"
plt.ylabel = "Y"
plt.show()

plt.plot(df.iloc[:, 1], df.iloc[:, 2], label = "XY без затухания")
plt.xlabel = "X"
plt.ylabel = "Y"
plt.show()

plt.plot(df.iloc[:, 0], df.iloc[:, 4], label = "tY с затуханием")
plt.xlabel = "t"
plt.ylabel = "Y"
plt.show()

plt.plot(df.iloc[:, 3], df.iloc[:, 4], label = "XY с затуханием")
plt.xlabel = "X"
plt.ylabel = "Y"
plt.show()