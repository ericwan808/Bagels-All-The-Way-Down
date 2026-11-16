using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bagels.Core
{
    /// <summary>
    /// One item an order asks for: a component, and for bread the contents it must hold (which may be bread with
    /// contents of their own). A bread must hold every listed content, but may hold extra toppings too; each extra
    /// makes the order imperfect and cuts its tip (see <see cref="Order"/>). Positions on the board don't matter.
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

        /// <summary>True if <paramref name="element"/> satisfies this item, perfectly or with extra toppings.</summary>
        public bool Matches(Element element) => ExtrasIn(element) != null;

        /// <summary>
        /// How many extra toppings <paramref name="element"/> holds beyond what this item lists, at any depth, or null
        /// if it doesn't satisfy the item. Each extra counts once; what is inside an extra bread doesn't add to it.
        /// When the listed contents could be matched in several ways, the way with the fewest extras counts.
        /// </summary>
        public int? ExtrasIn(Element element)
        {
            if (element == null || element.Symbol != Symbol) return null;
            var children = element.Child != null ? element.Child.Elements.ToList() : new List<Element>();
            if (children.Count < Contents.Count) return null;
            // Match each listed content against each child once, up front.
            var fits = new int?[Contents.Count, children.Count];
            for (int k = 0; k < Contents.Count; k++)
                for (int i = 0; i < children.Count; i++)
                    fits[k, i] = Contents[k].ExtrasIn(children[i]);
            int best = int.MaxValue;
            AssignContents(fits, 0, new bool[children.Count], children.Count - Contents.Count, ref best);
            return best == int.MaxValue ? (int?)null : best;
        }

        // Backtracking: each listed content needs its own child; unmatched children are extras.
        void AssignContents(int?[,] fits, int k, bool[] used, int extras, ref int best)
        {
            if (extras >= best) return;
            if (k == Contents.Count) { best = extras; return; }
            for (int i = 0; i < used.Length; i++)
            {
                if (used[i] || !(fits[k, i] is int inner)) continue;
                used[i] = true;
                AssignContents(fits, k + 1, used, extras + inner, ref best);
                used[i] = false;
            }
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

        /// <summary>
        /// True if every bread in this item holds only what its <see cref="ContentRules"/> allow, e.g. a C holds one of
        /// its listed topping sets, and an E holds at most one G or I anywhere inside it.
        /// </summary>
        public bool FollowsContentRules(Rules rules) =>
            rules.ContentRulesOf(Symbol).Allows(Contents.Select(c => c.Symbol).ToArray(), Inside(), rules) &&
            Contents.All(c => c.FollowsContentRules(rules));

        /// <summary>Every component inside this item, at any depth (not the item itself).</summary>
        public IEnumerable<Symbol> Inside() => Contents.SelectMany(c => c.Everything());

        /// <summary>This item and every component inside it.</summary>
        public IEnumerable<Symbol> Everything() => Inside().Prepend(Symbol);

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
    /// A request for up to <see cref="MaxItems"/> items. Once every item has been handed in (one at a time), it gives
    /// the components in <see cref="RewardComponents"/> and a tip in money. A perfect order earns the full
    /// <see cref="Tip"/>; each extra topping handed in makes it imperfect and cuts the tip (<see cref="TipAfter"/>).
    /// The components are given either way.
    /// </summary>
    public sealed class Order
    {
        public const int MaxItems = 3;
        /// <summary>At this many imperfections the tip is gone.</summary>
        public const int ImperfectionsToLoseTip = 5;
        /// <summary>The share of the tip earned with 0, 1, 2, ... extra toppings, up to <see cref="ImperfectionsToLoseTip"/>.</summary>
        static readonly double[] TipShares = { 1.0, 0.6, 0.3, 0.1, 0.05, 0.0 };

        readonly bool[] filled;

        public int Id { get; }
        public IReadOnlyList<ItemSpec> Items { get; }

        /// <summary>The reward's total value for a perfect order: the tip plus the price of each component given.</summary>
        public int Reward { get; }

        /// <summary>The tip a perfect order earns.</summary>
        public int Tip { get; }

        /// <summary>Components given to the player's stock, each worth its price (bread comes empty).</summary>
        public IReadOnlyList<Symbol> RewardComponents { get; }

        /// <summary>Extra toppings handed in so far, over all filled items.</summary>
        public int Imperfections { get; private set; }

        /// <summary>The tip this order earns with the imperfections so far.</summary>
        public int TipEarned => TipAfter(Imperfections);

        /// <summary>The special order for the target bagel; fulfilling it wins the game.</summary>
        public bool IsFinal { get; }

        /// <summary>An order whose whole reward is the tip.</summary>
        public Order(int id, IEnumerable<ItemSpec> items, int reward, bool isFinal = false)
            : this(id, items, reward, null, reward, isFinal) { }

        /// <summary>An order worth <paramref name="reward"/> in all: <paramref name="components"/> plus a perfect tip of <paramref name="tip"/>.</summary>
        public Order(int id, IEnumerable<ItemSpec> items, int reward, IEnumerable<Symbol> components, int tip, bool isFinal = false)
        {
            Id = id;
            Items = items.ToArray();
            if (Items.Count == 0 || Items.Count > MaxItems)
                throw new ArgumentException($"An order needs 1 to {MaxItems} items.");
            if (tip < 0 || tip > reward) throw new ArgumentException("The tip must be between 0 and the reward.");
            Reward = reward;
            Tip = tip;
            RewardComponents = (components ?? Enumerable.Empty<Symbol>()).ToArray();
            IsFinal = isFinal;
            filled = new bool[Items.Count];
        }

        /// <summary>
        /// The tip after <paramref name="imperfections"/> extra toppings: all of it for none, then 60%, 30%, 10%
        /// and 5%, and nothing from <see cref="ImperfectionsToLoseTip"/> on.
        /// </summary>
        public int TipAfter(int imperfections)
        {
            if (imperfections <= 0) return Tip;
            if (imperfections >= ImperfectionsToLoseTip) return 0;
            return (int)Math.Round(Tip * TipShares[imperfections]);
        }

        /// <summary>
        /// The reward as the player sees it, e.g. "C + tip" or "tip": the components, and whether there is a tip, but
        /// not how much (the player only finds that out on completing the order).
        /// </summary>
        public string RewardText()
        {
            var parts = RewardComponents.GroupBy(s => s).OrderByDescending(g => g.Key)
                                        .Select(g => g.Count() > 1 ? $"{g.Count()}×{g.Key}" : g.Key.ToString()).ToList();
            if (Tip > 0 || parts.Count == 0) parts.Add("tip");
            return string.Join(" + ", parts);
        }

        public bool IsFilled(int item) => filled[item];
        public bool IsComplete => filled.All(f => f);

        /// <summary>
        /// The unfilled item <paramref name="element"/> matches the most of, or -1: of the items it satisfies, the one
        /// that asks for the most components (all of which it holds), then the one it fits with the fewest extra
        /// toppings, then the first. For example, an H holding an A and a B goes to "H holding an A" over a plain
        /// "H". <paramref name="extras"/> is how many extras it holds for that item.
        /// </summary>
        public int FindItemFor(Element element, out int extras)
        {
            int best = -1;
            extras = 0;
            for (int i = 0; i < Items.Count; i++)
            {
                if (filled[i] || !(Items[i].ExtrasIn(element) is int n)) continue;
                bool better = best < 0 || Items[i].Size > Items[best].Size ||
                              (Items[i].Size == Items[best].Size && n < extras);
                if (better) { best = i; extras = n; }
            }
            return best;
        }

        public int FindItemFor(Element element) => FindItemFor(element, out _);

        internal void Fill(int item, int extras = 0)
        {
            filled[item] = true;
            Imperfections += extras;
        }

        public override string ToString() =>
            $"Order {Id}: {string.Join(" + ", Items)} for {RewardText()} (tip ${Tip}){(IsFinal ? " (final)" : "")}";
    }

    /// <summary>
    /// Makes random orders from what is currently unlocked and priced (design.md, "Orders"): at most five items, a
    /// reward worth about the total item cost (components plus a tip), bread nested no deeper than each bread's limit and filled only as
    /// its <see cref="ContentRules"/> allow, and only contents that can actually be built, i.e. that have an arrangement on their board that sets off no active formula.
    /// Components are picked by how recently they were unlocked, and one layer holds only a few high-level components.
    /// </summary>
    public sealed class OrderGenerator
    {
        const int MaxContents = 4;
        const int Attempts = 40;
        const int ArrangementTries = 24;

        /// <summary>This many of the most recently unlocked components are picked at full weight.</summary>
        public const int FullWeightCount = 3;
        /// <summary>Each step further back in the unlock order multiplies the weight by this.</summary>
        public const double WeightFalloff = 0.5;
        /// <summary>No component's weight drops below this, so older ones still turn up now and then.</summary>
        public const double MinWeight = 0.05;
        /// <summary>One layer (an order's items, or one bread's direct contents) usually holds at most this many high-level components...</summary>
        public const int UsualHighLevelPerLayer = 2;
        /// <summary>...and never more than this.</summary>
        public const int MaxHighLevelPerLayer = 3;
        /// <summary>Once a layer has its usual number of high-level components, the chance it may still take one more.</summary>
        const double ExtraHighLevelChance = 0.25;

        /// <summary>A reward never gives any of this many most recently unlocked components.</summary>
        public const int RecentNotGiven = 2;
        /// <summary>A reward gives at most this many components.</summary>
        public const int MaxRewardComponents = 5;
        /// <summary>A reward is worth about this share of the order's cost...</summary>
        public const double RewardShare = 1.0;
        /// <summary>...give or take this much of the cost.</summary>
        public const double RewardJitter = 0.05;
        /// <summary>The components aim to be worth about this share of the reward; the tip is the rest...</summary>
        public const double ComponentShare = 2.0 / 3.0;
        /// <summary>...and are never worth more than this share of it.</summary>
        public const double MaxComponentShare = 0.8;
        /// <summary>Components stop being added once they reach this share of their target.</summary>
        const double CloseEnough = 0.8;
        /// <summary>
        /// Among the components that still fit, the most valuable is picked at full weight and each cheaper one at
        /// this times the weight of the one above it.
        /// </summary>
        const double RewardWeightFalloff = 0.5;

        readonly Rules rules;
        readonly Random random;
        readonly Dictionary<Symbol, double> weights = new Dictionary<Symbol, double>();
        int nextId = 1;

        /// <summary>
        /// A limit from an enclosing bread that counts everything inside it (e.g. an E's "at most one G or I"), with
        /// how many limited components it still has room for.
        /// </summary>
        readonly struct Cap
        {
            public readonly ContentRules Rules;
            public readonly int Left;
            public Cap(ContentRules rules, int left) { Rules = rules; Left = left; }
            public Cap Minus(int n) => new Cap(Rules, Left - n);
        }

        public OrderGenerator(Rules rules, int seed)
        {
            this.rules = rules;
            random = new Random(seed);
        }

        /// <summary>
        /// The tutorial's first order (design.md, "Tutorial"): a B, and a C with an A inside, rewarded like any other
        /// order of that cost. Null if B or C isn't unlocked.
        /// </summary>
        public Order First(Progression progress)
        {
            if (!progress.IsUnlocked(Symbol.B) || !progress.IsUnlocked(Symbol.C)) return null;
            var specs = new[] { new ItemSpec(Symbol.B), new ItemSpec(Symbol.C, new ItemSpec(Symbol.A)) };
            var forSale = rules.Catalog.Where(i => i.Price > 0 && progress.IsUnlocked(i.Symbol)).ToList();
            return MakeOrder(specs, specs.Sum(s => s.Cost(rules)), forSale);
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

            weights.Clear();
            for (int i = 0; i < available.Count; i++)
                weights[available[i].Symbol] = RecencyWeight(available.Count - 1 - i);

            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                int maxItems = Math.Min(Order.MaxItems, 1 + items.Count / 2);
                int count = 1 + random.Next(maxItems);
                var specs = new List<ItemSpec>();
                int high = 0;
                for (int k = 0; k < count; k++)
                {
                    var pool = LayerPool(items, high);
                    if (pool.Count == 0) break;
                    var spec = RandomItem(pool, available, null, Array.Empty<Cap>(), isActive);
                    if (rules.IsHighLevel(spec.Symbol)) high++;
                    specs.Add(spec);
                }
                int cost = specs.Sum(s => s.Cost(rules));
                if (cost <= 0) continue;
                return MakeOrder(specs, cost, items);
            }
            var cheapest = items.OrderBy(i => i.Price).First();
            return MakeOrder(new[] { new ItemSpec(cheapest.Symbol) }, cheapest.Price.Value, items);
        }

        Order MakeOrder(IReadOnlyList<ItemSpec> specs, int cost, List<PastryInfo> forSale)
        {
            int reward = Reward(cost);
            var components = RewardComponents(reward, forSale);
            int tip = reward - components.Sum(s => rules.PriceOf(s).Value);
            return new Order(nextId++, specs, reward, components, tip);
        }

        /// <summary>
        /// The components a reward worth <paramref name="value"/> gives (design.md, "Orders"); the tip is the rest.
        /// They aim for <see cref="ComponentShare"/> of the value, never above <see cref="MaxComponentShare"/>.
        /// Components come from <paramref name="forSale"/> (unlocked, priced, not A, in unlock order) minus the
        /// <see cref="RecentNotGiven"/> newest. Each pick favours the most valuable component that still fits, so
        /// rewards shift to better components as the player progresses, with some randomness. Early on, when nothing
        /// can be given, the whole reward is the tip.
        /// </summary>
        List<Symbol> RewardComponents(int value, List<PastryInfo> forSale)
        {
            var giveable = forSale.Take(Math.Max(0, forSale.Count - RecentNotGiven)).ToList();
            double target = value * ComponentShare;
            int cap = (int)Math.Floor(value * MaxComponentShare);
            var components = new List<Symbol>();
            int given = 0;
            while (components.Count < MaxRewardComponents && given < target * CloseEnough)
            {
                var fits = giveable.Where(i => given + i.Price <= cap).OrderByDescending(i => i.Price).ToList();
                if (fits.Count == 0) break;
                var weight = new Dictionary<PastryInfo, double>();
                for (int k = 0; k < fits.Count; k++) weight[fits[k]] = Math.Pow(RewardWeightFalloff, k);
                var pick = Pick(fits, i => weight[i]);
                components.Add(pick.Symbol);
                given += pick.Price.Value;
            }
            return components;
        }

        /// <summary>The reward's total value: about <see cref="RewardShare"/> of the cost, give or take <see cref="RewardJitter"/>.</summary>
        int Reward(int cost)
        {
            double share = RewardShare + (random.NextDouble() * 2 - 1) * RewardJitter;
            return Math.Max(1, (int)Math.Round(cost * share));
        }

        /// <summary>
        /// How likely a component is to be picked, relative to the newest: <paramref name="stepsBack"/> is how many
        /// components were unlocked after it (0 for the newest). The newest <see cref="FullWeightCount"/> get full
        /// weight, each step further back halves it, and it never drops below <see cref="MinWeight"/>.
        /// </summary>
        public static double RecencyWeight(int stepsBack) =>
            stepsBack < FullWeightCount ? 1.0
                : Math.Max(MinWeight, Math.Pow(WeightFalloff, stepsBack - FullWeightCount + 1));

        double Weight(Symbol symbol) => weights.TryGetValue(symbol, out double w) ? w : 1.0;

        T Pick<T>(IReadOnlyList<T> list, Func<T, double> weight)
        {
            double total = list.Sum(weight);
            double r = random.NextDouble() * total;
            foreach (var x in list)
            {
                r -= weight(x);
                if (r < 0) return x;
            }
            return list[list.Count - 1];
        }

        /// <summary>
        /// What may still go in a layer that already holds <paramref name="high"/> high-level components: past
        /// <see cref="UsualHighLevelPerLayer"/> another one is only sometimes allowed, and past
        /// <see cref="MaxHighLevelPerLayer"/> never.
        /// </summary>
        List<PastryInfo> LayerPool(List<PastryInfo> pool, int high)
        {
            bool allowHigh = high < UsualHighLevelPerLayer ||
                             (high < MaxHighLevelPerLayer && random.NextDouble() < ExtraHighLevelChance);
            return allowHigh ? pool : pool.Where(c => !c.HighLevel).ToList();
        }

        /// <summary>True if one more <paramref name="symbol"/> fits within every enclosing bread's limits.</summary>
        bool MayAdd(Symbol symbol, IReadOnlyList<Cap> caps) =>
            caps.All(c => c.Rules.MayHold(symbol, rules) && (!c.Rules.IsLimited(symbol) || c.Left > 0));

        /// <summary>Uses up room in each limit for everything in <paramref name="item"/>.</summary>
        static void Use(List<Cap> caps, ItemSpec item)
        {
            for (int i = 0; i < caps.Count; i++)
                caps[i] = caps[i].Minus(item.Everything().Count(caps[i].Rules.IsLimited));
        }

        /// <summary>
        /// A random item. <paramref name="budget"/> is how many content levels the parent leaves (null at the top);
        /// a bread may fill min(its own limit, budget) levels, and with one level left its contents are bagels only.
        /// <paramref name="caps"/> are the limits of enclosing breads that count everything inside them; the caller
        /// has checked that the item itself fits them.
        /// </summary>
        ItemSpec RandomItem(List<PastryInfo> choices, List<PastryInfo> contentChoices, int? budget, IReadOnlyList<Cap> caps,
                            Func<Symbol, bool> isActive)
        {
            var info = Pick(choices, c => Weight(c.Symbol));
            var layout = rules.LayoutOf(info.Symbol);
            if (layout == null || random.NextDouble() < 0.4) return new ItemSpec(info.Symbol);

            int levels = Math.Min(info.OrderLevels, budget ?? int.MaxValue);
            // What may sit at the next level down at all, before any content rules.
            var deeper = levels >= 2 ? contentChoices : contentChoices.Where(c => rules.LayoutOf(c.Symbol) == null).ToList();
            var allowed = info.ContentRules;
            // The limits on everything inside this bread: the enclosing ones (minus the room this bread itself takes)
            // and its own.
            var inside = caps.Select(c => c.Rules.IsLimited(info.Symbol) ? c.Minus(1) : c).ToList();
            if (allowed.Limited.Count > 0 || allowed.Highest != null) inside.Add(new Cap(allowed, allowed.LimitedMax));
            var inner = deeper.Where(c => MayAdd(c.Symbol, inside)).ToList();
            if (inner.Count == 0) return new ItemSpec(info.Symbol);

            var contents = new List<ItemSpec>();
            if (allowed.AllowedSets.Count > 0)
            {
                // Only the listed topping sets whose every topping is available and fits the limits. A set is as
                // likely as its newest topping.
                var sets = allowed.AllowedSets.Where(set =>
                    set.Count <= layout.SlotCount &&
                    set.All(s => inner.Any(c => c.Symbol == s)) &&
                    set.Count(rules.IsHighLevel) <= MaxHighLevelPerLayer &&
                    inside.All(c => set.Count(c.Rules.IsLimited) <= c.Left)).ToList();
                if (sets.Count == 0) return new ItemSpec(info.Symbol);
                foreach (var s in Pick(sets, set => set.Max(Weight)))
                {
                    var item = RandomItem(new List<PastryInfo> { inner.First(c => c.Symbol == s) }, deeper, levels - 1, inside, isActive);
                    Use(inside, item);
                    contents.Add(item);
                }
            }
            else
            {
                int count = 1 + random.Next(Math.Min(MaxContents, layout.SlotCount));
                int high = 0;
                for (int k = 0; k < count; k++)
                {
                    var pool = LayerPool(inner.Where(c => MayAdd(c.Symbol, inside)).ToList(), high);
                    if (pool.Count == 0) break;
                    var item = RandomItem(pool, deeper, levels - 1, inside, isActive);
                    Use(inside, item);
                    if (rules.IsHighLevel(item.Symbol)) high++;
                    contents.Add(item);
                }
            }
            var spec = new ItemSpec(info.Symbol, contents.ToArray());
            return spec.FollowsContentRules(rules) && CanBuild(spec, isActive) ? spec : new ItemSpec(info.Symbol);
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
