using System.Collections.Generic;
using System.Linq;
using Bagels.Core;
using Bagels.Data;
using UnityEngine;

namespace Bagels.View
{
    /// <summary>
    /// Draws a formula's ingredient pattern: a line for normal adjacency, an arrow from the upper ingredient to the
    /// lower one for vertical adjacency, and a gold ring around catalysts. Vertical edges put the lower ingredient
    /// on a lower row.
    /// </summary>
    public static class FormulaDiagram
    {
        public static void Build(RectTransform box, FormulaAsset formula, GameView view, float iconSize)
        {
            var nodes = formula.Ingredients;
            var links = formula.Links;
            // The target's formula (L) is a deep tree, drawn top-down like the sketch at the bottom of design.md.
            bool target = view.Level != null && formula.Product == view.Level.Target;
            var pos = target ? Layered(nodes.Length, links, iconSize)
                             : Positions(nodes.Length, links, box.rect.size, iconSize);
            iconSize = FitToBox(pos, box.rect.size, iconSize);

            var lineColor = ViewColors.Paper;
            float trim = iconSize * 0.5f + 3f;
            float line = Mathf.Clamp(iconSize * 0.1f, 2f, 4f);
            float head = Mathf.Clamp(iconSize * 0.42f, 8f, 16f);
            foreach (var l in links)
            {
                // Arrows always point from the upper ingredient to the lower one.
                int upper = l.from, lower = l.to;
                if (l.kind == AdjacencyKind.Up) (upper, lower) = (lower, upper);
                var a = pos[upper];
                var b = pos[lower];
                var dir = (b - a).normalized;
                if (l.kind == AdjacencyKind.Normal)
                    UiFactory.Line(box, a + dir * trim, b - dir * trim, line, lineColor);
                else
                    UiFactory.Arrow(box, a + dir * trim, b - dir * trim, line, head, view.TriangleSprite, lineColor);
            }

            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].catalyst)
                {
                    var ring = UiFactory.Image("Catalyst", box, view.RingSprite, view.CatalystColor,
                                               Vector2.one * iconSize * 1.37f);
                    ring.rectTransform.anchoredPosition = pos[i];
                }
                var icon = UiFactory.PastryIcon(box, nodes[i].type, iconSize, view);
                icon.anchoredPosition = pos[i];
            }
        }

        /// <summary>
        /// Shrinks the layout (positions and icons together) until it fits the box, centring it. Big formulas such as
        /// K and L would otherwise spill out of their row. Returns the new icon size.
        /// </summary>
        static float FitToBox(Vector2[] pos, Vector2 box, float iconSize)
        {
            if (pos.Length == 0) return iconSize;
            var min = pos[0];
            var max = pos[0];
            foreach (var p in pos) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            var span = max - min + Vector2.one * iconSize;
            float scale = Mathf.Min(1f, box.x / Mathf.Max(1f, span.x), box.y / Mathf.Max(1f, span.y));
            var center = (min + max) * 0.5f;
            for (int i = 0; i < pos.Length; i++) pos[i] = (pos[i] - center) * scale;
            return iconSize * scale;
        }

        /// <summary>How many rows (vertical levels) the formula's diagram uses.</summary>
        public static int Rows(FormulaAsset formula)
        {
            var level = Levels(formula.Ingredients.Length, formula.Links);
            return level.Length > 0 ? level.Max() + 1 : 1;
        }

        /// <summary>
        /// Lays nodes out in rows by vertical level. On a single row, three mutually adjacent nodes form a triangle
        /// and a hub linked to every other node forms a star.
        /// </summary>
        static Vector2[] Positions(int count, FormulaAsset.Link[] links, Vector2 box, float iconSize)
        {
            if (TryLadder(count, links, iconSize, out var ladder)) return ladder;
            var level = Levels(count, links);
            int levels = count > 0 ? level.Max() + 1 : 1;

            // Full spacing even when it overflows: FitToBox then shrinks everything evenly, so arrows stay visible.
            float rowGap = levels > 1 ? iconSize * 1.5f : 0f;
            // Wider columns when there are rows, so diagonal arrows have room for a visible shaft.
            float colGap = iconSize * (levels > 1 ? 2.8f : 1.9f);
            var pos = new Vector2[count];
            for (int row = 0; row < levels; row++)
            {
                var members = Enumerable.Range(0, count).Where(i => level[i] == row).ToList();
                float y = ((levels - 1) * 0.5f - row) * rowGap;

                if (levels == 1 && members.Count == 3 && AllLinked(members, links))
                {
                    float h = iconSize * 0.75f;
                    pos[members[0]] = new Vector2(0f, h);
                    pos[members[1]] = new Vector2(-colGap * 0.6f, -h);
                    pos[members[2]] = new Vector2(colGap * 0.6f, -h);
                    continue;
                }
                if (levels == 1 && members.Count >= 4 && TryFindHub(members, links, out int hub))
                {
                    // Hub at the bottom centre, spokes fanned over the upper half circle from left to right.
                    var spokes = members.Where(m => m != hub).ToList();
                    float r = iconSize * 1.5f;
                    float lift = -r * 0.5f;
                    pos[hub] = new Vector2(0f, lift);
                    for (int k = 0; k < spokes.Count; k++)
                    {
                        float angle = Mathf.PI * (1f - (float)k / (spokes.Count - 1));
                        pos[spokes[k]] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r + new Vector2(0f, lift);
                    }
                    continue;
                }
                for (int k = 0; k < members.Count; k++)
                    pos[members[k]] = new Vector2((k - (members.Count - 1) * 0.5f) * colGap, y);
            }
            return pos;
        }

        /// <summary>
        /// Top-down tree layout, one row per vertical level. A node with nodes above it (vertical links) sits under
        /// their average; nodes sharing the same parents spread apart under them; a node with nothing above sits beside
        /// the node it is normally linked to in its row, left first, then right. For L this gives:
        /// <code>
        ///       K
        ///       ↓
        ///       J
        ///       ↓
        ///   G - I - G
        ///     ↘   ↙
        ///       F
        ///     ↙   ↘
        ///   D       D
        ///   ↓       ↓
        /// A-B-A   A-B-A
        /// </code>
        /// </summary>
        static Vector2[] Layered(int count, FormulaAsset.Link[] links, float iconSize)
        {
            var level = Levels(count, links);
            int levels = count > 0 ? level.Max() + 1 : 1;
            float unit = iconSize * 1.35f;
            float rowGap = iconSize * 1.6f;
            var x = new float?[count];

            IEnumerable<int> Parents(int v) => links
                .Where(l => l.kind != AdjacencyKind.Normal)
                .Select(l => l.kind == AdjacencyKind.Down ? (upper: l.from, lower: l.to) : (upper: l.to, lower: l.from))
                .Where(e => e.lower == v).Select(e => e.upper);
            IEnumerable<int> Beside(int v) => links
                .Where(l => l.kind == AdjacencyKind.Normal && (l.from == v || l.to == v))
                .Select(l => l.from == v ? l.to : l.from);

            for (int row = 0; row < levels; row++)
            {
                var members = Enumerable.Range(0, count).Where(i => level[i] == row).ToList();

                // Under the parents; siblings with the same parents spread three units apart.
                var groups = members.Select(v => (v, parents: Parents(v).Where(p => x[p] != null).OrderBy(p => p).ToList()))
                                    .Where(m => m.parents.Count > 0)
                                    .GroupBy(m => string.Join(",", m.parents));
                foreach (var g in groups)
                {
                    var sibs = g.ToList();
                    float center = sibs[0].parents.Average(p => x[p].Value);
                    for (int k = 0; k < sibs.Count; k++)
                        x[sibs[k].v] = center + (k - (sibs.Count - 1) * 0.5f) * unit * 3f;
                }

                // Beside an already-placed neighbour in the row, left then right; repeat for chains.
                bool placed = true;
                while (placed)
                {
                    placed = false;
                    foreach (int anchor in members.Where(m => x[m] != null).ToList())
                    {
                        var free = Beside(anchor).Where(n => level[n] == row && x[n] == null).ToList();
                        float a = x[anchor].Value;
                        foreach (int n in free)
                        {
                            bool leftTaken = members.Any(m => x[m] != null && Mathf.Abs(x[m].Value - (a - unit)) < 0.01f);
                            x[n] = leftTaken ? a + unit : a - unit;
                            placed = true;
                        }
                    }
                }

                // Anything left (e.g. the top node): to the right of the row, or centred if the row is empty.
                foreach (int v in members.Where(m => x[m] == null))
                {
                    var used = members.Where(m => x[m] != null).Select(m => x[m].Value).ToList();
                    x[v] = used.Count > 0 ? used.Max() + unit : 0f;
                }
            }

            var pos = new Vector2[count];
            for (int i = 0; i < count; i++)
                pos[i] = new Vector2(x[i] ?? 0f, ((levels - 1) * 0.5f - level[i]) * rowGap);
            return pos;
        }

        /// <summary>
        /// A single loop closed by two opposite "side" links, normal links whose two ends are both the upper end of a
        /// vertical link (K: J1-J&gt;F-F&lt;J-J&gt;F-F&lt;1). Drawn as two rows joined at their ends by the side links,
        /// e.g. <c>J → F – F ← J</c> over <c>J → F – F ← J</c>, with the inner nodes pulled towards the middle so
        /// the rectangle looks slightly pinched.
        /// </summary>
        static bool TryLadder(int count, FormulaAsset.Link[] links, float iconSize, out Vector2[] pos)
        {
            pos = null;
            if (count < 6 || count % 2 != 0 || links.Length != count) return false;
            var adj = new List<int>[count];
            for (int i = 0; i < count; i++) adj[i] = new List<int>();
            foreach (var l in links)
            {
                if (l.from == l.to) return false;
                adj[l.from].Add(l.to);
                adj[l.to].Add(l.from);
            }
            if (adj.Any(a => a.Count != 2)) return false;

            // The upper end of every vertical link.
            var upper = new bool[count];
            foreach (var l in links)
                if (l.kind == AdjacencyKind.Down) upper[l.from] = true;
                else if (l.kind == AdjacencyKind.Up) upper[l.to] = true;
            var side = links.FirstOrDefault(l => l.kind == AdjacencyKind.Normal && upper[l.from] && upper[l.to]);
            if (side == null) return false;

            // Walk the loop from one end of the side link, away from the other end.
            var cycle = new List<int> { side.to };
            int prev = side.from;
            while (cycle.Count < count)
            {
                int cur = cycle[cycle.Count - 1];
                int next = adj[cur][0] == prev ? adj[cur][1] : adj[cur][0];
                if (next == side.to) return false; // closed early: not one loop
                prev = cur;
                cycle.Add(next);
            }
            if (!adj[cycle[count - 1]].Contains(cycle[0]) || cycle[count - 1] != side.from) return false;

            // The opposite link must be a side link too.
            int half = count / 2;
            int s1 = cycle[half - 1], s2 = cycle[half];
            if (!upper[s1] || !upper[s2] ||
                !links.Any(l => l.kind == AdjacencyKind.Normal && ((l.from == s1 && l.to == s2) || (l.from == s2 && l.to == s1))))
                return false;

            float colGap = iconSize * 2f;
            float outer = iconSize * 1.1f;   // ends of each row
            float inner = iconSize * 0.75f;  // the pinched middle
            pos = new Vector2[count];
            for (int k = 0; k < half; k++)
            {
                float x = (k - (half - 1) * 0.5f) * colGap;
                float y = k == 0 || k == half - 1 ? outer : inner;
                pos[cycle[k]] = new Vector2(x, y);                // top row, left to right
                pos[cycle[count - 1 - k]] = new Vector2(x, -y);   // bottom row, under it
            }
            return true;
        }

        /// <summary>Each node's row: 0 is the top, and a Down edge puts its target one row lower.</summary>
        static int[] Levels(int count, FormulaAsset.Link[] links)
        {
            var level = new int[count];
            var seen = new bool[count];
            var queue = new Queue<int>();
            if (count > 0) { seen[0] = true; queue.Enqueue(0); }
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                foreach (var l in links)
                {
                    int delta = l.kind == AdjacencyKind.Down ? 1 : l.kind == AdjacencyKind.Up ? -1 : 0;
                    if (l.from == v && !seen[l.to]) { level[l.to] = level[v] + delta; seen[l.to] = true; queue.Enqueue(l.to); }
                    else if (l.to == v && !seen[l.from]) { level[l.from] = level[v] - delta; seen[l.from] = true; queue.Enqueue(l.from); }
                }
            }
            int min = count > 0 ? level.Min() : 0;
            for (int i = 0; i < count; i++) level[i] -= min;
            return level;
        }

        static bool Linked(FormulaAsset.Link[] links, int a, int b) =>
            links.Any(l => l.kind == AdjacencyKind.Normal && ((l.from == a && l.to == b) || (l.from == b && l.to == a)));

        // A node linked to every other member, with no links among the others.
        static bool TryFindHub(List<int> members, FormulaAsset.Link[] links, out int hub)
        {
            foreach (int h in members)
            {
                var others = members.Where(m => m != h).ToList();
                if (others.All(o => Linked(links, h, o)) &&
                    !others.Any(a => others.Any(b => a < b && Linked(links, a, b))))
                {
                    hub = h;
                    return true;
                }
            }
            hub = -1;
            return false;
        }

        static bool AllLinked(List<int> members, FormulaAsset.Link[] links) =>
            Linked(links, members[0], members[1]) && Linked(links, members[1], members[2]) &&
            Linked(links, members[0], members[2]);
    }
}
