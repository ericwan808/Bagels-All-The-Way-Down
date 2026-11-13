using System;
using System.Collections.Generic;
using System.Linq;

namespace Bagels.Core
{
    /// <summary>Economy facts about one component, in formula-list order (see design.md, "Economy").</summary>
    public sealed class PastryInfo
    {
        public Symbol Symbol { get; }
        public string Name { get; }

        /// <summary>What it costs to buy; null for components that can only be crafted (K and L).</summary>
        public int? Price { get; }

        /// <summary>Known from the start (A, B and C).</summary>
        public bool UnlockedAtStart { get; }

        /// <summary>Unlocks together with the component before it in the list (F with E).</summary>
        public bool UnlocksWithPrevious { get; }

        /// <summary>
        /// For bread: how many levels of contents an order may ask this bread to hold, counted from it downwards
        /// (C 1: only bagels inside; E 2; H 3). A bread nested inside another gets the smaller of its own limit and
        /// what its parent has left.
        /// </summary>
        public int OrderLevels { get; }

        /// <summary>For bread: what an order may put directly inside it, beyond the nesting limit.</summary>
        public ContentRules ContentRules { get; }

        public PastryInfo(Symbol symbol, string name, int? price, bool unlockedAtStart = false, bool unlocksWithPrevious = false,
                          int orderLevels = 1, ContentRules contentRules = null)
        {
            OrderLevels = Math.Max(1, orderLevels);
            ContentRules = contentRules ?? ContentRules.Any;
            Symbol = symbol;
            Name = name ?? symbol.ToString();
            Price = price;
            UnlockedAtStart = unlockedAtStart;
            UnlocksWithPrevious = unlocksWithPrevious;
        }

        public override string ToString() => $"{Symbol} ({Name})";
    }

    /// <summary>Everything that defines a level: formulas (in priority order), which symbols are bread, root and target.</summary>
    public sealed class Rules
    {
        readonly Dictionary<Symbol, BoardLayout> breadLayouts;
        readonly Dictionary<Symbol, Formula[]> bySymbol = new Dictionary<Symbol, Formula[]>();
        readonly Dictionary<Symbol, int> rank = new Dictionary<Symbol, int>(); // first catalog index of each symbol

        public Symbol RootSymbol { get; }

        /// <summary>Checked in order; the first formula with a match fires.</summary>
        public IReadOnlyList<Formula> Formulas { get; }

        public Symbol? Target { get; }

        /// <summary>
        /// Every component in formula-list order (which is also the unlock order), starting with the free basic
        /// element. Empty for levels without an economy.
        /// </summary>
        public IReadOnlyList<PastryInfo> Catalog { get; }

        public Rules(Symbol rootSymbol, IEnumerable<Formula> formulas,
                     IReadOnlyDictionary<Symbol, BoardLayout> breadLayouts, Symbol? target = null,
                     IEnumerable<PastryInfo> catalog = null)
        {
            Catalog = (catalog ?? Enumerable.Empty<PastryInfo>()).ToArray();
            for (int i = 0; i < Catalog.Count; i++)
                if (!rank.ContainsKey(Catalog[i].Symbol)) rank[Catalog[i].Symbol] = i;
            this.breadLayouts = breadLayouts.ToDictionary(kv => kv.Key, kv => kv.Value);
            if (!this.breadLayouts.ContainsKey(rootSymbol))
                throw new ArgumentException($"Root symbol {rootSymbol} must be bread (have a board layout).");
            RootSymbol = rootSymbol;
            Formulas = formulas.ToArray();
            Target = target;
        }

        /// <summary>
        /// The formulas a <paramref name="symbol"/> element could trigger, in priority order. Only these need checking
        /// when such an element is placed or created.
        /// </summary>
        public IReadOnlyList<Formula> FormulasInvolving(Symbol symbol)
        {
            if (!bySymbol.TryGetValue(symbol, out var list))
                bySymbol[symbol] = list = Formulas.Where(f => f.Uses(symbol)).ToArray();
            return list;
        }

        public PastryInfo InfoOf(Symbol symbol) => rank.TryGetValue(symbol, out int i) ? Catalog[i] : null;

        public int? PriceOf(Symbol symbol) => InfoOf(symbol)?.Price;

        /// <summary>Position in the catalog (formula-list) order, or -1 if the symbol is not in it.</summary>
        public int RankOf(Symbol symbol) => rank.TryGetValue(symbol, out int i) ? i : -1;

        /// <summary>The content rules of a bread symbol; <see cref="ContentRules.Any"/> if it has none.</summary>
        public ContentRules ContentRulesOf(Symbol symbol) => InfoOf(symbol)?.ContentRules ?? ContentRules.Any;

        public bool IsBread(Symbol symbol) => breadLayouts.ContainsKey(symbol);

        /// <summary>The board layout for a bread symbol, or null if the symbol is a plain bagel.</summary>
        public BoardLayout LayoutOf(Symbol symbol) =>
            breadLayouts.TryGetValue(symbol, out var layout) ? layout : null;
    }
}
