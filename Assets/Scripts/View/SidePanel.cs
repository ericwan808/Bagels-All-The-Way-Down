using System.Linq;
using System.Text;
using Bagels.Core;
using Bagels.Data;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Bagels.View
{
    /// <summary>
    /// Left-hand panel: goal, which board is being viewed, the A spawner, a scrolling list with a diagram of every
    /// formula, a legend, the last cascade, and undo and reset buttons. Built in code on a screen-space canvas scaled to a 1080-pixel-tall reference. Editor tooling saves the
    /// built panel into the scene, so every reference it needs at runtime is serialized.
    /// </summary>
    public sealed class SidePanel : MonoBehaviour
    {
        public const float Width = 440f;
        const float ReferenceHeight = 1080f;
        const float Pad = 24f;
        const float Gap = 6f;
        const float LegendHeight = 96f;
        const float StatusHeight = 56f;
        const float ResetHeight = 44f;
        const float FormulaIcon = 38f;
        const float ScrollbarWidth = 10f;

        static readonly Color Background = new Color(0.24f, 0.17f, 0.12f);
        static readonly Color Muted = new Color(0.85f, 0.78f, 0.68f);
        static readonly Color Win = new Color(0.98f, 0.8f, 0.25f);

        [SerializeField] GameView view;
        [SerializeField] Text status;
        [SerializeField] Text viewing;
        [SerializeField] Button resetButton;
        [SerializeField] Button undoButton;
        RectTransform panel;
        float cursor;
        bool hooked;

        /// <summary>Fraction of the screen width the panel covers (canvas matches height, so this is exact).</summary>
        public static float ScreenFraction(float aspect) => Width / (ReferenceHeight * aspect);

        public static SidePanel Create(GameView view)
        {
            if (Application.isPlaying) EnsureEventSystem();

            var go = new GameObject("Side Panel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(view.transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            var sidePanel = go.AddComponent<SidePanel>();
            sidePanel.Build(view);
            if (sidePanel.isActiveAndEnabled) sidePanel.Hook(); // OnEnable ran before Build set the references
            return sidePanel;
        }

        /// <summary>Makes sure the scene has an EventSystem so the panel receives clicks and drags.</summary>
        public static EventSystem EnsureEventSystem()
        {
            var existing = FindFirstObjectByType<EventSystem>();
            if (existing != null) return existing;
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            // The module assigns its default actions in OnEnable when it has none. Only do it here at runtime:
            // the defaults are an in-memory asset that can't be saved into a scene.
            if (Application.isPlaying) es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            return es.GetComponent<EventSystem>();
        }

        void OnEnable() => Hook();
        void OnDisable() => Unhook();

        void Hook()
        {
            if (hooked || view == null || !Application.isPlaying) return;
            view.Changed += Refresh;
            if (resetButton != null) resetButton.onClick.AddListener(view.NewGame);
            if (undoButton != null) undoButton.onClick.AddListener(view.Undo);
            hooked = true;
            Refresh();
        }

        void Unhook()
        {
            if (!hooked) return;
            if (view != null) view.Changed -= Refresh;
            if (resetButton != null && view != null) resetButton.onClick.RemoveListener(view.NewGame);
            if (undoButton != null && view != null) undoButton.onClick.RemoveListener(view.Undo);
            hooked = false;
        }

        void Build(GameView gameView)
        {
            view = gameView;
            var level = view.Level;

            var bg = UiFactory.Image("Panel", transform, null, Background, new Vector2(Width, 0f));
            bg.raycastTarget = true; // keep clicks on the panel from reaching anything behind it
            panel = bg.rectTransform;
            panel.anchorMin = new Vector2(0f, 0f);
            panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            cursor = Pad;

            // Title and goal.
            var title = UiFactory.Text("Title", Block("Title", 40f), "Bagels All The Way Down", 30, ViewColors.Paper,
                                       TextAnchor.MiddleLeft, FontStyle.Bold);
            UiFactory.Fill(title.rectTransform);

            var goal = Block("Goal", 60f);
            var goalText = UiFactory.Text("Label", goal, "Goal: make", 22, Muted);
            Place(goalText.rectTransform, new Vector2(0f, 0f), new Vector2(130f, 60f));
            if (level.Target != null)
                At(UiFactory.PastryIcon(goal, level.Target, 52f, view), new Vector2(160f, -30f));

            // Which board is the current one, and how to move between boards.
            var viewBlock = Block("Viewing", 48f);
            viewing = UiFactory.Text("Path", viewBlock, "", 19, ViewColors.Paper, TextAnchor.MiddleLeft, FontStyle.Bold);
            Place(viewing.rectTransform, Vector2.zero, new Vector2(Width - 2 * Pad, 26f));
            var nav = UiFactory.Text("Hint", viewBlock, "Scroll up on a board to open it, down to go back", 16, Muted);
            Place(nav.rectTransform, new Vector2(0f, 26f), new Vector2(Width - 2 * Pad, 22f));

            // Spawner.
            var spawnBlock = Block("Spawner", 140f);
            var spawnLabel = UiFactory.Text("Hint", spawnBlock, $"Drag out an {level.Basic.Symbol}, or double-click a slot", 19, Muted);
            Place(spawnLabel.rectTransform, Vector2.zero, new Vector2(Width - 2 * Pad, 28f));
            var halo = UiFactory.Image("Halo", spawnBlock, view.RingSprite, Muted, new Vector2(112f, 112f));
            At(halo.rectTransform, new Vector2((Width - 2 * Pad) * 0.5f, -84f));
            var spawner = UiFactory.PastryIcon(spawnBlock, level.Basic, 96f, view);
            At(spawner, new Vector2((Width - 2 * Pad) * 0.5f, -84f));
            spawner.GetComponent<Image>().raycastTarget = true;
            spawner.gameObject.AddComponent<ASpawner>().Init(view);

            // Formulas, ordered by product so B..E read top to bottom.
            var header = UiFactory.Text("Header", Block("Formulas Header", 32f), "Formulas", 24, ViewColors.Paper,
                                        TextAnchor.MiddleLeft, FontStyle.Bold);
            UiFactory.Fill(header.rectTransform);

            // The list takes whatever height the fixed blocks below it leave over.
            float below = LegendHeight + StatusHeight + ResetHeight + 2f * Gap + Pad;
            BuildFormulaList(Block("Formulas", ReferenceHeight - cursor - below - Gap), level);

            BuildLegend(Block("Legend", LegendHeight));

            status = UiFactory.Text("Status", Block("Status", StatusHeight), "", 20, Muted);
            UiFactory.Fill(status.rectTransform);

            var buttons = Block("Buttons", ResetHeight);
            undoButton = BuildButton(buttons, "Undo", 0f);
            resetButton = BuildButton(buttons, "Reset", 166f);
        }

        /// <summary>Formulas ordered by product so they read B, C, D... top to bottom, scrolling when they overflow.</summary>
        void BuildFormulaList(RectTransform block, LevelAsset level)
        {
            // Viewport: masks the rows and gives the scroll wheel something to hit.
            var hit = UiFactory.Image("Viewport", block, null, Color.clear, Vector2.zero);
            hit.raycastTarget = true;
            var viewport = hit.rectTransform;
            UiFactory.Fill(viewport);
            viewport.offsetMax = new Vector2(-(ScrollbarWidth + 4f), 0f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = UiFactory.Rect("Content", viewport, Vector2.zero);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0f, 1f);

            float y = 0f;
            foreach (var formula in level.Formulas.Where(f => f != null).OrderBy(f => f.Product.Symbol))
            {
                // Two rows of icons fit the base height; each extra vertical level adds one row gap.
                float height = 108f + Mathf.Max(0, FormulaDiagram.Rows(formula) - 2) * FormulaIcon * 1.5f;
                var row = UiFactory.Rect($"Formula {formula.Product.Symbol}", content, Vector2.zero);
                Place(row, new Vector2(0f, y), new Vector2(Width - 2 * Pad - ScrollbarWidth - 4f, height));
                y += height + Gap;

                var diagramBox = UiFactory.Rect("Pattern", row, new Vector2(230f, height - 4f));
                At(diagramBox, new Vector2(115f, -height * 0.5f));
                FormulaDiagram.Build(diagramBox, formula, view, FormulaIcon);

                var arrow = UiFactory.Text("Yields", row, "=", 34, Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
                Place(arrow.rectTransform, new Vector2(236f, 0f), new Vector2(40f, height));
                At(UiFactory.PastryIcon(row, formula.Product, 64f, view), new Vector2(322f, -height * 0.5f));
            }
            content.offsetMin = new Vector2(0f, -Mathf.Max(0f, y - Gap));
            content.offsetMax = Vector2.zero;

            var scroll = block.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.scrollSensitivity = 40f;
            scroll.verticalScrollbar = BuildScrollbar(block);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        Scrollbar BuildScrollbar(RectTransform block)
        {
            var track = UiFactory.Image("Scrollbar", block, null, ViewColors.Darken(Muted, 0.6f), Vector2.zero);
            track.raycastTarget = true;
            var rt = track.rectTransform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(ScrollbarWidth, 0f);
            rt.anchoredPosition = Vector2.zero;

            var area = UiFactory.Rect("Sliding Area", rt, Vector2.zero);
            UiFactory.Fill(area);
            var handle = UiFactory.Image("Handle", area, null, Muted, Vector2.zero);
            handle.raycastTarget = true;
            UiFactory.Fill(handle.rectTransform);

            var bar = track.gameObject.AddComponent<Scrollbar>();
            bar.handleRect = handle.rectTransform;
            bar.targetGraphic = handle;
            bar.direction = Scrollbar.Direction.BottomToTop;
            return bar;
        }

        void BuildLegend(RectTransform block)
        {
            const float rowH = 32f;
            const float sampleW = 46f;
            // Zero-size rect at the block's top-left so line/arrow coordinates are top-left relative.
            var origin = UiFactory.Rect("Samples", block, Vector2.zero);
            At(origin, Vector2.zero);
            for (int i = 0; i < 3; i++)
            {
                float y = -rowH * (i + 0.5f);
                var a = new Vector2(4f, y);
                var b = new Vector2(4f + sampleW, y);
                string text;
                switch (i)
                {
                    case 0:
                        UiFactory.Line(origin, a, b, 4f, ViewColors.Paper);
                        text = "adjacent on the same board";
                        break;
                    case 1:
                        UiFactory.Arrow(origin, a, b, 4f, 14f, view.TriangleSprite, ViewColors.Paper);
                        text = "A → B: B is inside a board next to A";
                        break;
                    default:
                        var ring = UiFactory.Image("Catalyst", origin, view.RingSprite, view.CatalystColor, new Vector2(26f, 26f));
                        ring.rectTransform.anchoredPosition = new Vector2(4f + sampleW * 0.5f, y);
                        text = "catalyst: kept unless it arrives last";
                        break;
                }
                var label = UiFactory.Text("Legend Text", block, text, 17, Muted);
                Place(label.rectTransform, new Vector2(sampleW + 18f, rowH * i), new Vector2(Width - 2 * Pad - sampleW - 18f, rowH));
            }
        }

        static Button BuildButton(RectTransform block, string text, float x)
        {
            var img = UiFactory.Image($"{text} Button", block, null, ViewColors.Darken(Muted, 0.35f), new Vector2(150f, ResetHeight));
            img.raycastTarget = true;
            Place(img.rectTransform, new Vector2(x, 0f), new Vector2(150f, ResetHeight));
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            var label = UiFactory.Text("Label", img.rectTransform, text, 22, ViewColors.Paper, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.Fill(label.rectTransform);
            return button;
        }

        void Refresh()
        {
            if (status == null || view == null) return;
            if (viewing != null) viewing.text = "Viewing: " + view.FocusPath();
            if (undoButton != null) undoButton.interactable = view.CanUndo;
            var state = view.State;
            var target = view.Level.Target;

            if (!string.IsNullOrEmpty(view.LastError))
            {
                status.color = new Color(1f, 0.5f, 0.45f);
                status.text = view.LastError;
            }
            else if (target != null && state != null && state.Contains(target.Symbol))
            {
                status.color = Win;
                status.text = $"You made {target.Symbol}!";
            }
            else
            {
                status.color = Muted;
                status.text = Describe(view.LastResult);
            }
        }

        static string Describe(PlacementResult result)
        {
            if (result == null || result.Steps.Count == 0) return "";
            var sb = new StringBuilder("Made: ");
            for (int i = 0; i < result.Steps.Count; i++)
            {
                var step = result.Steps[i];
                if (i > 0) sb.Append(",  ");
                sb.Append(string.Join(" + ", step.Consumed.Select(e => e.Symbol)));
                sb.Append(" → ").Append(step.Product.Symbol);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------ layout helpers

        /// <summary>A full-width block stacked below the previous one. Children use top-left local coordinates.</summary>
        RectTransform Block(string name, float height)
        {
            var rt = UiFactory.Rect(name, panel, Vector2.zero);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(Pad, -cursor - height);
            rt.offsetMax = new Vector2(-Pad, -cursor);
            cursor += height + Gap;
            return rt;
        }

        /// <summary>Positions a rect by its top-left corner relative to the block's top-left.</summary>
        static void Place(RectTransform rt, Vector2 topLeft, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
        }

        /// <summary>Positions a rect by its centre relative to the block's top-left (y negative = down).</summary>
        static void At(RectTransform rt, Vector2 center)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = center;
        }
    }
}
