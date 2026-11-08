using System;
using System.Collections.Generic;
using System.Linq;

namespace Bagels.Core
{
    /// <summary>An immutable slot graph: how many slots a board has and which pairs are adjacent.</summary>
    public sealed class BoardLayout
    {
        readonly int[][] neighbors;

        public string Name { get; }
        public int SlotCount { get; }

        public BoardLayout(string name, int slotCount, IEnumerable<(int a, int b)> edges)
        {
            if (slotCount <= 0) throw new ArgumentOutOfRangeException(nameof(slotCount));
            Name = name;
            SlotCount = slotCount;

            var sets = new SortedSet<int>[slotCount];
            for (int i = 0; i < slotCount; i++) sets[i] = new SortedSet<int>();
            foreach (var (a, b) in edges)
            {
                if (a < 0 || a >= slotCount || b < 0 || b >= slotCount || a == b)
                    throw new ArgumentException($"Invalid edge ({a},{b}) for {slotCount} slots.");
                sets[a].Add(b);
                sets[b].Add(a);
            }
            neighbors = sets.Select(s => s.ToArray()).ToArray();
        }

        /// <summary>Slots adjacent to <paramref name="slot"/>, in ascending order.</summary>
        public IReadOnlyList<int> Neighbors(int slot) => neighbors[slot];

        public bool AreAdjacent(int a, int b) => Array.IndexOf(neighbors[a], b) >= 0;

        public override string ToString() => Name;

        /// <summary>Wheel: hub slot 0 joined to rim slots 1–6, which form a cycle.</summary>
        public static BoardLayout W7 { get; } = new BoardLayout("W7", 7,
            Enumerable.Range(1, 6).SelectMany(i => new[] { (0, i), (i, i % 6 + 1) }));

        /// <summary>Path of three slots: 0–1–2.</summary>
        public static BoardLayout P3 { get; } = new BoardLayout("P3", 3, new[] { (0, 1), (1, 2) });

        /// <summary>Triangle: three mutually adjacent slots.</summary>
        public static BoardLayout K3 { get; } = new BoardLayout("K3", 3, new[] { (0, 1), (1, 2), (0, 2) });
    }
}
