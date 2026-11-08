using System;
using System.Collections.Generic;
using System.Linq;

namespace Bagels.Core
{
    /// <summary>Everything that defines a level: formulas (in priority order), which symbols are bread, root and target.</summary>
    public sealed class Rules
    {
        readonly Dictionary<Symbol, BoardLayout> breadLayouts;

        public Symbol RootSymbol { get; }

        /// <summary>Checked in order; the first formula with a match fires.</summary>
        public IReadOnlyList<Formula> Formulas { get; }

        public Symbol? Target { get; }

        public Rules(Symbol rootSymbol, IEnumerable<Formula> formulas,
                     IReadOnlyDictionary<Symbol, BoardLayout> breadLayouts, Symbol? target = null)
        {
            this.breadLayouts = breadLayouts.ToDictionary(kv => kv.Key, kv => kv.Value);
            if (!this.breadLayouts.ContainsKey(rootSymbol))
                throw new ArgumentException($"Root symbol {rootSymbol} must be bread (have a board layout).");
            RootSymbol = rootSymbol;
            Formulas = formulas.ToArray();
            Target = target;
        }

        /// <summary>The board layout for a bread symbol, or null if the symbol is a plain bagel.</summary>
        public BoardLayout LayoutOf(Symbol symbol) =>
            breadLayouts.TryGetValue(symbol, out var layout) ? layout : null;
    }
}
