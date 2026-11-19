using System;
using System.Collections.Generic;
using System.Linq;
using Bagels.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Bagels.View
{
    /// <summary>
    /// The tutorial (design.md, "Tutorial"): a sequence of overlays whose text comes from
    /// Assets/Resources/TutorialOverlays.txt. Each overlay shows its pages one at a time in a box at the top centre of
    /// the screen, and may point at something with a pulsing gold frame. A page either waits for a click anywhere on
    /// the screen (which only advances the text and reaches nothing else), or waits for the player to do what it asks (place an A, craft a B, ...) and moves on by itself.
    /// <para>
    /// Steps run in order and start over with each new game. After them come triggered overlays, each shown once a
    /// game when its condition first holds (D affordable, a priority order on the docket, the first D crafted, E or F
    /// unlocked, an imperfect order completed), queued behind whatever is showing. An overlay with no text in the file is skipped. The
    /// overlay hides while a popup is open, and conditions are checked only once animations have finished, so a
    /// page doesn't move on before the player has seen the result.
    /// </para>
    /// </summary>
    public sealed class TutorialOverlay : MonoBehaviour
    {
        /// <summary>The Resources text file holding the overlays' text.</summary>
        public const string ScriptResource = "TutorialOverlays";

        const float ReferenceHeight = 1080f;
        const float BoxWidth = 760f;
        const float BoxPad = 18f;
        const float TopMargin = 20f;
        const float HintHeight = 22f;
        const float FramePad = 6f;
        const float FrameThickness = 4f;
        const float PulseSpeed = 5f;
        const int SortingOrder = 200; // above the side panel and the drag ghosts

        static readonly Color BoxColor = new Color(0.24f, 0.17f, 0.12f, 0.96f);
        static readonly Color Gold = new Color(0.98f, 0.8f, 0.25f);
        static readonly Color Muted = new Color(0.85f, 0.78f, 0.68f);
        /// <summary>Laid over the rest of the screen while a page waits for a click, to draw the eye to the text.</summary>
        static readonly Color Dim = new Color(0f, 0f, 0f, 0.7f);
        const float DimFadeTime = 0.25f;

        /// <summary>
        /// One overlay: its id in the script, what it points at, per page what ends it (null = a click), and for a
        /// triggered overlay, what makes it show.
        /// </summary>
        sealed class Step
        {
            public string Id;
            public Func<int, RectTransform> Highlight; // by page; may return null
            public Func<bool>[] Until = Array.Empty<Func<bool>>();
            public Func<bool> When; // triggered overlays only
            public Action After; // runs once the overlay is done (or skipped for having no text)
        }

        /// <summary>The Resources video clip that loops after the vertical-adjacency overlay.</summary>
        public const string VerticalAdjacencyVideo = "vertical_adjacency_example";
        public const string VerticalAdjacencyVideoTitle = "Making a baguette";

        void ShowVideo(string resource, string title) => TutorialVideo.Open(transform, resource, title);

        GameView view;
        TutorialScript script;
        List<Step> sequence, triggered;
        /// <summary>What shows, in order: the fixed sequence, then each triggered overlay as it fires.</summary>
        readonly List<Step> steps = new List<Step>();
        /// <summary>Triggered overlays that haven't fired yet this game.</summary>
        readonly List<Step> waiting = new List<Step>();
        GameSession session;
        Order firstOrder;
        int stepIndex;
        int pageIndex;

        RectTransform root, box, frame;
        Text body, hint;
        Image catcher;

        public void Init(GameView gameView)
        {
            view = gameView;
            script = LoadScript();
            sequence = BuildSteps();
            triggered = BuildTriggeredSteps();
            BuildUi();
            Restart();
        }

        /// <summary>The overlay currently showing, or null once the tutorial is over (for tests and debugging).</summary>
        public string CurrentId => stepIndex < steps.Count ? steps[stepIndex].Id : null;

        /// <summary>True while an overlay's box is on screen.</summary>
        public bool IsShowing => root != null && root.gameObject.activeSelf && Current != null;

        static TutorialScript LoadScript()
        {
            var file = Resources.Load<TextAsset>(ScriptResource);
            if (file == null)
            {
                Debug.LogWarning($"Bagels: no Resources/{ScriptResource}.txt; the tutorial is skipped.");
                return TutorialScript.None;
            }
            try { return TutorialScript.Parse(file.text); }
            catch (FormatException e)
            {
                Debug.LogError($"Bagels: {ScriptResource}.txt: {e.Message}", file);
                return TutorialScript.None;
            }
        }

        // ------------------------------------------------------------ the steps

        List<Step> BuildSteps()
        {
            bool Crafted(Symbol s) => session.Progress.WasCrafted(s);
            bool AtRoot() => view.Focus == view.State.Root;
            RectTransform Formula(Symbol s) => view.Panel != null ? view.Panel.FormulaRow(s) : null;
            bool OnBoard(ItemSpec spec) => view.State.RootBoard.Elements.Any(spec.Matches);
            var cHoldingA = new ItemSpec(Symbol.C, new ItemSpec(Symbol.A));

            return new List<Step>
            {
                new Step { Id = "welcome" },
                new Step
                {
                    Id = "place-a",
                    Highlight = _ => view.Panel != null ? view.Panel.Spawner : null,
                    // The first page is clicked through; the second waits for something on the board.
                    Until = new Func<bool>[] { null, () => view.State.RootBoard.Elements.Any() || Crafted(Symbol.B) },
                },
                new Step
                {
                    Id = "first-formula",
                    Highlight = _ => Formula(Symbol.B),
                    Until = new Func<bool>[] { () => Crafted(Symbol.B) },
                },
                new Step { Id = "first-formula-complete" },
                new Step
                {
                    Id = "recursive-breads",
                    Highlight = _ => Formula(Symbol.C),
                    Until = new Func<bool>[] { () => Crafted(Symbol.C) },
                },
                new Step
                {
                    // Open a bread's board, then put an A in the C and come back to the main one.
                    Id = "zoom",
                    Until = new Func<bool>[] { () => !AtRoot(), () => AtRoot() && OnBoard(cHoldingA) },
                },
                new Step
                {
                    // Making the C used up the Bs, so make another one for the order.
                    Id = "consuming-ingredients",
                    Until = new Func<bool>[] { () => OnBoard(new ItemSpec(Symbol.B)) },
                },
                new Step
                {
                    // Hand in the first order; the second page follows the customer's reaction.
                    Id = "hand-in",
                    Highlight = page => page == 0 && view.Panel != null ? view.Panel.OrderCard(firstOrder) : null,
                    Until = new Func<bool>[] { () => firstOrder == null || !session.Orders.Contains(firstOrder) },
                },
            };
        }

        /// <summary>
        /// Overlays that show once per game, as soon as their condition first holds. One that fires while another
        /// overlay is showing waits its turn. Their pages are clicked through.
        /// </summary>
        List<Step> BuildTriggeredSteps()
        {
            bool Unlocked(Symbol s) => session.Progress.IsUnlocked(s);
            return new List<Step>
            {
                new Step
                {
                    Id = "unlocking-D",
                    When = () => Unlocked(Symbol.D) || session.Rules.PriceOf(Symbol.D) is int price && session.Money >= price,
                },
                new Step
                {
                    // Shown once a priority order is actually on the docket; points at its card.
                    Id = "priority-order",
                    When = () => session.Orders.Any(o => o.IsPriority),
                    Highlight = _ => view.Panel != null ? view.Panel.OrderCard(session.Orders.FirstOrDefault(o => o.IsPriority)) : null,
                },
                // The first D made by its formula (buying one doesn't count): A-B-A also holds A-B, but the
                // higher-tier D wins.
                new Step { Id = "creating-D", When = () => session.Progress.WasCrafted(Symbol.D) },
                new Step
                {
                    // Then a video of a baguette being made loops in the corner until the player closes it.
                    Id = "vertical-adjacency",
                    When = () => Unlocked(Symbol.E),
                    After = () => ShowVideo(VerticalAdjacencyVideo, VerticalAdjacencyVideoTitle),
                },
                new Step { Id = "catalysts", When = () => Unlocked(Symbol.F) },
                // Only once a whole order is in: an imperfect item alone doesn't cut the tip yet.
                new Step { Id = "extras-and-tips", When = () => session.ImperfectOrdersCompleted > 0 },
            };
        }

        void Restart()
        {
            session = view.Session;
            TutorialVideo.CloseAny(); // including one opened from the help popup
            firstOrder = session?.Orders.FirstOrDefault(o => !o.IsFinal);
            steps.Clear();
            steps.AddRange(sequence);
            waiting.Clear();
            waiting.AddRange(triggered);
            stepIndex = 0;
            pageIndex = 0;
            SkipEmptySteps();
            Show();
        }

        /// <summary>Queues every triggered overlay whose condition now holds, and shows it if nothing else is.</summary>
        void FireTriggers()
        {
            if (session == null) return;
            bool idle = Current == null;
            for (int i = 0; i < waiting.Count; i++)
            {
                if (!waiting[i].When()) continue;
                steps.Add(waiting[i]);
                waiting.RemoveAt(i--);
            }
            if (idle && Current != null)
            {
                pageIndex = 0;
                SkipEmptySteps();
                Show();
            }
        }

        Step Current => stepIndex < steps.Count ? steps[stepIndex] : null;
        IReadOnlyList<string> CurrentPages => Current != null ? script.PagesOf(Current.Id) : Array.Empty<string>();
        Func<bool> CurrentCondition =>
            Current != null && pageIndex < Current.Until.Length ? Current.Until[pageIndex] : null;

        void SkipEmptySteps()
        {
            while (Current != null && CurrentPages.Count == 0)
            {
                Current.After?.Invoke();
                stepIndex++;
            }
        }

        void Advance()
        {
            pageIndex++;
            if (pageIndex >= CurrentPages.Count)
            {
                Current.After?.Invoke();
                stepIndex++;
                pageIndex = 0;
                SkipEmptySteps();
            }
            Show();
        }

        void Update()
        {
            if (view == null || root == null) return;
            if (view.Session != session) Restart(); // a new game starts the tutorial over
            if (!view.IsZooming) FireTriggers();
            if (Current == null)
            {
                SetVisible(false);
                return;
            }

            bool blocked = view.Panel != null && view.Panel.PopupOpen;
            SetVisible(!blocked);
            if (blocked) return;

            // Wait for animations to finish so the player sees what happened before the text moves on.
            if (CurrentCondition is Func<bool> done && !view.IsZooming && session != null && done())
            {
                Advance();
                return;
            }
            PlaceFrame(Current.Highlight?.Invoke(pageIndex));
        }

        // ------------------------------------------------------------ drawing

        void BuildUi()
        {
            var go = new GameObject("Tutorial Overlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, ReferenceHeight);
            scaler.matchWidthOrHeight = 1f;
            root = (RectTransform)go.transform;

            // On click pages, an invisible layer over the whole screen takes every click (and keeps it, and the
            // scroll wheel, off the board and panels) to advance the text.
            catcher = UiFactory.Image("Click Catcher", root, null, Color.clear, Vector2.zero);
            UiFactory.Fill(catcher.rectTransform);
            var catchButton = catcher.gameObject.AddComponent<Button>();
            catchButton.transition = Selectable.Transition.None;
            catchButton.onClick.AddListener(() => { if (CurrentCondition == null) Advance(); });

            // The highlight: four gold bars around the target, under the text box.
            frame = UiFactory.Rect("Highlight", root, Vector2.zero);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
            var glow = UiFactory.Image("Glow", frame, null, new Color(Gold.r, Gold.g, Gold.b, 0.12f), Vector2.zero);
            UiFactory.Fill(glow.rectTransform);
            Bar(new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, FrameThickness)); // top
            Bar(new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, FrameThickness)); // bottom
            Bar(new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(FrameThickness, 0f)); // left
            Bar(new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(FrameThickness, 0f)); // right
            frame.gameObject.SetActive(false);

            var bg = UiFactory.Image("Box", root, null, BoxColor, new Vector2(BoxWidth, 100f));
            box = bg.rectTransform;
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 1f);
            box.anchoredPosition = new Vector2(0f, -TopMargin);
            bg.gameObject.AddComponent<Outline>().effectColor = Gold;

            body = UiFactory.Text("Text", box, "", 24, ViewColors.Paper, TextAnchor.UpperCenter);
            body.rectTransform.anchorMin = new Vector2(0f, 1f);
            body.rectTransform.anchorMax = new Vector2(1f, 1f);
            body.rectTransform.pivot = new Vector2(0.5f, 1f);
            hint = UiFactory.Text("Hint", box, "Click to continue", 16, Muted, TextAnchor.LowerRight, FontStyle.Italic);
            hint.rectTransform.anchorMin = new Vector2(0f, 0f);
            hint.rectTransform.anchorMax = new Vector2(1f, 0f);
            hint.rectTransform.pivot = new Vector2(0.5f, 0f);
            hint.rectTransform.offsetMin = new Vector2(BoxPad, BoxPad * 0.5f);
            hint.rectTransform.offsetMax = new Vector2(-BoxPad, BoxPad * 0.5f + HintHeight);
        }

        void Bar(Vector2 anchorMin, Vector2 anchorMax, Vector2 size)
        {
            var bar = UiFactory.Image("Edge", frame, null, Gold, size);
            var rt = bar.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(anchorMin.x == anchorMax.x ? anchorMin.x : 0.5f, anchorMin.y == anchorMax.y ? anchorMin.y : 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
        }

        /// <summary>Puts the current page's text in the box and sizes it; clicks are caught only on click pages.</summary>
        void Show()
        {
            if (Current == null || root == null) return;
            bool waitsForClick = CurrentCondition == null;
            body.text = CurrentPages[pageIndex];
            hint.gameObject.SetActive(waitsForClick);

            float textWidth = BoxWidth - 2 * BoxPad;
            var settings = body.GetGenerationSettings(new Vector2(textWidth, 0f));
            float textHeight = body.cachedTextGeneratorForLayout.GetPreferredHeight(body.text, settings) / body.pixelsPerUnit;
            body.rectTransform.sizeDelta = new Vector2(-2 * BoxPad, textHeight);
            body.rectTransform.anchoredPosition = new Vector2(0f, -BoxPad);
            float height = BoxPad + textHeight + BoxPad + (waitsForClick ? HintHeight : 0f);
            box.sizeDelta = new Vector2(BoxWidth, height);

            // Click pages catch every click; pages that wait for an action let everything through.
            catcher.raycastTarget = waitsForClick;
            // Click pages also dim everything else (the highlight frame and the box stay bright, above it).
            bool wasDimmed = catcher.color.a > 0f;
            catcher.color = waitsForClick ? Dim : Color.clear;
            if (waitsForClick && !wasDimmed) UiTween.On(catcher.rectTransform, UiTween.Now, DimFadeTime).Fade(0f, 1f);
            UiTween.On(box, UiTween.Now, 0.25f).Fade(0f, 1f);
        }

        void SetVisible(bool visible)
        {
            if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        }

        /// <summary>Moves the pulsing frame over <paramref name="target"/> (in the side panel's canvas), or hides it.</summary>
        void PlaceFrame(RectTransform target)
        {
            if (target == null || !target.gameObject.activeInHierarchy)
            {
                frame.gameObject.SetActive(false);
                return;
            }
            var canvas = target.GetComponentInParent<Canvas>();
            var cam = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
            foreach (var corner in corners)
            {
                var screen = RectTransformUtility.WorldToScreenPoint(cam, corner);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out var local);
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }
            frame.gameObject.SetActive(true);
            float pulse = 0.5f + 0.5f * Mathf.Sin(UiTween.Now * PulseSpeed);
            float grow = FramePad + pulse * 4f;
            frame.anchoredPosition = (min + max) * 0.5f;
            frame.sizeDelta = max - min + Vector2.one * 2f * grow;
            foreach (var g in frame.GetComponentsInChildren<Graphic>())
            {
                var c = g.color;
                c.a = g.name == "Glow" ? 0.06f + 0.12f * pulse : 0.55f + 0.45f * pulse;
                g.color = c;
            }
        }
    }
}
