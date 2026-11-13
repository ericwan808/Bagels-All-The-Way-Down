using System.Collections.Generic;

namespace Bagels.Core
{
    /// <summary>Addresses an element by the slot indices from the root down, so it can be found again in a copy of the tree.</summary>
    public static class TreePath
    {
        /// <summary>Slot indices from the root board down to <paramref name="element"/>; empty for the root.</summary>
        public static int[] Of(Element element)
        {
            var path = new List<int>();
            for (var e = element; e?.Location != null; e = e.Location.Owner) path.Add(e.SlotIndex);
            path.Reverse();
            return path.ToArray();
        }

        /// <summary>The element at <paramref name="path"/> in <paramref name="state"/>, or null if there is none.</summary>
        public static Element Find(GameState state, int[] path)
        {
            var e = state.Root;
            foreach (int slot in path)
            {
                var board = e.Child;
                if (board == null || slot < 0 || slot >= board.SlotCount || board.IsEmpty(slot)) return null;
                e = board[slot];
            }
            return e;
        }
    }
}
