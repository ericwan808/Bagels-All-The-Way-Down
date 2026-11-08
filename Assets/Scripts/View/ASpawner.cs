using UnityEngine;
using UnityEngine.EventSystems;

namespace Bagels.View
{
    /// <summary>The side-panel icon the player drags new As out of. Drops into the empty slot under the pointer.</summary>
    public sealed class ASpawner : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        GameView view;
        PastryView ghost;

        public void Init(GameView gameView) => view = gameView;

        public void OnBeginDrag(PointerEventData e)
        {
            if (view == null) return;
            ghost = view.CreateGhost();
            Follow(e.position);
        }

        public void OnDrag(PointerEventData e) => Follow(e.position);

        public void OnEndDrag(PointerEventData e)
        {
            if (view == null) return;
            ClearGhost();
            view.TryPlaceAt(view.ScreenToWorld(e.position));
        }

        void OnDisable()
        {
            ClearGhost();
            if (view != null) view.SetHover(null);
        }

        void Follow(Vector2 screen)
        {
            if (ghost == null) return;
            var world = view.ScreenToWorld(screen);
            var target = view.FindTarget(world);
            view.SetHover(target);
            // Snap the preview into the hovered slot so tiny nested slots are easy to read.
            ghost.transform.position = target != null ? (Vector3)target.Center : (Vector3)world;
            ghost.transform.localScale = Vector3.one * view.GhostRadius(target) * 2f;
        }

        void ClearGhost()
        {
            if (ghost != null) GameView.DestroySafe(ghost.gameObject);
            ghost = null;
        }
    }
}
