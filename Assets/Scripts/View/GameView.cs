using System;
using System.Collections.Generic;
using Bagels.Core;
using Bagels.Data;
using UnityEngine;

namespace Bagels.View
{
    /// <summary>
    /// Owns the <see cref="GameState"/> and draws it. The whole board is redrawn from the model after every
    /// placement, so the view never drifts from the backend. Bread is a disc with its own board inside; slots are
    /// empty rings joined by lines showing adjacency.
    /// </summary>
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

        // Sorting: each nesting level gets a band of 10; badges and the drag ghost sit above everything.
        const int DepthBand = 10;
        const int BadgeOrder = 500;
        const int GhostOrder = 1000;

        readonly Dictionary<Symbol, PastryType> types = new Dictionary<Symbol, PastryType>();
        readonly List<SlotTarget> targets = new List<SlotTarget>();
        Rules rules;
        Transform boardRoot;
        SidePanel panel;
        SlotTarget hovered;
        float rootSlotRadius = 0.5f;
        Camera cam;

        public LevelAsset Level => level;
        public GameState State { get; private set; }
        public PlacementResult LastResult { get; private set; }
        public string LastError { get; private set; }
        public Sprite CircleSprite => circleSprite;
        public Sprite RingSprite => ringSprite;
        public Sprite TriangleSprite => triangleSprite;
        public Color CatalystColor => catalystColor;
        public Camera Camera => cam != null ? cam : cam = Camera.main;

        /// <summary>Raised after every placement or reset.</summary>
        public event Action Changed;

        void Start()
        {
            if (State == null) Init();
        }

        void LateUpdate()
        {
            if (Camera != null) FitCamera(SidePanel.ScreenFraction(Camera.aspect));
        }

        /// <summary>Builds rules, the board and the side panel. Safe to call again (e.g. from editor tooling).</summary>
        public void Init()
        {
            if (level == null) { Debug.LogError("GameView has no LevelAsset assigned.", this); return; }
            rules = level.ToRules();
            types.Clear();
            foreach (var t in level.PastryTypes()) types[t.Symbol] = t;

            if (panel != null) DestroySafe(panel.gameObject);
            NewGame();
            panel = SidePanel.Create(this);
            if (Camera != null) FitCamera(SidePanel.ScreenFraction(Camera.aspect));
        }

        public void NewGame()
        {
            State = new GameState(rules);
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

        /// <summary>Fits the root board into the part of the screen not covered by the side panel.</summary>
        public void FitCamera(float panelFraction)
        {
            var c = Camera;
            if (c == null) return;
            float aspect = c.aspect;
            float half = rootRadius * 1.08f;
            float usable = Mathf.Max(0.2f, 1f - panelFraction);
            float size = Mathf.Max(half, half / (usable * aspect));
            c.orthographicSize = size;
            var p = transform.position;
            c.transform.position = new Vector3(p.x - size * aspect * panelFraction, p.y, c.transform.position.z);
        }

        // ---------------------------------------------------------------- drawing

        public void Redraw()
        {
            if (boardRoot != null) DestroySafe(boardRoot.gameObject);
            boardRoot = new GameObject("Board").transform;
            boardRoot.SetParent(transform, false);
            targets.Clear();
            hovered = null;
            DrawBread(State.Root, transform.position, rootRadius, 0, isRoot: true);
        }

        void DrawBread(Element bread, Vector2 center, float radius, int depth, bool isRoot)
        {
            int band = depth * DepthBand;
            var type = TypeOf(bread.Symbol);
            SpawnPastry(type, center, radius, band, showLabel: false, name: $"{bread.Symbol} (bread)");

            var board = bread.Child;
            var layout = board.Layout;
            var positions = LayoutGeometry.Positions(layout);
            float scale = radius * (isRoot ? 0.9f : 0.78f) / LayoutGeometry.Bound(layout);
            float slotRadius = LayoutGeometry.SlotRadius(layout) * scale;
            if (isRoot) rootSlotRadius = slotRadius;

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
                    targets.Add(new SlotTarget { Board = board, Slot = i, Center = p, Radius = slotRadius, Ring = ring, RingColor = ink });
                else if (element.IsBread)
                    DrawBread(element, p, slotRadius * 0.9f, depth + 1, isRoot: false);
                else
                    SpawnPastry(TypeOf(element.Symbol), p, slotRadius * 0.86f, band + 3, showLabel: true,
                                name: $"{element.Symbol} @ {i}");
            }

            // Bread shows its letter in a small badge on the rim (the root needs no label).
            if (!isRoot)
            {
                var dir = new Vector2(-0.7071f, 0.7071f);
                SpawnPastry(type, center + dir * radius * 0.88f, radius * 0.26f, BadgeOrder + depth * 2,
                            showLabel: true, name: $"{bread.Symbol} badge");
            }
        }

        PastryView SpawnPastry(PastryType type, Vector2 position, float radius, int order, bool showLabel, string name)
        {
            var view = Instantiate(pastryPrefab, boardRoot);
            view.name = name;
            view.transform.position = position;
            view.Bind(type, radius, order, showLabel);
            return view;
        }

        SpriteRenderer DrawSprite(Sprite sprite, string name, Vector2 position, float size, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(boardRoot, false);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * size;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
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

        internal static void DestroySafe(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

#if UNITY_EDITOR
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
