using System;
using System.Collections.Generic;

namespace Bagels.Core
{
    /// <summary>The slots inside one bread element.</summary>
    public sealed class Board
    {
        readonly Element[] slots;

        public BoardLayout Layout { get; }

        /// <summary>The bread element this board belongs to.</summary>
        public Element Owner { get; }

        public int SlotCount => slots.Length;
        public Element this[int slot] => slots[slot];
        public bool IsEmpty(int slot) => slots[slot] == null;
        public bool IsAlive => Owner.IsAlive;

        internal Board(BoardLayout layout, Element owner)
        {
            Layout = layout;
            Owner = owner;
            slots = new Element[layout.SlotCount];
        }

        /// <summary>Occupied slots in index order.</summary>
        public IEnumerable<Element> Elements
        {
            get
            {
                foreach (var e in slots)
                    if (e != null) yield return e;
            }
        }

        internal void Put(int slot, Element element)
        {
            if (slots[slot] != null) throw new InvalidOperationException($"Slot {slot} is occupied by {slots[slot]}.");
            slots[slot] = element;
            element.Location = this;
            element.SlotIndex = slot;
        }

        internal void Clear(int slot) => slots[slot] = null;
    }
}
