using System;
using System.Collections;
using System.Collections.Generic;
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
    /// disc with its badge. Scrolling up over a bread on the focus board zooms into it; scrolling down zooms out to
    /// the parent. Zooming never moves the camera: the board objects themselves are moved, scaled and faded.
    /// </para>
    /// <para>
    /// Outside Play mode the starting board is drawn as a preview so the scene isn't empty. Preview objects are
    /// flagged <see cref="HideFlags.DontSave"/>, so they never end up in the scene file, and are rebuilt when the
    /// scene loads or a setting changes. The side panel, by contrast, is saved into the scene by editor tooling.
    /// </para>
    /// </summary>
    [ExecuteAlways]
    public sealed class GameView : MonoBehaviour
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
        [Header("Input")]
        [Tooltip("Most seconds between the two clicks of a double click that places an A in an empty slot.")]
        [SerializeField] float doubleClickTime = 0.35f;
        [Tooltip("Side panel saved in the scene. If missing, one is built at runtime.")]
        [SerializeField] SidePanel panel;

        // Sorting: each nesting level gets a band of 10; badges and the drag ghost sit above everything. During a
        // zoom, the frame being faded is pushed far below the other one.
        const int DepthBand = 10;
        const int HoverOrder = 9;
        const int BadgeOrder = 500;
        const int GhostOrder = 1000;
        const int BackFrameOffset = -2000;
        /// <summary>Levels below the focus that are drawn open. Bread deeper than this is drawn closed.</summary>
        const int OpenDepth = 1;
        const string BoardName = "Board";
        const HideFlags PreviewFlags = HideFlags.DontSave | HideFlags.NotEditable;

        readonly Dictionary<Symbol, PastryType> types = new Dictionary<Symbol, PastryType>();
        readonly List<SlotTarget> targets = new List<SlotTarget>();
        readonly List<BreadTarget> breads = new List<BreadTarget>();
        readonly PlacementHistory history = new PlacementHistory();
        Rules rules;
        Transform boardRoot;
        SlotTarget hovered;
        BreadTarget hoveredBread;
        Element focus;
        float rootSlotRadius = 0.5f;
        Camera cam;
        Coroutine transition;
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

        public LevelAsset Level => level;
        public GameState State { get; private set; }
        public PlacementResult LastResult { get; private set; }
        public string LastError { get; private set; }
        public Sprite CircleSprite => circleSprite;
        public Sprite RingSprite => ringSprite;
        public Sprite TriangleSprite => triangleSprite;
        public Color CatalystColor => catalystColor;
        public Camera Camera => cam != null ? cam : cam = Camera.main;

        /// <summary>The bread whose board is the current board. The root unless the player has zoomed in.</summary>
        public Element Focus => focus;

        /// <summary>True while a zoom animation plays. Placement is disabled meanwhile.</summary>
        public bool IsZooming => transition != null;

        /// <summary>True if there is a placement to undo.</summary>
        public bool CanUndo => history.Count > 0;

        /// <summary>Raised after every placement, reset or change of the current board.</summary>
        public event Action Changed;

        void Start()
        {
            if (Application.isPlaying) Init();
        }

        void Update()
        {
            if (Application.isPlaying) HandlePointer();
        }

        void LateUpdate()
        {
            if (Camera != null) FitCamera(SidePanel.ScreenFraction(Camera.aspect));
        }

        /// <summary>Builds rules and the board, and uses the scene's side panel (building one if there is none).</summary>
        public void Init()
        {
            if (!LoadRules()) { Debug.LogError("GameView has no LevelAsset assigned.", this); return; }
            NewGame();
            if (panel == null) panel = GetComponentInChildren<SidePanel>(true);
            if (panel == null) panel = SidePanel.Create(this);
            if (Camera != null) FitCamera(SidePanel.ScreenFraction(Camera.aspect));
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
            history.Clear();
            State = new GameState(rules);
            focus = State.Root;
            LastResult = null;
            LastError = null;
            Redraw();
            Changed?.Invoke();
        }

        public PastryType TypeOf(Symbol symbol) => types.TryGetValue(symbol, out var t) ? t : null;

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

        /// <summary>Places an A in the slot under <paramref name="world"/>. Returns false if there is none.</summary>
        public bool TryPlaceAt(Vector2 world)
        {
            var target = FindTarget(world);
            SetHover(null);
            if (target == null) return false;
            Place(target.Board, target.Slot);
            return true;
        }

        public void Place(Board board, int slot)
        {
            if (IsZooming) return;
            if (board == null || !board.IsAlive || slot < 0 || slot >= board.SlotCount || !board.IsEmpty(slot)) return;
            history.Record(board, slot);
            try
            {
                LastResult = State.PlaceA(board, slot);
                LastError = null;
            }
            catch (CascadeLimitException e)
            {
                LastResult = null;
                LastError = e.Message;
                Debug.LogError(e.Message, this);
            }
            Redraw(); // also moves the focus up if a cascade consumed the current board
            Changed?.Invoke();
        }

        /// <summary>
        /// Takes back the last A placed, along with every formula it set off. Stays on the current board if it still
        /// exists afterwards, otherwise moves to its nearest surviving ancestor.
        /// </summary>
        public void Undo()
        {
            if (!CanUndo) return;
            StopZoom();
            var focusPath = PlacementHistory.PathOf(focus);
            State = history.UndoLast(rules);
            focus = State.Root;
            // Walk down the old focus path as far as it still leads to bread.
            foreach (int slot in focusPath)
            {
                var board = focus.Child;
                if (slot >= board.SlotCount || board.IsEmpty(slot) || !board[slot].IsBread) break;
                focus = board[slot];
            }
            LastResult = null;
            LastError = null;
            Redraw();
            Changed?.Invoke();
        }

        /// <summary>A translucent A that follows the pointer while dragging.</summary>
        public PastryView CreateGhost()
        {
            var ghost = Instantiate(pastryPrefab, transform);
            ghost.name = "Drag Ghost";
            ghost.Bind(level.Basic, rootSlotRadius * 0.86f, GhostOrder);
            ghost.SetAlpha(0.85f);
            return ghost;
        }

        public float GhostRadius(SlotTarget target) => (target != null ? target.Radius : rootSlotRadius) * 0.86f;

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

        /// <summary>Fits the root board into the part of the screen not covered by the side panel.</summary>
        public void FitCamera(float panelFraction)
        {
            var c = Camera;
            if (c == null) return;
            float aspect = c.aspect;
            float half = rootRadius * 1.08f;
            float usable = Mathf.Max(0.2f, 1f - panelFraction);
            float size = Mathf.Max(half, half / (usable * aspect));
            var p = transform.position;
            var position = new Vector3(p.x - size * aspect * panelFraction, p.y, c.transform.position.z);
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
            if (IsZooming) return;

            var es = EventSystem.current;
            bool overUi = es != null && es.IsPointerOverGameObject();
            var world = ScreenToWorld(mouse.position.ReadValue());
            SetBreadHover(overUi ? null : FindBread(world));
            if (pressed && !overUi) HandleClick(world);

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
            var layout = parent.Child.Layout;
            float scale = BoardScale(layout, rootRadius, depth: 0);
            center = (Vector2)transform.position + LayoutGeometry.Positions(layout)[child.SlotIndex] * scale;
            radius = LayoutGeometry.SlotRadius(layout) * scale * 0.9f;
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

        bool OnBoard(Element e)
        {
            for (; e != null; e = e.Location.Owner)
            {
                if (!e.IsAlive) return false;
                if (e.Location == null) return e == State.Root;
                if (e.Location[e.SlotIndex] != e) return false;
            }
            return false;
        }

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
            return root;
        }

        static float BoardScale(BoardLayout layout, float radius, int depth) =>
            radius * (depth == 0 ? 0.9f : 0.78f) / LayoutGeometry.Bound(layout);

        /// <summary>Draws an open bread: its disc, slots, links and contents. <paramref name="depth"/> is 0 for the focus.</summary>
        void DrawBread(Element bread, Vector2 center, float radius, int depth)
        {
            int band = depth * DepthBand;
            var type = TypeOf(bread.Symbol);
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
                else if (element == drawSkip) { }
                else if (element.IsBread && depth + 1 <= OpenDepth)
                {
                    DrawBread(element, p, slotRadius * 0.9f, depth + 1);
                    if (depth == 0 && drawInteractive) AddBreadTarget(element, p, slotRadius * 0.9f, depth + 1);
                }
                else if (element.IsBread)
                    DrawClosedBread(element, p, slotRadius * 0.9f, depth + 1);
                else
                    SpawnPastry(TypeOf(element.Symbol), p, slotRadius * 0.86f, band + 3, showLabel: true,
                                name: $"{element.Symbol} @ {i}");
            }

            if (depth > 0) DrawBadge(bread, type, center, radius, depth);
        }

        /// <summary>Bread too deep to open: just its disc and badge. Its contents stay hidden until zoomed closer.</summary>
        void DrawClosedBread(Element bread, Vector2 center, float radius, int depth)
        {
            var type = TypeOf(bread.Symbol);
            SpawnPastry(type, center, radius, depth * DepthBand, showLabel: false, name: $"{bread.Symbol} (bread, closed)");
            DrawBadge(bread, type, center, radius, depth);
        }

        // Bread shows its letter in a small badge on the rim (the focus needs no label).
        void DrawBadge(Element bread, PastryType type, Vector2 center, float radius, int depth)
        {
            var dir = new Vector2(-0.7071f, 0.7071f);
            SpawnPastry(type, center + dir * radius * 0.88f, radius * 0.26f, BadgeOrder + depth * 2,
                        showLabel: true, name: $"{bread.Symbol} badge");
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
                State = new GameState(rules);
                focus = State.Root;
                LastResult = null;
                LastError = null;
                Redraw();
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
