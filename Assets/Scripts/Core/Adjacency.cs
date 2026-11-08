using System;
using System.Collections.Generic;

namespace Bagels.Core
{
    /// <summary>
    /// Adjacency rules. Normal: neighbouring slots on one board. Down: every child of a board in a normally
    /// adjacent slot. Up: every element normally adjacent to the board I sit in. Only one level; children of two
    /// adjacent boards are not adjacent to each other.
    /// </summary>
    public static class Adjacency
    {
        public static AdjacencyKind Inverse(AdjacencyKind kind) => kind switch
        {
            AdjacencyKind.Normal => AdjacencyKind.Normal,
            AdjacencyKind.Down => AdjacencyKind.Up,
            AdjacencyKind.Up => AdjacencyKind.Down,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        /// <summary>Elements that are <paramref name="kind"/>-adjacent to <paramref name="element"/>, in deterministic order.</summary>
        public static IEnumerable<Element> Neighbors(Element element, AdjacencyKind kind)
        {
            switch (kind)
            {
                case AdjacencyKind.Normal: return NormalNeighbors(element);
                case AdjacencyKind.Down: return DownNeighbors(element);
                case AdjacencyKind.Up:
                    var owner = element.Location?.Owner;
                    return owner == null ? Array.Empty<Element>() : NormalNeighbors(owner);
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>True if <paramref name="other"/> is <paramref name="kind"/>-adjacent to <paramref name="element"/>.</summary>
        public static bool AreAdjacent(Element element, Element other, AdjacencyKind kind)
        {
            foreach (var n in Neighbors(element, kind))
                if (n == other) return true;
            return false;
        }

        static IEnumerable<Element> NormalNeighbors(Element element)
        {
            var board = element.Location;
            if (board == null) yield break; // root has no neighbours
            foreach (int slot in board.Layout.Neighbors(element.SlotIndex))
            {
                var n = board[slot];
                if (n != null) yield return n;
            }
        }

        static IEnumerable<Element> DownNeighbors(Element element)
        {
            foreach (var n in NormalNeighbors(element))
            {
                if (n.Child == null) continue;
                foreach (var child in n.Child.Elements) yield return child;
            }
        }
    }
}
