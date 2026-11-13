using System;
using System.Collections.Generic;
using System.Linq;

namespace Bagels.Core
{
    /// <summary>
    /// What an order may put inside one kind of bread (design.md, "Orders"). <see cref="AllowedSets"/> covers the
    /// bread's direct contents only; <see cref="Limited"/> and <see cref="Highest"/> cover everything inside it, at any
    /// depth. Bread inside it also follows its own rules. An empty bread is always allowed.
    /// </summary>
    public sealed class ContentRules
    {
        public static readonly ContentRules Any = new ContentRules();

        /// <summary>
        /// If not empty, the contents must be exactly one of these sets (order doesn't matter, repeats count), e.g.
        /// {B, A} for "a B and an A".
        /// </summary>
        public IReadOnlyList<IReadOnlyList<Symbol>> AllowedSets { get; }

        /// <summary>
        /// At most <see cref="LimitedMax"/> of everything inside the bread, in total and at any depth, may be any of these.
        /// </summary>
        public IReadOnlyCollection<Symbol> Limited { get; }

        public int LimitedMax { get; }

        /// <summary>
        /// If set, nothing inside the bread, at any depth, may come after this component in the catalog (formula-list) order.
        /// </summary>
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

        /// <summary>True if every rule that limits a whole subtree has room for these components.</summary>
        public bool Fits(IEnumerable<Symbol> everything, Rules rules)
        {
            int limited = 0;
            foreach (var s in everything)
            {
                if (!MayHold(s, rules)) return false;
                if (IsLimited(s) && ++limited > LimitedMax) return false;
            }
            return true;
        }

        /// <summary>
        /// True if an order may ask for exactly these contents: <paramref name="direct"/> are the bread's own toppings,
        /// <paramref name="everything"/> is every component inside it at any depth (the direct ones included).
        /// </summary>
        public bool Allows(IReadOnlyCollection<Symbol> direct, IEnumerable<Symbol> everything, Rules rules)
        {
            if (direct.Count == 0) return true;
            if (!Fits(everything, rules)) return false;
            return AllowedSets.Count == 0 || AllowedSets.Any(set => SameMultiset(set, direct));
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
