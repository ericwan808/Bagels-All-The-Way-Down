using System;
using System.Collections.Generic;

namespace Bagels.Core
{
    /// <summary>
    /// The player's A placements in order, for undo. Placement is deterministic, so undoing is done by replaying
    /// every placement but the last into a fresh <see cref="GameState"/>; that also undoes whole cascades.
    /// Boards are recorded as slot paths from the root, because replay builds new Board objects.
    /// </summary>
    public sealed class PlacementHistory
    {
        readonly List<(int[] board, int slot)> moves = new List<(int[] board, int slot)>();

        public int Count => moves.Count;

        /// <summary>Call before placing: once the A lands, a cascade may have consumed <paramref name="board"/>.</summary>
        public void Record(Board board, int slot) => moves.Add((PathOf(board.Owner), slot));

        public void Clear() => moves.Clear();

        /// <summary>Forgets the last placement and returns a new game with every earlier one replayed.</summary>
        public GameState UndoLast(Rules rules)
        {
            if (moves.Count == 0) throw new InvalidOperationException("Nothing to undo.");
            moves.RemoveAt(moves.Count - 1);
            var state = new GameState(rules);
            foreach (var (path, slot) in moves)
            {
                var board = Find(state, path)?.Child
                            ?? throw new InvalidOperationException("Replay diverged: a recorded board is missing.");
                try { state.PlaceA(board, slot); }
                catch (CascadeLimitException) { } // the original placement hit the same limit and stayed on the board
            }
            return state;
        }

        /// <summary>Slot indices from the root board down to <paramref name="element"/>; empty for the root.</summary>
        public static int[] PathOf(Element element)
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
