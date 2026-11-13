using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Bagels.Core;
using UnityEngine;

namespace Bagels.View
{
    // Replaying a cascade as merge animations, and the geometry for placing any element relative to a focus bread.
    public sealed partial class GameView
    {
        // ---------------------------------------------------------------- formula animation

        /// <summary>Where a bread is drawn as an open disc: its centre, radius, and nesting depth below the focus.</summary>
        struct Frame
        {
            public Vector2 Center;
            public float Radius;
            public int Depth;
        }

        /// <summary>The frame of the current board: centred on the view at full size.</summary>
        Frame FocusFrame => new Frame { Center = transform.position, Radius = rootRadius, Depth = 0 };

        /// <summary>
        /// Replays a cascade one step at a time from the snapshots taken before each step, then draws the live board.
        /// The model has already moved on, so this only draws; nothing here touches the game state.
        /// </summary>
        IEnumerator PlayCascade(IReadOnlyList<CascadeStep> steps, int[] focusPath)
        {
            SetHover(null);
            SetBreadHover(null);
            targets.Clear();
            breads.Clear();
            elements.Clear();

            foreach (var step in steps)
            {
                var before = step.Before;
                if (before == null) break;
                int last = step.ProductPath.Length - 1;
                var owner = TreePath.Find(before, step.ProductPath.Take(last).ToArray());
                if (owner == null || !owner.IsBread) continue;
                int slot = step.ProductPath[last];
                var consumed = step.ConsumedPaths.Select(p => TreePath.Find(before, p)).Where(e => e != null).ToList();
                var stepFocus = AnimationFocus(before, focusPath, owner);

                // Everything that stays put, pushed far below; each consumed component on its own transform above.
                ClearBoard();
                drawHidden.Clear();
                drawHidden.UnionWith(consumed);
                var frame = DrawFrame(stepFocus, skip: null, orderOffset: BackFrameOffset, interactive: false);
                drawHidden.Clear();

                SlotPlacement(stepFocus, owner, slot, out var targetCenter, out var targetSlotRadius);
                var movers = new List<(Transform t, Vector2 from, float scale, Fader fade)>();
                foreach (var e in consumed)
                {
                    var mover = DrawMover(stepFocus, e, frame, out var center, out var radius);
                    float endScale = targetSlotRadius * mergeEndScale / Mathf.Max(1e-4f, radius);
                    movers.Add((mover, center, endScale, new Fader(mover)));
                }

                for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / Mathf.Max(0.01f, mergeDuration))
                {
                    float move = Mathf.SmoothStep(0f, 1f, t);
                    float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1f, t));
                    foreach (var m in movers)
                    {
                        // Geometric scaling reads as steady motion when a component falls from far above or rises
                        // from deep below.
                        SetSimilarity(m.t, m.from, Vector2.Lerp(m.from, targetCenter, move), Mathf.Pow(m.scale, move));
                        m.fade.Set(alpha);
                    }
                    yield return null;
                }
            }

            transition = null;
            Redraw(); // also moves the focus up if a cascade consumed the current board
            Changed?.Invoke();
        }

        /// <summary>
        /// The bread to centre a step's frame on: the player's current board as it was in <paramref name="state"/>,
        /// moved up until the board the product lands on is in view.
        /// </summary>
        static Element AnimationFocus(GameState state, int[] focusPath, Element productBoardOwner)
        {
            var f = BreadAlong(state, focusPath);
            while (f.Location != null && !IsWithin(productBoardOwner, f)) f = f.Location.Owner;
            return f;
        }

        /// <summary>The deepest bread reached by walking <paramref name="path"/> down from the root of <paramref name="state"/>.</summary>
        static Element BreadAlong(GameState state, int[] path)
        {
            var f = state.Root;
            foreach (int slot in path)
            {
                var board = f.Child;
                if (slot >= board.SlotCount || board.IsEmpty(slot) || !board[slot].IsBread) break;
                f = board[slot];
            }
            return f;
        }

        /// <summary>True if <paramref name="e"/> is <paramref name="ancestor"/> or sits somewhere inside it.</summary>
        static bool IsWithin(Element e, Element ancestor)
        {
            for (; e != null; e = e.Location?.Owner)
                if (e == ancestor) return true;
            return false;
        }

        /// <summary>
        /// Where <paramref name="bread"/> would be drawn as an open disc with <paramref name="viewFocus"/> as the
        /// current board. This covers bread too deep to be drawn open, and bread above the focus, which is drawn
        /// larger than the screen, as in the zoom.
        /// </summary>
        Frame FrameOf(Element viewFocus, Element bread)
        {
            if (bread == viewFocus) return FocusFrame;
            if (!IsWithin(viewFocus, bread)) // inside the focus, or in a branch beside it
            {
                var parent = FrameOf(viewFocus, bread.Location.Owner);
                ChildOf(parent, bread.Location.Layout, bread.SlotIndex, out var center, out var slotRadius);
                return new Frame { Center = center, Radius = slotRadius * 0.9f, Depth = parent.Depth + 1 };
            }

            // An ancestor of the focus: take the frame it has as the focus and scale it up about the child on the
            // path to the focus, so that child lands exactly where it is drawn.
            var child = viewFocus;
            while (child.Location.Owner != bread) child = child.Location.Owner;
            var childFrame = FrameOf(viewFocus, child);
            var standard = FocusFrame;
            ChildOf(standard, bread.Child.Layout, child.SlotIndex, out var c, out var r);
            float k = childFrame.Radius / (r * 0.9f);
            return new Frame { Center = childFrame.Center + k * (standard.Center - c), Radius = k * rootRadius, Depth = 0 };
        }

        /// <summary>The centre and radius of slot <paramref name="slot"/> of a bread drawn at <paramref name="frame"/>.</summary>
        static void ChildOf(Frame frame, BoardLayout layout, int slot, out Vector2 center, out float slotRadius)
        {
            float scale = BoardScale(layout, frame.Radius, frame.Depth);
            center = frame.Center + LayoutGeometry.Positions(layout)[slot] * scale;
            slotRadius = LayoutGeometry.SlotRadius(layout) * scale;
        }

        void SlotPlacement(Element viewFocus, Element owner, int slot, out Vector2 center, out float slotRadius) =>
            ChildOf(FrameOf(viewFocus, owner), owner.Child.Layout, slot, out center, out slotRadius);

        /// <summary>
        /// Draws one consumed component (bread with its contents) under its own transform in <paramref name="frame"/>,
        /// so it can be moved as a whole.
        /// </summary>
        Transform DrawMover(Element viewFocus, Element element, Transform frame, out Vector2 center, out float radius)
        {
            var parent = FrameOf(viewFocus, element.Location.Owner);
            ChildOf(parent, element.Location.Layout, element.SlotIndex, out center, out var slotRadius);
            int depth = parent.Depth + 1;

            var mover = new GameObject($"{element.Symbol} (merging)").transform;
            mover.SetParent(frame, false);
            var oldRoot = drawRoot;
            drawRoot = mover;
            drawOrderOffset = 0; // the rest of the frame is pushed far below, so movers draw over it
            if (element.IsBread)
            {
                radius = slotRadius * 0.9f;
                if (depth <= OpenDepth) DrawBread(element, center, radius, depth);
                else DrawClosedBread(element, center, radius, depth);
            }
            else
            {
                radius = slotRadius * 0.86f;
                SpawnPastry(TypeOf(element.Symbol), center, radius, depth * DepthBand + 3, showLabel: true,
                            name: $"{element.Symbol} (merging)");
            }
            drawRoot = oldRoot;
            return mover;
        }
    }
}
