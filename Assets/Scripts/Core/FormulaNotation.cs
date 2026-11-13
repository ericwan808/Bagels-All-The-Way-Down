using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bagels.Core
{
    /// <summary>
    /// The developer-facing text notation for formula patterns (see design.md, "Text notation").
    /// <para>
    /// Explicit form: <c>A B [C] | 0-1 1&lt;2</c>. Nodes, then a bar, then edges between 0-based node indices.
    /// Condensed form (SMILES-like): <c>A1-B-A-1</c>, <c>D&lt;A&gt;D</c>, <c>A(-B)(-C)-D</c>, <c>B-[C]-B</c>.
    /// </para>
    /// <para>
    /// Bonds: <c>a-b</c> is normal adjacency, <c>a&lt;b</c> means a is one level below b, and <c>a&gt;b</c> means b is
    /// one level below a. As a <see cref="PatternEdge"/> (B relative to A) these are Normal, Up and Down.
    /// </para>
    /// </summary>
    public static class FormulaNotation
    {
        /// <summary>A parsed pattern: ingredient nodes and the adjacencies between them.</summary>
        public sealed class Pattern
        {
            public IReadOnlyList<PatternNode> Nodes { get; }
            public IReadOnlyList<PatternEdge> Edges { get; }

            internal Pattern(List<PatternNode> nodes, List<PatternEdge> edges)
            {
                Nodes = nodes;
                Edges = edges;
            }

            public override string ToString() => ToExplicit(Nodes, Edges);
        }

        /// <summary>Parses either form: a string containing <c>|</c> is explicit, anything else condensed.</summary>
        public static Pattern Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            return text.IndexOf('|') >= 0 ? ParseExplicit(text) : new CondensedParser(text).Parse();
        }

        /// <summary>Builds a formula from a notation string.</summary>
        public static Formula ToFormula(string name, Symbol product, string text)
        {
            var p = Parse(text);
            return new Formula(name, product, p.Nodes, p.Edges);
        }

        /// <summary>The explicit form of a formula's pattern, e.g. <c>A B A | 0-1 1-2 0-2</c>.</summary>
        public static string ToExplicit(Formula formula) => ToExplicit(formula.Nodes, formula.Edges);

        public static string ToExplicit(IEnumerable<PatternNode> nodes, IEnumerable<PatternEdge> edges)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(" ", nodes.Select(n => n.IsCatalyst ? $"[{n.Symbol}]" : n.Symbol.ToString())));
            sb.Append(" |");
            foreach (var e in edges) sb.Append(' ').Append(e.A).Append(BondChar(e.Kind)).Append(e.B);
            return sb.ToString();
        }

        /// <summary>
        /// True if two patterns are the same formula: some renumbering of nodes maps one onto the other, keeping
        /// symbols, catalysts and every edge with its direction. Spelling is not unique, so compare with this rather
        /// than with strings.
        /// </summary>
        public static bool Equivalent(Pattern a, Pattern b) => Equivalent(a.Nodes, a.Edges, b.Nodes, b.Edges);

        public static bool Equivalent(IReadOnlyList<PatternNode> nodesA, IReadOnlyList<PatternEdge> edgesA,
                                      IReadOnlyList<PatternNode> nodesB, IReadOnlyList<PatternEdge> edgesB)
        {
            int n = nodesA.Count;
            if (n != nodesB.Count || edgesA.Count != edgesB.Count) return false;
            var setA = EdgeSet(edgesA);
            var setB = EdgeSet(edgesB);
            if (setA.Count != setB.Count) return false;
            var map = new int[n];
            var used = new bool[n];
            for (int i = 0; i < n; i++) map[i] = -1;

            bool Assign(int i)
            {
                if (i == n) return true;
                for (int j = 0; j < n; j++)
                {
                    if (used[j] || nodesA[i].Symbol != nodesB[j].Symbol || nodesA[i].IsCatalyst != nodesB[j].IsCatalyst) continue;
                    map[i] = j;
                    bool ok = true;
                    // Every edge between i and an already-mapped node must exist in B, and vice versa.
                    for (int k = 0; k < i && ok; k++)
                        ok = setA.Contains((k, i, 0)) == setB.Contains(Norm(map[k], j, 0)) &&
                             setA.Contains((k, i, 1)) == setB.Contains(Norm(map[k], j, 1)) &&
                             setA.Contains((k, i, -1)) == setB.Contains(Norm(map[k], j, -1));
                    if (ok)
                    {
                        used[j] = true;
                        if (Assign(i + 1)) return true;
                        used[j] = false;
                    }
                    map[i] = -1;
                }
                return false;
            }
            return Assign(0);
        }

        // Edges as (low, high, d): d = 0 normal, 1 when high is below low, -1 when low is below high.
        static HashSet<(int, int, int)> EdgeSet(IEnumerable<PatternEdge> edges)
        {
            var set = new HashSet<(int, int, int)>();
            foreach (var e in edges)
            {
                int d = e.Kind == AdjacencyKind.Normal ? 0 : e.Kind == AdjacencyKind.Down ? 1 : -1; // B relative to A
                set.Add(Norm(e.A, e.B, d));
            }
            return set;
        }

        static (int, int, int) Norm(int a, int b, int d) => a <= b ? (a, b, d) : (b, a, -d);

        static char BondChar(AdjacencyKind kind) => kind switch
        {
            AdjacencyKind.Normal => '-',
            AdjacencyKind.Up => '<',   // B is Up from A: A is below B
            AdjacencyKind.Down => '>', // B is Down from A: B is below A
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        static bool TryBond(char c, out AdjacencyKind kind)
        {
            switch (c)
            {
                case '-': kind = AdjacencyKind.Normal; return true;
                case '<': kind = AdjacencyKind.Up; return true;
                case '>': kind = AdjacencyKind.Down; return true;
                default: kind = default; return false;
            }
        }

        static Symbol ParseSymbol(string token, string text)
        {
            if (token.Length == 1 && token[0] >= 'A' && token[0] <= 'Z') return (Symbol)(token[0] - 'A');
            throw new FormatException($"'{token}' is not a pastry symbol (A–Z) in \"{text}\".");
        }

        // ------------------------------------------------------------ explicit form

        static Pattern ParseExplicit(string text)
        {
            int bar = text.IndexOf('|');
            if (text.IndexOf('|', bar + 1) >= 0) throw new FormatException($"More than one '|' in \"{text}\".");

            var nodes = new List<PatternNode>();
            foreach (var token in Split(text.Substring(0, bar)))
            {
                bool catalyst = token.Length >= 2 && token[0] == '[' && token[token.Length - 1] == ']';
                var symbol = ParseSymbol(catalyst ? token.Substring(1, token.Length - 2) : token, text);
                nodes.Add(new PatternNode(symbol, catalyst));
            }

            var edges = new List<PatternEdge>();
            foreach (var token in Split(text.Substring(bar + 1)))
            {
                int op = token.IndexOfAny(new[] { '-', '<', '>' });
                if (op <= 0 || op == token.Length - 1 ||
                    !int.TryParse(token.Substring(0, op), out int a) ||
                    !int.TryParse(token.Substring(op + 1), out int b))
                    throw new FormatException($"Bad edge '{token}' in \"{text}\"; expected e.g. 0-1, 0<1 or 0>1.");
                if (a < 0 || a >= nodes.Count || b < 0 || b >= nodes.Count)
                    throw new FormatException($"Edge '{token}' refers to a missing node in \"{text}\".");
                TryBond(token[op], out var kind);
                edges.Add(new PatternEdge(a, b, kind));
            }
            return new Pattern(nodes, edges);
        }

        static IEnumerable<string> Split(string s) =>
            s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

        // ------------------------------------------------------------ condensed form

        /// <summary>
        /// Recursive descent over: chain := atom tail (bond atom tail)*; tail := ring* branch*;
        /// ring := [bond] digit; branch := '(' bond chain ')'; atom := LETTER | '[' LETTER ']'.
        /// </summary>
        sealed class CondensedParser
        {
            readonly string text;
            readonly string s; // text without whitespace
            readonly List<PatternNode> nodes = new List<PatternNode>();
            readonly List<PatternEdge> edges = new List<PatternEdge>();
            readonly int?[] rings = new int?[10]; // digit -> tagged node
            int pos;

            public CondensedParser(string text)
            {
                this.text = text;
                s = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
            }

            public Pattern Parse()
            {
                if (s.Length == 0) throw new FormatException("Empty formula.");
                Chain(null, default);
                if (pos < s.Length) throw Error($"unexpected '{s[pos]}'");
                for (int d = 0; d < rings.Length; d++)
                    if (rings[d] != null) throw new FormatException($"Ring {d} is never closed in \"{text}\".");
                return new Pattern(nodes, edges);
            }

            char Peek(int ahead = 0) => pos + ahead < s.Length ? s[pos + ahead] : '\0';

            FormatException Error(string what) => new FormatException($"Formula \"{text}\": {what} at position {pos}.");

            void Chain(int? prev, AdjacencyKind bond)
            {
                int cur = Atom();
                if (prev is int p) edges.Add(new PatternEdge(p, cur, bond));
                Tail(cur);
                while (TryBond(Peek(), out var next) && !char.IsDigit(Peek(1)))
                {
                    pos++;
                    int atom = Atom();
                    edges.Add(new PatternEdge(cur, atom, next));
                    Tail(atom);
                    cur = atom;
                }
            }

            int Atom()
            {
                bool catalyst = Peek() == '[';
                if (catalyst) pos++;
                char c = Peek();
                if (c < 'A' || c > 'Z') throw Error(c == '\0' ? "expected a pastry symbol but the formula ended" : $"expected a pastry symbol, found '{c}'");
                pos++;
                if (catalyst)
                {
                    if (Peek() != ']') throw Error("expected ']'");
                    pos++;
                }
                nodes.Add(new PatternNode((Symbol)(c - 'A'), catalyst));
                return nodes.Count - 1;
            }

            void Tail(int atom)
            {
                // Ring tags and closures: "1" opens (or closes with a normal bond), "-1", "<1", ">1" close.
                while (char.IsDigit(Peek()) || (TryBond(Peek(), out _) && char.IsDigit(Peek(1))))
                {
                    bool hasBond = TryBond(Peek(), out var bond);
                    if (hasBond) pos++;
                    int d = Peek() - '0';
                    pos++;
                    if (rings[d] is int tagged)
                    {
                        // Read as if the tagged atom were written here: "<current> <bond> <tagged>". A normal bond is
                        // stored with the earlier (tagged) atom first, like a chain bond ("A1-B-A-1" gives 0-1 1-2 0-2);
                        // a vertical bond keeps its written direction ("...F<1" gives 7<0).
                        if (hasBond && bond != AdjacencyKind.Normal) edges.Add(new PatternEdge(atom, tagged, bond));
                        else edges.Add(new PatternEdge(tagged, atom));
                        rings[d] = null;
                    }
                    else
                    {
                        if (hasBond) throw Error($"ring {d} is opened with a bond; put the bond on the closing digit");
                        rings[d] = atom;
                    }
                }

                while (Peek() == '(')
                {
                    pos++;
                    if (!TryBond(Peek(), out var bond)) throw Error("a branch must start with a bond (-, < or >)");
                    pos++;
                    Chain(atom, bond);
                    if (Peek() != ')') throw Error("expected ')'");
                    pos++;
                }
            }
        }
    }
}
