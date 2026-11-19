using Bagels.Core;
using Bagels.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Bagels.View
{
    /// <summary>Small helpers for building uGUI elements in code.</summary>
    public static class UiFactory
    {
        static Font font;
        public static Font Font => font != null ? font : font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static RectTransform Rect(string name, Transform parent, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Image(string name, Transform parent, Sprite sprite, Color color, Vector2 size)
        {
            var rt = Rect(name, parent, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Text Text(string name, Transform parent, string content, int size, Color color,
                                TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(name, parent, Vector2.zero);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Stretches <paramref name="rt"/> to fill its parent.</summary>
        public static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>A straight bar from a to b (in the parent's local space).</summary>
        public static Image Line(Transform parent, Vector2 a, Vector2 b, float thickness, Color color)
        {
            var d = b - a;
            var img = Image("Line", parent, null, color, new Vector2(d.magnitude, thickness));
            var rt = img.rectTransform;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = a;
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            return img;
        }

        /// <summary>A line from a to b with an arrowhead whose tip touches b.</summary>
        public static void Arrow(Transform parent, Vector2 a, Vector2 b, float thickness, float headSize,
                                 Sprite triangle, Color color)
        {
            var dir = (b - a).normalized;
            var baseOfHead = b - dir * headSize;
            Line(parent, a, baseOfHead + dir * 1f, thickness, color);
            var head = Image("Arrowhead", parent, triangle, color, new Vector2(headSize, headSize));
            head.rectTransform.anchoredPosition = b - dir * (headSize * 0.5f);
            head.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        }

        /// <summary>
        /// A pastry icon: its artwork, or a coloured circle with its letter. Bread icons also show a miniature of their
        /// slot layout.
        /// </summary>
        public static RectTransform PastryIcon(Transform parent, PastryType type, float size, GameView view)
        {
            var color = type != null ? type.Color : Color.gray;
            bool art = type != null && type.Sprite != null;
            var disc = Image($"Icon {(type != null ? type.Symbol.ToString() : "?")}", parent,
                             art ? type.Sprite : view.CircleSprite, art ? Color.white : color,
                             new Vector2(size, size));
            disc.preserveAspect = art; // artwork isn't always square
            var root = disc.rectTransform;
            var layout = type != null ? type.ToBoardLayout() : null;

            if (layout == null)
            {
                if (art) return root; // the artwork identifies the pastry; no letter over it
                var label = Text("Letter", root, type != null ? type.Symbol.ToString() : "?",
                                 Mathf.RoundToInt(size * 0.55f), ViewColors.TextOn(color), TextAnchor.MiddleCenter, FontStyle.Bold);
                Fill(label.rectTransform);
                return root;
            }

            // Miniature board.
            var ink = ViewColors.Darken(color, 0.6f);
            var positions = LayoutGeometry.Positions(layout);
            float scale = size * 0.5f * 0.72f / LayoutGeometry.Bound(layout);
            float slotRadius = LayoutGeometry.SlotRadius(layout) * scale;
            for (int i = 0; i < layout.SlotCount; i++)
                foreach (int j in layout.Neighbors(i))
                {
                    if (j <= i) continue;
                    var a = positions[i] * scale;
                    var b = positions[j] * scale;
                    var dir = (b - a).normalized;
                    Line(root, a + dir * slotRadius, b - dir * slotRadius, Mathf.Max(1.5f, slotRadius * 0.18f), ink);
                }
            for (int i = 0; i < layout.SlotCount; i++)
            {
                var ring = Image("Slot", root, view.RingSprite, ink, Vector2.one * slotRadius * 2f);
                ring.rectTransform.anchoredPosition = positions[i] * scale;
            }
            return root;
        }

        /// <summary>
        /// An order item drawn the way it looks on the board: its pastry icon, and for bread its toppings drawn inside
        /// its slots (in slot order), each again with its own toppings, down to <paramref name="layers"/> layers.
        /// A bread below the last layer is drawn empty.
        /// </summary>
        public static RectTransform ItemIcon(Transform parent, ItemSpec spec, float size, GameView view, int layers)
        {
            var type = view.TypeOf(spec.Symbol);
            var root = PastryIcon(parent, type, size, view);
            var layout = type != null ? type.ToBoardLayout() : null;
            if (layout == null || layers <= 0 || spec.Contents.Count == 0) return root;

            var positions = LayoutGeometry.Positions(layout);
            float scale = size * 0.5f * 0.72f / LayoutGeometry.Bound(layout);
            float inner = LayoutGeometry.SlotRadius(layout) * scale * 2f;
            for (int k = 0; k < spec.Contents.Count && k < layout.SlotCount; k++)
            {
                var child = ItemIcon(root, spec.Contents[k], inner, view, layers - 1);
                child.anchoredPosition = positions[k] * scale;
            }
            return root;
        }
    }
}
