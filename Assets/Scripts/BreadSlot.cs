using UnityEngine;

/// <summary>A circular slot on the bread that can hold one bagel.</summary>
public class BreadSlot : MonoBehaviour
{
    public DraggableBagel Occupant { get; private set; }
    public bool IsOccupied => Occupant != null;

    public void Fill(DraggableBagel bagel) => Occupant = bagel;
}
