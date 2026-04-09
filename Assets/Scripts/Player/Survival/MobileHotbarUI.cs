using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class MobileHotbarUI : MonoBehaviour
{
    [Header("Reference")]
    public PlayerInventory inventory;
    public NetworkInventoryBridge networkBridge;

    [Header("UI")]
    public List<Button> slotButtons = new List<Button>();
    public List<Image> slotIcons = new List<Image>();

    [Header("Setting")]
    public int maxSlots = 5;

    [Header("Visual")]
    public Color normalColor = Color.white;
    public Color selectedColor = Color.yellow;

    [Header("Mobile Input")]
    public float swipeThreshold = 50f;

    [Header("Icon")]
    public ItemIconDatabase iconDatabase;

    [Header("Item Count UI")]
    public List<TMP_Text> slotCounts = new List<TMP_Text>();

    [Header("Drag Settings")]
    [HideInInspector] public System.Action<int, ItemType> OnSlotDragStart;
    [HideInInspector] public System.Action<ItemType, int> OnSlotDragEnd;

    private ItemType?[] slotItems;
    private int selectedSlot = -1;
    private int dragSourceSlot = -1;
    private ItemType? draggedItemType;
    private ItemType? itemToSkipFromAssignment;

    void Start()
    {
        slotItems = new ItemType?[maxSlots];
        if (inventory != null)
        {
            inventory.InventoryChanged += OnInventoryChanged;
        }

        inventory.InventoryChanged += Refresh;
        SetupDragEvents();
        Refresh();
    }

    void Update()
    {
        HandleSwipe();
    }

    void OnDisable()
    {
        if (inventory != null)
        {
            inventory.InventoryChanged -= OnInventoryChanged;
        }
    }

    void OnInventoryChanged()
    {
        AutoAssignFromInventory();
        Refresh();
    }

    void AutoAssignFromInventory()
    {
        ItemType? skipThisItem = itemToSkipFromAssignment;
        itemToSkipFromAssignment = null;

        for (int slotIndex = 0; slotIndex < maxSlots; slotIndex++)
        {
            if (slotItems[slotIndex] != null) continue;

            foreach (var entry in inventory.Entries)
            {
                if (skipThisItem != null && entry.itemType == skipThisItem.Value) continue;

                bool alreadyInHotbar = false;
                for (int i = 0; i < maxSlots; i++)
                {
                    if (slotItems[i] == entry.itemType)
                    {
                        alreadyInHotbar = true;
                        break;
                    }
                }

                if (!alreadyInHotbar)
                {
                    slotItems[slotIndex] = entry.itemType;
                    break;
                }
            }
        }
    }

    void Refresh()
    {
        for (int i = 0; i < maxSlots; i++)
        {
            // 🔥 DEFAULT VISUAL SLOT
            slotButtons[i].image.color = (i == selectedSlot) ? selectedColor : normalColor;

            if (slotItems[i] == null)
            {
                slotIcons[i].enabled = false;
                slotCounts[i].text = "";
                slotCounts[i].gameObject.SetActive(false);
                continue;
            }

            ItemType type = slotItems[i].Value;
            int amount = inventory.GetAmount(type);

            if (amount <= 0)
            {
                slotItems[i] = null;
                slotIcons[i].enabled = false;
                slotCounts[i].text = "";
                slotCounts[i].gameObject.SetActive(false);
                continue;
            }

            // ICON
            Sprite icon = iconDatabase.GetIcon(type);
            slotIcons[i].sprite = icon;
            slotIcons[i].enabled = true;

            // 🔢 COUNT
            slotCounts[i].text = amount.ToString();
            slotCounts[i].gameObject.SetActive(true);
        }
    }

    public void SelectSlot(int slotIndex)
    {
        selectedSlot = slotIndex;
        Refresh();
    }

    void UseItem(ItemType itemType)
    {
        Debug.Log("Use item: " + itemType);

        // 🔥 MULTIPLAYER SAFE
        if (networkBridge != null && networkBridge.UseNetworkedInventory)
        {
            bool success = networkBridge.TryRequestDrop(itemType, 1);

            if (!success)
            {
                Debug.Log("Gagal pakai item (no authority)");
            }

            return;
        }

        // LOCAL
        inventory.DropItem(itemType, 1);
    }

    void HandleSwipe()
    {
        if (Input.touchCount == 0) return;

        Touch touch = Input.GetTouch(0);

        if (touch.phase == TouchPhase.Ended)
        {
            if (touch.deltaPosition.x > swipeThreshold)
            {
                SelectNext();
            }
            else if (touch.deltaPosition.x < -swipeThreshold)
            {
                SelectPrevious();
            }
        }
    }

    void SelectNext()
    {
        selectedSlot = (selectedSlot + 1) % maxSlots;
        SelectSlot(selectedSlot);
    }

    void SelectPrevious()
    {
        selectedSlot--;

        if (selectedSlot < 0)
            selectedSlot = maxSlots - 1;

        SelectSlot(selectedSlot);
    }

    public void DropFromSlot(int index)
    {
        if (slotItems[index] == null) return;

        ItemType type = slotItems[index].Value;

        if (networkBridge != null && networkBridge.UseNetworkedInventory)
        {
            networkBridge.TryRequestDrop(type, 1);
        }
        else
        {
            inventory.DropItem(type, 1);
        }
    }

    public void SwapSlot(int a, int b)
    {
        var temp = slotItems[a];
        slotItems[a] = slotItems[b];
        slotItems[b] = temp;

        Refresh();
    }

    public void AssignToSlot(int slotIndex, ItemType type)
    {
        if (slotIndex < 0 || slotIndex >= maxSlots) return;
        slotItems[slotIndex] = type;
        Refresh();
    }

    public int GetSlotIndex(Button button)
    {
        if (button == null) return -1;
        for (int i = 0; i < slotButtons.Count; i++)
        {
            if (slotButtons[i] == button)
            {
                return i;
            }
        }
        return -1;
    }

    public int GetFirstEmptySlot()
    {
        for (int i = 0; i < maxSlots; i++)
        {
            if (slotItems[i] == null)
            {
                return i;
            }
        }
        return -1;
    }

    private void SetupDragEvents()
    {
        for (int i = 0; i < slotButtons.Count && i < maxSlots; i++)
        {
            int slotIndex = i;
            EventTrigger trigger = slotButtons[i].GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = slotButtons[i].gameObject.AddComponent<EventTrigger>();
            }

            EventTrigger.Entry pointerDown = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            pointerDown.callback.AddListener((data) => OnHotbarSlotPointerDown(slotIndex));
            trigger.triggers.Add(pointerDown);

            EventTrigger.Entry pointerUp = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
            pointerUp.callback.AddListener((data) => OnHotbarSlotPointerUp(slotIndex));
            trigger.triggers.Add(pointerUp);
        }
    }

    private void OnHotbarSlotPointerDown(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= maxSlots || slotItems[slotIndex] == null) return;

        draggedItemType = slotItems[slotIndex];
        dragSourceSlot = slotIndex;
        OnSlotDragStart?.Invoke(slotIndex, draggedItemType.Value);
    }

    private void OnHotbarSlotPointerUp(int slotIndex)
    {
        if (draggedItemType == null) return;

        ItemType itemType = draggedItemType.Value;
        OnSlotDragEnd?.Invoke(itemType, dragSourceSlot);
        draggedItemType = null;
        dragSourceSlot = -1;
    }

    public void RemoveItemFromSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= maxSlots || slotItems[slotIndex] == null) return;

        ItemType type = slotItems[slotIndex].Value;
        slotItems[slotIndex] = null;
        Refresh();
    }

    public ItemType? GetSlotItem(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= maxSlots) return null;
        return slotItems[slotIndex];
    }

    public void ClearSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= maxSlots) return;
        slotItems[slotIndex] = null;
        Refresh();
    }

    public void SetItemToSkipFromAssignment(ItemType itemType)
    {
        itemToSkipFromAssignment = itemType;
    }

    public void SwapOrMoveSlot(int sourceSlot, int targetSlot)
    {
        if (sourceSlot < 0 || sourceSlot >= maxSlots) return;
        if (targetSlot < 0 || targetSlot >= maxSlots) return;
        if (sourceSlot == targetSlot) return;

        ItemType? sourceItem = slotItems[sourceSlot];
        ItemType? targetItem = slotItems[targetSlot];

        slotItems[targetSlot] = sourceItem;
        slotItems[sourceSlot] = targetItem;

        Refresh();
    }
}