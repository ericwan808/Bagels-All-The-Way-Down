using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bagels.Core
{
    /// <summary>
    /// One item an order asks for: a component, and for bread its exact contents (which may be bread with contents of
    /// their own). Matching is strict: a bread must hold exactly the listed contents and nothing else. Positions on
    /// the board don't matter. A bread with no listed contents must be empty.
    /// </summary>
    public sealed class ItemSpec
    {
        public Symbol Symbol { get; }
        public IReadOnlyList<ItemSpec> Contents { get; }

        public ItemSpec(Symbol symbol, params ItemSpec[] contents)
        {
            Symbol = symbol;
            Contents = contents ?? Array.Empty<ItemSpec>();
        }

        public bool Matches(Element element)
        {
            if (element == null || element.Symbol != Symbol) return false;
            var children = element.Child != null ? element.Child.Elements.ToList() : new List<Element>();
            if (children.Count != Contents.Count) return false;
            return AssignContents(children, 0, new bool[children.Count]);
        }

        // Backtracking: each listed content needs its own child.
        bool AssignContents(List<Element> children, int k, bool[] used)
        {
            if (k == Contents.Count) return true;
            for (int i = 0; i < children.Count; i++)
            {
                if (used[i] || !Contents[k].Matches(children[i])) continue;
                used[i] = true;
                if (AssignContents(children, k + 1, used)) return true;
                used[i] = false;
            }
            return false;
        }

        /// <summary>What buying this item and everything in it would cost. Unpriced components count as 0.</summary>
        public int Cost(Rules rules) => (rules.PriceOf(Symbol) ?? 0) + Contents.Sum(c => c.Cost(rules));

        /// <summary>
        /// Levels of contents below this item: 0 for a bagel or an empty bread, 1 for bread holding only bagels, 2 when
        /// it holds a bread (even an empty one), and so on.
        /// </summary>
        public int Levels(Rules rules) =>
            Contents.Count == 0 ? 0 : 1 + Contents.Max(c => rules.IsBread(c.Symbol) ? Math.Max(1, c.Levels(rules)) : 0);

        /// <summary>
        /// True if every bread in this item stays within its nesting limit (design.md, "Orders"): a bread may fill at
        /// most min(its own <see cref="PastryInfo.OrderLevels"/>, what its parent leaves) levels, so with one level
        /// it holds bagels or nothing, never bread.
        /// </summary>
        public bool WithinNestingLimits(Rules rules, int? budget = null)
        {
            if (Contents.Count == 0) return true;
            int limit = Math.Min(rules.InfoOf(Symbol)?.OrderLevels ?? 1, budget ?? int.MaxValue);
            if (limit < 1) return false;
            foreach (var c in Contents)
                if (rules.IsBread(c.Symbol) && (limit < 2 || !c.WithinNestingLimits(rules, limit - 1))) return false;
            return true;
        }

        /// <summary>This item and everything inside it, e.g. 3 for "C containing A and B".</summary>
        public int Size => 1 + Contents.Sum(c => c.Size);

        public override string ToString()
        {
            if (Contents.Count == 0) return Symbol.ToString();
            var sb = new StringBuilder().Append(Symbol).Append('(');
            sb.Append(string.Join(", ", Contents.Select(c => c.ToString())));
            return sb.Append(')').ToString();
        }
    }

    /// <summary>
    /// A request for up to <see cref="MaxItems"/> items, paying <see cref="Reward"/> once every item has been handed
    /// in. Items can be handed in one at a time.
    /// </summary>
    public sealed class Order
    {
        public const int MaxItems = 5;

        readonly bool[] filled;

        public int Id { get; }
        public IReadOnlyList<ItemSpec> Items { get; }
        public int Reward { get; }

        /// <summary>The special order for the target bagel; fulfilling it wins the game.</summary>
        public bool IsFinal { get; }

        public Order(int id, IEnumerable<ItemSpec> items, int reward, bool isFinal = false)
        {
            Id = id;
            Items = items.ToArray();
            if (Items.Count == 0 || Items.Count > MaxItems)
                throw new ArgumentException($"An order needs 1 to {MaxItems} items.");
            Reward = reward;
            IsFinal = isFinal;
            filled = new bool[Items.Count];
        }

        public bool IsFilled(int item) => filled[item];
        public bool IsComplete => filled.All(f => f);

        /// <summary>The first unfilled item <paramref name="element"/> satisfies, or -1.</summary>
        public int FindItemFor(Element element)
        {
            for (int i = 0; i < Items.Count; i++)
                if (!filled[i] && Items[i].Matches(element)) return i;
            return -1;
        }

        internal void Fill(int item) => filled[item] = true;

        public override string ToString() =>
            $"Order {Id}: {string.Join(" + ", Items)} for ${Reward}{(IsFinal ? " (final)" : "")}";
    }

    /// <summary>
    /// Makes random orders from what is currently unlocked and priced (design.md, "Orders"): at most five items, a
    /// reward of at most half the total item cost, bread nested no deeper than each bread's limit, and only contents
    /// that can actually be built, i.e. that have an arrangement on their board that sets off no active formula.
    /// </summary>
    public sealed class OrderGenerator
    {
        const int MaxContents = 4;
        const int Attempts = 40;
        const int ArrangementTries = 24;

        readonly Rules rules;
        readonly Random random;
        int nextId = 1;

        public OrderGenerator(Rules rules, int seed)
        {
            this.rules = rules;
            random = new Random(seed);
        }

        public Order Final(Symbol target) => new Order(nextId++, new[] { new ItemSpec(target) }, 0, isFinal: true);

        /// <summary>A new order, or null if nothing priced is unlocked.</summary>
        public Order Generate(Progression progress, Func<Symbol, bool> isActive)
        {
            // Items are drawn from unlocked, priced components. A on its own pays nothing, so it only appears as
            // bread contents.
            var available = rules.Catalog.Where(i => i.Price != null && progress.IsUnlocked(i.Symbol)).ToList();
            var items = available.Where(i => i.Price > 0).ToList();
            if (items.Count == 0) return null; // nothing for sale yet, so nothing to order

            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                int maxItems = Math.Min(Order.MaxItems, 1 + items.Count / 2);
                int count = 1 + random.Next(maxItems);
                var specs = new List<ItemSpec>();
                for (int k = 0; k < count; k++) specs.Add(RandomItem(items, available, null, isActive));
                int cost = specs.Sum(s => s.Cost(rules));
                if (cost <= 0) continue;
                return new Order(nextId++, specs, Reward(cost));
            }
            var cheapest = items.OrderBy(i => i.Price).First();
            return new Order(nextId++, new[] { new ItemSpec(cheapest.Symbol) }, Reward(cheapest.Price.Value));
        }

        /// <summary>Between 30% and 50% of the cost: the design caps the reward at half the cost.</summary>
        int Reward(int cost)
        {
            int cap = cost / 2;
            int reward = (int)Math.Round(cost * (0.3 + random.NextDouble() * 0.2));
            return Math.Max(1, Math.Min(cap, reward));
        }

        /// <summary>
        /// A random item. <paramref name="budget"/> is how many content levels the parent leaves (null at the top);
        /// a bread may fill min(its own limit, budget) levels, and with one level left its contents are bagels only.
        /// </summary>
        ItemSpec RandomItem(List<PastryInfo> choices, List<PastryInfo> contentChoices, int? budget, Func<Symbol, bool> isActive)
        {
            var info = choices[random.Next(choices.Count)];
            var layout = rules.LayoutOf(info.Symbol);
            if (layout == null || random.NextDouble() < 0.4) return new ItemSpec(info.Symbol);

            int levels = Math.Min(info.OrderLevels, budget ?? int.MaxValue);
            var inner = levels >= 2 ? contentChoices : contentChoices.Where(c => rules.LayoutOf(c.Symbol) == null).ToList();
            if (inner.Count == 0) return new ItemSpec(info.Symbol);

            int count = 1 + random.Next(Math.Min(MaxContents, layout.SlotCount));
            var contents = new ItemSpec[count];
            for (int k = 0; k < count; k++) contents[k] = RandomItem(inner, inner, levels - 1, isActive);
            var spec = new ItemSpec(info.Symbol, contents);
            return CanBuild(spec, isActive) ? spec : new ItemSpec(info.Symbol);
        }

        /// <summary>
        /// True if the item has an arrangement in which no active formula fires. Formulas only need more
        /// adjacencies to match, never fewer, so if the finished arrangement is quiet, every order of placing it is.
        /// </summary>
        public bool CanBuild(ItemSpec spec, Func<Symbol, bool> isActive)
        {
            for (int t = 0; t < ArrangementTries; t++)
            {
                var sandbox = new GameState(rules) { IsActive = isActive };
                var built = new List<Element>();
                if (!TryArrange(sandbox, sandbox.RootBoard, 0, spec, built)) return false;
                if (built.All(e => !sandbox.TryMatch(e, out _))) return true;
            }
            return false;
        }

        bool TryArrange(GameState sandbox, Board board, int slot, ItemSpec spec, List<Element> built)
        {
            var element = sandbox.PutWithoutCascade(board, slot, spec.Symbol);
            built.Add(element);
            if (spec.Contents.Count == 0) return true;
            if (element.Child == null || element.Child.SlotCount < spec.Contents.Count) return false;
            var slots = Enumerable.Range(0, element.Child.SlotCount).OrderBy(_ => random.Next()).ToArray();
            for (int k = 0; k < spec.Contents.Count; k++)
                if (!TryArrange(sandbox, element.Child, slots[k], spec.Contents[k], built)) return false;
            return true;
        }
    }
}
