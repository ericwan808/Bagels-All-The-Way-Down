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

        public override string ToString() => $"{Name}: {string.Join("+", Nodes)} -> {Product}";
    }
}
