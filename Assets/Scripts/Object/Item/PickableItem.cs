using UnityEngine;

public enum ItemType
{
    Wood,
    Stone,
    Food,
    Axe,
    HealthConsumable,
    HungerConsumable,
    ThirstConsumable,
    Fiber,
    Cloth,
    Bandage,
    CraftingTable,
    Campfire,
    StorageChest,
    RawChicken,
    CookedChicken,
    RawFish,
    CookedFish,
    Iron,
    CookingPot,
    IronIngot,
    Furnace,
    Ash,
    Fertilizer
}

public class PickableItem : MonoBehaviour
{
    public ItemType itemType;
    public string itemName;
    public int amount = 1;

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            itemName = itemType.ToString();
        }

        if (amount < 1)
        {
            amount = 1;
        }
    }
}
