// ═══════════════════════════════════════════════════════════════════════════
//  FakeTreeGenerator.cs — ANDAMIO TEMPORAL
// ═══════════════════════════════════════════════════════════════════════════
//
//  Genera árboles con ramificación recursiva simple. NO es un L-system:
//  no hay gramática, ni cadena, ni tortuga. Su único propósito es entregar
//  TreeSkeleton válidos desde el día 2 para que [B] pueda construir el
//  mesher, el instancing y el LOD sin esperar al motor real.
//
//  Cumple el contrato en todo lo que importa:
//    · Es determinista: mismo (species, seed) => mismo árbol.
//    · Respeta el orden topológico (padre siempre antes que hijo).
//    · Rellena metadatos y pasa Validate().
//
//  BORRAR cuando LSystemTreeGenerator esté funcionando. Como ambos
//  implementan ITreeGenerator, el cambio es de una línea en el lado de [B].
//
//  Responsable: [A]   ·   Consumidor: [B]
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Bosque.Contract;

namespace Bosque.Generation
{
    public class FakeTreeGenerator : ITreeGenerator
    {
        // Cuántas ramas hijas salen de cada punta. El L-system real lo
        // decidirá con la gramática; aquí es fijo.
        const int CHILDREN_PER_TIP = 3;

        public TreeSkeleton Generate(TreeSpecies species, int seed)
        {
            var sw = Stopwatch.StartNew();

            var rng = new TreeSkeletonUtils.Rng(seed ^ (species.speciesId * 7919));
            var branches = new List<BranchSegment>(512);
            var leaves = new List<LeafAnchor>(512);

            // ── Tronco: primer segmento, apuntando hacia arriba ──────────
            var trunkDir = Vector3.up;
            var trunkStart = Vector3.zero;
            float trunkLen = species.baseLength * rng.Jitter(species.lengthJitter);
            var trunkEnd = trunkStart + trunkDir * trunkLen;

            branches.Add(new BranchSegment
            {
                start = trunkStart,
                end = trunkEnd,
                radiusStart = species.baseRadius,
                radiusEnd = species.baseRadius * species.radiusScale,
                orientation = Quaternion.LookRotation(trunkDir, Vector3.forward),
                parentIndex = -1,
                depth = 0,
                order = 0   // lo calcula ComputeStrahlerOrders al final
            });

            // ── Ramificación recursiva ───────────────────────────────────
            Branch(
                branches, leaves, ref rng, species,
                parentIndex: 0,
                origin: trunkEnd,
                dir: trunkDir,
                up: Vector3.forward,
                length: trunkLen * species.lengthScale,
                radius: species.baseRadius * species.radiusScale,
                depth: 1,
                remaining: Mathf.Clamp(species.iterations, 1, 6)
            );

            // ── Ensamblar y rellenar metadatos ───────────────────────────
            var skel = new TreeSkeleton
            {
                branches = branches.ToArray(),
                leaves = leaves.ToArray(),
                speciesId = species.speciesId,
                seed = seed,
                iterations = species.iterations,
                symbolCount = 0   // el fake no tiene cadena; el real sí
            };

            TreeSkeletonUtils.ComputeStrahlerOrders(skel);
            TreeSkeletonUtils.RecalculateMetadata(skel);

            sw.Stop();
            skel.generationMs = (float)sw.Elapsed.TotalMilliseconds;

#if UNITY_EDITOR
            if (!skel.Validate(out string err))
                UnityEngine.Debug.LogError($"[FakeTreeGenerator] contrato violado: {err}");
#endif
            return skel;
        }

        // ─────────────────────────────────────────────────────────────────
        void Branch(
            List<BranchSegment> branches,
            List<LeafAnchor> leaves,
            ref TreeSkeletonUtils.Rng rng,
            TreeSpecies species,
            int parentIndex,
            Vector3 origin,
            Vector3 dir,
            Vector3 up,
            float length,
            float radius,
            int depth,
            int remaining)
        {
            if (remaining <= 0 || length < 0.02f) return;

            float angle = species.angleDeg * Mathf.Deg2Rad;

            for (int c = 0; c < CHILDREN_PER_TIP; c++)
            {
                // Reparte las hijas alrededor del eje del padre (roll)
                float roll = (c / (float)CHILDREN_PER_TIP) * Mathf.PI * 2f
                             + rng.Range(-0.3f, 0.3f);

                float pitch = angle * rng.Jitter(species.angleJitter);

                // Inclinar respecto al padre y luego rotar alrededor de él
                var side = Vector3.Cross(dir, up).normalized;
                var childDir = Quaternion.AngleAxis(pitch * Mathf.Rad2Deg, side) * dir;
                childDir = Quaternion.AngleAxis(roll * Mathf.Rad2Deg, dir) * childDir;
                childDir.Normalize();

                float childLen = length * rng.Jitter(species.lengthJitter);
                float childRadEnd = radius * species.radiusScale;
                var childEnd = origin + childDir * childLen;

                var childUp = Vector3.Cross(childDir, side).normalized;
                if (childUp.sqrMagnitude < 1e-6f) childUp = Vector3.forward;

                int myIndex = branches.Count;
                branches.Add(new BranchSegment
                {
                    start = origin,
                    end = childEnd,
                    radiusStart = radius,
                    radiusEnd = childRadEnd,
                    orientation = Quaternion.LookRotation(childDir, childUp),
                    parentIndex = parentIndex,
                    depth = (byte)Mathf.Min(depth, 255),
                    order = 0
                });

                bool isTip = (remaining - 1 <= 0) || (childLen * species.lengthScale < 0.02f);

                if (isTip && rng.NextFloat() <= species.leafDensity)
                {
                    leaves.Add(new LeafAnchor
                    {
                        position = childEnd,
                        // El contrato define forward = NORMAL DE LA LÁMINA.
                        // childUp es perpendicular a la rama (equivale al
                        // vector U de la tortuga), así que va como forward;
                        // childDir corre a lo largo de la rama y va como up.
                        // Invertir estos dos deja las hojas de canto.
                        orientation = Quaternion.LookRotation(childUp, childDir),
                        scale = species.leafScale * rng.Range(0.8f, 1.2f),
                        branchIndex = myIndex
                    });
                }

                Branch(
                    branches, leaves, ref rng, species,
                    parentIndex: myIndex,
                    origin: childEnd,
                    dir: childDir,
                    up: childUp,
                    length: childLen * species.lengthScale,
                    radius: childRadEnd,
                    depth: depth + 1,
                    remaining: remaining - 1
                );
            }
        }
    }
}