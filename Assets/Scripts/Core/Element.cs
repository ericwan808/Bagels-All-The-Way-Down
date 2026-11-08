namespace Bagels.Core
{
    /// <summary>One pastry instance in the tree. Bread elements own a child <see cref="Board"/>.</summary>
    public sealed class Element
    {
        public Symbol Symbol { get; }

        /// <summary>The board this element holds, or null if it is not bread.</summary>
        public Board Child { get; }

        /// <summary>The board this element sits on; null only for the root. Kept after removal for reference.</summary>
        public Board Location { get; internal set; }

        public int SlotIndex { get; internal set; } = -1;

        /// <summary>False once consumed, or destroyed along with a consumed board.</summary>
        public bool IsAlive { get; internal set; } = true;

        public bool IsBread => Child != null;

        internal Element(Symbol symbol, BoardLayout layout)
        {
            Symbol = symbol;
            if (layout != null) Child = new Board(layout, this);
        }

        public override string ToString() =>
            Location == null ? $"{Symbol}(root)" : $"{Symbol}@{SlotIndex}";
    }
}
