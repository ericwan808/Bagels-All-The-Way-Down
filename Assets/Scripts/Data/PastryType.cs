using Bagels.Core;
using UnityEngine;

namespace Bagels.Data
{
    // Append new kinds at the end: assets store the numeric value.
    public enum LayoutKind { None, P3, K3, W7, K23 }

    /// <summary>Authoring data for one pastry type. A non-None layout makes it bread.</summary>
    [CreateAssetMenu(menuName = "Bagels/Pastry Type", fileName = "PastryType")]
    public sealed class PastryType : ScriptableObject
    {
        [SerializeField] string displayName;
        [SerializeField] Symbol symbol;
        [Tooltip("Optional sprite override; the view falls back to a plain circle.")]
        [SerializeField] Sprite sprite;
        [SerializeField] Color color = Color.white;
        [Tooltip("Slot layout if this pastry is bread; None for a plain bagel.")]
        [SerializeField] LayoutKind layout;
        [Header("Economy")]
        [Tooltip("Can be bought once unlocked. Off for components that can only be crafted (K, L).")]
        [SerializeField] bool purchasable = true;
        [SerializeField] int price;
        [Tooltip("Known from the start (A, B, C).")]
        [SerializeField] bool unlockedAtStart;
        [Tooltip("Unlocks together with the component before it in the formula list (F with E).")]
        [SerializeField] bool unlocksWithPrevious;
        [Tooltip("Bread only: how many levels deep an order may fill it (C 1 = only bagels inside, E 2, H 3).")]
        [Min(1)] [SerializeField] int orderLevels = 1;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public Symbol Symbol => symbol;
        public Sprite Sprite => sprite;
        public Color Color => color;
        public LayoutKind Layout => layout;
        public bool IsBread => layout != LayoutKind.None;
        public int? Price => purchasable ? price : (int?)null;
        public bool UnlockedAtStart => unlockedAtStart;
        public bool UnlocksWithPrevious => unlocksWithPrevious;

        public int OrderLevels => orderLevels;

        public PastryInfo ToInfo() =>
            new PastryInfo(symbol, DisplayName, Price, unlockedAtStart, unlocksWithPrevious, orderLevels);

        public BoardLayout ToBoardLayout() => layout switch
        {
            LayoutKind.P3 => BoardLayout.P3,
            LayoutKind.K3 => BoardLayout.K3,
            LayoutKind.W7 => BoardLayout.W7,
            LayoutKind.K23 => BoardLayout.K23,
            _ => null,
        };

#if UNITY_EDITOR
        /// <summary>Used by editor tooling to author assets.</summary>
        public void EditorInit(string displayName, Symbol symbol, Color color, LayoutKind layout)
        {
            this.displayName = displayName;
            this.symbol = symbol;
            this.color = color;
            this.layout = layout;
        }

        /// <summary>Used by editor tooling. A null price makes the component craft-only.</summary>
        public void EditorInitEconomy(int? price, bool unlockedAtStart, bool unlocksWithPrevious = false, int orderLevels = 1)
        {
            this.orderLevels = Mathf.Max(1, orderLevels);
            purchasable = price != null;
            this.price = price ?? 0;
            this.unlockedAtStart = unlockedAtStart;
            this.unlocksWithPrevious = unlocksWithPrevious;
        }
#endif
    }
}
