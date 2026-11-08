using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Lets the player drag this bagel with the mouse or a finger. When released close enough to a free
/// <see cref="BreadSlot"/>, it clicks into that slot and is locked there for good.
/// </summary>
[RequireComponent(typeof(Collider2D), typeof(SpriteRenderer))]
public class DraggableBagel : MonoBehaviour
{
    [Tooltip("How close (world units) the bagel's center must be to a slot's center to snap in on release.")]
    [SerializeField] float snapDistance = 0.6f;
    [Tooltip("Seconds the bagel takes to slide into the slot.")]
    [SerializeField] float snapDuration = 0.08f;
    [SerializeField] int restingSortingOrder = 2;
    [SerializeField] int draggingSortingOrder = 10;

    // Only one bagel may be held at a time, even if several overlap under the pointer.
    static DraggableBagel held;

    Camera cam;
    Collider2D col;
    SpriteRenderer sr;
    Vector3 grabOffset;
    bool locked;
    bool pointerWasDown;

    public bool IsLocked => locked;

    void Awake()
    {
        cam = Camera.main;
        col = GetComponent<Collider2D>();
        sr = GetComponent<SpriteRenderer>();
        sr.sortingOrder = restingSortingOrder;
    }

    void OnDisable()
    {
        if (held == this) held = null;
    }

    void Update()
    {
        if (locked || cam == null || !ReadPointer(out Vector2 screenPos, out bool down))
            return;

        // Detect press/release edges ourselves (rather than wasPressedThisFrame) so they are never missed
        // regardless of how input updates line up with frames.
        bool pressed = down && !pointerWasDown;
        bool released = !down && pointerWasDown;
        pointerWasDown = down;

        Vector3 world = ScreenToWorld(screenPos);

        if (pressed && held == null && col.OverlapPoint(world))
        {
            held = this;
            grabOffset = transform.position - world;
            sr.sortingOrder = draggingSortingOrder;
        }

        if (held != this) return;

        transform.position = world + grabOffset;

        if (released)
        {
            held = null;
            sr.sortingOrder = restingSortingOrder;
            TrySnap();
        }
    }

    void TrySnap()
    {
        BreadSlot best = null;
        float bestDist = snapDistance;
        foreach (var slot in FindObjectsByType<BreadSlot>(FindObjectsSortMode.None))
        {
            if (slot.IsOccupied) continue;
            float d = Vector2.Distance(transform.position, slot.transform.position);
            if (d <= bestDist) { bestDist = d; best = slot; }
        }
        if (best == null) return;

        locked = true;
        best.Fill(this);
        transform.SetParent(best.transform, worldPositionStays: true); // moves with the bread from now on
        StartCoroutine(SlideIntoSlot(best.transform));
    }

    IEnumerator SlideIntoSlot(Transform slot)
    {
        Vector3 start = transform.position;
        Vector3 end = new Vector3(slot.position.x, slot.position.y, start.z);
        for (float t = 0f; t < snapDuration; t += Time.deltaTime)
        {
            transform.position = Vector3.Lerp(start, end, t / snapDuration);
            yield return null;
        }
        transform.position = end;
    }

    Vector3 ScreenToWorld(Vector2 screenPos)
    {
        float depth = transform.position.z - cam.transform.position.z;
        Vector3 world = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, depth));
        world.z = transform.position.z;
        return world;
    }

    static bool ReadPointer(out Vector2 position, out bool down)
    {
#if ENABLE_INPUT_SYSTEM
        var pointer = Pointer.current; // mouse, pen or touch
        if (pointer == null) { position = default; down = false; return false; }
        position = pointer.position.ReadValue();
        down = pointer.press.isPressed;
        return true;
#else
        position = Input.mousePosition;
        down = Input.GetMouseButton(0);
        return true;
#endif
    }
}
