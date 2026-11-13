using System.Collections.Generic;
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
    /// The two screen-side panels, on one camera-space canvas scaled to a 1080-pixel-tall reference.
    /// <para>
    /// Left: money, which board is being viewed, the free A spawner, the unlocked formulas split into pages (each
    /// product icon can be dragged out to buy it), a greyed-out stand-in for the next unlock with its condition, the
    /// status line, and undo and reset. Right: the active orders, each a tray that board elements are dragged onto,
    /// and a legend.
    /// </para>
    /// <para>
    /// Editor tooling saves the fixed skeleton into the scene. The formula pages and order cards depend on the game
    /// and are rebuilt from the <see cref="GameSession"/> on every change; outside Play mode they are a preview that
    /// is never saved.
    /// </para>
    /// </summary>
    public sealed class SidePanel : MonoBehaviour
    {
        public const float Width = 440f;
        public const float RightWidth = 400f;
        const float ReferenceHeight = 1080f;
        const float Pad = 24f;
        const float Gap = 6f;
        const float StatusHeight = 56f;
        const float ButtonHeight = 44f;
        const float LegendHeight = 96f;
        const float FormulaIcon = 38f;
        const float ItemRow = 46f;
        const float LockedHeight = 64f;
        const float Caption = 22f;
        const HideFlags PreviewFlags = HideFlags.DontSave | HideFlags.NotEditable;

        static readonly Color Background = new Color(0.24f, 0.17f, 0.12f);
        static readonly Color Card = new Color(0.32f, 0.23f, 0.17f);
        static readonly Color CardGood = new Color(0.25f, 0.45f, 0.22f);
        static readonly Color CardBad = new Color(0.55f, 0.2f, 0.17f);
        static readonly Color Locked = new Color(0.42f, 0.38f, 0.35f, 0.6f);
        static readonly Color Muted = new Color(0.85f, 0.78f, 0.68f);
        static readonly Color Gold = new Color(0.98f, 0.8f, 0.25f);
        static readonly Color Error = new Color(1f, 0.5f, 0.45f);

        [SerializeField] GameView view;
        [SerializeField] Text money;
        [SerializeField] Text status;
        [SerializeField] Text viewing;
        [SerializeField] Text pageLabel;
        [SerializeField] Text undoLabel;
        [SerializeField] Button resetButton;
        [SerializeField] Button undoButton;
        [SerializeField] Button prevPage;
        [SerializeField] Button nextPage;
        [SerializeField] RectTransform pageRoot;
        [SerializeField] RectTransform ordersRoot;

        readonly List<(Order order, RectTransform card, Image background)> cards =
            new List<(Order, RectTransform, Image)>();
        RectTransform panel;
        float cursor;
        bool hooked;
        int page;
        int knownFormulas = -1;
        Order highlighted;

        /// <summary>Fraction of the screen width the left panel covers (the canvas matches height, so this is exact).</summary>
        public static float ScreenFraction(float aspect) => Width / (ReferenceHeight * aspect);

        /// <summary>Fraction of the screen width the orders panel covers.</summary>
        public static float RightScreenFraction(float aspect) => RightWidth / (ReferenceHeight * aspect);

        /// <summary>The formula page currently shown (0-based).</summary>
        public int Page => page;

        public static SidePanel Create(GameView view)
        {
            if (Application.isPlaying) EnsureEventSystem();

            var go = new GameObject("Side Panel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(view.transform, false);
            var canvas = go.GetComponent<Canvas>();
            ConfigureCanvas(canvas, view);
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

        /// <summary>
        /// Screen Space - Camera, so the canvas sorts against sprites: the panels cover the board, and drag ghosts
        /// (which sort higher) draw over the panels. An overlay canvas would always hide them.
        /// </summary>
        static void ConfigureCanvas(Canvas canvas, GameView view)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = view.Camera;
            canvas.planeDistance = 1f;
            canvas.sortingOrder = GameView.PanelOrder;
        }

        Camera UiCamera
        {
            get
            {
                var canvas = GetComponent<Canvas>();
                return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            }
        }

        void Awake()
        {
            // The camera is saved with the scene, but re-link it if it went missing.
            var canvas = GetComponent<Canvas>();
            if (canvas != null && view != null && canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null)
                canvas.worldCamera = view.Camera;
        }

        void OnEnable() => Hook();
        void OnDisable() => Unhook();

        void Hook()
        {
            if (hooked || view == null || !Application.isPlaying) return;
            view.Changed += Refresh;
            if (resetButton != null) resetButton.onClick.AddListener(view.NewGame);
            if (undoButton != null) undoButton.onClick.AddListener(view.Undo);
            if (prevPage != null) prevPage.onClick.AddListener(PreviousPage);
            if (nextPage != null) nextPage.onClick.AddListener(NextPage);
            hooked = true;
            Refresh();
        }

        void Unhook()
        {
            if (!hooked) return;
            if (view != null)
            {
                view.Changed -= Refresh;
                if (resetButton != null) resetButton.onClick.RemoveListener(view.NewGame);
                if (undoButton != null) undoButton.onClick.RemoveListener(view.Undo);
            }
            if (prevPage != null) prevPage.onClick.RemoveListener(PreviousPage);
            if (nextPage != null) nextPage.onClick.RemoveListener(NextPage);
            hooked = false;
        }

        // ------------------------------------------------------------ orders as drop targets

        /// <summary>The order whose tray is under a screen point, or null.</summary>
        public Order OrderAt(Vector2 screen)
        {
            foreach (var (order, card, _) in cards)
                if (card != null && RectTransformUtility.RectangleContainsScreenPoint(card, screen, UiCamera)) return order;
            return null;
        }

        /// <summary>Tints an order's tray while something is dragged over it: green if it fits, red if not.</summary>
        public void HighlightOrder(Order order, bool fits)
        {
            highlighted = order;
            foreach (var (o, _, bg) in cards)
                if (bg != null) bg.color = o != order ? Card : fits ? CardGood : CardBad;
        }

        // ------------------------------------------------------------ fixed skeleton

        void Build(GameView gameView)
        {
            view = gameView;
            var level = view.Level;

            // ----- left panel
            panel = SidePanelRect("Panel", left: true, Width);
            cursor = Pad;

            var title = UiFactory.Text("Title", Block("Title", 40f), "Bagels All The Way Down", 30, ViewColors.Paper,
                                       TextAnchor.MiddleLeft, FontStyle.Bold);
            UiFactory.Fill(title.rectTransform);

            var moneyBlock = Block("Money", 40f);
            money = UiFactory.Text("Amount", moneyBlock, "$0", 30, Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiFactory.Fill(money.rectTransform);

            var viewBlock = Block("Viewing", 48f);
            viewing = UiFactory.Text("Path", viewBlock, "", 19, ViewColors.Paper, TextAnchor.MiddleLeft, FontStyle.Bold);
            Place(viewing.rectTransform, Vector2.zero, new Vector2(Width - 2 * Pad, 26f));
            var nav = UiFactory.Text("Hint", viewBlock, "Scroll up on a board to open it, down to go back", 16, Muted);
            Place(nav.rectTransform, new Vector2(0f, 26f), new Vector2(Width - 2 * Pad, 22f));

            var spawnBlock = Block("Spawner", 104f);
            var spawnLabel = UiFactory.Text("Hint", spawnBlock,
                $"Free: click or drag an {level.Basic.Symbol} onto a slot, or double-click a slot. " +
                "Drag a formula's product to buy it.", 16, Muted);
            Place(spawnLabel.rectTransform, new Vector2(110f, 0f), new Vector2(Width - 2 * Pad - 110f, 104f));
            var halo = UiFactory.Image("Halo", spawnBlock, view.RingSprite, Muted, new Vector2(96f, 96f));
            At(halo.rectTransform, new Vector2(50f, -52f));
            var spawner = UiFactory.PastryIcon(spawnBlock, level.Basic, 82f, view);
            At(spawner, new Vector2(50f, -52f));
            spawner.GetComponent<Image>().raycastTarget = true;
            spawner.gameObject.AddComponent<PastrySpawner>().Init(view, level.Basic.Symbol);

            // Formulas header with page controls.
            var header = Block("Formulas Header", 34f);
            var headerText = UiFactory.Text("Header", header, "Formulas", 24, ViewColors.Paper, TextAnchor.MiddleLeft, FontStyle.Bold);
            Place(headerText.rectTransform, Vector2.zero, new Vector2(180f, 34f));
            float right = Width - 2 * Pad;
            nextPage = BuildButton(header, ">", new Vector2(right - 40f, 0f), new Vector2(40f, 34f), out _);
            pageLabel = UiFactory.Text("Page", header, "1/1", 18, Muted, TextAnchor.MiddleCenter);
            Place(pageLabel.rectTransform, new Vector2(right - 110f, 0f), new Vector2(70f, 34f));
            prevPage = BuildButton(header, "<", new Vector2(right - 150f, 0f), new Vector2(40f, 34f), out _);

            float below = StatusHeight + ButtonHeight + 2f * Gap + Pad;
            pageRoot = Block("Formula Page", ReferenceHeight - cursor - below - Gap);

            status = UiFactory.Text("Status", Block("Status", StatusHeight), "", 18, Muted);
            UiFactory.Fill(status.rectTransform);

            var buttons = Block("Buttons", ButtonHeight);
            undoButton = BuildButton(buttons, "Undo", Vector2.zero, new Vector2(150f, ButtonHeight), out undoLabel);
            resetButton = BuildButton(buttons, "Reset", new Vector2(166f, 0f), new Vector2(150f, ButtonHeight), out _);

            // ----- right panel: orders
            panel = SidePanelRect("Orders Panel", left: false, RightWidth);
            cursor = Pad;
            var ordersTitle = UiFactory.Text("Title", Block("Orders Title", 40f), "Orders", 28, ViewColors.Paper,
                                             TextAnchor.MiddleLeft, FontStyle.Bold);
            UiFactory.Fill(ordersTitle.rectTransform);
            var ordersHint = UiFactory.Text("Hint", Block("Orders Hint", 40f),
                "Drag items from the board onto an order. Bread must hold exactly what is listed.", 16, Muted);
            UiFactory.Fill(ordersHint.rectTransform);
            ordersRoot = Block("Order Cards", ReferenceHeight - cursor - LegendHeight - Pad - Gap);
            BuildLegend(Block("Legend", LegendHeight), RightWidth - 2 * Pad);
        }

        RectTransform SidePanelRect(string name, bool left, float width)
        {
            var bg = UiFactory.Image(name, transform, null, Background, new Vector2(width, 0f));
            bg.raycastTarget = true; // keep clicks on the panel from reaching anything behind it
            var rt = bg.rectTransform;
            float x = left ? 0f : 1f;
            rt.anchorMin = new Vector2(x, 0f);
            rt.anchorMax = new Vector2(x, 1f);
            rt.pivot = new Vector2(x, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        void BuildLegend(RectTransform block, float width)
        {
            const float rowH = 32f;
            const float sampleW = 46f;
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
                var label = UiFactory.Text("Legend Text", block, text, 16, Muted);
                Place(label.rectTransform, new Vector2(sampleW + 18f, rowH * i), new Vector2(width - sampleW - 18f, rowH));
            }
        }

        static Button BuildButton(RectTransform block, string text, Vector2 topLeft, Vector2 size, out Text label)
        {
            var img = UiFactory.Image($"{text} Button", block, null, ViewColors.Darken(Muted, 0.35f), size);
            img.raycastTarget = true;
            Place(img.rectTransform, topLeft, size);
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            label = UiFactory.Text("Label", img.rectTransform, text, 22, ViewColors.Paper, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.Fill(label.rectTransform);
            return button;
        }

        // ------------------------------------------------------------ dynamic content

        /// <summary>Fills in the dynamic parts outside Play mode so the scene shows the starting state.</summary>
        public void RefreshPreview()
        {
            if (!Application.isPlaying) Refresh();
        }

        void Refresh()
        {
            if (view == null || view.Session == null || status == null) return;
            var session = view.Session;

            money.text = $"${session.Money}";
            if (viewing != null) viewing.text = "Viewing: " + view.FocusPath();
            if (undoButton != null) undoButton.interactable = view.CanUndo;
            if (undoLabel != null) undoLabel.text = view.CanUndo ? $"Undo ({session.UndoCount})" : "Undo";
            RefreshStatus(session);
            RebuildFormulaPage(session);
            RebuildOrders(session);
        }

        void RefreshStatus(GameSession session)
        {
            var target = view.Level.Target;
            if (!string.IsNullOrEmpty(view.LastError))
            {
                status.color = Error;
                status.text = view.LastError;
            }
            else if (session.Won)
            {
                status.color = Gold;
                status.text = $"You delivered the {(target != null ? target.DisplayName : "final bagel")}. You win!";
            }
            else
            {
                status.color = Muted;
                status.text = string.Join("\n", new[] { view.Message, Describe(view.LastResult) }
                                                    .Where(t => !string.IsNullOrEmpty(t)));
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

        void PreviousPage() => TurnPage(-1);
        void NextPage() => TurnPage(1);

        public void TurnPage(int delta)
        {
            page += delta;
            if (view != null && view.Session != null) RebuildFormulaPage(view.Session);
        }

        /// <summary>
        /// Unlocked formulas in list order, then a greyed-out stand-in for the next unlock, split into pages that fit
        /// the panel. After a new unlock the panel turns to the last page, where it appears.
        /// </summary>
        void RebuildFormulaPage(GameSession session)
        {
            if (pageRoot == null) return;
            Clear(pageRoot);

            var unlocked = view.Level.Formulas
                .Where(f => f != null && f.Product != null && session.Progress.IsUnlocked(f.Product.Symbol))
                .ToList();
            var next = session.Progress.NextLocked;
            var heights = unlocked.Select(RowHeight).ToList();
            if (next != null) heights.Add(LockedHeight);

            // Greedy pagination by height.
            float available = Mathf.Max(100f, pageRoot.rect.height > 0 ? pageRoot.rect.height : 400f);
            var pages = new List<(int start, int count)>();
            int start = 0;
            float used = 0f;
            for (int i = 0; i < heights.Count; i++)
            {
                if (i > start && used + heights[i] > available)
                {
                    pages.Add((start, i - start));
                    start = i;
                    used = 0f;
                }
                used += heights[i] + Gap;
            }
            pages.Add((start, heights.Count - start));

            if (knownFormulas >= 0 && unlocked.Count > knownFormulas) page = pages.Count - 1;
            knownFormulas = unlocked.Count;
            page = Mathf.Clamp(page, 0, pages.Count - 1);
            if (pageLabel != null) pageLabel.text = $"{page + 1}/{pages.Count}";
            if (prevPage != null) prevPage.interactable = page > 0;
            if (nextPage != null) nextPage.interactable = page < pages.Count - 1;

            float width = pageRoot.rect.width > 0 ? pageRoot.rect.width : Width - 2 * Pad;
            float y = 0f;
            var (first, count) = pages[page];
            for (int i = first; i < first + count; i++)
            {
                if (i < unlocked.Count) BuildFormulaRow(unlocked[i], session, y, width, heights[i]);
                else BuildLockedRow(next, session, y, width);
                y += heights[i] + Gap;
            }
            MarkPreview(pageRoot);
        }

        static float RowHeight(FormulaAsset formula)
        {
            int rows = FormulaDiagram.Rows(formula);
            int nodes = formula.Ingredients.Length;
            // Deep or wide formulas get taller rows; the diagram shrinks to fit whatever it gets.
            float diagram = 92f + Mathf.Clamp(rows - 2, 0, 4) * 44f + (nodes > 5 ? 30f : 0f);
            return diagram + Caption;
        }

        void BuildFormulaRow(FormulaAsset formula, GameSession session, float y, float width, float height)
        {
            var product = formula.Product;
            var row = UiFactory.Rect($"Formula {product.Symbol}", pageRoot, Vector2.zero);
            Place(row, new Vector2(0f, y), new Vector2(width, height));
            float diagramHeight = height - Caption;

            var diagramBox = UiFactory.Rect("Pattern", row, new Vector2(236f, diagramHeight - 4f));
            At(diagramBox, new Vector2(118f, -diagramHeight * 0.5f));
            FormulaDiagram.Build(diagramBox, formula, view, FormulaIcon);

            var arrow = UiFactory.Text("Yields", row, "=", 34, Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
            Place(arrow.rectTransform, new Vector2(238f, 0f), new Vector2(36f, diagramHeight));

            // The product icon doubles as the shop: drag it out to buy one.
            float iconX = 238f + 36f + 40f;
            var icon = UiFactory.PastryIcon(row, product, 60f, view);
            At(icon, new Vector2(iconX, -diagramHeight * 0.5f + 8f));
            int? price = product.Price;
            bool affordable = session.CanBuy(product.Symbol);
            if (price != null)
            {
                icon.GetComponent<Image>().raycastTarget = true;
                icon.gameObject.AddComponent<PastrySpawner>().Init(view, product.Symbol);
                if (!affordable) icon.gameObject.AddComponent<CanvasGroup>().alpha = 0.45f;
            }
            var priceText = UiFactory.Text("Price", row, price != null ? $"${price}" : "craft only", 17,
                                           affordable ? Gold : Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
            Place(priceText.rectTransform, new Vector2(iconX - 45f, diagramHeight * 0.5f + 32f), new Vector2(90f, 20f));

            var caption = UiFactory.Text("Name", row, $"{product.Symbol}  ·  {product.DisplayName}", 15, Muted);
            caption.horizontalOverflow = HorizontalWrapMode.Overflow;
            Place(caption.rectTransform, new Vector2(4f, diagramHeight), new Vector2(width - 8f, Caption));
        }

        void BuildLockedRow(PastryInfo next, GameSession session, float y, float width)
        {
            var box = UiFactory.Image("Next Unlock", pageRoot, null, Locked, Vector2.zero);
            Place(box.rectTransform, new Vector2(0f, y), new Vector2(width, LockedHeight));
            var mark = UiFactory.Text("Mystery", box.rectTransform, "?", 34, Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
            Place(mark.rectTransform, new Vector2(8f, 0f), new Vector2(48f, LockedHeight));

            var text = UiFactory.Text("Condition", box.rectTransform, "Next formula\n" + UnlockCondition(next, session),
                                      16, ViewColors.Paper);
            Place(text.rectTransform, new Vector2(64f, 0f), new Vector2(width - 72f, LockedHeight));
        }

        /// <summary>How the next component unlocks, without saying what it is.</summary>
        public static string UnlockCondition(PastryInfo next, GameSession session)
        {
            // A follower (F) unlocks with the one before it, so its condition is that one's.
            var gate = next;
            while (gate.UnlocksWithPrevious && session.Progress.Previous(gate) is PastryInfo before) gate = before;
            if (gate.Price is int price) return $"Unlocks when you have ${price} (you have ${session.Money})";
            var previous = session.Progress.Previous(gate);
            return $"Unlocks when you craft a {previous?.Name ?? "previous component"}";
        }

        void RebuildOrders(GameSession session)
        {
            if (ordersRoot == null) return;
            Clear(ordersRoot);
            cards.Clear();
            float width = ordersRoot.rect.width > 0 ? ordersRoot.rect.width : RightWidth - 2 * Pad;
            float y = 0f;
            // The final order first: it is the goal.
            foreach (var order in session.Orders.OrderByDescending(o => o.IsFinal))
                y += BuildOrderCard(order, y, width) + Gap * 2f;
            if (session.Won)
            {
                var done = UiFactory.Text("Won", ordersRoot, "All done!", 22, Gold, TextAnchor.UpperLeft, FontStyle.Bold);
                Place(done.rectTransform, new Vector2(0f, y), new Vector2(width, 40f));
            }
            HighlightOrder(highlighted != null && session.Orders.Contains(highlighted) ? highlighted : null, false);
            MarkPreview(ordersRoot);
        }

        float BuildOrderCard(Order order, float y, float width)
        {
            float height = 36f + order.Items.Count * ItemRow + 8f;
            var bg = UiFactory.Image($"Order {order.Id}", ordersRoot, null, Card, Vector2.zero);
            bg.raycastTarget = true;
            var card = bg.rectTransform;
            Place(card, new Vector2(0f, y), new Vector2(width, height));
            cards.Add((order, card, bg));

            int filled = Enumerable.Range(0, order.Items.Count).Count(order.IsFilled);
            string title = order.IsFinal ? "FINAL ORDER: deliver it to win" : $"Order #{order.Id}  ·  pays ${order.Reward}";
            var header = UiFactory.Text("Header", card, title, 18, order.IsFinal ? Gold : ViewColors.Paper,
                                        TextAnchor.MiddleLeft, FontStyle.Bold);
            Place(header.rectTransform, new Vector2(10f, 4f), new Vector2(width - 70f, 28f));
            var progress = UiFactory.Text("Progress", card, $"{filled}/{order.Items.Count}", 16, Muted, TextAnchor.MiddleRight);
            Place(progress.rectTransform, new Vector2(width - 64f, 4f), new Vector2(54f, 28f));

            for (int i = 0; i < order.Items.Count; i++)
            {
                var row = UiFactory.Rect($"Item {i}", card, Vector2.zero);
                Place(row, new Vector2(10f, 36f + i * ItemRow), new Vector2(width - 20f, ItemRow));
                float x = BuildItem(row, order.Items[i], 0f, 40f);
                if (order.IsFilled(i))
                {
                    row.gameObject.AddComponent<CanvasGroup>().alpha = 0.35f;
                    var tick = UiFactory.Text("Done", card, "handed in", 15, new Color(0.6f, 0.9f, 0.5f), TextAnchor.MiddleRight);
                    Place(tick.rectTransform, new Vector2(width - 100f, 36f + i * ItemRow), new Vector2(90f, ItemRow));
                }
                else if (order.Items[i].Contents.Count == 0 && x < width - 150f)
                {
                    var name = UiFactory.Text("Name", row, view.TypeOf(order.Items[i].Symbol)?.DisplayName ?? "", 15, Muted);
                    Place(name.rectTransform, new Vector2(x + 8f, 0f), new Vector2(width - 40f - x, ItemRow));
                }
            }
            return height;
        }

        /// <summary>
        /// Draws an item left to right: its icon, then for bread its required contents in brackets at a smaller size
        /// (recursively). Returns the x after the last thing drawn.
        /// </summary>
        float BuildItem(RectTransform row, ItemSpec spec, float x, float size)
        {
            var icon = UiFactory.PastryIcon(row, view.TypeOf(spec.Symbol), size, view);
            At(icon, new Vector2(x + size * 0.5f, -ItemRow * 0.5f));
            x += size + 3f;
            if (spec.Contents.Count == 0) return x;

            int font = Mathf.RoundToInt(size * 0.7f);
            x = Bracket(row, "[", x, font);
            foreach (var content in spec.Contents) x = BuildItem(row, content, x, Mathf.Max(16f, size * 0.62f)) + 1f;
            return Bracket(row, "]", x, font);
        }

        static float Bracket(RectTransform row, string text, float x, int font)
        {
            var t = UiFactory.Text(text, row, text, font, Muted, TextAnchor.MiddleCenter);
            Place(t.rectTransform, new Vector2(x, 0f), new Vector2(font * 0.5f, ItemRow));
            return x + font * 0.5f;
        }

        // ------------------------------------------------------------ helpers

        static void Clear(RectTransform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--) GameView.DestroySafe(root.GetChild(i).gameObject);
        }

        // Dynamic content built outside Play mode is a preview; keep it out of the scene file.
        static void MarkPreview(RectTransform root)
        {
            if (Application.isPlaying) return;
            for (int i = 0; i < root.childCount; i++)
                foreach (var t in root.GetChild(i).GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.hideFlags = PreviewFlags;
                    foreach (var c in t.GetComponents<Component>()) c.hideFlags = PreviewFlags;
                }
        }

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
