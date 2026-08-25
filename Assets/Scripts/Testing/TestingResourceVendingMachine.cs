using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class TestingResourceVendingMachine : MonoBehaviour
{
    private const int DispenseAmount = 5;

    [Header("UI References (assign in Editor)")]
    [SerializeField] private GameObject vendingPanel;
    [SerializeField] private Button closeButton;

    private PlayerInventory currentInventory;

    private void Awake()
    {
        if (vendingPanel != null)
            vendingPanel.SetActive(false);
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
    }

    public void OpenForInteractor(PlayerInteractionSystem interactor)
    {
        currentInventory = null;

        if (interactor == null)
        {
            ShowInfo("No player selected");
            return;
        }

        currentInventory = interactor.GetComponent<PlayerInventory>();
        if (currentInventory == null)
        {
            ShowInfo("No player inventory found");
            return;
        }

        if (vendingPanel == null)
        {
            ShowInfo("Vending UI not set up");
            return;
        }

        vendingPanel.SetActive(true);
    }

    public void OpenForNearestInteractor()
    {
        PlayerInteractionSystem[] interactors = FindObjectsByType<PlayerInteractionSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        PlayerInteractionSystem nearest = null;
        float nearestSqr = 9f;

        for (int i = 0; i < interactors.Length; i++)
        {
            PlayerInteractionSystem candidate = interactors[i];
            if (candidate == null) continue;

            float distanceSqr = (candidate.transform.position - transform.position).sqrMagnitude;
            if (distanceSqr <= nearestSqr)
            {
                nearestSqr = distanceSqr;
                nearest = candidate;
            }
        }

        OpenForInteractor(nearest);
    }

    public void Close()
    {
        if (vendingPanel != null)
            vendingPanel.SetActive(false);
    }

    public void DispenseWood() { Dispense(ItemType.Wood, "Wood"); }
    public void DispenseFiber() { Dispense(ItemType.Fiber, "Fiber"); }
    public void DispenseStone() { Dispense(ItemType.Stone, "Stone"); }
    public void DispenseCloth() { Dispense(ItemType.Cloth, "Cloth"); }
    public void DispenseRawMeat() { Dispense(ItemType.RawMeat, "Raw Meat"); }
    public void DispenseRawFish() { Dispense(ItemType.RawFish, "Raw Fish"); }
    public void DispenseIron() { Dispense(ItemType.Iron, "Iron"); }

    public void DispenseCookingPot()
    {
        if (currentInventory == null) { ShowInfo("Open vending first"); return; }
        int accepted = currentInventory.AddItem(ItemType.CookingPot, 1);
        if (accepted > 0) ShowInfo("Cooking Pot +" + accepted);
    }

    public void DispenseFurnace()
    {
        if (currentInventory == null) { ShowInfo("Open vending first"); return; }
        int accepted = currentInventory.AddItem(ItemType.Furnace, 1);
        if (accepted > 0) ShowInfo("Furnace +" + accepted);
    }

    public void DispenseCampfire()
    {
        if (currentInventory == null) { ShowInfo("Open vending first"); return; }
        int accepted = currentInventory.AddItem(ItemType.Campfire, 1);
        if (accepted > 0) ShowInfo("Campfire +" + accepted);
    }

    public void DispenseWall()
    {
        if (currentInventory == null) { ShowInfo("Open vending first"); return; }
        int accepted = currentInventory.AddItem(ItemType.WallItem, 1);
        if (accepted > 0) ShowInfo("Wall +" + accepted);
    }

    public void DispenseFloor()
    {
        if (currentInventory == null) { ShowInfo("Open vending first"); return; }
        int accepted = currentInventory.AddItem(ItemType.FloorItem, 1);
        if (accepted > 0) ShowInfo("Floor +" + accepted);
    }

    public void DispenseRoof()
    {
        if (currentInventory == null) { ShowInfo("Open vending first"); return; }
        int accepted = currentInventory.AddItem(ItemType.RoofItem, 1);
        if (accepted > 0) ShowInfo("Roof +" + accepted);
    }

    public void DispenseDoor()
    {
        if (currentInventory == null) { ShowInfo("Open vending first"); return; }
        int accepted = currentInventory.AddItem(ItemType.DoorItem, 1);
        if (accepted > 0) ShowInfo("Door +" + accepted);
    }

    private void Dispense(ItemType itemType, string label)
    {
        if (currentInventory == null) { ShowInfo("Open vending first"); return; }
        int accepted = currentInventory.AddItem(itemType, DispenseAmount);
        if (accepted > 0) ShowInfo(label + " +" + accepted);
        if (accepted < DispenseAmount) ShowInfo("Inventory Full");
    }

    private static void ShowInfo(string message)
    {
        if (PickupUIManager.instance != null)
            PickupUIManager.instance.ShowInfo(message);
        else
            Debug.Log(message);
    }
}
