// ═══════════════════════════════════════════════════════════════════════════
//  Contract.cs — CONTRATO DE DATOS DEL PROYECTO
// ═══════════════════════════════════════════════════════════════════════════
//
//  Este archivo define la frontera entre los dos módulos del proyecto:
//
//      [A] Motor L-system  ──produce──>  TreeSkeleton  ──consume──>  [B] Bosque
//
//  REGLAS:
//    1. Ningún cambio a los tipos de este archivo sin acuerdo de ambos.
//       Si hay que cambiar algo, se avisa, se hace en una rama aparte y se
//       actualiza la versión de abajo.
//    2. Este archivo NO depende de nada del proyecto. Solo UnityEngine.
//       No debe aparecer aquí ni Mesh, ni Material, ni MonoBehaviour.
//    3. Convenciones de espacio (memorizar, es fuente típica de bugs):
//         · Origen  = base del tronco, a nivel del suelo.
//         · +Y      = arriba.
//         · Unidades = metros. Un árbol adulto mide ~8-15 en Y.
//         · Todo en espacio LOCAL del árbol. El posicionamiento en el
//           terreno es responsabilidad de [B], nunca de [A].
//
//  VERSIÓN DEL CONTRATO: 1.0
//  Última modificación: (fecha) — (quién) — (qué)
//
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bosque.Contract
{
    // ═══════════════════════════════════════════════════════════════════════
    //  1. UNIDAD ATÓMICA: EL SEGMENTO DE RAMA
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Un tramo recto de rama. Es lo que la tortuga 3D produce cada vez que
    /// interpreta un símbolo de dibujo (F, S, 0, 1...).
    ///
    /// Es struct, no class: van a existir cientos de miles de estos y deben
    /// vivir contiguos en memoria (cache-friendly, compatible con Jobs/Burst).
    /// </summary>
    [Serializable]
    public struct BranchSegment
    {
        // ── Geometría ────────────────────────────────────────────────────
        /// <summary>Punto de origen del segmento, en espacio local del árbol.</summary>
        public Vector3 start;

        /// <summary>Punto final. La dirección es (end - start).normalized.</summary>
        public Vector3 end;

        /// <summary>Radio en la base. Permite taper real entre segmentos.</summary>
        public float radiusStart;

        /// <summary>Radio en la punta. Debe ser menor o igual a radiusStart.</summary>
        public float radiusEnd;

        /// <summary>
        /// Orientación completa de la tortuga al dibujar este segmento.
        /// La dirección ya está implícita en (end - start), pero el ROLL
        /// (giro sobre el propio eje) solo vive aquí. [B] lo necesita para
        /// orientar hojas y para que las ramas no roten entre LODs.
        /// Convención: forward = heading, up = vector U de la tortuga.
        /// </summary>
        public Quaternion orientation;

        // ── Topología ────────────────────────────────────────────────────
        /// <summary>
        /// Índice del segmento padre dentro de TreeSkeleton.branches.
        /// -1 si es la raíz del árbol. Define el grafo sin punteros.
        /// </summary>
        public int parentIndex;

        /// <summary>
        /// Profundidad de ramificación: cuántos '[' hay por encima.
        /// 0 = tronco. Sirve para colorear y para variación de material.
        /// </summary>
        public byte depth;

        /// <summary>
        /// Orden de Strahler. 1 = ramita terminal; el tronco tiene el valor
        /// más alto. ES EL CRITERIO DE LOD: "descartar orden &lt;= 1" poda el
        /// detalle fino sin destruir la silueta. Lo calcula
        /// TreeSkeletonUtils.ComputeStrahlerOrders, no el generador.
        /// </summary>
        public byte order;

        // ── Derivados (conveniencia, no se serializan) ───────────────────
        public Vector3 Direction => (end - start).normalized;
        public float Length => Vector3.Distance(start, end);
        public bool IsRoot => parentIndex < 0;
    }


    // ═══════════════════════════════════════════════════════════════════════
    //  2. HOJAS
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Punto de anclaje de una hoja. [A] decide DÓNDE va cada hoja;
    /// [B] decide CON QUÉ se dibuja (quad, cruz de quads, atlas de textura).
    /// </summary>
    [Serializable]
    public struct LeafAnchor
    {
        /// <summary>Posición en espacio local del árbol.</summary>
        public Vector3 position;

        /// <summary>Orientación de la hoja. forward = normal de la lámina.</summary>
        public Quaternion orientation;

        /// <summary>Escala relativa (1.0 = tamaño base de la especie).</summary>
        public float scale;

        /// <summary>
        /// Índice de la rama a la que pertenece, dentro de branches.
        /// Necesario para que la hoja herede el desplazamiento de viento
        /// de su rama, y para poder podarla junto con ella en los LODs.
        /// </summary>
        public int branchIndex;
    }


    // ═══════════════════════════════════════════════════════════════════════
    //  3. LA SALIDA: TreeSkeleton
    //     Esto es LO QUE [A] ENTREGA Y [B] RECIBE.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Esqueleto completo de un árbol: topología + geometría, SIN mallas.
    ///
    /// Deliberadamente NO contiene un Mesh. El paso esqueleto -> malla es
    /// una etapa aparte (TreeMesher), porque [B] necesita el esqueleto crudo
    /// para generar LODs, billboards, pesos de viento y el cache de ramas.
    /// Si [A] entregara triángulos ya cocinados, [B] quedaría bloqueado.
    /// </summary>
    public class TreeSkeleton
    {
        // ── Datos principales ────────────────────────────────────────────
        public BranchSegment[] branches;
        public LeafAnchor[] leaves;

        // ── Metadatos: [B] los necesita SIN recorrer los arrays ──────────
        /// <summary>AABB en espacio local. Para culling y selección de LOD.</summary>
        public Bounds bounds;

        /// <summary>Altura total en metros (equivale a bounds.max.y).</summary>
        public float height;

        /// <summary>Radio del tronco en la base. Para colisión aproximada.</summary>
        public float trunkRadius;

        /// <summary>Identificador de especie. Debe coincidir con TreeSpecies.speciesId.</summary>
        public int speciesId;

        /// <summary>
        /// Semilla usada. CLAVE: mismo (species, seed) => mismo árbol, siempre.
        /// Permite guardar 20 semillas en vez de 5000 mallas.
        /// </summary>
        public int seed;

        /// <summary>Profundidad máxima de ramificación encontrada.</summary>
        public byte maxDepth;

        /// <summary>Orden de Strahler del tronco (= orden máximo del árbol).</summary>
        public byte maxOrder;

        // ── Trazabilidad: para el informe técnico y para debuggear ───────
        /// <summary>Longitud de la cadena L-system que produjo este árbol.</summary>
        public int symbolCount;

        /// <summary>Iteraciones aplicadas.</summary>
        public int iterations;

        /// <summary>Tiempo de generación en milisegundos (expansión + tortuga).</summary>
        public float generationMs;

        // ── Utilidad ─────────────────────────────────────────────────────
        public int BranchCount => branches?.Length ?? 0;
        public int LeafCount => leaves?.Length ?? 0;

        /// <summary>
        /// Chequeo de sanidad. [A] lo llama antes de entregar; [B] lo llama
        /// en modo debug al recibir. Barato y ahorra horas de depuración.
        /// </summary>
        public bool Validate(out string error)
        {
            error = null;

            if (branches == null || branches.Length == 0)
            { error = "branches vacío o nulo."; return false; }

            if (leaves == null)
            { error = "leaves es nulo (usar array vacío, no null)."; return false; }

            for (int i = 0; i < branches.Length; i++)
            {
                var b = branches[i];

                if (b.parentIndex >= i)
                { error = $"branches[{i}].parentIndex={b.parentIndex} debe ser < {i} (orden topológico)."; return false; }

                if (b.parentIndex < -1)
                { error = $"branches[{i}].parentIndex inválido: {b.parentIndex}."; return false; }

                if (b.radiusEnd > b.radiusStart + 1e-5f)
                { error = $"branches[{i}]: radiusEnd ({b.radiusEnd}) > radiusStart ({b.radiusStart})."; return false; }

                if (b.Length < 1e-6f)
                { error = $"branches[{i}] tiene longitud ~0."; return false; }
            }

            for (int i = 0; i < leaves.Length; i++)
            {
                int bi = leaves[i].branchIndex;
                if (bi < 0 || bi >= branches.Length)
                { error = $"leaves[{i}].branchIndex={bi} fuera de rango."; return false; }
            }

            if (height <= 0f)
            { error = "height <= 0."; return false; }

            return true;
        }

        public override string ToString() =>
            $"TreeSkeleton[sp={speciesId} seed={seed}] " +
            $"{BranchCount} ramas, {LeafCount} hojas, h={height:F2}m, " +
            $"{symbolCount} símbolos, {generationMs:F1}ms";
    }


    // ═══════════════════════════════════════════════════════════════════════
    //  4. LA ENTRADA: TreeSpecies
    //     [A] define el formato. [B] crea assets y los pasa al generador.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Una producción del L-system: símbolo -> sucesor.</summary>
    [Serializable]
    public struct ProductionRule
    {
        public char symbol;
        public string successor;

        public ProductionRule(char symbol, string successor)
        {
            this.symbol = symbol;
            this.successor = successor;
        }
    }

    /// <summary>
    /// Definición completa de una especie de árbol. Es un ScriptableObject
    /// para poder crear y tunear especies desde el editor sin recompilar,
    /// y para que [B] pueda tener varias en escena sin tocar código de [A].
    ///
    /// Crear con: Assets > Create > Bosque > Especie de árbol
    /// </summary>
    [CreateAssetMenu(fileName = "Especie", menuName = "Bosque/Especie de árbol", order = 0)]
    public class TreeSpecies : ScriptableObject
    {
        [Header("Identidad")]
        public int speciesId = 0;
        public string displayName = "Especie sin nombre";

        [Header("Gramática L-system")]
        [Tooltip("Axioma (ω): cadena inicial antes de iterar.")]
        public string axiom = "F";

        [Tooltip("Producciones. Símbolos: F S dibujan · +- giro · &^ inclinación · \\/ roll · [] rama · ! adelgaza · L hoja")]
        public ProductionRule[] rules = new[] { new ProductionRule('F', "F[&+F]F[&-F][/^F]") };

        [Range(1, 8)]
        public int iterations = 5;

        [Header("Geometría")]
        [Tooltip("Ángulo base de giro, en grados.")]
        [Range(1f, 90f)] public float angleDeg = 22.5f;

        [Tooltip("Longitud del segmento inicial, en metros.")]
        public float baseLength = 1.0f;

        [Tooltip("Factor por el que se multiplica la longitud al entrar en '['.")]
        [Range(0.4f, 1f)] public float lengthScale = 0.85f;

        [Tooltip("Radio del tronco en la base, en metros.")]
        public float baseRadius = 0.06f;

        [Tooltip("Factor por el que se multiplica el radio al entrar en '['.")]
        [Range(0.4f, 1f)] public float radiusScale = 0.75f;

        [Tooltip("Factor aplicado por cada símbolo '!' (adelgazar).")]
        [Range(0.5f, 1f)] public float pinchFactor = 0.85f;

        [Header("Variación estocástica")]
        [Tooltip("0 = todos los árboles idénticos. Sube esto para que se vean orgánicos.")]
        [Range(0f, 0.6f)] public float angleJitter = 0.15f;

        [Range(0f, 0.6f)] public float lengthJitter = 0.10f;

        [Header("Hojas")]
        public float leafScale = 0.12f;

        [Tooltip("Probabilidad de instanciar cada hoja. 1 = todas.")]
        [Range(0f, 1f)] public float leafDensity = 1f;

        [Header("Colocación en el terreno (usado por [B])")]
        [Tooltip("Rango de altura de terreno donde puede aparecer esta especie.")]
        public Vector2 altitudeRange = new Vector2(0f, 1000f);

        [Tooltip("Pendiente máxima en grados donde puede crecer.")]
        [Range(0f, 90f)] public float maxSlopeDeg = 35f;

        [Tooltip("Separación mínima entre árboles de esta especie, en metros.")]
        public float minSpacing = 4f;

        [Tooltip("Rango de escala aleatoria aplicada a la instancia.")]
        public Vector2 scaleRange = new Vector2(0.85f, 1.25f);
    }


    // ═══════════════════════════════════════════════════════════════════════
    //  5. LA FUNCIÓN: única puerta de entrada
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Todo generador de árboles implementa esto. [B] programa contra la
    /// interfaz, nunca contra la clase concreta: así el generador fake y el
    /// L-system real son intercambiables sin tocar el código del bosque.
    /// </summary>
    public interface ITreeGenerator
    {
        /// <summary>
        /// Genera un árbol. DEBE ser determinista:
        /// mismo (species, seed) => TreeSkeleton idéntico, siempre.
        /// No debe tocar Unity fuera del hilo principal salvo que se
        /// documente lo contrario.
        /// </summary>
        TreeSkeleton Generate(TreeSpecies species, int seed);
    }


    // ═══════════════════════════════════════════════════════════════════════
    //  6. UTILIDADES COMPARTIDAS
    //     Las usan ambos. Viven aquí para no duplicarlas.
    // ═══════════════════════════════════════════════════════════════════════

    public static class TreeSkeletonUtils
    {
        /// <summary>
        /// Tabla de hijos por segmento. El contrato guarda parentIndex
        /// (compacto, cache-friendly); si hace falta recorrer hacia abajo,
        /// se construye esta tabla una sola vez.
        /// </summary>
        public static List<int>[] BuildChildTable(TreeSkeleton skel)
        {
            var table = new List<int>[skel.branches.Length];
            for (int i = 0; i < table.Length; i++) table[i] = new List<int>();

            for (int i = 0; i < skel.branches.Length; i++)
            {
                int p = skel.branches[i].parentIndex;
                if (p >= 0) table[p].Add(i);
            }
            return table;
        }

        /// <summary>
        /// Calcula el orden de Strahler de cada segmento y lo escribe en
        /// branches[i].order. Regla clásica:
        ///   · sin hijos            -> orden 1
        ///   · un hijo dominante    -> hereda su orden
        ///   · dos o más hijos con el mismo orden máximo k -> k + 1
        ///
        /// Aprovecha que el array está en orden topológico (padre siempre
        /// antes que hijo), así que un solo recorrido hacia atrás basta.
        /// Devuelve el orden del tronco.
        /// </summary>
        public static byte ComputeStrahlerOrders(TreeSkeleton skel)
        {
            var b = skel.branches;
            int n = b.Length;

            // maxOrder[i] y cuántos hijos alcanzan ese máximo
            var best = new byte[n];
            var bestCount = new int[n];

            for (int i = n - 1; i >= 0; i--)
            {
                byte ord = (bestCount[i] == 0)
                    ? (byte)1
                    : (bestCount[i] >= 2 ? (byte)(best[i] + 1) : best[i]);

                b[i].order = ord;

                int p = b[i].parentIndex;
                if (p >= 0)
                {
                    if (ord > best[p]) { best[p] = ord; bestCount[p] = 1; }
                    else if (ord == best[p]) { bestCount[p]++; }
                }
            }

            byte trunk = n > 0 ? b[0].order : (byte)0;
            skel.maxOrder = trunk;
            return trunk;
        }

        /// <summary>
        /// Recalcula bounds, height, trunkRadius y maxDepth a partir de los
        /// segmentos. [A] la llama al final de Generate para no olvidar nada.
        /// </summary>
        public static void RecalculateMetadata(TreeSkeleton skel)
        {
            var b = skel.branches;
            if (b == null || b.Length == 0)
            {
                skel.bounds = new Bounds(Vector3.zero, Vector3.zero);
                skel.height = 0f;
                return;
            }

            var bounds = new Bounds(b[0].start, Vector3.zero);
            byte maxDepth = 0;

            for (int i = 0; i < b.Length; i++)
            {
                // Se expande con el radio para que el AABB no corte la corteza
                float r = Mathf.Max(b[i].radiusStart, b[i].radiusEnd);
                bounds.Encapsulate(b[i].start + Vector3.one * r);
                bounds.Encapsulate(b[i].start - Vector3.one * r);
                bounds.Encapsulate(b[i].end + Vector3.one * r);
                bounds.Encapsulate(b[i].end - Vector3.one * r);

                if (b[i].depth > maxDepth) maxDepth = b[i].depth;
            }

            for (int i = 0; i < skel.leaves.Length; i++)
                bounds.Encapsulate(skel.leaves[i].position);

            skel.bounds = bounds;
            skel.height = bounds.max.y;
            skel.maxDepth = maxDepth;
            skel.trunkRadius = b[0].radiusStart;
        }

        /// <summary>
        /// Devuelve los índices de segmento que sobreviven a un nivel de LOD,
        /// podando por orden de Strahler. minOrder=1 -> árbol completo.
        /// Esta es la base del sistema de LOD de [B].
        /// </summary>
        public static List<int> FilterByOrder(TreeSkeleton skel, int minOrder)
        {
            var keep = new List<int>();
            for (int i = 0; i < skel.branches.Length; i++)
                if (skel.branches[i].order >= minOrder) keep.Add(i);
            return keep;
        }

        /// <summary>
        /// PRNG determinista e independiente de UnityEngine.Random (que es
        /// estado global y rompe la reproducibilidad si otro sistema lo usa).
        /// Implementación de xorshift32.
        /// </summary>
        public struct Rng
        {
            uint state;

            public Rng(int seed)
            {
                state = (uint)seed;
                if (state == 0) state = 0x9E3779B9u;
            }

            public uint NextUInt()
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return state;
            }

            /// <summary>Float en [0, 1).</summary>
            public float NextFloat() => (NextUInt() & 0x00FFFFFF) / 16777216f;

            /// <summary>Float en [min, max).</summary>
            public float Range(float min, float max) => min + NextFloat() * (max - min);

            /// <summary>Factor multiplicativo en [1-j, 1+j]. Para jitter.</summary>
            public float Jitter(float j) => 1f + (NextFloat() * 2f - 1f) * j;
        }
    }
}