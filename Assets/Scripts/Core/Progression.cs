using System;
using System.Collections.Generic;

namespace Bagels.Core
{
    /// <summary>
    /// Which components are unlocked (see design.md, "Unlocking formulas"). Components unlock in catalog order: a
    /// priced one as soon as current money reaches its price, an unpriced one once the component before it has been
    /// crafted by a formula. Unlocks are permanent; undo does not take them back.
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

        /// <summary>Unlocks everything whose condition now holds, in order. Returns what was newly unlocked.</summary>
        public List<PastryInfo> Update(int money)
        {
            var gained = new List<PastryInfo>();
            while (NextLockedIndex() is int i)
            {
                var info = catalog[i];
                bool ready = info.UnlocksWithPrevious && i > 0 && unlocked.Contains(catalog[i - 1].Symbol)
                             || (info.Price is int price ? money >= price
                                                         : i > 0 && crafted.Contains(catalog[i - 1].Symbol));
                if (!ready) break;
                Unlock(i, gained);
                // Followers unlock at the same time (F with E).
                for (int j = i + 1; j < catalog.Count && catalog[j].UnlocksWithPrevious; j++) Unlock(j, gained);
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
