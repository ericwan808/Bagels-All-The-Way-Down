using System;
using System.Collections.Generic;
using System.Linq;
using Bagels.Core;
using UnityEngine;

namespace Bagels.Data
{
    /// <summary>
    /// Authoring data for a level. Formulas are listed in product order, which is also the unlock order; when several
    /// match, the one furthest down the list fires (design.md, "Formula list").
    /// </summary>
    [CreateAssetMenu(menuName = "Bagels/Level", fileName = "Level")]
    public sealed class LevelAsset : ScriptableObject
    {
        [SerializeField] PastryType root;
        [SerializeField] PastryType target;
        [Tooltip("The pastry the player drags out (infinite supply).")]
        [SerializeField] PastryType basic;
        [SerializeField] FormulaAsset[] formulas = Array.Empty<FormulaAsset>();

        public PastryType Root => root;
        public PastryType Target => target;
        public PastryType Basic => basic;
        /// <summary>In product (unlock) order.</summary>
        public IReadOnlyList<FormulaAsset> Formulas => formulas;

        /// <summary>The basic element, then every formula's product in list order: the unlock order.</summary>
        public IEnumerable<PastryType> Catalog()
        {
            var seen = new HashSet<PastryType>();
            if (basic != null && seen.Add(basic)) yield return basic;
            foreach (var f in formulas)
                if (f != null && f.Product != null && seen.Add(f.Product)) yield return f.Product;
        }

        /// <summary>Every pastry type referenced by this level, without duplicates.</summary>
        public IEnumerable<PastryType> PastryTypes()
        {
            var types = new List<PastryType> { root, target, basic };
            foreach (var f in formulas.Where(f => f != null))
            {
                types.Add(f.Product);
                types.AddRange(f.Ingredients.Select(i => i.type));
            }
            return types.Where(t => t != null).Distinct();
        }

        public Rules ToRules()
        {
            if (root == null) throw new InvalidOperationException($"Level '{name}' has no root pastry.");

            var layouts = new Dictionary<Symbol, BoardLayout>();
            var bySymbol = new Dictionary<Symbol, PastryType>();
            foreach (var t in PastryTypes())
            {
                if (bySymbol.TryGetValue(t.Symbol, out var other) && other != t)
                    throw new InvalidOperationException(
                        $"Level '{name}': '{t.name}' and '{other.name}' share symbol {t.Symbol}.");
                bySymbol[t.Symbol] = t;
                var layout = t.ToBoardLayout();
                if (layout != null) layouts[t.Symbol] = layout;
            }

            // The furthest-down formula wins, so the matcher checks the list bottom-up.
            return new Rules(root.Symbol,
                formulas.Where(f => f != null).Reverse().Select(f => f.ToFormula()),
                layouts,
                target != null ? target.Symbol : (Symbol?)null,
                Catalog().Select(t => t.ToInfo()));
        }

#if UNITY_EDITOR
        /// <summary>Used by editor tooling to author assets.</summary>
        public void EditorInit(PastryType root, PastryType target, PastryType basic, FormulaAsset[] formulas)
        {
            this.root = root;
            this.target = target;
            this.basic = basic;
            this.formulas = formulas;
        }
#endif
    }
}
