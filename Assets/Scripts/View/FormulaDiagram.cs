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
            var pos = Positions(nodes.Length, links, box.rect.size, iconSize);

            var lineColor = ViewColors.Paper;
            float trim = iconSize * 0.5f + 3f;
            foreach (var l in links)
            {
                // Arrows always point from the upper ingredient to the lower one.
                int upper = l.from, lower = l.to;
                if (l.kind == AdjacencyKind.Up) (upper, lower) = (lower, upper);
                var a = pos[upper];
                var b = pos[lower];
                var dir = (b - a).normalized;
                if (l.kind == AdjacencyKind.Normal)
                    UiFactory.Line(box, a + dir * trim, b - dir * trim, 4f, lineColor);
                else
                    UiFactory.Arrow(box, a + dir * trim, b - dir * trim, 4f, 16f, view.TriangleSprite, lineColor);
            }

            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].catalyst)
                {
                    var ring = UiFactory.Image("Catalyst", box, view.RingSprite, view.CatalystColor,
                                               Vector2.one * (iconSize + 14f));
                    ring.rectTransform.anchoredPosition = pos[i];
                }
                var icon = UiFactory.PastryIcon(box, nodes[i].type, iconSize, view);
                icon.anchoredPosition = pos[i];
            }
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
            var level = Levels(count, links);
            int levels = count > 0 ? level.Max() + 1 : 1;

            float rowGap = levels > 1 ? Mathf.Min(iconSize * 1.5f, (box.y - iconSize) / (levels - 1)) : 0f;
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
