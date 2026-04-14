using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class MobileHotbarUI : MonoBehaviour
{
    [Header("Reference")]
    public PlayerInventory inventory;
    public NetworkInventoryBridge networkBridge;

    [Header("UI")]
    public System.Collections.Generic.List<Button> slotButtons = new System.Collections.Generic.List<Button>();
    public System.Collections.Generic.List<Image> slotIcons = new System.Collections.Generic.List<Image>();

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
    public System.Collections.Generic.List<TMP_Text> slotCounts = new System.Collections.Generic.List<TMP_Text>();

    [Header("Drag Settings")]
    [HideInInspector] public Action<int, ItemType> OnSlotDragStart;
    [HideInInspector] public Action<ItemType, int> OnSlotDragEnd;

    public event Action<int, ItemType?> SelectedSlotChanged;

    private int selectedSlot = -1;
    private int dragSourceSlot = -1;
    private ItemType? draggedItemType;
    private bool dragEventsInitialized;
    private int lastSelectedSlotBroadcast = int.MinValue;
    private ItemType? lastSelectedItemBroadcast;

    private int SlotCount => inventory != null ? Mathf.Min(Mathf.Max(1, maxSlots), inventory.HotbarSlotCount) : Mathf.Max(1, maxSlots);
    public int SelectedSlotIndex => selectedSlot;
    public ItemType? SelectedItem => this.GetSlotItem(selectedSlot);

    private void Awake()
    {
        this.ResolveReferences();
    }

    private void OnEnable()
    {
        this.ResolveReferences();
        this.SubscribeInventory();
        this.SetupDragEvents();
        this.Refresh();
    }

    private void OnDisable()
    {
        this.UnsubscribeInventory();
    }

    private void OnValidate()
    {
        if (maxSlots < 1)
        {
            maxSlots = 1;
        }
    }

    private void Update()
    {
        this.HandleSwipe();
    }

    private void ResolveReferences()
    {
        if (inventory == null)
        {
            inventory = GetComponent<PlayerInventory>();
        }

        if (networkBridge == null)
        {
            networkBridge = GetComponent<NetworkInventoryBridge>();
        }
    }

    private void SubscribeInventory()
    {
        if (inventory == null)
        {
            return;
        }

        inventory.InventoryChanged -= this.OnInventoryChanged;
        inventory.InventoryChanged += this.OnInventoryChanged;
    }

    private void UnsubscribeInventory()
    {
        if (inventory == null)
        {
            return;
        }

        inventory.InventoryChanged -= this.OnInventoryChanged;
    }

    private void OnInventoryChanged()
    {
        this.Refresh();
    }

    public int GetHotbarGlobalSlotIndex(int hotbarSlotIndex)
    {
        if (inventory == null || hotbarSlotIndex < 0 || hotbarSlotIndex >= SlotCount)
        {
            return -1;
        }

        return inventory.HotbarStartIndex + hotbarSlotIndex;
    }

    public ItemType? GetSlotItem(int hotbarSlotIndex)
    {
        int globalSlotIndex = this.GetHotbarGlobalSlotIndex(hotbarSlotIndex);
        return globalSlotIndex >= 0 && inventory != null ? inventory.GetSlotItemType(globalSlotIndex) : (ItemType?)null;
    }

    public int GetSlotAmount(int hotbarSlotIndex)
    {
        int globalSlotIndex = this.GetHotbarGlobalSlotIndex(hotbarSlotIndex);
        return globalSlotIndex >= 0 && inventory != null ? inventory.GetSlotAmount(globalSlotIndex) : 0;
    }

    public void Refresh()
    {
        this.ResolveReferences();
        this.EnsureSelectedSlotValidity();

        for (int i = 0; i < slotButtons.Count; i++)
        {
            Button button = slotButtons[i];
            if (button != null && button.image != null)
            {
                button.image.color = i == selectedSlot ? selectedColor : normalColor;
            }

            Image icon = i < slotIcons.Count ? slotIcons[i] : null;
            TMP_Text countLabel = i < slotCounts.Count ? slotCounts[i] : null;

            if (i >= SlotCount || inventory == null)
            {
                this.ClearSlotVisual(icon, countLabel);
                continue;
            }

            ItemType? itemType = this.GetSlotItem(i);
            int amount = this.GetSlotAmount(i);
            if (itemType == null || amount <= 0)
            {
                this.ClearSlotVisual(icon, countLabel);
                continue;
            }

            if (icon != null)
            {
                icon.sprite = iconDatabase != null ? iconDatabase.GetIcon(itemType.Value) : null;
                icon.enabled = icon.sprite != null;
            }

            if (countLabel != null)
            {
                countLabel.text = amount.ToString();
                countLabel.gameObject.SetActive(true);
            }
        }

        this.BroadcastSelectedStateIfNeeded();
    }

    private void ClearSlotVisual(Image icon, TMP_Text countLabel)
    {
        if (icon != null)
        {
            icon.sprite = null;
            icon.enabled = false;
        }

        if (countLabel != null)
        {
            countLabel.text = string.Empty;
            countLabel.gameObject.SetActive(false);
        }
    }

    private void BroadcastSelectedStateIfNeeded()
    {
        ItemType? currentItem = this.SelectedItem;
        if (lastSelectedSlotBroadcast == selectedSlot && lastSelectedItemBroadcast == currentItem)
        {
            return;
        }

        lastSelectedSlotBroadcast = selectedSlot;
        lastSelectedItemBroadcast = currentItem;
        SelectedSlotChanged?.Invoke(selectedSlot, currentItem);
    }

    public void SelectSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount)
        {
            return;
        }

        selectedSlot = slotIndex;
        this.Refresh();
    }

    public void SelectNext()
    {
        if (SlotCount <= 0)
        {
            return;
        }

        int next = selectedSlot < 0 ? -1 : selectedSlot;
        for (int attempt = 0; attempt < SlotCount; attempt++)
        {
            next = (next + 1 + SlotCount) % SlotCount;
            if (this.GetSlotItem(next) != null)
            {
                this.SelectSlot(next);
                return;
            }
        }

        selectedSlot = -1;
        this.Refresh();
    }

    public void SelectPrevious()
    {
        if (SlotCount <= 0)
        {
            return;
        }

        int previous = selectedSlot < 0 ? 0 : selectedSlot;
        for (int attempt = 0; attempt < SlotCount; attempt++)
        {
            previous = (previous - 1 + SlotCount) % SlotCount;
            if (this.GetSlotItem(previous) != null)
            {
                this.SelectSlot(previous);
                return;
            }
        }

        selectedSlot = -1;
        this.Refresh();
    }

    private void HandleSwipe()
    {
        if (Input.touchCount == 0)
        {
            return;
        }

        Touch touch = Input.GetTouch(0);
        if (touch.phase != TouchPhase.Ended)
        {
            return;
        }

        if (touch.deltaPosition.x > swipeThreshold)
        {
            this.SelectNext();
        }
        else if (touch.deltaPosition.x < -swipeThreshold)
        {
            this.SelectPrevious();
        }
    }

    public void DropSelectedItem()
    {
        if (selectedSlot < 0)
        {
            return;
        }

        this.DropFromSlot(selectedSlot);
    }

    public void DropFromSlot(int hotbarSlotIndex)
    {
        int globalSlotIndex = this.GetHotbarGlobalSlotIndex(hotbarSlotIndex);
        if (globalSlotIndex < 0 || inventory == null || inventory.IsSlotEmpty(globalSlotIndex))
        {
            return;
        }

        bool dropRequested;
        if (networkBridge != null && networkBridge.UseNetworkedInventory)
        {
            dropRequested = networkBridge.TryRequestDropFromSlot(globalSlotIndex, 1);
        }
        else
        {
            dropRequested = inventory.DropItemFromSlot(globalSlotIndex, 1);
        }

        if (!dropRequested)
        {
            return;
        }

        if (inventory.IsSlotEmpty(globalSlotIndex) && selectedSlot == hotbarSlotIndex)
        {
            selectedSlot = this.FindFirstOccupiedSlot();
        }

        this.Refresh();
    }

    public bool AssignInventorySlotToHotbar(int sourceInventorySlot, int hotbarSlotIndex)
    {
        int targetGlobalSlotIndex = this.GetHotbarGlobalSlotIndex(hotbarSlotIndex);
        if (inventory == null || targetGlobalSlotIndex < 0)
        {
            return false;
        }

        bool moved = networkBridge != null && networkBridge.UseNetworkedInventory
            ? networkBridge.TryRequestMoveSlot(sourceInventorySlot, targetGlobalSlotIndex)
            : inventory.MoveOrSwapSlot(sourceInventorySlot, targetGlobalSlotIndex);

        if (moved)
        {
            selectedSlot = hotbarSlotIndex;
            this.Refresh();
        }

        return moved;
    }

    public void SwapOrMoveSlot(int sourceHotbarSlot, int targetHotbarSlot)
    {
        int sourceGlobal = this.GetHotbarGlobalSlotIndex(sourceHotbarSlot);
        int targetGlobal = this.GetHotbarGlobalSlotIndex(targetHotbarSlot);
        if (sourceGlobal < 0 || targetGlobal < 0)
        {
            return;
        }

        bool moved = networkBridge != null && networkBridge.UseNetworkedInventory
            ? networkBridge.TryRequestMoveSlot(sourceGlobal, targetGlobal)
            : inventory != null && inventory.MoveOrSwapSlot(sourceGlobal, targetGlobal);

        if (!moved)
        {
            return;
        }

        if (selectedSlot == sourceHotbarSlot)
        {
            selectedSlot = targetHotbarSlot;
        }
        else if (selectedSlot == targetHotbarSlot)
        {
            selectedSlot = sourceHotbarSlot;
        }

        this.Refresh();
    }

    public void SwapSlot(int sourceHotbarSlot, int targetHotbarSlot)
    {
        this.SwapOrMoveSlot(sourceHotbarSlot, targetHotbarSlot);
    }

    public bool MoveHotbarSlotToInventory(int hotbarSlotIndex, int inventorySlotIndex)
    {
        int sourceGlobal = this.GetHotbarGlobalSlotIndex(hotbarSlotIndex);
        if (sourceGlobal < 0 || inventory == null || inventorySlotIndex < 0 || inventorySlotIndex >= inventory.InventorySlotCount)
        {
            return false;
        }

        bool moved = networkBridge != null && networkBridge.UseNetworkedInventory
            ? networkBridge.TryRequestMoveSlot(sourceGlobal, inventorySlotIndex)
            : inventory.MoveOrSwapSlot(sourceGlobal, inventorySlotIndex);

        if (moved && selectedSlot == hotbarSlotIndex && inventory.IsSlotEmpty(sourceGlobal))
        {
            selectedSlot = this.FindFirstOccupiedSlot();
        }

        if (moved)
        {
            this.Refresh();
        }

        return moved;
    }

    public void AssignToSlot(int hotbarSlotIndex, ItemType type)
    {
        if (inventory == null)
        {
            return;
        }

        int sourceSlotIndex = inventory.FindFirstSlotWithItemType(type);
        if (sourceSlotIndex < 0)
        {
            return;
        }

        this.AssignInventorySlotToHotbar(sourceSlotIndex, hotbarSlotIndex);
    }

    public int GetSlotIndex(Button button)
    {
        if (button == null)
        {
            return -1;
        }

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
        for (int i = 0; i < SlotCount; i++)
        {
            if (this.GetSlotItem(i) == null)
            {
                return i;
            }
        }

        return -1;
    }

    private void SetupDragEvents()
    {
        if (dragEventsInitialized)
        {
            return;
        }

        for (int i = 0; i < slotButtons.Count; i++)
        {
            Button slotButton = slotButtons[i];
            if (slotButton == null)
            {
                continue;
            }

            int slotIndex = i;
            EventTrigger trigger = slotButton.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = slotButton.gameObject.AddComponent<EventTrigger>();
            }

            EventTrigger.Entry pointerDown = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            pointerDown.callback.AddListener(_ => this.OnHotbarSlotPointerDown(slotIndex));
            trigger.triggers.Add(pointerDown);

            EventTrigger.Entry pointerUp = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
            pointerUp.callback.AddListener(_ => this.OnHotbarSlotPointerUp(slotIndex));
            trigger.triggers.Add(pointerUp);
        }

        dragEventsInitialized = true;
    }

    private void OnHotbarSlotPointerDown(int slotIndex)
    {
        ItemType? slotItem = this.GetSlotItem(slotIndex);
        if (slotItem == null)
        {
            return;
        }

        draggedItemType = slotItem;
        dragSourceSlot = slotIndex;
        OnSlotDragStart?.Invoke(slotIndex, slotItem.Value);
    }

    private void OnHotbarSlotPointerUp(int slotIndex)
    {
        if (draggedItemType == null)
        {
            return;
        }

        ItemType itemType = draggedItemType.Value;
        OnSlotDragEnd?.Invoke(itemType, dragSourceSlot);
        draggedItemType = null;
        dragSourceSlot = -1;
    }

    private void EnsureSelectedSlotValidity()
    {
        if (selectedSlot >= SlotCount)
        {
            selectedSlot = -1;
        }

        if (selectedSlot >= 0)
        {
            return;
        }

        selectedSlot = this.FindFirstOccupiedSlot();
    }

    private int FindFirstOccupiedSlot()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (this.GetSlotItem(i) != null)
            {
                return i;
            }
        }

        return -1;
    }
}
