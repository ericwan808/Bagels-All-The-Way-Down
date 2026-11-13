using System;
using System.Collections.Generic;
using System.Linq;

namespace Bagels.Core
{
    /// <summary>What a delivery to an order did.</summary>
    public enum DeliveryOutcome
    {
        /// <summary>The element doesn't match any unfilled item of that order; nothing changed.</summary>
        Rejected,
        /// <summary>The element filled an item; the order still wants more.</summary>
        Accepted,
        /// <summary>The element filled the last item; the reward was paid and the order replaced.</summary>
        Completed,
        /// <summary>The final order was fulfilled: the game is won.</summary>
        Won,
    }

    /// <summary>
    /// One playthrough: the board plus the economy from design.md (money, unlocks, orders) and undo. Pure logic; no
    /// Unity dependencies.
    /// <para>
    /// Undo restores a snapshot taken before each of the player's last <see cref="MaxUndo"/> placements (As and
    /// purchases), refunding purchases. Delivering to an order is final: it clears the undo history. Unlocks are
    /// never taken back. Only unlocked formulas fire.
    /// </para>
    /// </summary>
    public sealed class GameSession
    {
        public const int MaxUndo = 10;
        public const int MaxOrders = 3;
        public const Symbol Basic = Symbol.A;

        readonly List<Order> orders = new List<Order>();
        readonly List<(GameState state, int money)> undo = new List<(GameState, int)>();
        readonly OrderGenerator generator;
        bool finalOrderAdded;

        public Rules Rules { get; }
        public GameState State { get; private set; }
        public Progression Progress { get; }
        public int Money { get; private set; }
        public bool Won { get; private set; }
        public IReadOnlyList<Order> Orders => orders;
        public int UndoCount => undo.Count;
        public bool CanUndo => undo.Count > 0;

        /// <summary>Components unlocked by the last action, for the view to announce.</summary>
        public IReadOnlyList<PastryInfo> LastUnlocked { get; private set; } = Array.Empty<PastryInfo>();

        public GameSession(Rules rules, int seed)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Progress = new Progression(rules.Catalog);
            State = new GameState(rules) { IsActive = IsFormulaUnlocked };
            generator = new OrderGenerator(rules, seed);
            RefreshUnlocks();
            LastUnlocked = Array.Empty<PastryInfo>();
            AddOrder();
        }

        /// <summary>A formula is active once its product is unlocked.</summary>
        public bool IsFormulaUnlocked(Symbol product) => Progress.IsUnlocked(product);

        /// <summary>True if the player can buy <paramref name="symbol"/> now: unlocked, priced, and affordable.</summary>
        public bool CanBuy(Symbol symbol) =>
            Progress.IsUnlocked(symbol) && Rules.PriceOf(symbol) is int price && Money >= price;

        public PlacementResult PlaceA(Board board, int slot) => Place(board, slot, Basic);

        /// <summary>
        /// Places a component the player chose: the free A, or one they buy. Throws if the slot is not free or the
        /// component can't be bought. A cascade that runs away leaves the board as it stopped, and rethrows.
        /// </summary>
        public PlacementResult Place(Board board, int slot, Symbol symbol)
        {
            if (Won) throw new InvalidOperationException("The game is over.");
            if (!GameState.CanPlace(board, slot)) throw new InvalidOperationException("That slot is not free.");
            int price = 0;
            if (symbol != Basic)
            {
                if (!CanBuy(symbol)) throw new InvalidOperationException($"{symbol} can't be bought right now.");
                price = Rules.PriceOf(symbol).Value;
            }

            // Snapshot a copy; the board objects passed in belong to the live state, which stays live.
            undo.Add((State.Clone(), Money));
            if (undo.Count > MaxUndo) undo.RemoveAt(0);
            Money -= price;

            try
            {
                var result = State.Place(board, slot, symbol);
                NoteCrafted(result.Steps);
                return result;
            }
            catch (CascadeLimitException e)
            {
                NoteCrafted(e.Steps);
                throw;
            }
            finally
            {
                RefreshUnlocks();
            }
        }

        /// <summary>Reverts the last placement and its cascade, refunding a purchase.</summary>
        public void Undo()
        {
            if (!CanUndo) throw new InvalidOperationException("Nothing to undo.");
            var (state, money) = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            State = state;
            Money = money;
            LastUnlocked = Array.Empty<PastryInfo>();
        }

        /// <summary>
        /// Hands an element from the board, with everything inside it, to an order. The element leaves the board;
        /// once every item is in, the order pays and is replaced, and one more order is added while fewer than
        /// <see cref="MaxOrders"/> are active.
        /// </summary>
        public DeliveryOutcome Deliver(Order order, Element element)
        {
            if (Won || order == null || !orders.Contains(order)) return DeliveryOutcome.Rejected;
            if (element == null || element == State.Root || !State.IsInPlay(element)) return DeliveryOutcome.Rejected;
            int item = order.FindItemFor(element);
            if (item < 0) return DeliveryOutcome.Rejected;

            State.Remove(element);
            order.Fill(item);
            undo.Clear(); // sales are final
            LastUnlocked = Array.Empty<PastryInfo>();
            if (!order.IsComplete) return DeliveryOutcome.Accepted;

            Money += order.Reward;
            int index = orders.IndexOf(order);
            orders.RemoveAt(index);
            if (order.IsFinal)
            {
                Won = true;
                return DeliveryOutcome.Won;
            }

            RefreshUnlocks(); // the reward may unlock components, which new orders can then ask for
            AddOrder(index);
            if (RegularOrderCount < MaxOrders) AddOrder();
            return DeliveryOutcome.Completed;
        }

        int RegularOrderCount => orders.Count(o => !o.IsFinal);

        void AddOrder(int index = -1)
        {
            var order = generator.Generate(Progress, IsFormulaUnlocked);
            if (order == null) return;
            if (index < 0) orders.Add(order);
            else orders.Insert(index, order);
        }

        /// <summary>For tests: adds money (or, with 0, just re-checks unlocks) as if an order had paid it.</summary>
        internal void GrantMoney(int amount)
        {
            Money += amount;
            RefreshUnlocks();
        }

        void NoteCrafted(IEnumerable<CascadeStep> steps)
        {
            foreach (var step in steps) Progress.NoteCrafted(step.Product.Symbol);
        }

        void RefreshUnlocks()
        {
            LastUnlocked = Progress.Update(Money);
            // As soon as the target's formula is unlocked, a special order asks for it alone.
            if (!finalOrderAdded && Rules.Target is Symbol target && Progress.IsUnlocked(target))
            {
                finalOrderAdded = true;
                orders.Add(generator.Final(target));
            }
        }
    }
}
