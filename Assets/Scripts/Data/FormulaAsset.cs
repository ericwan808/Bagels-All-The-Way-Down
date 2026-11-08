using System;
using System.Linq;
using Bagels.Core;
using UnityEngine;

namespace Bagels.Data
{
    /// <summary>Authoring data for one formula: ingredient nodes, the adjacencies between them, and the product.</summary>
    [CreateAssetMenu(menuName = "Bagels/Formula", fileName = "Formula")]
    public sealed class FormulaAsset : ScriptableObject
    {
        [Serializable]
        public sealed class Ingredient
        {
            public PastryType type;
            [Tooltip("Only consumed if it is the element that triggered the formula.")]
            public bool catalyst;
        }

        [Serializable]
        public sealed class Link
        {
            [Tooltip("Index into Ingredients.")] public int from;
            [Tooltip("Index into Ingredients.")] public int to;
            [Tooltip("Where 'to' sits relative to 'from'. Down = 'to' is a child of a board next to 'from'.")]
            public AdjacencyKind kind;
        }

        [SerializeField] Ingredient[] ingredients = Array.Empty<Ingredient>();
        [SerializeField] Link[] links = Array.Empty<Link>();
        [SerializeField] PastryType product;

        public Ingredient[] Ingredients => ingredients;
        public Link[] Links => links;
        public PastryType Product => product;

        public Formula ToFormula()
        {
            if (product == null) throw new InvalidOperationException($"Formula '{name}' has no product.");
            if (ingredients.Any(i => i.type == null))
                throw new InvalidOperationException($"Formula '{name}' has an ingredient with no type.");

            return new Formula(name, product.Symbol,
                ingredients.Select(i => new PatternNode(i.type.Symbol, i.catalyst)),
                links.Select(l => new PatternEdge(l.from, l.to, l.kind)));
        }

#if UNITY_EDITOR
        /// <summary>Used by editor tooling to author assets.</summary>
        public void EditorInit(PastryType product, Ingredient[] ingredients, Link[] links)
        {
            this.product = product;
            this.ingredients = ingredients;
            this.links = links;
        }
#endif
    }
}
