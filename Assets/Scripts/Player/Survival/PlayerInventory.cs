using System.Collections.Generic;
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    public Dictionary<ItemType, int> items = new Dictionary<ItemType, int>();

    public void AddItem(PickableItem item)
    {
        if (!items.ContainsKey(item.itemType))
            items[item.itemType] = 0;

        items[item.itemType] += item.amount;

        // 🔥 trigger UI
        PickupUIManager.instance.ShowPickup(item.itemName, item.amount);
    }
}