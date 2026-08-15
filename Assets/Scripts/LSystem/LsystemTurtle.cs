// ═══════════════════════════════════════════════════════════════════════════
//  LSystemTurtle.cs — INTERPRETACIÓN GEOMÉTRICA DE LA CADENA
// ═══════════════════════════════════════════════════════════════════════════
//
//  Responsable: [A]
//
//  Traduce la cadena expandida a BranchSegment[] + LeafAnchor[].
//
//  BASE ORTONORMAL (H, L, U) — el corazón del 3D
//  ─────────────────────────────────────────────
//  En 2D bastaba un ángulo theta. En 3D la orientación necesita tres vectores:
//
//      H (heading) : hacia dónde avanza la tortuga
//      L (left)    : su izquierda
//      U (up)      : su "arriba",  U = H × L
//
//  Cada símbolo de giro rota DOS de los tres alrededor del tercero:
//
//      + -   yaw    rota H y L  alrededor de U
//      & ^   pitch  rota H y U  alrededor de L
//      / \   roll   rota L y U  alrededor de H
//
//  Estado inicial: H = +Y (el árbol crece hacia arriba), L = +X, U = H × L.
//
//  DERIVA NUMÉRICA: tras decenas de miles de rotaciones acumuladas, la base
//  deja de ser ortonormal por error de coma flotante y el árbol se deforma
//  o colapsa. Por eso se reortonormaliza cada N giros (ver REORTHO_EVERY).
//
//  ORDEN TOPOLÓGICO: el contrato exige parentIndex < i. Se cumple solo porque
//  el padre de un segmento es siempre un segmento ya emitido (lastBranch),
//  y los segmentos se agregan en orden de dibujo. No romper esa invariante.
//
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;
using Bosque.Contract;

namespace Bosque.LSystem
{
    public static class LSystemTurtle
    {
        /// <summary>Cada cuántas rotaciones se corrige la deriva de la base.</summary>
        const int REORTHO_EVERY = 64;

        /// <summary>Segmentos más cortos que esto se descartan (el contrato los rechaza).</summary>
        const float MIN_SEGMENT_LENGTH = 1e-4f;

        struct State
        {
            public Vector3 pos;
            public Vector3 H, L, U;
            public float length;
            public float radius;
            public int lastBranch;   // índice del último segmento dibujado en esta rama
            public byte depth;
        }

        public struct Stats
        {
            public int drawnSegments;
            public int skippedTinySegments;
            public int orphanLeaves;      // 'L' antes de dibujar nada: se descartan
            public int unbalancedPops;    // ']' sin '[' — no debería ocurrir tras compilar
            public int maxStackDepth;
        }

        /// <summary>
        /// Recorre la cadena y llena las listas. No asigna metadatos: de eso
        /// se encarga LSystemTreeGenerator llamando a TreeSkeletonUtils.
        /// </summary>
        public static Stats Interpret(
            string str,
            TreeSpecies sp,
            ref TreeSkeletonUtils.Rng rng,
            List<BranchSegment> branches,
            List<LeafAnchor> leaves)
        {
            var stats = new Stats();

            var st = new State
            {
                pos = Vector3.zero,
                H = Vector3.up,        // el árbol crece en +Y
                L = Vector3.right,
                U = Vector3.Cross(Vector3.up, Vector3.right),   // = (0,0,-1)
                length = sp.baseLength,
                radius = sp.baseRadius,
                lastBranch = -1,
                depth = 0
            };

            var stack = new Stack<State>(32);
            float angle = sp.angleDeg;      // en grados: AngleAxis los espera así
            int rotationCounter = 0;

            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];

                switch (c)
                {
                    // ── Avanzar dibujando ────────────────────────────────
                    case 'F':
                    case 'S':
                    case '0':
                    case '1':
                    {
                        float len = st.length * rng.Jitter(sp.lengthJitter);
                        Vector3 next = st.pos + st.H * len;

                        if (len < MIN_SEGMENT_LENGTH)
                        {
                            stats.skippedTinySegments++;
                            st.pos = next;      // la tortuga igual se mueve
                            break;
                        }

                        // El radio decae a lo largo del segmento para que el
                        // taper sea continuo y no escalonado entre ramas.
                        float rStart = st.radius;
                        float rEnd = st.radius * Mathf.Lerp(1f, sp.radiusScale, 0.35f);

                        branches.Add(new BranchSegment
                        {
                            start = st.pos,
                            end = next,
                            radiusStart = rStart,
                            radiusEnd = rEnd,
                            orientation = Quaternion.LookRotation(st.H, st.U),
                            parentIndex = st.lastBranch,
                            depth = st.depth,
                            order = 0            // lo calcula Strahler después
                        });

                        st.lastBranch = branches.Count - 1;
                        st.radius = rEnd;
                        st.pos = next;
                        stats.drawnSegments++;
                        break;
                    }

                    // ── Avanzar sin dibujar ──────────────────────────────
                    case 'f':
                        st.pos = st.pos + st.H * st.length;
                        break;

                    // ── Hoja ─────────────────────────────────────────────
                    case 'L':
                    {
                        if (st.lastBranch < 0) { stats.orphanLeaves++; break; }
                        if (rng.NextFloat() > sp.leafDensity) break;

                        leaves.Add(new LeafAnchor
                        {
                            position = st.pos,
                            // El contrato define forward = normal de la lámina.
                            // La normal es U (perpendicular a la rama); el "up"
                            // de la hoja corre a lo largo de la rama (H).
                            orientation = Quaternion.LookRotation(st.U, st.H),
                            scale = sp.leafScale * rng.Range(0.75f, 1.25f),
                            branchIndex = st.lastBranch
                        });
                        break;
                    }

                    // ── Rotaciones ───────────────────────────────────────
                    case '+': Yaw(ref st,  angle * rng.Jitter(sp.angleJitter)); rotationCounter++; break;
                    case '-': Yaw(ref st, -angle * rng.Jitter(sp.angleJitter)); rotationCounter++; break;
                    case '&': Pitch(ref st,  angle * rng.Jitter(sp.angleJitter)); rotationCounter++; break;
                    case '^': Pitch(ref st, -angle * rng.Jitter(sp.angleJitter)); rotationCounter++; break;
                    case '/': Roll(ref st,  angle * rng.Jitter(sp.angleJitter)); rotationCounter++; break;
                    case '\\': Roll(ref st, -angle * rng.Jitter(sp.angleJitter)); rotationCounter++; break;

                    case '|': Yaw(ref st, 180f); rotationCounter++; break;

                    // Nivelar: gira sobre H hasta que L quede horizontal.
                    // Evita que las ramas queden "torcidas" tras muchos rolls.
                    case '$':
                    {
                        Vector3 l = Vector3.Cross(Vector3.up, st.H);
                        if (l.sqrMagnitude > 1e-8f)
                        {
                            st.L = l.normalized;
                            st.U = Vector3.Cross(st.H, st.L);
                        }
                        break;
                    }

                    // ── Grosor ───────────────────────────────────────────
                    case '!':
                        st.radius *= sp.pinchFactor;
                        break;

                    // ── Ramificación ─────────────────────────────────────
                    case '[':
                        stack.Push(st);
                        if (stack.Count > stats.maxStackDepth) stats.maxStackDepth = stack.Count;
                        st.length *= sp.lengthScale;
                        st.radius *= sp.radiusScale;
                        if (st.depth < 255) st.depth++;
                        break;

                    case ']':
                        if (stack.Count > 0) st = stack.Pop();
                        else stats.unbalancedPops++;
                        break;

                    // Cualquier otro símbolo es una variable de la gramática
                    // que sobrevivió a la última iteración (A, X, B...).
                    // No tiene efecto geométrico: se ignora.
                    default:
                        break;
                }

                if (rotationCounter >= REORTHO_EVERY)
                {
                    Orthonormalize(ref st);
                    rotationCounter = 0;
                }
            }

            return stats;
        }

        // ═══════════════════════════════════════════════════════════════
        //  ROTACIONES
        // ═══════════════════════════════════════════════════════════════

        static void Yaw(ref State st, float deg)      // alrededor de U
        {
            var q = Quaternion.AngleAxis(deg, st.U);
            st.H = q * st.H;
            st.L = q * st.L;
        }

        static void Pitch(ref State st, float deg)    // alrededor de L
        {
            var q = Quaternion.AngleAxis(deg, st.L);
            st.H = q * st.H;
            st.U = q * st.U;
        }

        static void Roll(ref State st, float deg)     // alrededor de H
        {
            var q = Quaternion.AngleAxis(deg, st.H);
            st.L = q * st.L;
            st.U = q * st.U;
        }

        /// <summary>
        /// Gram-Schmidt: corrige la deriva acumulada. H manda; L se le hace
        /// perpendicular y U se reconstruye con el producto cruz.
        /// </summary>
        static void Orthonormalize(ref State st)
        {
            st.H = st.H.normalized;

            Vector3 l = st.L - st.H * Vector3.Dot(st.L, st.H);
            if (l.sqrMagnitude < 1e-8f)
            {
                // L quedó paralelo a H: elegir cualquier perpendicular estable
                l = Vector3.Cross(st.H, Vector3.up);
                if (l.sqrMagnitude < 1e-8f) l = Vector3.Cross(st.H, Vector3.right);
            }

            st.L = l.normalized;
            st.U = Vector3.Cross(st.H, st.L);
        }
    }
}