using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bagels.Core;
using Bagels.Data;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Bagels.View
{
    /// <summary>
    /// Owns the <see cref="GameState"/> and draws it. The board is redrawn from the model after every placement, so
    /// the view never drifts from the backend. Bread is a disc with its own board inside; slots are empty rings
    /// joined by lines showing adjacency.
    /// <para>
    /// The view is centred on a <see cref="Focus"/> bread (the "current board") and shows two levels below it: the
    /// focus board's elements, and the contents of bread sitting on it. Bread two levels down is drawn closed, as a
    /// plain disc. Scrolling up over a bread on the focus board zooms into it; scrolling down zooms out to
    /// the parent. Zooming never moves the camera: the board objects themselves are moved, scaled and faded.
    /// </para>
    /// <para>
    /// When a placement sets off formulas, each step of the cascade is replayed from the tree as it was just before
    /// that step: the consumed components fly into the slot where the product lands and fade, then the next step
    /// plays. Components from a board above the current one come down from off-screen, shrinking as they fall;
    /// components from deeper boards rise out of them, growing.
    /// </para>
    /// <para>
    /// Outside Play mode the starting board is drawn as a preview so the scene isn't empty. Preview objects are
    /// flagged <see cref="HideFlags.DontSave"/>, so they never end up in the scene file, and are rebuilt when the
    /// scene loads or a setting changes. The side panel, by contrast, is saved into the scene by editor tooling.
    /// </para>
    /// </summary>
    [ExecuteAlways]
    public sealed partial class GameView : MonoBehaviour
    {
        /// <summary>An empty slot the player can drop an A into.</summary>
        public sealed class SlotTarget
        {
            public Board Board;
            public int Slot;
            public Vector2 Center;
            public float Radius;
            public SpriteRenderer Ring;
            public Color RingColor;
        }

        /// <summary>A drawn element the player can drag to an order tray.</summary>
        sealed class ElementTarget
        {
            public Element Element;
            public Vector2 Center;
            public float Radius;
        }

        /// <summary>A bread on the focus board that can be zoomed into.</summary>
        sealed class BreadTarget
        {
            public Element Bread;
            public Vector2 Center;
            public float Radius;
            public SpriteRenderer Highlight;
        }

        [SerializeField] LevelAsset level;
        [SerializeField] PastryView pastryPrefab;
        [Header("Sprites (1 world unit across at scale 1)")]
        [SerializeField] Sprite circleSprite;
        [SerializeField] Sprite ringSprite;
        [SerializeField] Sprite squareSprite;
        [SerializeField] Sprite triangleSprite;
        [Header("Look")]
        [SerializeField] float rootRadius = 3f;
        [SerializeField] Color highlightColor = new Color(0.95f, 0.55f, 0.1f);
        [SerializeField] Color catalystColor = new Color(0.93f, 0.72f, 0.12f);
        [Header("Zoom")]
        [Tooltip("Seconds a zoom in or out takes.")]
        [SerializeField] float zoomDuration = 0.45f;
        [Tooltip("Seconds after a zoom during which further scrolling is ignored, so one flick moves one level.")]
        [SerializeField] float scrollCooldown = 0.15f;
        [Header("Formulas")]
        [Tooltip("Seconds each formula of a cascade takes to merge its components into the product's slot.")]
        [SerializeField] float mergeDuration = 0.5f;
        [Tooltip("Size of the merging components, relative to the product's slot, when they reach it.")]
        [SerializeField] float mergeEndScale = 0.55f;
        [Header("Input")]
        [Tooltip("Most seconds between the two clicks of a double click that places an A in an empty slot.")]
        [SerializeField] float doubleClickTime = 0.35f;
        [Tooltip("Pixels the pointer must move while pressed on an element before it is picked up for an order.")]
        [SerializeField] float dragThreshold = 8f;
        [Tooltip("Side panel saved in the scene. If missing, one is built at runtime.")]
        [SerializeField] SidePanel panel;
        [Tooltip("Shows a button that grants $10M per click. Only works in the Editor and in Development Builds.")]
        [SerializeField] bool developerMode;

        // Sorting: each nesting level gets a band of 10; the drag ghost sits above everything. During a
        // zoom, the frame being faded is pushed far below the other one.
        const int DepthBand = 10;
        const int HoverOrder = 9;
        const int GhostOrder = 1000;
        /// <summary>
        /// Sorting order of the side panels' canvas: above every board sprite, so boards slide under the panels
        /// while zooming, but below
        /// <see cref="GhostOrder"/>, so anything being dragged shows over the panels.
        /// </summary>
        public const int PanelOrder = 900;
        const int BackFrameOffset = -2000;
        /// <summary>Levels below the focus that are drawn open. Bread deeper than this is drawn closed.</summary>
        const int OpenDepth = 1;
        const string BoardName = "Board";
        const HideFlags PreviewFlags = HideFlags.DontSave | HideFlags.NotEditable;

        readonly Dictionary<Symbol, PastryType> types = new Dictionary<Symbol, PastryType>();
        readonly List<SlotTarget> targets = new List<SlotTarget>();
        readonly List<BreadTarget> breads = new List<BreadTarget>();
        readonly List<ElementTarget> elements = new List<ElementTarget>();
        Rules rules;
        GameSession session;
        Transform boardRoot;
        SlotTarget hovered;
        BreadTarget hoveredBread;
        Element focus;
        float rootSlotRadius = 0.5f;
        Camera cam;
        Coroutine transition;
        PastryView carried; // a component picked up by clicking a spawner, following the pointer until dropped
        Symbol carriedSymbol;
        // Dragging an element from the board towards an order tray.
        Element pressedElement;
        Vector2 pressScreen;
        PastryView dragged;
        float scrollReadyTime;
        bool pointerWasDown;
        // The empty slot hit by the last click, to recognise a double click. Targets are rebuilt on every redraw,
        // so the slot is kept by board and index rather than by its SlotTarget.
        Board lastClickBoard;
        int lastClickSlot = -1;
        float lastClickTime = float.NegativeInfinity;

        // Drawing context for the frame currently being built.
        Transform drawRoot;
        int drawOrderOffset;
        bool drawInteractive;
        Element drawSkip;
        readonly HashSet<Element> drawHidden = new HashSet<Element>();

        public LevelAsset Level => level;
        public Rules Rules => rules;
        public GameSession Session => session;
        public GameState State => session?.State;
        public PlacementResult LastResult { get; private set; }
        public string LastError { get; private set; }

        /// <summary>A short note about the last action (an unlock, a sale, a refusal), for the status line.</summary>
        public string Message { get; private set; }
        public Sprite CircleSprite => circleSprite;
        public Sprite RingSprite => ringSprite;
        public Sprite TriangleSprite => triangleSprite;
        public Color CatalystColor => catalystColor;
        public Camera Camera => cam != null ? cam : cam = Camera.main;

        /// <summary>The side panel, once <see cref="Init"/> has found or built it.</summary>
        public SidePanel Panel => panel;

        /// <summary>The bread whose board is the current board. The root unless the player has zoomed in.</summary>
        public Element Focus => focus;

        /// <summary>True while a zoom or formula animation plays. Placement is disabled meanwhile.</summary>
        public bool IsZooming => transition != null;

        /// <summary>True while a component picked up from the side panel follows the pointer.</summary>
        public bool IsCarrying => carried != null;

        /// <summary>True while an element is being dragged from the board towards an order.</summary>
        public bool IsDraggingElement => dragged != null;

        /// <summary>True if there is a placement to undo.</summary>
        public bool CanUndo => session != null && session.CanUndo;

        /// <summary>Raised after every placement, reset or change of the current board.</summary>
        public event Action Changed;

        void Start()
        {
            if (Application.isPlaying) Init();
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            HandlePointer();
            TickOrders();
        }

        TutorialOverlay tutorial;

        /// <summary>True while priority orders' timers are stopped: a popup or a tutorial overlay is showing.</summary>
        public bool TimersPaused =>
            (panel != null && panel.PopupOpen) ||
            ((tutorial != null || (tutorial = GetComponent<TutorialOverlay>()) != null) && tutorial.IsShowing);

        /// <summary>Runs priority orders' timers; a customer whose timer runs out leaves.</summary>
        void TickOrders()
        {
            if (session == null || TimersPaused) return;
            var left = session.Tick(Time.unscaledDeltaTime);
            if (left.Count == 0) return;
            Say(left.Count == 1 ? $"The customer for order #{left[0].Id} got tired of waiting and left."
                                : $"{left.Count} customers got tired of waiting and left.");
        }

        void LateUpdate()
        {
            if (Camera != null) FitCamera(SidePanel.ScreenFraction(Camera.aspect), SidePanel.RightScreenFraction(Camera.aspect));
        }

        /// <summary>Builds rules and the board, and uses the scene's side panel (building one if there is none).</summary>
        public void Init()
        {
            if (!LoadRules()) { Debug.LogError("GameView has no LevelAsset assigned.", this); return; }
            NewGame();
            if (panel == null) panel = GetComponentInChildren<SidePanel>(true);
            if (panel != null && panel.IsOutdated)
            {
                Debug.LogWarning("Bagels: the side panel saved in the scene is out of date, so it was rebuilt for this " +
                                 "run. Run Bagels/Rebuild Side Panel and save the scene to update it for good.", panel);
                foreach (var old in GetComponentsInChildren<SidePanel>(true)) { old.gameObject.SetActive(false); Destroy(old.gameObject); }
                panel = null;
            }
            if (panel == null) panel = SidePanel.Create(this);
            if (FindAnyObjectByType<BackgroundMusic>() == null) gameObject.AddComponent<BackgroundMusic>();
            if (Application.isPlaying && GetComponent<TutorialOverlay>() == null) gameObject.AddComponent<TutorialOverlay>().Init(this);
            if (Camera != null) FitCamera(SidePanel.ScreenFraction(Camera.aspect), SidePanel.RightScreenFraction(Camera.aspect));
        }

        bool LoadRules()
        {
            if (level == null) return false;
            rules = level.ToRules();
            types.Clear();
            foreach (var t in level.PastryTypes()) types[t.Symbol] = t;
            return true;
        }

        public void NewGame()
        {
            StopZoom();
            CancelCarry();
            CancelElementDrag();
            // Play gets fresh orders every time; the edit-mode preview stays the same between redraws.
            session = new GameSession(rules, Application.isPlaying ? Environment.TickCount : 0)
            {
                RecordSnapshots = Application.isPlaying, // for replaying cascades
            };
            focus = State.Root;
            LastResult = null;
            LastError = null;
            Message = null;
            Redraw();
            Changed?.Invoke();
        }

        public PastryType TypeOf(Symbol symbol) => types.TryGetValue(symbol, out var t) ? t : null;

        /// <summary>
        /// The type whose look an element is drawn with. The top board always looks like the original root bread
        /// (a plain disc), even after a reset has made it an H.
        /// </summary>
        PastryType LookOf(Element element) =>
            element == State?.Root && rules != null ? TypeOf(rules.RootSymbol) ?? TypeOf(element.Symbol) : TypeOf(element.Symbol);

        public Vector2 ScreenToWorld(Vector2 screen) => Camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));

        /// <summary>The empty slot under a world point, preferring the one whose centre is closest.</summary>
        public SlotTarget FindTarget(Vector2 world)
        {
            if (IsZooming) return null;
            SlotTarget best = null;
            float bestScore = 1.15f; // allow a little slack outside the ring
            foreach (var t in targets)
            {
                float score = Vector2.Distance(world, t.Center) / t.Radius;
                if (score < bestScore) { bestScore = score; best = t; }
            }
            return best;
        }

        public void SetHover(SlotTarget target)
        {
            if (hovered == target) return;
            if (hovered != null && hovered.Ring != null) hovered.Ring.color = hovered.RingColor;
            hovered = target;
            if (hovered != null && hovered.Ring != null) hovered.Ring.color = highlightColor;
        }

        /// <summary>Places (or buys) <paramref name="symbol"/> in the slot under <paramref name="world"/>. Returns false if there is none.</summary>
        public bool TryPlaceAt(Vector2 world, Symbol symbol = GameSession.Basic)
        {
            var target = FindTarget(world);
            SetHover(null);
            if (target == null) return false;
            Place(target.Board, target.Slot, symbol);
            return true;
        }

        public void Place(Board board, int slot) => Place(board, slot, GameSession.Basic);

        /// <summary>Places a free A, a stocked component, or buys and places another one.</summary>
        public void Place(Board board, int slot, Symbol symbol)
        {
            if (IsZooming || session == null || session.Won || !GameState.CanPlace(board, slot)) return;
            if (BuyRefusal(symbol) is string refusal)
            {
                Say(refusal);
                return;
            }
            var focusPath = TreePath.Of(focus);
            try
            {
                LastResult = session.Place(board, slot, symbol);
                LastError = null;
            }
            catch (CascadeLimitException e)
            {
                LastResult = null;
                LastError = e.Message;
                Debug.LogError(e.Message, this);
            }
            Message = Unlocked();
            if (Application.isPlaying && LastResult != null && LastResult.Steps.Count > 0 && LastResult.Steps[0].Before != null)
            {
                transition = StartCoroutine(PlayCascade(LastResult.Steps, focusPath));
                return; // the animation redraws and raises Changed when it ends
            }
            Redraw(); // also moves the focus up if a cascade consumed the current board
            Changed?.Invoke();
        }

        /// <summary>Why <paramref name="symbol"/> can't be placed right now, or null if it can.</summary>
        public string BuyRefusal(Symbol symbol)
        {
            if (session == null) return "The game hasn't started.";
            if (session.Won) return "The game is won.";
            if (session.CanTake(symbol)) return null;
            var info = rules.InfoOf(symbol);
            if (info == null || info.Price == null) return $"{symbol} can only be crafted.";
            if (!session.Progress.IsUnlocked(symbol)) return $"{symbol} is not unlocked yet.";
            return $"{info.Name} costs ${info.Price}; you have ${session.Money}.";
        }

        /// <summary>
        /// Hands <paramref name="element"/> to <paramref name="order"/>. Returns what happened; a rejected element
        /// stays on the board.
        /// </summary>
        public DeliveryOutcome Deliver(Order order, Element element)
        {
            if (session == null || IsZooming) return DeliveryOutcome.Rejected;
            int imperfectBefore = order?.Imperfections ?? 0;
            var outcome = session.Deliver(order, element);
            int extras = (order?.Imperfections ?? 0) - imperfectBefore;
            string extraNote = extras == 0 ? "" : $" It had {extras} extra topping{(extras == 1 ? "" : "s")}.";
            switch (outcome)
            {
                case DeliveryOutcome.Rejected:
                    Say($"Order #{order?.Id} doesn't need that {Describe(element)}.");
                    return outcome;
                case DeliveryOutcome.Accepted:
                    Message = $"Handed in {Describe(element)}." + extraNote;
                    break;
                case DeliveryOutcome.Completed:
                    // The tip shows only as the customer's reaction and the floating amount, not in the status text.
                    string given = order.RewardComponents.Count > 0
                        ? " Got " + string.Join(", ", order.RewardComponents) + "." : "";
                    Message = $"Order #{order.Id} complete." + given + Prefix(" ", Unlocked());
                    if (panel != null)
                        panel.ShowCustomerResponse(Responses.Pick(order.Imperfections, responseRandom), order.TipEarned);
                    break;
                case DeliveryOutcome.Won:
                    Message = null;
                    break;
            }
            LastResult = null;
            LastError = null;
            Redraw();
            Changed?.Invoke();
            return outcome;
        }

        /// <summary>The Resources text file holding what customers say on completing an order.</summary>
        public const string ResponsesResource = "CustomerResponses";

        CustomerResponses responses;
        readonly System.Random responseRandom = new System.Random();

        /// <summary>Customer lines from Assets/Resources/CustomerResponses.txt, read once.</summary>
        CustomerResponses Responses
        {
            get
            {
                if (responses != null) return responses;
                var file = Resources.Load<TextAsset>(ResponsesResource);
                try
                {
                    responses = file != null ? CustomerResponses.Parse(file.text) : CustomerResponses.None;
                }
                catch (FormatException e)
                {
                    Debug.LogError($"Bagels: {ResponsesResource}.txt: {e.Message}", file);
                    responses = CustomerResponses.None;
                }
                if (file == null) Debug.LogWarning($"Bagels: no Resources/{ResponsesResource}.txt; customers will say nothing.");
                return responses;
            }
        }

        string Describe(Element element) =>
            element == null ? "item" : TypeOf(element.Symbol)?.DisplayName ?? element.Symbol.ToString();

        string Unlocked()
        {
            if (session == null || session.LastUnlocked.Count == 0) return null;
            return "Unlocked " + string.Join(" and ", session.LastUnlocked.Select(i => $"{i.Symbol} ({i.Name})")) + "!";
        }

        static string Prefix(string prefix, string text) => string.IsNullOrEmpty(text) ? "" : prefix + text;

        void Say(string message)
        {
            Message = message;
            Changed?.Invoke();
        }

        /// <summary>Buys the next formula, if the player can afford it (design.md, "Unlocking formulas").</summary>
        public void BuyNextFormula()
        {
            if (session == null || !session.CanBuyNextFormula) return;
            session.BuyNextFormula();
            LastResult = null;
            LastError = null;
            Say(Unlocked());
        }

        /// <summary>
        /// Takes back the last A placed, along with every formula it set off. Stays on the current board if it still
        /// exists afterwards, otherwise moves to its nearest surviving ancestor.
        /// </summary>
        public void Undo()
        {
            if (!CanUndo) return;
            StopZoom();
            CancelCarry();
            CancelElementDrag();
            var focusPath = TreePath.Of(focus);
            session.Undo();
            focus = BreadAlong(State, focusPath);
            LastResult = null;
            LastError = null;
            Message = null;
            Redraw();
            Changed?.Invoke();
        }

        /// <summary>
        /// The Inspector's developer mode, honoured only in the Editor and in Development Builds so a release build
        /// never shows the money button even if the box was left ticked.
        /// </summary>
        public bool DeveloperMode =>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            developerMode;
#else
            false;
#endif

        /// <summary>Developer mode: grants <see cref="GameSession.DevGrantAmount"/>.</summary>
        public void DevGrantMoney()
        {
            if (!DeveloperMode || session == null) return;
            session.DevGrantMoney(GameSession.DevGrantAmount);
            Changed?.Invoke();
        }

        /// <summary>True if the board can be swapped for a fresh H (see <see cref="GameSession.ResetBoard"/>).</summary>
        public bool CanResetBoard => session != null && session.CanResetBoard;

        /// <summary>Throws away the board and starts over on an empty H, keeping money, stock, unlocks and orders.</summary>
        public void ResetBoard()
        {
            if (!CanResetBoard) return;
            StopZoom();
            CancelCarry();
            CancelElementDrag();
            session.ResetBoard();
            focus = State.Root;
            LastResult = null;
            LastError = null;
            Message = "Started over on a fresh board.";
            Redraw();
            Changed?.Invoke();
        }

        /// <summary>A translucent component that follows the pointer while dragging.</summary>
        public PastryView CreateGhost(Symbol symbol = GameSession.Basic)
        {
            var ghost = Instantiate(pastryPrefab, transform);
            ghost.name = "Drag Ghost";
            ghost.Bind(TypeOf(symbol), rootSlotRadius * 0.86f, GhostOrder);
            ghost.SetAlpha(0.85f);
            return ghost;
        }

        public float GhostRadius(SlotTarget target) => (target != null ? target.Radius : rootSlotRadius) * 0.86f;

        /// <summary>
        /// Moves a ghost A to the pointer and highlights the slot it would drop into. The ghost snaps into that slot,
        /// so tiny nested slots are easy to read.
        /// </summary>
        public void FollowPointer(PastryView ghost, Vector2 screen)
        {
            if (ghost == null || Camera == null) return;
            var world = ScreenToWorld(screen);
            var target = FindTarget(world);
            SetHover(target);
            ghost.transform.position = target != null ? (Vector3)target.Center : (Vector3)world;
            ghost.transform.localScale = Vector3.one * GhostRadius(target) * 2f;
        }

        /// <summary>
        /// Picks up a component from the side panel (clicking a spawner). It follows the pointer; the next click on an
        /// empty slot places (or buys) it there, and a click anywhere else, a right click or Escape puts it back.
        /// </summary>
        public void BeginCarry(Symbol symbol = GameSession.Basic)
        {
            if (!Application.isPlaying || carried != null) return;
            if (BuyRefusal(symbol) is string refusal) { Say(refusal); return; }
            carriedSymbol = symbol;
            carried = CreateGhost(symbol);
            var mouse = Mouse.current;
            if (mouse != null) FollowPointer(carried, mouse.position.ReadValue());
        }

        public void CancelCarry()
        {
            if (carried != null) DestroySafe(carried.gameObject);
            carried = null;
            SetHover(null);
        }

        /// <summary>The path from the root to the current board, e.g. "Root Bread › C".</summary>
        public string FocusPath()
        {
            if (focus == null) return "";
            var chain = new List<Element>();
            for (var e = focus; e != null; e = e.Location?.Owner) chain.Add(e);
            var sb = new StringBuilder();
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                if (sb.Length > 0) sb.Append("  ›  ");
                var type = TypeOf(chain[i].Symbol);
                sb.Append(i == chain.Count - 1 && type != null ? type.DisplayName : chain[i].Symbol.ToString());
            }
            return sb.ToString();
        }

        /// <summary>Fits the root board into the part of the screen between the side panels.</summary>
        public void FitCamera(float leftFraction, float rightFraction)
        {
            var c = Camera;
            if (c == null) return;
            float aspect = c.aspect;
            float half = rootRadius * 1.08f;
            float usable = Mathf.Max(0.2f, 1f - leftFraction - rightFraction);
            float size = Mathf.Max(half, half / (usable * aspect));
            var p = transform.position;
            // Centre the board in the gap: the camera sits (right - left) half-widths off the board.
            var position = new Vector3(p.x + size * aspect * (rightFraction - leftFraction), p.y, c.transform.position.z);
            // Only write on a real change, so the edit-mode preview doesn't keep marking the scene as modified.
            if (!Mathf.Approximately(c.orthographicSize, size)) c.orthographicSize = size;
            if ((c.transform.position - position).sqrMagnitude > 1e-8f) c.transform.position = position;
        }

        // ---------------------------------------------------------------- pointer: hover, click and scroll

        void HandlePointer()
        {
            var mouse = Mouse.current;
            if (mouse == null || Camera == null || State == null) return;

            // Track the press edge ourselves (rather than wasPressedThisFrame) so a press is never missed however
            // input updates line up with frames. Keep tracking during a zoom so a held button doesn't read as new.
            bool down = mouse.leftButton.isPressed;
            bool pressed = down && !pointerWasDown;
            pointerWasDown = down;
            var screen = mouse.position.ReadValue();

            var kb = Keyboard.current;
            bool cancel = mouse.rightButton.wasPressedThisFrame || (kb != null && kb.escapeKey.wasPressedThisFrame);
            if (carried != null)
            {
                if (cancel) CancelCarry();
                else FollowPointer(carried, screen);
            }
            if (pressedElement != null && HandleElementDrag(screen, down, cancel)) return;
            if (IsZooming) return;

            var es = EventSystem.current;
            bool overUi = es != null && es.IsPointerOverGameObject();
            var world = ScreenToWorld(screen);
            SetBreadHover(overUi ? null : FindBread(world));
            if (pressed && !overUi)
            {
                if (carried != null)
                {
                    var target = FindTarget(world);
                    var symbol = carriedSymbol;
                    CancelCarry();
                    if (target != null) Place(target.Board, target.Slot, symbol);
                }
                else
                {
                    HandleClick(world);
                    // Pressing on an element may start dragging it to an order once the pointer moves.
                    var hit = FindElement(world);
                    if (hit != null && session != null && !session.Won)
                    {
                        pressedElement = hit.Element;
                        pressScreen = screen;
                    }
                }
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (overUi || Mathf.Abs(scroll) < 0.01f || Time.unscaledTime < scrollReadyTime) return;
            if (scroll > 0f && hoveredBread != null) ZoomIn(hoveredBread.Bread);
            else if (scroll < 0f) ZoomOut();
        }

        /// <summary>A second click on the same empty slot within <see cref="doubleClickTime"/> places an A there.</summary>
        void HandleClick(Vector2 world)
        {
            var target = FindTarget(world);
            float now = Time.unscaledTime;
            if (target != null && target.Board == lastClickBoard && target.Slot == lastClickSlot &&
                now - lastClickTime <= doubleClickTime)
            {
                lastClickBoard = null; // a third click starts a new double click
                lastClickSlot = -1;
                Place(target.Board, target.Slot);
                return;
            }
            lastClickBoard = target?.Board;
            lastClickSlot = target != null ? target.Slot : -1;
            lastClickTime = now;
        }

        /// <summary>
        /// Moves a dragged element with the pointer and hands it to the order under the pointer on release. Returns
        /// true while the drag owns the pointer.
        /// </summary>
        bool HandleElementDrag(Vector2 screen, bool down, bool cancel)
        {
            if (cancel || IsZooming || session == null || !State.IsInPlay(pressedElement))
            {
                CancelElementDrag();
                return false;
            }
            if (dragged == null)
            {
                if (!down) { pressedElement = null; return false; } // a plain click
                if ((screen - pressScreen).magnitude < dragThreshold) return false;
                dragged = CreateGhost(pressedElement.Symbol);
                dragged.name = "Delivery Ghost";
                SetBreadHover(null);
            }

            var order = panel != null ? panel.OrderAt(screen) : null;
            if (down)
            {
                dragged.transform.position = (Vector3)ScreenToWorld(screen);
                if (panel != null) panel.HighlightOrder(order, order != null && order.FindItemFor(pressedElement) >= 0);
                return true;
            }

            var element = pressedElement;
            CancelElementDrag();
            if (order != null) Deliver(order, element);
            return true;
        }

        void CancelElementDrag()
        {
            if (dragged != null) DestroySafe(dragged.gameObject);
            dragged = null;
            pressedElement = null;
            if (panel != null) panel.HighlightOrder(null, false);
        }

        /// <summary>The smallest drawn element under a world point, so the innermost one wins.</summary>
        ElementTarget FindElement(Vector2 world)
        {
            ElementTarget best = null;
            foreach (var t in elements)
                if (Vector2.Distance(world, t.Center) <= t.Radius && (best == null || t.Radius < best.Radius)) best = t;
            return best;
        }

        BreadTarget FindBread(Vector2 world)
        {
            foreach (var b in breads)
                if (Vector2.Distance(world, b.Center) <= b.Radius) return b;
            return null;
        }

        void SetBreadHover(BreadTarget target)
        {
            if (hoveredBread == target) return;
            if (hoveredBread != null && hoveredBread.Highlight != null) hoveredBread.Highlight.enabled = false;
            hoveredBread = target;
            if (hoveredBread != null && hoveredBread.Highlight != null) hoveredBread.Highlight.enabled = true;
        }

        // ---------------------------------------------------------------- zoom

        /// <summary>Makes <paramref name="bread"/>, which must sit on the current board, the current board.</summary>
        public void ZoomIn(Element bread)
        {
            if (IsZooming || bread == null || !bread.IsBread || bread.Location?.Owner != focus) return;
            if (!Application.isPlaying) { focus = bread; Redraw(); Changed?.Invoke(); return; }
            transition = StartCoroutine(Zoom(parent: focus, child: bread, zoomingIn: true));
        }

        /// <summary>Makes the current board's parent the current board. Does nothing at the root.</summary>
        public void ZoomOut()
        {
            if (IsZooming || focus == null || focus.Location == null) return;
            var parent = focus.Location.Owner;
            if (!Application.isPlaying) { focus = parent; Redraw(); Changed?.Invoke(); return; }
            transition = StartCoroutine(Zoom(parent: parent, child: focus, zoomingIn: false));
        }

        /// <summary>
        /// Zooming in: the child's frame grows from where the child sits on the parent board to full size, while the
        /// rest of the parent frame (parent board and siblings) is pushed outward past the screen edges and fades.
        /// Zooming out plays the same motion in reverse.
        /// </summary>
        IEnumerator Zoom(Element parent, Element child, bool zoomingIn)
        {
            SetHover(null);
            SetBreadHover(null);
            ClearBoard();
            targets.Clear();
            breads.Clear();
            elements.Clear();

            ChildPlacement(parent, child, out var childCenter, out var childRadius);
            var center = (Vector2)transform.position;
            float small = childRadius / rootRadius; // child frame scale when it sits on the parent board

            // Parent frame without the child (the child frame stands in for it), then the child frame on top.
            var back = DrawFrame(parent, skip: child, orderOffset: BackFrameOffset, interactive: false);
            var front = DrawFrame(child, skip: null, orderOffset: 0, interactive: false);
            var backFade = new Fader(back);

            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / Mathf.Max(0.01f, zoomDuration))
            {
                float e = Mathf.SmoothStep(0f, 1f, t);
                float zoomed = zoomingIn ? e : 1f - e; // 0 = parent view, 1 = child view
                Pose(front, back, zoomed, center, childCenter, small);
                backFade.Set(1f - zoomed);
                yield return null;
            }

            transition = null;
            focus = zoomingIn ? child : parent;
            scrollReadyTime = Time.unscaledTime + scrollCooldown;
            Redraw();
            Changed?.Invoke();
        }

        /// <summary>
        /// Places both frames for a zoom amount between 0 (parent view) and 1 (child view). The child frame is drawn
        /// at full size around <paramref name="center"/>; the parent frame has the child at <paramref name="childCenter"/>
        /// at scale <paramref name="small"/>. Both share one similarity transform, so they stay lined up.
        /// </summary>
        void Pose(Transform front, Transform back, float zoomed, Vector2 center, Vector2 childCenter, float small)
        {
            float frontScale = Mathf.Pow(small, 1f - zoomed); // geometric, so the growth reads as steady
            var anchor = Vector2.Lerp(childCenter, center, zoomed); // where the child's centre is drawn
            SetSimilarity(front, center, anchor, frontScale);
            SetSimilarity(back, childCenter, anchor, frontScale / small);
        }

        /// <summary>Transforms a frame so that world point <paramref name="from"/> lands on <paramref name="to"/>, scaled by <paramref name="scale"/>.</summary>
        void SetSimilarity(Transform frame, Vector2 from, Vector2 to, float scale)
        {
            var origin = (Vector2)transform.position;
            frame.localScale = new Vector3(scale, scale, 1f);
            frame.localPosition = (Vector3)(to - origin - scale * (from - origin));
        }

        void StopZoom()
        {
            if (transition != null) StopCoroutine(transition);
            transition = null;
        }

        /// <summary>Where <paramref name="child"/> is drawn when <paramref name="parent"/> is the current board.</summary>
        void ChildPlacement(Element parent, Element child, out Vector2 center, out float radius)
        {
            ChildOf(FocusFrame, parent.Child.Layout, child.SlotIndex, out center, out var slotRadius);
            radius = slotRadius * 0.9f;
        }

        /// <summary>Multiplies the alpha of every sprite and letter in a frame.</summary>
        sealed class Fader
        {
            readonly List<SpriteRenderer> sprites = new List<SpriteRenderer>();
            readonly List<Color> spriteColors = new List<Color>();
            readonly List<TextMesh> texts = new List<TextMesh>();
            readonly List<Color> textColors = new List<Color>();

            public Fader(Transform root)
            {
                foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true)) { sprites.Add(sr); spriteColors.Add(sr.color); }
                foreach (var tm in root.GetComponentsInChildren<TextMesh>(true)) { texts.Add(tm); textColors.Add(tm.color); }
            }

            public void Set(float alpha)
            {
                for (int i = 0; i < sprites.Count; i++)
                    if (sprites[i] != null) { var c = spriteColors[i]; c.a *= alpha; sprites[i].color = c; }
                for (int i = 0; i < texts.Count; i++)
                    if (texts[i] != null) { var c = textColors[i]; c.a *= alpha; texts[i].color = c; }
            }
        }

        // ---------------------------------------------------------------- drawing

        public void Redraw()
        {
            ClearBoard();
            targets.Clear();
            breads.Clear();
            elements.Clear();
            hovered = null;
            hoveredBread = null;
            focus = VisibleFocus();
            boardRoot = DrawFrame(focus, skip: null, orderOffset: 0, interactive: true);
            if (!Application.isPlaying) MarkAsPreview(boardRoot);
        }

        /// <summary>The focus, or its nearest ancestor still on the board if a cascade consumed it.</summary>
        Element VisibleFocus()
        {
            var e = focus;
            while (e != null && !OnBoard(e)) e = e.Location?.Owner;
            return e ?? State.Root;
        }

        bool OnBoard(Element e) => State.IsInPlay(e);

        Transform DrawFrame(Element focusBread, Element skip, int orderOffset, bool interactive)
        {
            var root = new GameObject(BoardName).transform;
            root.SetParent(transform, false);
            drawRoot = root;
            drawOrderOffset = orderOffset;
            drawInteractive = interactive;
            drawSkip = skip;
            DrawBread(focusBread, transform.position, rootRadius, 0);
            drawRoot = null;
            drawOrderOffset = 0;
            return root;
        }

        static float BoardScale(BoardLayout layout, float radius, int depth) =>
            radius * (depth == 0 ? 0.9f : 0.78f) / LayoutGeometry.Bound(layout);

        /// <summary>Draws an open bread: its disc, slots, links and contents. <paramref name="depth"/> is 0 for the focus.</summary>
        void DrawBread(Element bread, Vector2 center, float radius, int depth)
        {
            int band = depth * DepthBand;
            var type = LookOf(bread);
            SpawnPastry(type, center, radius, band, showLabel: false, name: $"{bread.Symbol} (bread)");

            var board = bread.Child;
            var layout = board.Layout;
            var positions = LayoutGeometry.Positions(layout);
            float scale = BoardScale(layout, radius, depth);
            float slotRadius = LayoutGeometry.SlotRadius(layout) * scale;
            if (depth == 0 && drawInteractive) rootSlotRadius = slotRadius;

            var ink = ViewColors.Darken(type != null ? type.Color : Color.gray, 0.6f);
            float thickness = Mathf.Max(0.01f, slotRadius * 0.12f);

            for (int i = 0; i < board.SlotCount; i++)
                foreach (int j in layout.Neighbors(i))
                    if (j > i)
                        DrawLine(center + positions[i] * scale, center + positions[j] * scale,
                                 slotRadius, thickness, ink, band + 1);

            for (int i = 0; i < board.SlotCount; i++)
            {
                var p = center + positions[i] * scale;
                var ring = DrawSprite(ringSprite, $"Slot {i}", p, slotRadius * 2f, ink, band + 2);
                var element = board[i];
                if (element == null)
                {
                    if (drawInteractive)
                        targets.Add(new SlotTarget { Board = board, Slot = i, Center = p, Radius = slotRadius, Ring = ring, RingColor = ink });
                }
                else if (element == drawSkip || drawHidden.Contains(element)) { }
                else
                {
                    if (element.IsBread && depth + 1 <= OpenDepth)
                    {
                        DrawBread(element, p, slotRadius * 0.9f, depth + 1);
                        if (depth == 0 && drawInteractive) AddBreadTarget(element, p, slotRadius * 0.9f, depth + 1);
                    }
                    else if (element.IsBread)
                        DrawClosedBread(element, p, slotRadius * 0.9f, depth + 1);
                    else
                        SpawnPastry(TypeOf(element.Symbol), p, slotRadius * 0.86f, band + 3, showLabel: true,
                                    name: $"{element.Symbol} @ {i}");
                    if (drawInteractive)
                        elements.Add(new ElementTarget { Element = element, Center = p, Radius = slotRadius * 0.9f });
                }
            }

        }

        /// <summary>Bread too deep to open: just its disc. Its contents stay hidden until zoomed closer.</summary>
        void DrawClosedBread(Element bread, Vector2 center, float radius, int depth)
        {
            var type = TypeOf(bread.Symbol);
            SpawnPastry(type, center, radius, depth * DepthBand, showLabel: false, name: $"{bread.Symbol} (bread, closed)");
        }

        void AddBreadTarget(Element bread, Vector2 center, float radius, int depth)
        {
            var glow = DrawSprite(ringSprite, $"{bread.Symbol} hover", center, radius * 2.16f, highlightColor,
                                  depth * DepthBand + HoverOrder);
            glow.enabled = false;
            breads.Add(new BreadTarget { Bread = bread, Center = center, Radius = radius, Highlight = glow });
        }

        PastryView SpawnPastry(PastryType type, Vector2 position, float radius, int order, bool showLabel, string name)
        {
            var view = Instantiate(pastryPrefab, drawRoot);
            view.name = name;
            view.transform.position = position;
            view.Bind(type, radius, order + drawOrderOffset, showLabel);
            return view;
        }

        SpriteRenderer DrawSprite(Sprite sprite, string name, Vector2 position, float size, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(drawRoot, false);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * size;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order + drawOrderOffset;
            return sr;
        }

        void DrawLine(Vector2 a, Vector2 b, float trim, float thickness, Color color, int order)
        {
            var d = b - a;
            float length = d.magnitude - 2f * trim;
            if (length <= 0f) return;
            var sr = DrawSprite(squareSprite, "Link", (a + b) * 0.5f, 1f, color, order);
            sr.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            sr.transform.localScale = new Vector3(length, thickness, 1f);
        }

        /// <summary>
        /// Removes every drawn frame. Frames are found by name rather than through <see cref="boardRoot"/>, which is
        /// not serialized and is lost when scripts reload while preview objects survive.
        /// </summary>
        void ClearBoard()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name == BoardName) DestroySafe(child.gameObject);
            }
            boardRoot = null;
        }

        static void MarkAsPreview(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.hideFlags = PreviewFlags;
                foreach (var c in t.GetComponents<Component>()) c.hideFlags = PreviewFlags;
            }
        }

        internal static void DestroySafe(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

#if UNITY_EDITOR
        // ---------------------------------------------------------------- edit-mode preview

        void OnEnable()
        {
            if (Application.isPlaying) return;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            QueuePreview();
        }

        void OnDisable()
        {
            if (Application.isPlaying) return;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            // Destroying children here can fail while the hierarchy is being deactivated, so wait a tick.
            EditorApplication.delayCall += () => { if (this != null && !isActiveAndEnabled) ClearBoard(); };
        }

        void OnValidate()
        {
            if (!Application.isPlaying) QueuePreview(); // objects can't be created inside OnValidate itself
        }

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            // Play mode builds the real board from scratch.
            if (change == PlayModeStateChange.ExitingEditMode && this != null) ClearBoard();
        }

        void QueuePreview()
        {
            EditorApplication.delayCall -= RefreshPreview;
            EditorApplication.delayCall += RefreshPreview;
        }

        /// <summary>Draws the starting board outside Play mode. The objects are never saved.</summary>
        public void RefreshPreview()
        {
            if (this == null || Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!isActiveAndEnabled || pastryPrefab == null || ringSprite == null || squareSprite == null || !LoadRules())
            {
                ClearBoard();
                return;
            }
            try
            {
                session = new GameSession(rules, 0);
                focus = State.Root;
                LastResult = null;
                LastError = null;
                Message = null;
                Redraw();
                var sidePanel = panel != null ? panel : GetComponentInChildren<SidePanel>(true);
                if (sidePanel != null) sidePanel.RefreshPreview();
            }
            catch (Exception e)
            {
                ClearBoard();
                Debug.LogWarning($"Board preview failed: {e.Message}", this);
            }
        }

        /// <summary>Replaces the scene's side panel with a fresh one built from the current level.</summary>
        public SidePanel RebuildPanel()
        {
            foreach (var old in GetComponentsInChildren<SidePanel>(true)) DestroyImmediate(old.gameObject);
            panel = null;
            if (!LoadRules()) return null;
            panel = SidePanel.Create(this);
            return panel;
        }

        /// <summary>Used by editor tooling when setting up the scene.</summary>
        public void EditorInit(LevelAsset level, PastryView prefab, Sprite circle, Sprite ring, Sprite square, Sprite triangle)
        {
            this.level = level;
            pastryPrefab = prefab;
            circleSprite = circle;
            ringSprite = ring;
            squareSprite = square;
            triangleSprite = triangle;
        }
#endif
    }
}
