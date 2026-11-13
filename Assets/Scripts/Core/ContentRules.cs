using System;
using System.Collections.Generic;
using System.Linq;

namespace Bagels.Core
{
    /// <summary>
    /// What an order may put directly inside one kind of bread (design.md, "Orders"). The rules cover the bread's own
    /// contents only; bread inside it follows its own rules. An empty bread is always allowed.
    /// </summary>
    public sealed class ContentRules
    {
        public static readonly ContentRules Any = new ContentRules();

        /// <summary>
        /// If not empty, the contents must be exactly one of these sets (order doesn't matter, repeats count), e.g.
        /// {B, A} for "a B and an A".
        /// </summary>
        public IReadOnlyList<IReadOnlyList<Symbol>> AllowedSets { get; }

        /// <summary>At most <see cref="LimitedMax"/> of the contents, in total, may be any of these.</summary>
        public IReadOnlyCollection<Symbol> Limited { get; }

        public int LimitedMax { get; }

        /// <summary>If set, no content may come after this component in the catalog (formula-list) order.</summary>
        public Symbol? Highest { get; }

        public ContentRules(IEnumerable<IEnumerable<Symbol>> allowedSets = null, IEnumerable<Symbol> limited = null,
                            int limitedMax = 1, Symbol? highest = null)
        {
            AllowedSets = (allowedSets ?? Enumerable.Empty<IEnumerable<Symbol>>())
                          .Select(s => (IReadOnlyList<Symbol>)s.ToArray()).Where(s => s.Count > 0).ToArray();
            Limited = new HashSet<Symbol>(limited ?? Enumerable.Empty<Symbol>());
            LimitedMax = Math.Max(0, limitedMax);
            Highest = highest;
        }

        public bool IsLimited(Symbol symbol) => Limited.Contains(symbol);

        /// <summary>True if <paramref name="symbol"/> is not ruled out by <see cref="Highest"/>.</summary>
        public bool MayHold(Symbol symbol, Rules rules) =>
            Highest is not Symbol cap || rules.RankOf(symbol) <= rules.RankOf(cap);

        /// <summary>True if an order may ask for exactly these contents.</summary>
        public bool Allows(IReadOnlyCollection<Symbol> contents, Rules rules)
        {
            if (contents.Count == 0) return true;
            if (!contents.All(s => MayHold(s, rules))) return false;
            if (contents.Count(IsLimited) > LimitedMax) return false;
            return AllowedSets.Count == 0 || AllowedSets.Any(set => SameMultiset(set, contents));
        }

        static bool SameMultiset(IReadOnlyCollection<Symbol> a, IReadOnlyCollection<Symbol> b) =>
            a.Count == b.Count && a.OrderBy(s => s).SequenceEqual(b.OrderBy(s => s));

        /// <summary>Reads symbols written as letters, e.g. "BA" → {B, A}. Spaces and commas are ignored.</summary>
        public static Symbol[] ParseSymbols(string letters)
        {
            var result = new List<Symbol>();
            foreach (char ch in letters ?? "")
            {
                if (char.IsWhiteSpace(ch) || ch == ',') continue;
                if (!Enum.TryParse(ch.ToString().ToUpperInvariant(), out Symbol s))
                    throw new FormatException($"'{ch}' in \"{letters}\" is not a pastry symbol.");
                result.Add(s);
            }
            return result.ToArray();
        }
    }
}
