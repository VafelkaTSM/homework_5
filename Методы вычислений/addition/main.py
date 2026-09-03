import gradio as gr
import numpy as np
import matplotlib.pyplot as plt
from module import Euler_1, Euler_2, Runge_Kutta_4, Adams_Explicit, Adams_Modified, Runge_Kutta_45

def gen_plot(method_name, a, b, c, d, x1, x2, t_max, h):
    method_dict = {
        'Euler_1': Euler_1, 'Euler_2': Euler_2, 'Runge_Kutta_4': Runge_Kutta_4, 
        'Adams_Explicit': Adams_Explicit, 'Adams_Modified': Adams_Modified, 'Runge_Kutta_45': Runge_Kutta_45
    }
    method = method_dict[method_name]

    f = [
        lambda t, x: a * x[0] - b * x[0] * x[1],
        lambda t, x: c * x[0] * x[1] - d * x[1]
    ]

    num_steps = int(min(t_max / h, 5000)) 
    t_eval = np.linspace(0, t_max, num_steps)

    victims = []
    predators = []

    current_state = [float(x1), float(x2)]
    victims.append(current_state[0])
    predators.append(current_state[1])

    for i in range(len(t_eval) - 1):
        t_start = t_eval[i]
        t_end = t_eval[i+1]
        step_size = t_end - t_start

        current_state = method(f, current_state, t_start, t_end, step_size)
        
        victims.append(current_state[0])
        predators.append(current_state[1])

    fig, ax = plt.subplots(figsize=(8, 4.5))
    ax.plot(t_eval, victims, label='Жертвы', color='blue', linewidth=2)
    ax.plot(t_eval, predators, label='Хищники', color='red', linewidth=2)
    
    ax.set_title(f'Модель Лотки-Вольтерры ({method_name})')
    ax.set_xlabel('Время (T)')
    ax.set_ylabel('Размер популяции')
    ax.grid(True, linestyle='--', alpha=0.6)
    ax.legend(loc='upper right')

    return fig

with gr.Blocks() as demo:
    with gr.Row():
        with gr.Column():
            a_slider = gr.Slider(0, 10, label='Alpha (Рождаемость жертв)', step=0.01, precision=2, value=1)
            b_slider = gr.Slider(0, 10, label='Beta (Убийство жертв)', step=0.01, precision=2, value=0.1)
            c_slider = gr.Slider(0, 10, label='Delta (Размножение хищников)', step=0.01, precision=2, value=0.1)
            d_slider = gr.Slider(0, 10, label='Gamma (Смертность хищников)', step=0.01, precision=2, value=2.5)
            x1_slider = gr.Slider(0, 100, label='Жертв изначально', step=1, value=30)
            x2_slider = gr.Slider(0, 100, label='Хищников изначально', step=1, value=10)
            t_max_num = gr.Number(label='Длительность моделирования', precision=0, value=50)
        with gr.Column():
            h_num = gr.Number(label='Шаг дифференцирования', value=5e-2)
            method_drop = gr.Dropdown(['Euler_1', 'Euler_2', 'Runge_Kutta_4', 'Adams_Explicit', 'Adams_Modified', 'Runge_Kutta_45'], 
                                 label='Метод расчёта', value='Runge_Kutta_45')
            btn = gr.Button('Запуск моделирования', variant='primary')
            plot_output = gr.Plot(label='Результат')
            
        btn.click(
            fn=gen_plot, 
            inputs=[method_drop, a_slider, b_slider, c_slider, d_slider, x1_slider, x2_slider, t_max_num, h_num], 
            outputs=plot_output
        )

if __name__ == "__main__":
    demo.launch()