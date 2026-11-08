using UnityEngine;

namespace Bagels.View
{
    public static class ViewColors
    {
        public static readonly Color Ink = new Color(0.23f, 0.16f, 0.11f);
        public static readonly Color Paper = new Color(1f, 0.97f, 0.92f);

        /// <summary>Dark text on light fills, light text on dark fills.</summary>
        public static Color TextOn(Color fill)
        {
            float luminance = 0.2126f * fill.r + 0.7152f * fill.g + 0.0722f * fill.b;
            return luminance > 0.5f ? Ink : Paper;
        }

        public static Color Darken(Color c, float amount) =>
            new Color(c.r * (1f - amount), c.g * (1f - amount), c.b * (1f - amount), c.a);
    }
}
