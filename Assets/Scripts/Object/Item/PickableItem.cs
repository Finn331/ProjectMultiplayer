using UnityEngine;

public enum ItemType
{
    Wood,
    Stone,
    Food
}

public class PickableItem : MonoBehaviour
{
    public ItemType itemType;
    public string itemName;
    public int amount = 1;
}