// ═══════════════════════════════════════════════════════════════════════════
//  LSystemTreeGenerator.cs — IMPLEMENTACIÓN REAL DE ITreeGenerator
// ═══════════════════════════════════════════════════════════════════════════
//
//  Responsable: [A]   ·   Consumidor: [B]
//
//  Sustituye a FakeTreeGenerator. Para [B] el cambio es una línea:
//
//      ITreeGenerator gen = new LSystemTreeGenerator();
//
//  Une las tres etapas y rellena todo lo que el contrato exige:
//
//      TreeSpecies ──compilar──> CompiledGrammar ──expandir──> string
//                  ──interpretar──> segmentos+hojas ──Strahler+metadatos──> TreeSkeleton
//
//  CACHE DE GRAMÁTICAS: compilar valida corchetes y recorre todos los
//  sucesores. Hacerlo por árbol sería tirar tiempo en el bosque, así que se
//  compila una vez por especie y se reutiliza. La caché se invalida sola si
//  [B] cambia el asset en el editor (ver GrammarCacheKey).
//
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Bosque.Contract;

namespace Bosque.LSystem
{
    public class LSystemTreeGenerator : ITreeGenerator
    {
        // ── Caché de gramáticas compiladas, por especie ──────────────────
        struct CacheEntry
        {
            public CompiledGrammar grammar;
            public int fingerprint;    // detecta ediciones del asset en el editor
        }

        readonly Dictionary<TreeSpecies, CacheEntry> cache =
            new Dictionary<TreeSpecies, CacheEntry>();

        /// <summary>Última cadena expandida. Solo para depurar y para el informe.</summary>
        public string LastExpandedString { get; private set; }

        /// <summary>Estadísticas de la última interpretación.</summary>
        public LSystemTurtle.Stats LastStats { get; private set; }

        /// <summary>Se dispara si la última generación tocó el tope de símbolos.</summary>
        public bool LastWasCapped { get; private set; }

        // ═══════════════════════════════════════════════════════════════
        //  ITreeGenerator
        // ═══════════════════════════════════════════════════════════════

        public TreeSkeleton Generate(TreeSpecies species, int seed)
        {
            var sw = Stopwatch.StartNew();

            CompiledGrammar grammar = GetGrammar(species);
            if (grammar == null) return EmptySkeleton(species, seed);

            // Un rng por árbol. El mezclado con speciesId evita que dos
            // especies distintas con el mismo seed salgan correlacionadas.
            var rng = new TreeSkeletonUtils.Rng(seed ^ (species.speciesId * 73856093));

            // ── 1. Expansión ─────────────────────────────────────────────
            int iterationsReached;
            bool capped;
            string expanded = grammar.Expand(species.iterations, ref rng,
                                             out iterationsReached, out capped);

            LastExpandedString = expanded;
            LastWasCapped = capped;

#if UNITY_EDITOR
            if (capped)
                UnityEngine.Debug.LogWarning(
                    "[LSystem] '" + species.displayName + "' superó " +
                    CompiledGrammar.MAX_SYMBOLS + " símbolos. Se usó la iteración " +
                    iterationsReached + " de " + species.iterations + ".");
#endif

            // ── 2. Interpretación ────────────────────────────────────────
            // Capacidad estimada: ~1 de cada 3 símbolos suele dibujar.
            var branches = new List<BranchSegment>(expanded.Length / 3 + 8);
            var leaves = new List<LeafAnchor>(expanded.Length / 8 + 8);

            LastStats = LSystemTurtle.Interpret(expanded, species, ref rng, branches, leaves);

            if (branches.Count == 0)
            {
#if UNITY_EDITOR
                UnityEngine.Debug.LogWarning(
                    "[LSystem] '" + species.displayName + "' no dibujó ningún segmento. " +
                    "¿La gramática incluye 'F' en algún sucesor?");
#endif
                return EmptySkeleton(species, seed);
            }

            // ── 3. Ensamblado y metadatos ────────────────────────────────
            var skel = new TreeSkeleton
            {
                branches = branches.ToArray(),
                leaves = leaves.ToArray(),
                speciesId = species.speciesId,
                seed = seed,
                iterations = iterationsReached,
                symbolCount = expanded.Length
            };

            TreeSkeletonUtils.ComputeStrahlerOrders(skel);
            TreeSkeletonUtils.RecalculateMetadata(skel);

            sw.Stop();
            skel.generationMs = (float)sw.Elapsed.TotalMilliseconds;

#if UNITY_EDITOR
            string err;
            if (!skel.Validate(out err))
                UnityEngine.Debug.LogError("[LSystem] contrato violado: " + err);
#endif
            return skel;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Utilidades
        // ═══════════════════════════════════════════════════════════════

        CompiledGrammar GetGrammar(TreeSpecies species)
        {
            int fp = Fingerprint(species);

            CacheEntry entry;
            if (cache.TryGetValue(species, out entry) && entry.fingerprint == fp)
                return entry.grammar;

            CompiledGrammar g;
            string error;
            if (!CompiledGrammar.TryCompile(species, out g, out error))
            {
#if UNITY_EDITOR
                UnityEngine.Debug.LogError(
                    "[LSystem] gramática inválida en '" + species.displayName + "': " + error);
#endif
                cache[species] = new CacheEntry { grammar = null, fingerprint = fp };
                return null;
            }

#if UNITY_EDITOR
            foreach (var w in g.Warnings)
                UnityEngine.Debug.LogWarning("[LSystem] " + species.displayName + ": " + w);
#endif

            cache[species] = new CacheEntry { grammar = g, fingerprint = fp };
            return g;
        }

        /// <summary>
        /// Huella de los campos que afectan la GRAMÁTICA (no la geometría).
        /// Cambiar el ángulo o el jitter no obliga a recompilar; cambiar una
        /// producción sí. Así el tuneo de sliders en el editor sigue siendo fluido.
        /// </summary>
        static int Fingerprint(TreeSpecies sp)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (sp.axiom != null ? sp.axiom.GetHashCode() : 0);
                h = h * 31 + sp.iterations;
                if (sp.rules != null)
                {
                    for (int i = 0; i < sp.rules.Length; i++)
                    {
                        h = h * 31 + sp.rules[i].symbol.GetHashCode();
                        var s = sp.rules[i].successor;
                        h = h * 31 + (s != null ? s.GetHashCode() : 0);
                    }
                }
                return h;
            }
        }

        static TreeSkeleton EmptySkeleton(TreeSpecies sp, int seed)
        {
            var skel = new TreeSkeleton
            {
                branches = new BranchSegment[0],
                leaves = new LeafAnchor[0],
                speciesId = sp != null ? sp.speciesId : -1,
                seed = seed,
                height = 0f
            };
            return skel;
        }

        /// <summary>Fuerza la recompilación de todas las gramáticas.</summary>
        public void ClearCache() { cache.Clear(); }
    }
}