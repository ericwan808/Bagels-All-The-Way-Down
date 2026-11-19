using Bagels.Data;
using UnityEngine;

namespace Bagels.View
{
    /// <summary>
    /// Prefab component for one pastry on the board: a circle sprite (1 unit across at scale 1) with a letter.
    /// Scale is set from the radius, so the label sizes with it.
    /// </summary>
    public sealed class PastryView : MonoBehaviour
    {
        [SerializeField] SpriteRenderer body;
        [SerializeField] TextMesh label;
        [Tooltip("Letter height as a fraction of the circle's diameter.")]
        [SerializeField] float labelHeight = 0.55f;

        public PastryType Type { get; private set; }

        public void Bind(PastryType type, float radius, int sortingOrder, bool showLabel = true)
        {
            Type = type;
            transform.localScale = Vector3.one * (radius * 2f);
            // Artwork is drawn as-is (its sprite is imported 1 unit across, like the circle) and carries no letter;
            // the plain circle is tinted with the type's colour and labelled.
            bool art = type != null && type.Sprite != null;
            if (art) body.sprite = type.Sprite;
            body.color = art ? Color.white : type != null ? type.Color : Color.gray;
            body.sortingOrder = sortingOrder;

            label.gameObject.SetActive(showLabel && !art);
            label.text = type != null ? type.Symbol.ToString() : "?";
            label.color = LabelColor(ViewColors.TextOn(body.color));
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 128;
            label.characterSize = labelHeight / 12.8f; // TextMesh glyphs are ~fontSize/10 units tall at characterSize 1
            var mr = label.GetComponent<MeshRenderer>();
            mr.sortingOrder = sortingOrder + 1;
            if (label.font != null) mr.sharedMaterial = label.font.material;
        }

        public void SetAlpha(float alpha)
        {
            var c = body.color; c.a = alpha; body.color = c;
            var t = label.color; t.a = alpha; label.color = t;
        }

        // The legacy text shader uses vertex colours as-is, so in a linear-colour-space project they must be
        // converted up front to look the same as sprite colours.
        static Color LabelColor(Color srgb) =>
            QualitySettings.activeColorSpace == ColorSpace.Linear ? srgb.linear : srgb;

#if UNITY_EDITOR
        /// <summary>Used by editor tooling when building the prefab.</summary>
        public void EditorInit(SpriteRenderer body, TextMesh label)
        {
            this.body = body;
            this.label = label;
        }
#endif
    }
}
