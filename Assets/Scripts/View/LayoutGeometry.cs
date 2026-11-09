using Bagels.Core;
using UnityEngine;

namespace Bagels.View
{
    /// <summary>Where each slot of a layout is drawn, in a unit space centred on the board.</summary>
    public static class LayoutGeometry
    {
        /// <summary>Slot centres. Neighbouring slots are roughly 1 unit apart.</summary>
        public static Vector2[] Positions(BoardLayout layout)
        {
            switch (layout.Name)
            {
                case "W7":
                {
                    // Hub at the centre; rim slot 1 at the top, then clockwise.
                    var p = new Vector2[7];
                    for (int i = 1; i <= 6; i++) p[i] = OnCircle(90f - (i - 1) * 60f, 1f);
                    return p;
                }
                case "K3":
                    return new[] { OnCircle(90f, 1f), OnCircle(210f, 1f), OnCircle(330f, 1f) };
                case "P3":
                    return new[] { new Vector2(-1f, 0f), Vector2.zero, new Vector2(1f, 0f) };
                case "K2,3":
                    // The two-slot side above and below a row of the three-slot side.
                    return new[]
                    {
                        new Vector2(0f, 1f), new Vector2(0f, -1f),
                        new Vector2(-1.1f, 0f), Vector2.zero, new Vector2(1.1f, 0f),
                    };
                default:
                {
                    var p = new Vector2[layout.SlotCount];
                    for (int i = 0; i < p.Length; i++) p[i] = OnCircle(90f - i * 360f / p.Length, 1f);
                    return p;
                }
            }
        }

        /// <summary>Radius of one slot circle in the same unit space.</summary>
        public static float SlotRadius(BoardLayout layout) => layout.Name switch
        {
            "W7" => 0.34f,
            "K3" => 0.5f,
            "P3" => 0.36f,
            "K2,3" => 0.34f,
            _ => Mathf.Min(0.4f, Mathf.Sin(Mathf.PI / Mathf.Max(2, layout.SlotCount)) * 0.8f),
        };

        /// <summary>Radius of a circle that contains every slot.</summary>
        public static float Bound(BoardLayout layout)
        {
            float max = 0f;
            foreach (var p in Positions(layout)) max = Mathf.Max(max, p.magnitude);
            return max + SlotRadius(layout);
        }

        static Vector2 OnCircle(float degrees, float radius)
        {
            float r = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * radius;
        }
    }
}
