using Bagels.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Bagels.View
{
    /// <summary>
    /// A side-panel icon that places a component: the free A, or a formula's product, which is bought on placement.
    /// Drag it onto an empty slot, or click it to pick one up that follows the pointer until the next click drops it
    /// (see <see cref="GameView.BeginCarry"/>). Components the player can't buy right now explain why instead.
    /// </summary>
    public sealed class PastrySpawner : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] GameView view;
        [SerializeField] Symbol symbol = GameSession.Basic;
        PastryView ghost;

        public Symbol Symbol => symbol;

        public void Init(GameView gameView, Symbol spawns)
        {
            view = gameView;
            symbol = spawns;
        }

        // The EventSystem sends no click once a drag has started, so clicks and drags never both fire.
        public void OnPointerClick(PointerEventData e)
        {
            if (view == null || e.button != PointerEventData.InputButton.Left) return;
            if (view.IsCarrying) view.CancelCarry(); // clicking a spawner while carrying puts the component back
            else view.BeginCarry(symbol);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (view == null) return;
            view.CancelCarry();
            if (view.BuyRefusal(symbol) != null)
            {
                view.BeginCarry(symbol); // shows the reason without picking anything up
                return;
            }
            ghost = view.CreateGhost(symbol);
            view.FollowPointer(ghost, e.position);
        }

        public void OnDrag(PointerEventData e)
        {
            if (view != null && ghost != null) view.FollowPointer(ghost, e.position);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (view == null || ghost == null) return;
            ClearGhost();
            view.TryPlaceAt(view.ScreenToWorld(e.position), symbol);
        }

        void OnDisable()
        {
            ClearGhost();
            if (view != null) view.SetHover(null);
        }

        void ClearGhost()
        {
            if (ghost != null) GameView.DestroySafe(ghost.gameObject);
            ghost = null;
        }
    }
}
