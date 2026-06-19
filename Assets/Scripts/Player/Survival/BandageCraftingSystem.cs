using UnityEngine;

[DisallowMultipleComponent]
public class BandageCraftingSystem : MonoBehaviour
{
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private int fiberCost = 2;
    [SerializeField] private int clothCost = 1;
    [SerializeField] private int bandageOutput = 1;

    public int FiberCost => Mathf.Max(1, fiberCost);
    public int ClothCost => Mathf.Max(1, clothCost);
    public int BandageOutput => Mathf.Max(1, bandageOutput);

    private void Awake()
    {
        ResolveReferences();
    }

    public bool CanCraftBandage()
    {
        ResolveReferences();
        return inventory != null &&
            inventory.HasItem(ItemType.Fiber, FiberCost) &&
            inventory.HasItem(ItemType.Cloth, ClothCost);
    }

    public bool TryCraftBandage()
    {
        ResolveReferences();
        if (!CanCraftBandage())
        {
            ShowInfo("Need 2 Fiber + 1 Cloth");
            return false;
        }

        if (!inventory.RemoveItem(ItemType.Fiber, FiberCost))
        {
            ShowInfo("Need 2 Fiber");
            return false;
        }

        if (!inventory.RemoveItem(ItemType.Cloth, ClothCost))
        {
            inventory.AddItem(ItemType.Fiber, FiberCost);
            ShowInfo("Need 1 Cloth");
            return false;
        }

        int added = inventory.AddItem(ItemType.Bandage, BandageOutput);
        if (added < BandageOutput)
        {
            if (added > 0)
            {
                inventory.RemoveItem(ItemType.Bandage, added);
            }

            inventory.AddItem(ItemType.Fiber, FiberCost);
            inventory.AddItem(ItemType.Cloth, ClothCost);
            ShowInfo("Inventory Full");
            return false;
        }

        ShowInfo("Crafted Bandage");
        return true;
    }

    private void ResolveReferences()
    {
        if (inventory == null)
        {
            inventory = GetComponent<PlayerInventory>();
        }
    }

    private static void ShowInfo(string message)
    {
        if (PickupUIManager.instance != null)
        {
            PickupUIManager.instance.ShowInfo(message);
        }
    }
}
