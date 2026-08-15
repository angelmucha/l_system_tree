import numpy as np
import matplotlib.pyplot as plt

def generar_l_system(axiom, rules, iterations):
    """
    Realiza la expansión de caracteres del sistema-L de manera iterativa.
    Aplica las reglas de producción en paralelo a todas las variables.
    """
    current_string = axiom
    history = [current_string]
    for _ in range(iterations):
        next_string = []
        for char in current_string:
            next_string.append(rules.get(char, char))
        current_string = "".join(next_string)
        history.append(current_string)
    return history

def render_l_system(ax, l_string, initial_state=(0, 0, np.pi/2, 1.0), scale_factor=0.6, angle_delta=np.pi/4):
    """
    Traduce la cadena de caracteres en coordenadas geométricas y dibuja
    el modelo usando Matplotlib (Geometría de Tortuga).
    """
    # Estado de la tortuga: (x, y, ángulo, longitud_paso)
    state_stack = []
    curr_x, curr_y, curr_theta, curr_len = initial_state
    
    segments = []
    leaves = []
    
    for char in l_string:
        if char == '1' or char == '0':
            # Calcular la siguiente posición en base a la trigonometría básica
            next_x = curr_x + curr_len * np.cos(curr_theta)
            next_y = curr_y + curr_len * np.sin(curr_theta)
            
            segments.append(((curr_x, curr_y), (next_x, next_y), char))
            
            if char == '0':
                leaves.append((next_x, next_y))
                
            curr_x, curr_y = next_x, next_y
        elif char == '+':
            # Girar hacia la izquierda (CCW) y reducir escala de paso
            curr_theta += angle_delta
            curr_len *= scale_factor
        elif char == '-':
            # Girar hacia la derecha (CW) y reducir escala de paso
            curr_theta -= angle_delta
            curr_len *= scale_factor
        elif char == '[':
            # Guardar el estado actual en la pila
            state_stack.append((curr_x, curr_y, curr_theta, curr_len))
        elif char == ']':
            # Recuperar el último estado guardado
            if state_stack:
                curr_x, curr_y, curr_theta, curr_len = state_stack.pop()
                
    # Dibujar los segmentos de línea
    for pt1, pt2, char_type in segments:
        color = '#8B4513' if char_type == '1' else '#2E8B57'  # Marrón para tallos, verde para ramas jóvenes
        linewidth = 2.5 if char_type == '1' else 1.5
        ax.plot([pt1, pt2], [pt1[1], pt2[1]], color=color, linewidth=linewidth, solid_capstyle='round')
        
    # Dibujar las hojas en los extremos terminales (símbolo '0')
    if leaves:
        leaf_x, leaf_y = zip(*leaves)
        ax.scatter(leaf_x, leaf_y, color='#32CD32', s=40, zorder=3, edgecolors='#228B22', linewidths=0.5)

    ax.set_aspect('equal')
    ax.axis('off')

# --- CONFIGURACIÓN DE LA SIMULACIÓN ---
axiom = "0"
rules = {"0": "1[+0][-0]"}
iterations = 10

# Generar la historia evolutiva paso a paso
history = generar_l_system(axiom, rules, iterations)

# Mostrar la evolución de la gramática en consola
print("--- EVOLUCIÓN DE LAS CADENAS DEL SISTEMA-L ---")
for i, s in enumerate(history):
    print(f"Paso {i} (n={i}):")
    # Si la cadena es muy larga, mostramos solo los primeros 80 caracteres
    print(s if len(s) <= 80 else f"{s[:80]}... [Longitud total: {len(s)} caracteres]")
print("-" * 46)

# Crear la figura para mostrar los gráficos lado a lado
fig, axes = plt.subplots(1, iterations + 1, figsize=(15, 5))

for i in range(iterations + 1):
    render_l_system(axes[i], history[i], scale_factor=0.6, angle_delta=np.pi/4)
    axes[i].set_title(f"Paso {i} (n={i})", fontsize=12, fontweight='bold', color='#2F4F4F')
    
    # Contar la cantidad de ramas (1) y hojas (0) en esta iteración
    stems = history[i].count('1')
    leaves = history[i].count('0')
    axes[i].text(0.5, -0.05, f"Tallos (1): {stems}\nHojas (0): {leaves}", 
                 transform=axes[i].transAxes, ha='center', fontsize=9, 
                 color='#555555', bbox=dict(facecolor='whitesmoke', alpha=0.8, boxstyle='round,pad=0.3'))

plt.tight_layout()
plt.show()