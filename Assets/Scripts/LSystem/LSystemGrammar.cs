// ═══════════════════════════════════════════════════════════════════════════
//  LSystemGrammar.cs — COMPILACIÓN Y EXPANSIÓN DE GRAMÁTICAS
// ═══════════════════════════════════════════════════════════════════════════
//
//  Responsable: [A]
//
//  Dos etapas separadas a propósito:
//
//    1. CompiledGrammar.TryCompile(species)  ← valida y precompila UNA vez
//    2. grammar.Expand(iterations, ref rng)  ← se ejecuta por cada árbol
//
//  La separación importa: validar la gramática (paréntesis balanceados,
//  símbolos desconocidos, pesos mal escritos) es caro y solo hay que hacerlo
//  cuando la especie cambia, no 5000 veces al poblar el bosque.
//
//  SINTAXIS DE SUCESORES
//  ─────────────────────
//    Determinista:   F[+F]F[-F]
//    Estocástica:    0.4:F[+F]  |  0.6:F[-F][+F]
//
//  Las alternativas se separan con '|' y el peso va antes de ':'. Los pesos
//  se normalizan solos, así que 1:A | 3:B es válido (25% / 75%).
//  Esto da variación de TOPOLOGÍA (árboles con distinta estructura de ramas),
//  que es distinto del jitter de TreeSpecies, que solo varía la GEOMETRÍA
//  (ángulos y longitudes) sobre una topología fija.
//
//  Nota: la sintaxis estocástica vive dentro del string `successor`, así que
//  NO requiere tocar el contrato. ProductionRule sigue igual que en v1.0.
//
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Bosque.Contract;

namespace Bosque.LSystem
{
    public sealed class CompiledGrammar
    {
        // ── Alfabeto que la tortuga sabe interpretar ─────────────────────
        //  Dibujo:      F S 0 1   (avanzar dibujando)
        //               f         (avanzar sin dibujar)
        //               L         (colocar hoja)
        //  Rotación:    + -       yaw   (izquierda / derecha, alrededor de U)
        //               & ^       pitch (abajo / arriba,      alrededor de L)
        //               / \       roll  (giro sobre el propio eje H)
        //               |         media vuelta (180°)
        //               $         nivelar: rota hasta que L quede horizontal
        //  Grosor:      !         adelgazar por pinchFactor
        //  Ramificación:[ ]       apilar / desapilar estado
        public const string TURTLE_SYMBOLS = "FS01fL+-&^/\\|$![]";

        /// <summary>Tope de seguridad: el crecimiento es exponencial.</summary>
        public const int MAX_SYMBOLS = 2_000_000;

        struct Choice
        {
            public float cumulative;   // peso acumulado normalizado (para ruleta)
            public string text;
        }

        readonly Dictionary<char, Choice[]> table;
        readonly string axiom;

        public string Axiom { get { return axiom; } }
        public bool IsStochastic { get; private set; }
        public string[] Warnings { get; private set; }

        CompiledGrammar(string axiom, Dictionary<char, Choice[]> table,
                        bool stochastic, string[] warnings)
        {
            this.axiom = axiom;
            this.table = table;
            IsStochastic = stochastic;
            Warnings = warnings;
        }

        // ═══════════════════════════════════════════════════════════════
        //  COMPILACIÓN
        // ═══════════════════════════════════════════════════════════════

        public static bool TryCompile(TreeSpecies species,
                                      out CompiledGrammar grammar,
                                      out string error)
        {
            grammar = null;
            error = null;

            if (species == null) { error = "TreeSpecies es null."; return false; }

            string ax = Strip(species.axiom);
            if (string.IsNullOrEmpty(ax)) { error = "El axioma está vacío."; return false; }

            var table = new Dictionary<char, Choice[]>();
            var warnings = new List<string>();
            bool stochastic = false;

            // ── Parsear cada producción ──────────────────────────────────
            if (species.rules != null)
            {
                for (int i = 0; i < species.rules.Length; i++)
                {
                    char sym = species.rules[i].symbol;
                    string raw = species.rules[i].successor;

                    if (sym == '\0' || char.IsWhiteSpace(sym))
                    { error = "Regla " + i + ": el símbolo está vacío."; return false; }

                    if (sym == '[' || sym == ']')
                    { error = "Regla " + i + ": no se puede reescribir '[' ni ']'."; return false; }

                    if (table.ContainsKey(sym))
                    { error = "Regla " + i + ": el símbolo '" + sym + "' está definido dos veces."; return false; }

                    Choice[] choices;
                    if (!ParseSuccessor(raw, out choices, out error))
                    { error = "Regla " + i + " ('" + sym + "'): " + error; return false; }

                    if (choices.Length > 1) stochastic = true;
                    table[sym] = choices;
                }
            }

            // ── Validar balance de corchetes ─────────────────────────────
            if (!BracketsBalanced(ax))
            { error = "Corchetes desbalanceados en el axioma."; return false; }

            foreach (var kv in table)
                foreach (var ch in kv.Value)
                    if (!BracketsBalanced(ch.text))
                    { error = "Corchetes desbalanceados en el sucesor de '" + kv.Key + "'."; return false; }

            // ── Avisar de símbolos que no hacen nada ─────────────────────
            //  Un símbolo que no es de tortuga y no tiene regla es tierra
            //  muerta: casi siempre es un typo (una 'x' donde iba una 'X').
            CollectUnknownSymbols(ax, table, "axioma", warnings);
            foreach (var kv in table)
                foreach (var ch in kv.Value)
                    CollectUnknownSymbols(ch.text, table, "sucesor de '" + kv.Key + "'", warnings);

            grammar = new CompiledGrammar(ax, table, stochastic, warnings.ToArray());
            return true;
        }

        static bool ParseSuccessor(string raw, out Choice[] choices, out string error)
        {
            choices = null;
            error = null;

            if (raw == null) raw = "";

            string[] parts = raw.Split('|');
            var list = new List<Choice>(parts.Length);
            var weights = new List<float>(parts.Length);
            float total = 0f;

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                float w = 1f;
                string text;

                int colon = part.IndexOf(':');
                if (colon >= 0)
                {
                    string wStr = part.Substring(0, colon).Trim();
                    if (!float.TryParse(wStr, System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out w))
                    { error = "peso no numérico: '" + wStr + "'"; return false; }

                    if (w <= 0f) { error = "peso debe ser > 0 (recibido " + w + ")"; return false; }
                    text = part.Substring(colon + 1);
                }
                else text = part;

                text = Strip(text);
                // Un sucesor vacío es legal: significa "borrar este símbolo".
                list.Add(new Choice { text = text });
                weights.Add(w);
                total += w;
            }

            // Normalizar a acumulado en [0,1] para selección por ruleta
            var arr = new Choice[list.Count];
            float acc = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                acc += weights[i] / total;
                arr[i] = new Choice { text = list[i].text, cumulative = acc };
            }
            arr[arr.Length - 1].cumulative = 1f;   // blindar contra error de redondeo

            choices = arr;
            return true;
        }

        static string Strip(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
                if (!char.IsWhiteSpace(s[i])) sb.Append(s[i]);
            return sb.ToString();
        }

        static bool BracketsBalanced(string s)
        {
            int d = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '[') d++;
                else if (s[i] == ']') { d--; if (d < 0) return false; }
            }
            return d == 0;
        }

        static void CollectUnknownSymbols(string s, Dictionary<char, Choice[]> table,
                                          string where, List<string> warnings)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (TURTLE_SYMBOLS.IndexOf(c) >= 0) continue;
                if (table.ContainsKey(c)) continue;

                string msg = "Símbolo '" + c + "' en " + where +
                             " no es de tortuga ni tiene regla: será ignorado.";
                if (!warnings.Contains(msg)) warnings.Add(msg);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  EXPANSIÓN
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Aplica las producciones en paralelo, `iterations` veces.
        /// El rng solo se consume si la gramática es estocástica, así que
        /// una gramática determinista da el mismo string sin importar el seed.
        /// </summary>
        public string Expand(int iterations, ref TreeSkeletonUtils.Rng rng,
                             out int iterationsReached, out bool capped)
        {
            string current = axiom;
            iterationsReached = 0;
            capped = false;

            for (int it = 0; it < iterations; it++)
            {
                // Estimar el tamaño de salida evita decenas de realloc del
                // StringBuilder cuando la cadena pasa de miles a millones.
                var sb = new StringBuilder(current.Length * 2 + 16);
                bool overflow = false;

                for (int i = 0; i < current.Length; i++)
                {
                    char c = current[i];
                    Choice[] choices;

                    if (table.TryGetValue(c, out choices))
                        sb.Append(Pick(choices, ref rng));
                    else
                        sb.Append(c);

                    if (sb.Length > MAX_SYMBOLS) { overflow = true; break; }
                }

                if (overflow)
                {
                    capped = true;
                    break;      // se devuelve la última iteración completa
                }

                current = sb.ToString();
                iterationsReached = it + 1;
            }

            return current;
        }

        string Pick(Choice[] choices, ref TreeSkeletonUtils.Rng rng)
        {
            if (choices.Length == 1) return choices[0].text;

            float r = rng.NextFloat();
            for (int i = 0; i < choices.Length; i++)
                if (r <= choices[i].cumulative) return choices[i].text;

            return choices[choices.Length - 1].text;
        }

        /// <summary>
        /// Devuelve el historial completo de expansión. Solo para depurar y
        /// para las capturas del informe — NO usar en el bosque, guarda todas
        /// las cadenas intermedias en memoria.
        /// </summary>
        public List<string> ExpandWithHistory(int iterations, ref TreeSkeletonUtils.Rng rng)
        {
            var history = new List<string> { axiom };
            string current = axiom;

            for (int it = 0; it < iterations; it++)
            {
                var sb = new StringBuilder(current.Length * 2 + 16);
                for (int i = 0; i < current.Length; i++)
                {
                    Choice[] choices;
                    if (table.TryGetValue(current[i], out choices))
                        sb.Append(Pick(choices, ref rng));
                    else
                        sb.Append(current[i]);

                    if (sb.Length > MAX_SYMBOLS) return history;
                }
                current = sb.ToString();
                history.Add(current);
            }
            return history;
        }
    }
}