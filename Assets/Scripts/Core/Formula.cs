using System;
using System.Collections.Generic;
using System.Linq;

namespace Bagels.Core
{
    public readonly struct PatternNode
    {
        public Symbol Symbol { get; }

        /// <summary>A catalyst is only consumed when it is the element that triggered the formula.</summary>
        public bool IsCatalyst { get; }

        public PatternNode(Symbol symbol, bool isCatalyst = false)
        {
            Symbol = symbol;
            IsCatalyst = isCatalyst;
        }

        public override string ToString() => IsCatalyst ? $"{Symbol}*" : Symbol.ToString();
    }

    /// <summary>Requires node <see cref="B"/> to be <see cref="Kind"/>-adjacent to node <see cref="A"/>
    /// (e.g. Down means B is one level below A).</summary>
    public readonly struct PatternEdge
    {
        public int A { get; }
        public int B { get; }
        public AdjacencyKind Kind { get; }

        public PatternEdge(int a, int b, AdjacencyKind kind = AdjacencyKind.Normal)
        {
            A = a;
            B = b;
            Kind = kind;
        }
    }

    /// <summary>A recipe: a connected pattern of ingredients that, once present, is replaced by <see cref="Product"/>.</summary>
    public sealed class Formula
    {
        public string Name { get; }
        public IReadOnlyList<PatternNode> Nodes { get; }
        public IReadOnlyList<PatternEdge> Edges { get; }
        public Symbol Product { get; }

        public Formula(string name, Symbol product, IEnumerable<PatternNode> nodes, IEnumerable<PatternEdge> edges)
        {
            Name = name;
            Product = product;
            Nodes = nodes.ToArray();
            Edges = edges.ToArray();
            Validate();
            Plans = Enumerable.Range(0, Nodes.Count).Select(start => new MatchPlan(this, start)).ToArray();
        }

        /// <summary>Search order for the matcher, one per possible start node. Built once per formula.</summary>
        internal MatchPlan[] Plans { get; }

        /// <summary>True if any ingredient has this symbol, i.e. an element of it could trigger the formula.</summary>
        public bool Uses(Symbol symbol)
        {
            foreach (var n in Nodes)
                if (n.Symbol == symbol) return true;
            return false;
        }

        void Validate()
        {
            int n = Nodes.Count;
            if (n < 2) throw new ArgumentException($"Formula '{Name}' needs at least two ingredients.");
            foreach (var e in Edges)
                if (e.A < 0 || e.A >= n || e.B < 0 || e.B >= n || e.A == e.B)
                    throw new ArgumentException($"Formula '{Name}' has an invalid edge ({e.A},{e.B}).");

            // Connected: otherwise ingredients could not all be reached from the trigger.
            var seen = new bool[n];
            var stack = new Stack<int>();
            stack.Push(0);
            seen[0] = true;
            while (stack.Count > 0)
            {
                int v = stack.Pop();
                foreach (var e in Edges)
                {
                    int other = e.A == v ? e.B : e.B == v ? e.A : -1;
                    if (other >= 0 && !seen[other]) { seen[other] = true; stack.Push(other); }
                }
            }
            if (seen.Contains(false)) throw new ArgumentException($"Formula '{Name}' pattern is not connected.");
        }

        public override string ToString() => $"{Name}: {FormulaNotation.ToExplicit(this)} -> {Product}";
    }

    /// <summary>
    /// How to search for a formula starting from one node: the other nodes in BFS order, each with the
    /// already-placed node it is reached from (its anchor) and the edges back to earlier nodes that must be checked.
    /// </summary>
    internal sealed class MatchPlan
    {
        public readonly int Start;
        public readonly int[] Order;                  // Order[0] == Start
        public readonly int[] Anchor;                 // by node
        public readonly AdjacencyKind[] AnchorKind;   // node relative to its anchor
        public readonly PatternEdge[][] Checks;       // by step: edges to earlier nodes other than the anchor edge

        public MatchPlan(Formula formula, int start)
        {
            int n = formula.Nodes.Count;
            var edges = formula.Edges;
            Start = start;
            Anchor = new int[n];
            AnchorKind = new AdjacencyKind[n];
            var order = new List<int> { start };
            var queued = new bool[n];
            queued[start] = true;
            for (int i = 0; i < order.Count; i++)
            {
                int v = order[i];
                foreach (var e in edges)
                {
                    int other;
                    AdjacencyKind kind;
                    if (e.A == v) { other = e.B; kind = e.Kind; }
                    else if (e.B == v) { other = e.A; kind = Adjacency.Inverse(e.Kind); }
                    else continue;
                    if (queued[other]) continue;
                    queued[other] = true;
                    Anchor[other] = v;
                    AnchorKind[other] = kind;
                    order.Add(other);
                }
            }
            Order = order.ToArray();

            var position = new int[n];
            for (int i = 0; i < n; i++) position[Order[i]] = i;
            Checks = new PatternEdge[n][];
            Checks[0] = Array.Empty<PatternEdge>();
            for (int k = 1; k < n; k++)
            {
                int node = Order[k];
                bool anchorEdgeSkipped = false;
                var list = new List<PatternEdge>();
                foreach (var e in edges)
                {
                    int other = e.A == node ? e.B : e.B == node ? e.A : -1;
                    if (other < 0 || position[other] >= k) continue;
                    // The candidate came from the anchor's neighbours, so one anchor edge of that kind holds already.
                    if (!anchorEdgeSkipped && other == Anchor[node] &&
                        (e.A == other ? e.Kind : Adjacency.Inverse(e.Kind)) == AnchorKind[node])
                    {
                        anchorEdgeSkipped = true;
                        continue;
                    }
                    list.Add(e);
                }
                Checks[k] = list.ToArray();
            }
        }
    }
}
