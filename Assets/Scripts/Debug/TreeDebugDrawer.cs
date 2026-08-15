// ═══════════════════════════════════════════════════════════════════════════
//  TreeDebugDrawer.cs — VISOR DE ESQUELETOS EN EL EDITOR
// ═══════════════════════════════════════════════════════════════════════════
//
//  Responsable: [A]   ·   Herramienta de trabajo, no parte del producto final
//
//  Un TreeSpecies es un ScriptableObject: datos puros, no se ve en la escena.
//  Este componente es el puente: lee la especie, llama al generador y pinta
//  el TreeSkeleton con Gizmos.
//
//  CÓMO USARLO
//  ───────────
//    1. En la escena: GameObject > Create Empty, renómbralo "Tree Debug".
//    2. Add Component > Tree Debug Drawer.
//    3. Arrastra tu asset de especie al campo "Species".
//    4. El árbol aparece en la vista de escena (NO en Game: los Gizmos solo
//       se dibujan en Scene view).
//    5. Mueve cualquier parámetro del asset y se regenera solo.
//
//  Los Gizmos no tienen grosor de línea, así que las ramas se ven como
//  alambres. Eso es correcto: esto muestra el ESQUELETO, no la malla.
//  El grosor real llega con el mesher.
//
//  BORRAR (o dejar solo para depurar) cuando el mesher esté funcionando.
//
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using Bosque.Contract;
using Bosque.LSystem;
using Bosque.Generation;

namespace Bosque.Debugging
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class TreeDebugDrawer : MonoBehaviour
    {
        public enum GeneratorKind { LSystem, Fake }

        [Header("Entrada")]
        [Tooltip("Asset de especie. Assets > Create > Bosque > Especie de árbol")]
        public TreeSpecies species;

        [Tooltip("Cambiar esto da un árbol distinto con la misma gramática.")]
        public int seed = 1;

        [Tooltip("LSystem = motor real. Fake = andamio de [B].")]
        public GeneratorKind generator = GeneratorKind.LSystem;

        [Header("Visualización")]
        [Tooltip("Colorea por orden de Strahler en vez de por profundidad.")]
        public bool colorByStrahler = true;

        [Tooltip("Simula un nivel de LOD: oculta las ramas de orden menor a este.")]
        [Range(1, 8)] public int minOrderShown = 1;

        public bool showLeaves = true;
        public bool showBounds = false;

        [Tooltip("Tope de seguridad: dibujar 50k gizmos congela el editor.")]
        [Range(100, 20000)] public int maxSegmentsDrawn = 6000;

        [Header("Resultado (solo lectura)")]
        [TextArea(4, 10)]
        public string info = "(sin generar)";

        // ── Estado ───────────────────────────────────────────────────────
        TreeSkeleton skeleton;
        ITreeGenerator gen;
        int lastFingerprint;

        static readonly Color C_TRUNK = new Color(0.42f, 0.26f, 0.13f);
        static readonly Color C_TIP = new Color(0.56f, 0.75f, 0.42f);
        static readonly Color C_LEAF = new Color(0.25f, 0.65f, 0.28f, 0.85f);
        static readonly Color C_BOUNDS = new Color(0.35f, 0.55f, 0.75f, 0.5f);

        // ═══════════════════════════════════════════════════════════════

        void OnEnable() { Regenerate(); }

        void OnValidate()
        {
            // Se dispara al tocar un campo de ESTE componente.
            Regenerate();
        }

        [ContextMenu("Regenerar")]
        public void Regenerate()
        {
            if (species == null)
            {
                skeleton = null;
                info = "Falta asignar una especie.";
                return;
            }

            if (gen == null || !MatchesKind(gen))
                gen = (generator == GeneratorKind.LSystem)
                    ? (ITreeGenerator)new LSystemTreeGenerator()
                    : new FakeTreeGenerator();

            skeleton = gen.Generate(species, seed);
            lastFingerprint = SpeciesFingerprint();

            if (skeleton == null || skeleton.BranchCount == 0)
            {
                info = "El generador no produjo ramas.\n" +
                       "Revisa que algún sucesor contenga 'F'.";
                return;
            }

            var ls = gen as LSystemTreeGenerator;

            info =
                "ramas: " + skeleton.BranchCount + "\n" +
                "hojas: " + skeleton.LeafCount + "\n" +
                "altura: " + skeleton.height.ToString("F2") + " m\n" +
                "símbolos: " + skeleton.symbolCount + "\n" +
                "iteraciones: " + skeleton.iterations + "\n" +
                "orden máx (Strahler): " + skeleton.maxOrder + "\n" +
                "profundidad máx: " + skeleton.maxDepth + "\n" +
                "generación: " + skeleton.generationMs.ToString("F2") + " ms" +
                (ls != null && ls.LastWasCapped ? "\n⚠ TOPE DE SÍMBOLOS ALCANZADO" : "");

            string err;
            if (!skeleton.Validate(out err))
                info += "\n⚠ CONTRATO ROTO: " + err;
        }

        bool MatchesKind(ITreeGenerator g)
        {
            return generator == GeneratorKind.LSystem
                ? (g is LSystemTreeGenerator)
                : (g is FakeTreeGenerator);
        }

        void Update()
        {
            // OnValidate no se dispara al editar el ASSET de especie, solo al
            // editar este componente. Esta huella detecta cambios en el asset
            // para que mover un slider de la especie actualice el árbol al vuelo.
            if (species == null) return;
            int fp = SpeciesFingerprint();
            if (fp != lastFingerprint) Regenerate();
        }

        int SpeciesFingerprint()
        {
            if (species == null) return 0;
            unchecked
            {
                int h = 17;
                h = h * 31 + (species.axiom != null ? species.axiom.GetHashCode() : 0);
                h = h * 31 + species.iterations;
                h = h * 31 + species.angleDeg.GetHashCode();
                h = h * 31 + species.baseLength.GetHashCode();
                h = h * 31 + species.lengthScale.GetHashCode();
                h = h * 31 + species.baseRadius.GetHashCode();
                h = h * 31 + species.radiusScale.GetHashCode();
                h = h * 31 + species.pinchFactor.GetHashCode();
                h = h * 31 + species.angleJitter.GetHashCode();
                h = h * 31 + species.lengthJitter.GetHashCode();
                h = h * 31 + species.leafScale.GetHashCode();
                h = h * 31 + species.leafDensity.GetHashCode();
                if (species.rules != null)
                    for (int i = 0; i < species.rules.Length; i++)
                    {
                        h = h * 31 + species.rules[i].symbol.GetHashCode();
                        var s = species.rules[i].successor;
                        h = h * 31 + (s != null ? s.GetHashCode() : 0);
                    }
                return h;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  DIBUJO
        // ═══════════════════════════════════════════════════════════════

        void OnDrawGizmos()
        {
            if (skeleton == null || skeleton.BranchCount == 0) return;

            var b = skeleton.branches;

            // Escala de color: por Strahler (útil para previsualizar LOD) o
            // por profundidad de ramificación.
            float maxScale = colorByStrahler
                ? Mathf.Max(skeleton.maxOrder, 1)
                : Mathf.Max(skeleton.maxDepth, 1);

            int drawn = 0;
            for (int i = 0; i < b.Length && drawn < maxSegmentsDrawn; i++)
            {
                if (b[i].order < minOrderShown) continue;

                // Strahler alto = tronco. Se invierte para que el gradiente
                // vaya de tronco (oscuro) a punta (claro) en ambos modos.
                float t = colorByStrahler
                    ? 1f - (b[i].order - 1) / maxScale
                    : b[i].depth / maxScale;

                Gizmos.color = Color.Lerp(C_TRUNK, C_TIP, Mathf.Clamp01(t));
                Gizmos.DrawLine(transform.TransformPoint(b[i].start),
                                transform.TransformPoint(b[i].end));
                drawn++;
            }

            if (showLeaves && skeleton.leaves != null)
            {
                Gizmos.color = C_LEAF;
                var lv = skeleton.leaves;
                // Muestreo: con miles de hojas, dibujarlas todas ahoga el editor.
                int step = Mathf.Max(1, lv.Length / 1500);
                for (int i = 0; i < lv.Length; i += step)
                {
                    if (b[lv[i].branchIndex].order < minOrderShown) continue;
                    Gizmos.DrawSphere(transform.TransformPoint(lv[i].position),
                                      lv[i].scale * 0.35f);
                }
            }

            if (showBounds)
            {
                Gizmos.color = C_BOUNDS;
                Gizmos.DrawWireCube(transform.TransformPoint(skeleton.bounds.center),
                                    skeleton.bounds.size);
            }
        }
    }
}