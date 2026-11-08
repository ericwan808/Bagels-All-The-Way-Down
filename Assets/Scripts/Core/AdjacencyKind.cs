namespace Bagels.Core
{
    /// <summary>How one element relates to another, read as "the other is ___ of me".</summary>
    public enum AdjacencyKind
    {
        /// <summary>Neighbouring slots on the same board.</summary>
        Normal,
        /// <summary>The other is a child of a board that sits in a slot normally adjacent to me (one level lower).</summary>
        Down,
        /// <summary>The other is normally adjacent to the board I sit in (one level higher). Inverse of <see cref="Down"/>.</summary>
        Up,
    }
}
