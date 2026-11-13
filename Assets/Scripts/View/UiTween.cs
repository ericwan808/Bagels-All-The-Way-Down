using UnityEngine;

namespace Bagels.View
{
    /// <summary>
    /// A small time-based UI animation: moves, fades and/or scales a rect between two states with smoothstep easing,
    /// then removes itself (or its whole object). Timing uses absolute unscaled times, so a panel that rebuilds its
    /// contents mid-animation can start a new tween with the same start time and carry on where the old one was.
    /// Several tweens can run on one object, e.g. a move and a fade with different start times.
    /// </summary>
    public sealed class UiTween : MonoBehaviour
    {
        RectTransform rt;
        CanvasGroup group;
        float start, duration;
        bool move, fade, scale, destroyObject;
        Vector2 posFrom, posTo;
        float alphaFrom, alphaTo;
        Vector3 scaleFrom, scaleTo;

        public static float Now => Time.unscaledTime;

        /// <summary>Starts a tween on <paramref name="target"/> that runs from <paramref name="startTime"/> for <paramref name="seconds"/>.</summary>
        public static UiTween On(RectTransform target, float startTime, float seconds)
        {
            var t = target.gameObject.AddComponent<UiTween>();
            t.rt = target;
            t.start = startTime;
            t.duration = Mathf.Max(0.01f, seconds);
            return t;
        }

        public UiTween Move(Vector2 from, Vector2 to)
        {
            move = true;
            posFrom = from;
            posTo = to;
            return Apply();
        }

        public UiTween Fade(float from, float to)
        {
            fade = true;
            alphaFrom = from;
            alphaTo = to;
            group = rt.GetComponent<CanvasGroup>();
            if (group == null) group = rt.gameObject.AddComponent<CanvasGroup>();
            return Apply();
        }

        public UiTween Scale(Vector3 from, Vector3 to)
        {
            scale = true;
            scaleFrom = from;
            scaleTo = to;
            return Apply();
        }

        /// <summary>Destroys the animated object when the tween ends, e.g. for something fading away.</summary>
        public UiTween DestroyWhenDone()
        {
            destroyObject = true;
            return this;
        }

        /// <summary>Progress from 0 to 1 at <paramref name="time"/>, eased.</summary>
        float Eased(float time)
        {
            float t = Mathf.Clamp01((time - start) / duration);
            return t * t * (3f - 2f * t);
        }

        UiTween Apply()
        {
            if (rt == null) return this;
            float e = Eased(Now);
            if (move) rt.anchoredPosition = Vector2.LerpUnclamped(posFrom, posTo, e);
            if (fade && group != null) group.alpha = Mathf.LerpUnclamped(alphaFrom, alphaTo, e);
            if (scale) rt.localScale = Vector3.LerpUnclamped(scaleFrom, scaleTo, e);
            return this;
        }

        void Update()
        {
            Apply();
            if (Now < start + duration) return;
            if (destroyObject) Destroy(gameObject);
            else Destroy(this);
        }
    }
}
