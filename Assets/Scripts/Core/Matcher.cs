using System.Collections.Generic;

namespace Bagels.Core
{
    /// <summary>A formula found on the board: <see cref="Elements"/>[i] fills pattern node i.</summary>
    public sealed class Match
    {
        public Formula Formula { get; }
        public Element Trigger { get; }
        public IReadOnlyList<Element> Elements { get; }

        internal Match(Formula formula, Element trigger, Element[] elements)
        {
            Formula = formula;
            Trigger = trigger;
            Elements = elements;
        }
    }

    /// <summary>
    /// Finds formulas involving a trigger element. A match maps each pattern node to a distinct element so that
    /// every pattern edge holds with its kind and direction; extra adjacencies on the board are fine.
    /// Search order is deterministic: formulas in list order, start nodes in index order, candidates in
    /// board/slot order.
    /// </summary>
    public static class Matcher
    {
        public static bool TryMatch(Element trigger, IEnumerable<Formula> formulas, out Match match)
        {
            foreach (var formula in formulas)
                if (TryMatch(trigger, formula, out match)) return true;
            match = null;
            return false;
        }

        public static bool TryMatch(Element trigger, Formula formula, out Match match)
        {
            var nodes = formula.Nodes;
            for (int start = 0; start < nodes.Count; start++)
            {
                if (nodes[start].Symbol != trigger.Symbol) continue;
                var search = new Search(formula, start);
                search.Mapping[start] = trigger;
                if (search.Extend(1))
                {
                    match = new Match(formula, trigger, search.Mapping);
                    return true;
                }
            }
            match = null;
            return false;
        }

        sealed class Search
        {
            readonly Formula formula;
            readonly List<int> order = new List<int>();   // nodes in BFS order from the start node
            readonly int[] anchor;                        // already-ordered node each node is reached from
            readonly AdjacencyKind[] anchorKind;          // kind of node relative to its anchor
            public readonly Element[] Mapping;

            public Search(Formula formula, int start)
            {
                this.formula = formula;
                int n = formula.Nodes.Count;
                Mapping = new Element[n];
                anchor = new int[n];
                anchorKind = new AdjacencyKind[n];

                var queued = new bool[n];
                order.Add(start);
                queued[start] = true;
                for (int i = 0; i < order.Count; i++)
                {
                    int v = order[i];
                    foreach (var e in formula.Edges)
                    {
                        int other;
                        AdjacencyKind kind;
                        if (e.A == v) { other = e.B; kind = e.Kind; }
                        else if (e.B == v) { other = e.A; kind = Adjacency.Inverse(e.Kind); }
                        else continue;
                        if (queued[other]) continue;
                        queued[other] = true;
                        anchor[other] = v;
                        anchorKind[other] = kind;
                        order.Add(other);
                    }
                }
            }

            public bool Extend(int k)
            {
                if (k == order.Count) return true;
                int node = order[k];
                var wanted = formula.Nodes[node].Symbol;

                foreach (var candidate in Adjacency.Neighbors(Mapping[anchor[node]], anchorKind[node]))
                {
                    if (candidate.Symbol != wanted || IsUsed(candidate)) continue;
                    Mapping[node] = candidate;
                    if (EdgesHold(node) && Extend(k + 1)) return true;
                    Mapping[node] = null;
                }
                return false;
            }

            bool IsUsed(Element element)
            {
                foreach (var m in Mapping)
                    if (m == element) return true;
                return false;
            }

            // Every edge between this node and an already-mapped node must exist on the board.
            bool EdgesHold(int node)
            {
                foreach (var e in formula.Edges)
                {
                    if (e.A != node && e.B != node) continue;
                    var a = Mapping[e.A];
                    var b = Mapping[e.B];
                    if (a == null || b == null) continue;
                    if (!Adjacency.AreAdjacent(a, b, e.Kind)) return false;
                }
                return true;
            }
        }
    }
}
