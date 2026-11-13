using System;
using System.Collections.Generic;

namespace Bagels.Core
{
    /// <summary>One formula firing during a placement.</summary>
    public sealed class CascadeStep
    {
        public Formula Formula { get; }
        public IReadOnlyList<Element> Consumed { get; }
        public Element Product { get; }
        public Board Board { get; }
        public int Slot { get; }

        internal CascadeStep(Formula formula, IReadOnlyList<Element> consumed, Element product, Board board, int slot)
        {
            Formula = formula;
            Consumed = consumed;
            Product = product;
            Board = board;
            Slot = slot;
        }
    }

    /// <summary>Everything that happened because of one placement, in order, so visuals can replay it.</summary>
    public sealed class PlacementResult
    {
        public Element Placed { get; }
        public IReadOnlyList<CascadeStep> Steps { get; }
        public bool TargetReached { get; }

        /// <summary>The element left in the placed slot after the cascade (the last product, or the placed element).</summary>
        public Element Final => Steps.Count > 0 ? Steps[Steps.Count - 1].Product : Placed;

        internal PlacementResult(Element placed, IReadOnlyList<CascadeStep> steps, bool targetReached)
        {
            Placed = placed;
            Steps = steps;
            TargetReached = targetReached;
        }
    }

    public sealed class CascadeLimitException : InvalidOperationException
    {
        public IReadOnlyList<CascadeStep> Steps { get; }

        public CascadeLimitException(IReadOnlyList<CascadeStep> steps)
            : base($"Cascade exceeded {GameState.MaxCascadeSteps} steps; the formulas probably form a cycle.")
        {
            Steps = steps;
        }
    }

    /// <summary>The whole game tree plus the placement/cascade rules. Pure logic; no Unity dependencies.</summary>
    public sealed class GameState
    {
        public const int MaxCascadeSteps = 256;

        public Rules Rules { get; }
        public Element Root { get; }
        public Board RootBoard => Root.Child;

        /// <summary>
        /// Which formulas may fire, by product symbol. Null means every formula is active. Locked formulas are
        /// inactive: building their pattern does nothing.
        /// </summary>
        public Func<Symbol, bool> IsActive { get; set; }

        public GameState(Rules rules)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Root = Create(rules.RootSymbol);
        }

        GameState(Rules rules, Element root, Func<Symbol, bool> isActive)
        {
            Rules = rules;
            Root = root;
            IsActive = isActive;
        }

        /// <summary>A deep copy of the whole tree, for undo snapshots. Shares <see cref="Rules"/> and <see cref="IsActive"/>.</summary>
        public GameState Clone()
        {
            var root = new Element(Root.Symbol, Root.Child.Layout);
            CopyInto(Root.Child, root.Child);
            return new GameState(Rules, root, IsActive);
        }

        static void CopyInto(Board from, Board to)
        {
            foreach (var e in from.Elements)
            {
                var copy = new Element(e.Symbol, e.Child?.Layout);
                to.Put(e.SlotIndex, copy);
                if (e.Child != null) CopyInto(e.Child, copy.Child);
            }
        }

        /// <summary>The free move: drop an A into an empty slot on any live board.</summary>
        public PlacementResult PlaceA(Board board, int slot) => Place(board, slot, Symbol.A);

        /// <summary>True if an element can be placed in this slot: the board is in play and the slot is empty.</summary>
        public static bool CanPlace(Board board, int slot) =>
            board != null && board.IsAlive && slot >= 0 && slot < board.SlotCount && board.IsEmpty(slot);

        /// <summary>
        /// Places any symbol (the player places As, or components they bought) and runs the cascade it triggers.
        /// Bought bread arrives empty.
        /// </summary>
        public PlacementResult Place(Board board, int slot, Symbol symbol)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (!board.IsAlive) throw new InvalidOperationException("That board has been destroyed.");
            if (slot < 0 || slot >= board.SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
            if (!board.IsEmpty(slot)) throw new InvalidOperationException($"Slot {slot} is already occupied.");

            var placed = Create(symbol);
            board.Put(slot, placed);

            var steps = new List<CascadeStep>();
            var trigger = placed;
            while (TryMatch(trigger, out var match))
            {
                if (steps.Count >= MaxCascadeSteps) throw new CascadeLimitException(steps);
                var step = Apply(match);
                steps.Add(step);
                trigger = step.Product;
            }

            bool reached = Rules.Target is Symbol target && Contains(RootBoard, target);
            return new PlacementResult(placed, steps, reached);
        }

        /// <summary>The first active formula, in priority order, that <paramref name="trigger"/> completes.</summary>
        public bool TryMatch(Element trigger, out Match match)
        {
            foreach (var formula in Rules.FormulasInvolving(trigger.Symbol))
                if ((IsActive == null || IsActive(formula.Product)) && Matcher.TryMatch(trigger, formula, out match))
                    return true;
            match = null;
            return false;
        }

        /// <summary>
        /// Takes an element (and, if it is bread, everything in it) off the board, e.g. to hand it to an order.
        /// Removal triggers no formulas.
        /// </summary>
        public void Remove(Element element)
        {
            if (!IsInPlay(element) || element == Root)
                throw new InvalidOperationException($"{element} is not on the board.");
            element.Location.Clear(element.SlotIndex);
            Kill(element);
        }

        /// <summary>True if the element is the root or sits on the board, and every board above it does too.</summary>
        public bool IsInPlay(Element element)
        {
            for (var e = element; e != null; e = e.Location.Owner)
            {
                if (!e.IsAlive) return false;
                if (e.Location == null) return e == Root;
                if (e.Location[e.SlotIndex] != e) return false;
            }
            return false;
        }

        /// <summary>Puts an element in a slot without running any formula. For sandboxes, e.g. order feasibility.</summary>
        internal Element PutWithoutCascade(Board board, int slot, Symbol symbol)
        {
            var e = Create(symbol);
            board.Put(slot, e);
            return e;
        }

        /// <summary>True if any live element with this symbol exists anywhere in the tree.</summary>
        public bool Contains(Symbol symbol) => Contains(RootBoard, symbol);

        Element Create(Symbol symbol) => new Element(symbol, Rules.LayoutOf(symbol));

        CascadeStep Apply(Match match)
        {
            var trigger = match.Trigger;
            var consumed = new List<Element>();
            for (int i = 0; i < match.Elements.Count; i++)
            {
                var e = match.Elements[i];
                if (!match.Formula.Nodes[i].IsCatalyst || e == trigger) consumed.Add(e);
            }

            foreach (var e in consumed)
            {
                e.Location.Clear(e.SlotIndex);
                Kill(e);
            }

            // The product lands where the trigger was. If the trigger's own board was consumed (reachable via an
            // Up edge then a Normal edge), fall back to the slot of the nearest consumed ancestor board.
            var board = trigger.Location;
            int slot = trigger.SlotIndex;
            while (!board.IsAlive)
            {
                var owner = board.Owner;
                board = owner.Location;
                slot = owner.SlotIndex;
            }

            var product = Create(match.Formula.Product);
            board.Put(slot, product);
            return new CascadeStep(match.Formula, consumed, product, board, slot);
        }

        // Consumed boards simply destroy their contents.
        static void Kill(Element element)
        {
            element.IsAlive = false;
            if (element.Child == null) return;
            foreach (var child in element.Child.Elements) Kill(child);
        }

        static bool Contains(Board board, Symbol symbol)
        {
            foreach (var e in board.Elements)
                if (e.Symbol == symbol || (e.Child != null && Contains(e.Child, symbol))) return true;
            return false;
        }
    }
}
