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
    /// Undo restores a snapshot taken before each of the player's last <see cref="MaxUndo"/> placements (As,
    /// purchases and components taken from stock), refunding purchases and returning stock. Delivering to an order is
    /// final: it clears the undo history. Unlocks are never taken back. Only unlocked formulas fire.
    /// </para>
    /// <para>
    /// Orders reward components and a tip in money, which extra toppings cut. Given components go to a stock;
    /// placing a component uses a stocked one for free before buying one. Formulas are bought with money; stock
    /// can't pay for them.
    /// </para>
    /// </summary>
    public sealed class GameSession
    {
        public const int MaxUndo = 10;
        public const int MaxOrders = 3;
        public const Symbol Basic = Symbol.A;

        readonly List<Order> orders = new List<Order>();
        readonly List<(GameState state, int money, Dictionary<Symbol, int> stock)> undo =
            new List<(GameState, int, Dictionary<Symbol, int>)>();
        Dictionary<Symbol, int> stock = new Dictionary<Symbol, int>();
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

        /// <summary>Whether cascade steps carry snapshots for replaying (see <see cref="GameState.RecordSnapshots"/>).</summary>
        public bool RecordSnapshots
        {
            get => State.RecordSnapshots;
            set => State.RecordSnapshots = value; // undo snapshots are clones, so they keep the setting
        }

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
            // The first order is always the tutorial's B and C holding an A, if those exist.
            var first = generator.First(Progress);
            if (first != null) orders.Add(first);
            else AddOrder();
        }

        /// <summary>A formula is active once its product is unlocked.</summary>
        public bool IsFormulaUnlocked(Symbol product) => Progress.IsUnlocked(product);

        /// <summary>True if the player can buy <paramref name="symbol"/> now: unlocked, priced, and affordable.</summary>
        public bool CanBuy(Symbol symbol) =>
            Progress.IsUnlocked(symbol) && Rules.PriceOf(symbol) is int price && Money >= price;

        /// <summary>True if the next formula is one that is bought, and the player has the money for it.</summary>
        public bool CanBuyNextFormula => !Won && Progress.NextFormulaPrice is int price && Money >= price;

        /// <summary>
        /// Buys the next formula for its component's price, unlocking it (and any that come with it, like F with E).
        /// Unlocks are never taken back, so undo keeps the formula: every undo snapshot is charged the price too, and
        /// undoing still refunds only placements. Throws if the formula can't be bought now.
        /// </summary>
        public void BuyNextFormula()
        {
            if (!CanBuyNextFormula) throw new InvalidOperationException("The next formula can't be bought right now.");
            int price = Progress.NextFormulaPrice.Value;
            Money -= price;
            // A snapshot's money is at least the money now (placements since then only spent), so this stays >= 0.
            for (int i = 0; i < undo.Count; i++) undo[i] = (undo[i].state, undo[i].money - price, undo[i].stock);
            var gained = Progress.Purchase();
            CheckFinalOrder();
            LastUnlocked = gained;
        }

        /// <summary>How many free copies of <paramref name="symbol"/> order rewards have put in stock.</summary>
        public int StockOf(Symbol symbol) => stock.TryGetValue(symbol, out int n) ? n : 0;

        /// <summary>True if the player can place <paramref name="symbol"/> now: the free A, a stocked copy, or one they can buy.</summary>
        public bool CanTake(Symbol symbol) => symbol == Basic || StockOf(symbol) > 0 || CanBuy(symbol);

        public PlacementResult PlaceA(Board board, int slot) => Place(board, slot, Basic);

        /// <summary>
        /// Places a component the player chose: the free A, a copy from stock, or one they buy (stock is used first).
        /// Throws if the slot is not free or the component can't be taken. A cascade that runs away leaves the board
        /// as it stopped, and rethrows.
        /// </summary>
        public PlacementResult Place(Board board, int slot, Symbol symbol)
        {
            if (Won) throw new InvalidOperationException("The game is over.");
            if (!GameState.CanPlace(board, slot)) throw new InvalidOperationException("That slot is not free.");
            if (!CanTake(symbol)) throw new InvalidOperationException($"{symbol} can't be bought right now.");
            bool fromStock = symbol != Basic && StockOf(symbol) > 0;
            int price = symbol == Basic || fromStock ? 0 : Rules.PriceOf(symbol).Value;

            // Snapshot a copy; the board objects passed in belong to the live state, which stays live.
            undo.Add((State.Clone(), Money, new Dictionary<Symbol, int>(stock)));
            if (undo.Count > MaxUndo) undo.RemoveAt(0);
            Money -= price;
            if (fromStock) stock[symbol]--;

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

        /// <summary>Reverts the last placement and its cascade, refunding a purchase or returning a copy to stock.</summary>
        public void Undo()
        {
            if (!CanUndo) throw new InvalidOperationException("Nothing to undo.");
            var (state, money, stocked) = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            State = state;
            Money = money;
            stock = stocked;
            LastUnlocked = Array.Empty<PastryInfo>();
        }

        /// <summary>The bread a board reset starts over with.</summary>
        public const Symbol ResetBoardSymbol = Symbol.H;

        /// <summary>True once the player may swap the board for a fresh one: after H is unlocked.</summary>
        public bool CanResetBoard => !Won && Progress.IsUnlocked(ResetBoardSymbol);

        /// <summary>
        /// Throws away the whole board and starts over on an empty H. Everything else (money, stock, unlocks, orders)
        /// stays. Like a delivery, it is final: it clears the undo history. Throws before H is unlocked.
        /// </summary>
        public void ResetBoard()
        {
            if (!CanResetBoard) throw new InvalidOperationException($"The board can't be reset before {ResetBoardSymbol} is unlocked.");
            State = new GameState(Rules, ResetBoardSymbol) { IsActive = IsFormulaUnlocked, RecordSnapshots = RecordSnapshots };
            undo.Clear();
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
            int item = order.FindItemFor(element, out int extras);
            if (item < 0) return DeliveryOutcome.Rejected;

            State.Remove(element);
            order.Fill(item, extras);
            undo.Clear(); // sales are final
            LastUnlocked = Array.Empty<PastryInfo>();
            if (!order.IsComplete) return DeliveryOutcome.Accepted;

            Money += order.TipEarned;
            foreach (var component in order.RewardComponents) stock[component] = StockOf(component) + 1;
            orders.Remove(order);
            if (order.IsFinal)
            {
                Won = true;
                return DeliveryOutcome.Won;
            }

            // The orders below move up, and the replacement joins the bottom of the docket.
            AddOrder();
            if (RegularOrderCount < MaxOrders) AddOrder();
            return DeliveryOutcome.Completed;
        }

        int RegularOrderCount => orders.Count(o => !o.IsFinal);

        void AddOrder()
        {
            var order = generator.Generate(Progress, IsFormulaUnlocked);
            if (order != null) orders.Add(order);
        }

        /// <summary>For tests: adds money (or, with 0, just re-checks crafting unlocks) as if an order had paid it.</summary>
        internal void GrantMoney(int amount)
        {
            Money += amount;
            RefreshUnlocks();
        }

        /// <summary>For tests: unlocks formulas as if every one priced up to <paramref name="budget"/> had been bought, without paying.</summary>
        internal void UnlockUpTo(int budget)
        {
            LastUnlocked = Progress.UnlockUpTo(budget);
            CheckFinalOrder();
        }

        /// <summary>For tests: adds a hand-made order to the active ones.</summary>
        internal void AddOrder(Order order) => orders.Insert(0, order);

        /// <summary>For tests: adds copies of a component to stock, as if an order had given them.</summary>
        internal void GrantStock(Symbol symbol, int count = 1) => stock[symbol] = StockOf(symbol) + count;

        void NoteCrafted(IEnumerable<CascadeStep> steps)
        {
            foreach (var step in steps) Progress.NoteCrafted(step.Product.Symbol);
        }

        void RefreshUnlocks()
        {
            LastUnlocked = Progress.Update();
            CheckFinalOrder();
        }

        void CheckFinalOrder()
        {
            // As soon as the target's formula is unlocked, a special order asks for it alone.
            if (!finalOrderAdded && Rules.Target is Symbol target && Progress.IsUnlocked(target))
            {
                finalOrderAdded = true;
                orders.Add(generator.Final(target));
            }
        }
    }
}
