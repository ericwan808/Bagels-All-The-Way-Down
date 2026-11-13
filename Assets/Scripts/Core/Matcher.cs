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
    /// board/slot order. The per-formula search order is precomputed (<see cref="MatchPlan"/>).
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
            Element[] mapping = null;
            foreach (var plan in formula.Plans)
            {
                if (nodes[plan.Start].Symbol != trigger.Symbol) continue;
                if (mapping == null) mapping = new Element[nodes.Count];
                else System.Array.Clear(mapping, 0, mapping.Length);
                mapping[plan.Start] = trigger;
                if (Extend(formula, plan, mapping, 1))
                {
                    match = new Match(formula, trigger, mapping);
                    return true;
                }
            }
            match = null;
            return false;
        }

        static bool Extend(Formula formula, MatchPlan plan, Element[] mapping, int k)
        {
            if (k == plan.Order.Length) return true;
            int node = plan.Order[k];
            var wanted = formula.Nodes[node].Symbol;

            foreach (var candidate in Adjacency.Neighbors(mapping[plan.Anchor[node]], plan.AnchorKind[node]))
            {
                if (candidate.Symbol != wanted || IsUsed(mapping, candidate)) continue;
                mapping[node] = candidate;
                if (EdgesHold(plan.Checks[k], mapping) && Extend(formula, plan, mapping, k + 1)) return true;
                mapping[node] = null;
            }
            return false;
        }

        static bool IsUsed(Element[] mapping, Element element)
        {
            foreach (var m in mapping)
                if (m == element) return true;
            return false;
        }

        static bool EdgesHold(PatternEdge[] checks, Element[] mapping)
        {
            foreach (var e in checks)
                if (!Adjacency.AreAdjacent(mapping[e.A], mapping[e.B], e.Kind)) return false;
            return true;
        }
    }
}
