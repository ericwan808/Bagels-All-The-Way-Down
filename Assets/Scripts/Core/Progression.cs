using System;
using System.Collections.Generic;

namespace Bagels.Core
{
    /// <summary>
    /// Which components are unlocked (see design.md, "Unlocking formulas"). Components unlock in catalog order: a
    /// priced one when the player buys its formula (<see cref="Purchase"/>), an unpriced one once the component
    /// before it has been crafted by a formula. Unlocks are permanent; undo does not take them back.
    /// </summary>
    public sealed class Progression
    {
        readonly IReadOnlyList<PastryInfo> catalog;
        readonly HashSet<Symbol> unlocked = new HashSet<Symbol>();
        readonly HashSet<Symbol> crafted = new HashSet<Symbol>();

        public Progression(IReadOnlyList<PastryInfo> catalog)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            foreach (var info in catalog)
                if (info.UnlockedAtStart) unlocked.Add(info.Symbol);
        }

        public bool IsUnlocked(Symbol symbol) => unlocked.Contains(symbol);

        /// <summary>True once a formula has created this symbol (buying one does not count).</summary>
        public bool WasCrafted(Symbol symbol) => crafted.Contains(symbol);

        public void NoteCrafted(Symbol symbol) => crafted.Add(symbol);

        /// <summary>The next component to unlock, or null when everything is unlocked.</summary>
        public PastryInfo NextLocked => NextLockedIndex() is int i ? catalog[i] : null;

        /// <summary>The component before <paramref name="info"/> in the list, whose crafting unlocks an unpriced one.</summary>
        public PastryInfo Previous(PastryInfo info)
        {
            for (int i = 1; i < catalog.Count; i++)
                if (catalog[i] == info) return catalog[i - 1];
            return null;
        }

        /// <summary>
        /// What the next formula costs to buy, or null if it isn't bought: it unlocks by crafting instead (K, L), or
        /// everything is unlocked. The price is the component's own price.
        /// </summary>
        public int? NextFormulaPrice => NextLockedIndex() is int i ? catalog[i].Price : null;

        /// <summary>
        /// Unlocks the next formula, which must be one that is bought (<see cref="NextFormulaPrice"/>), then anything
        /// whose crafting condition already holds. Paying is the caller's job.
        /// Returns what was newly unlocked.
        /// </summary>
        public List<PastryInfo> Purchase()
        {
            if (NextFormulaPrice == null) throw new InvalidOperationException("The next formula can't be bought.");
            var gained = new List<PastryInfo>();
            Unlock(NextLockedIndex().Value, gained);
            gained.AddRange(Update());
            return gained;
        }

        /// <summary>Unlocks every unpriced component whose crafting condition now holds, in order. Returns what was newly unlocked.</summary>
        public List<PastryInfo> Update() => Advance(0);

        /// <summary>
        /// For tests and setup: unlocks in order, as if every formula priced up to <paramref name="budget"/> had been
        /// bought (without paying), together with any crafting unlocks that hold.
        /// </summary>
        public List<PastryInfo> UnlockUpTo(int budget) => Advance(budget);

        List<PastryInfo> Advance(int budget)
        {
            var gained = new List<PastryInfo>();
            while (NextLockedIndex() is int i)
            {
                var info = catalog[i];
                bool ready = info.Price is int price ? budget >= price
                                                     : i > 0 && crafted.Contains(catalog[i - 1].Symbol);
                if (!ready) break;
                Unlock(i, gained);
            }
            return gained;
        }

        void Unlock(int i, List<PastryInfo> gained)
        {
            if (unlocked.Add(catalog[i].Symbol)) gained.Add(catalog[i]);
        }

        int? NextLockedIndex()
        {
            for (int i = 0; i < catalog.Count; i++)
                if (!unlocked.Contains(catalog[i].Symbol)) return i;
            return null;
        }
    }
}
