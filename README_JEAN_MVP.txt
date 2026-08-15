JEAN - MVP DE BOSQUE PROCEDURAL
===============================

Objetivo de esta parte:
Tomar el arbol procedural base, que entrega un TreeSkeleton generado
por L-system, y convertirlo en un bosque renderizado de forma eficiente.

Que demuestra la escena mejorada:
1. Arbol base:
   Se muestra un solo arbol generado desde el L-system.

2. Generacion del bosque:
   El mismo arbol base se instancia muchas veces sobre terreno procedural.
   Los arboles ya no aparecen de golpe: crecen desde el suelo mediante una
   onda radial de revelado.

3. Rendimiento:
   Las mallas se reutilizan con GPU instancing. No se crean cientos de mallas
   diferentes; se dibujan lotes de matrices.

4. LOD:
   Cerca se dibuja el arbol completo.
   A media distancia se podan ramas finas usando orden de Strahler.
   Lejos se usa un billboard liviano.

5. Culling:
   Arboles fuera de la camara o demasiado lejos no se mandan a renderizar.

6. Presentacion:
   La demo empieza sola: primero un arbol, luego el bosque crece alrededor.
   El panel muestra FPS, arboles visibles, arboles culled, LODs y draw calls.

7. Ambiente:
   La escena incluye suelo visible, sendero, claro central, musgo, pasto,
   rocas, troncos caidos, niebla, luz calida y camara cinematica.

Como probar:
1. Abrir el proyecto D:\Eric\l_system_tree en Unity.
2. Esperar a que compile.
3. En el menu superior: Bosque > Jean > Build Forest Showcase Scene.
4. Abrir/usar la escena Assets/Scenes/Jean_Forest_Showcase.unity.
5. Presionar Play.

Frase corta para explicar:
"Mi companero genera la estructura del arbol con L-systems. Mi parte toma ese
TreeSkeleton y lo convierte en un bosque renderizable: creo mallas, distribuyo
instancias en terreno, aplico LOD, culling, GPU instancing, viento y benchmarks
en pantalla."
