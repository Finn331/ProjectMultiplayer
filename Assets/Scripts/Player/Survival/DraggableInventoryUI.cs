using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class DraggableInventoryUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private ItemIconDatabase iconDatabase;
    [SerializeField] private MobileHotbarUI hotbarUI;

    [Header("Settings")]
    [SerializeField] private int maxSlots = 12;

    private List<InventorySlotData> slotDataList = new List<InventorySlotData>();
    private bool initialized;
    private GameObject draggedIcon;
    private Image draggedImage;
    private ItemType? draggedItemType;
    private int draggedFromSlotIndex = -1;
    private bool isDraggingFromHotbar;

    private class InventorySlotData
    {
        public GameObject slotObject;
        public Image itemIcon;
        public TextMeshProUGUI amountText;
        public int slotIndex = -1;
        public ItemType? itemType;
    }

    private void Awake()
    {
        if (inventory == null)
        {
            inventory = GetComponent<PlayerInventory>();
        }
        if (hotbarUI == null)
        {
            hotbarUI = GetComponent<MobileHotbarUI>();
        }
    }

    private void Start()
    {
        SetupSlots();
        if (inventory != null)
        {
            inventory.InventoryChanged += RefreshUI;
        }
        SetupHotbarDrag();
        RefreshUI();
    }

    private void OnDestroy()
    {
        if (inventory != null)
        {
            inventory.InventoryChanged -= RefreshUI;
        }
    }

    private void Update()
    {
        if (draggedIcon != null)
        {
            draggedIcon.transform.position = Input.mousePosition;
        }
    }

    private void SetupSlots()
    {
        if (initialized) return;
        if (iconDatabase == null) return;

        slotDataList.Clear();

        Transform itemsTransform = FindItemsTransform();
        if (itemsTransform == null) return;

        for (int i = 0; i < maxSlots; i++)
        {
            string slotName = $"Slot_{i}";
            Transform slotTransform = FindDeepChild(itemsTransform, slotName);
            if (slotTransform == null) continue;

            InventorySlotData slotData = new InventorySlotData();
            slotData.slotObject = slotTransform.gameObject;
            slotData.slotIndex = i;

            Transform iconTransform = FindDeepChild(slotTransform, "ItemIcon");
            if (iconTransform != null)
            {
                slotData.itemIcon = iconTransform.GetComponent<Image>();

                Transform countTransform = FindDeepChild(iconTransform, "CountTextInventory");
                if (countTransform != null)
                {
                    slotData.amountText = countTransform.GetComponent<TextMeshProUGUI>();
                    if (slotData.amountText != null)
                    {
                        slotData.amountText.text = "";
                        slotData.amountText.gameObject.SetActive(false);
                    }
                }
            }

            if (slotData.itemIcon != null)
            {
                slotData.itemIcon.enabled = false;
            }

            AddDragEvents(slotData);
            slotDataList.Add(slotData);
        }

        initialized = true;
    }

    private void AddDragEvents(InventorySlotData slotData)
    {
        if (slotData.slotObject == null) return;

        EventTrigger trigger = slotData.slotObject.AddComponent<EventTrigger>();

        EventTrigger.Entry pointerDown = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        pointerDown.callback.AddListener((data) => OnSlotPointerDown(slotData));
        trigger.triggers.Add(pointerDown);

        EventTrigger.Entry pointerUp = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        pointerUp.callback.AddListener((data) => OnSlotPointerUp(slotData));
        trigger.triggers.Add(pointerUp);
    }

    private void OnSlotPointerDown(InventorySlotData slotData)
    {
        if (slotData.itemType == null || slotData.itemIcon == null) return;

        draggedItemType = slotData.itemType;
        draggedFromSlotIndex = slotData.slotIndex;
        isDraggingFromHotbar = false;
        CreateDraggedIcon(slotData);
    }

    private void OnSlotPointerUp(InventorySlotData slotData)
    {
        if (draggedIcon == null || draggedItemType == null || isDraggingFromHotbar) return;

        TryMoveToHotbar();

        DestroyDraggedIcon();
        draggedItemType = null;
        draggedFromSlotIndex = -1;
    }

    private int GetInventorySlotFromMouse()
    {
        Vector2 mousePos = Input.mousePosition;
        PointerEventData pointerData = new PointerEventData(EventSystem.current);
        pointerData.position = mousePos;

        var results = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);

        foreach (var result in results)
        {
            if (result.gameObject.name.StartsWith("Slot_"))
            {
                Transform parent = result.gameObject.transform.parent;
                while (parent != null)
                {
                    if (parent.name == "Items")
                    {
                        Transform grandParent = parent.parent;
                        if (grandParent != null && grandParent.name.Contains("Inventory"))
                        {
                            return GetSlotIndexFromName(result.gameObject.name);
                        }
                        break;
                    }
                    parent = parent.parent;
                }
            }
        }

        return -1;
    }

    private int GetSlotIndexFromName(string name)
    {
        if (name.StartsWith("Slot_"))
        {
            string indexStr = name.Substring(5);
            if (int.TryParse(indexStr, out int index))
            {
                return index;
            }
        }
        return -1;
    }

    private void PerformInventorySwap(int sourceSlot, int targetSlot)
    {
        if (sourceSlot < 0 || targetSlot < 0) return;
        if (sourceSlot == targetSlot) return;
        if (sourceSlot >= inventory.Entries.Count) return;

        var sourceEntry = inventory.Entries[sourceSlot];
        if (sourceEntry == null) return;

        ItemType sourceType = sourceEntry.itemType;
        int sourceAmount = sourceEntry.amount;

        bool targetHasItem = targetSlot < inventory.Entries.Count && inventory.Entries[targetSlot] != null;

        if (targetHasItem)
        {
            var targetEntry = inventory.Entries[targetSlot];
            ItemType targetType = targetEntry.itemType;
            int targetAmount = targetEntry.amount;

            inventory.RemoveItem(sourceType, sourceAmount);
            inventory.RemoveItem(targetType, targetAmount);
            inventory.AddItem(targetType, sourceAmount);
            inventory.AddItem(sourceType, targetAmount);
        }
        else
        {
            inventory.RemoveItem(sourceType, sourceAmount);
            inventory.AddItem(sourceType, sourceAmount);
        }

        RefreshUI();
    }

    private bool TryMoveToHotbar()
    {
        if (draggedItemType == null || hotbarUI == null) return false;

        Vector2 mousePos = Input.mousePosition;
        PointerEventData pointerData = new PointerEventData(EventSystem.current);
        pointerData.position = mousePos;

        var results = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);

        foreach (var result in results)
        {
            Button button = result.gameObject.GetComponent<Button>();
            if (button != null)
            {
                int slotIndex = hotbarUI.GetSlotIndex(button);
                if (slotIndex >= 0)
                {
                    hotbarUI.AssignToSlot(slotIndex, draggedItemType.Value);
                    return true;
                }
            }
        }

        return false;
    }

    private void CreateDraggedIcon(InventorySlotData slotData)
    {
        if (draggedIcon != null) DestroyDraggedIcon();

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;

        draggedIcon = new GameObject("Dragged Icon", typeof(RectTransform), typeof(Image));
        draggedIcon.transform.SetParent(canvas.transform, false);
        draggedIcon.transform.SetAsLastSibling();

        RectTransform rect = draggedIcon.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(50f, 50f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        draggedImage = draggedIcon.GetComponent<Image>();
        draggedImage.sprite = slotData.itemIcon.sprite;
        draggedImage.preserveAspect = true;
    }

    private void DestroyDraggedIcon()
    {
        if (draggedIcon != null)
        {
            Destroy(draggedIcon);
            draggedIcon = null;
            draggedImage = null;
        }
    }

    private void RefreshUI()
    {
        if (!initialized || slotDataList.Count == 0) return;
        if (inventory == null) return;

        if (inventory.Entries == null) return;

        for (int i = 0; i < slotDataList.Count; i++)
        {
            InventorySlotData slotData = slotDataList[i];
            if (slotData.itemIcon == null) continue;

            if (i < inventory.Entries.Count)
            {
                var entry = inventory.Entries[i];
                if (entry == null) continue;

                Sprite icon = iconDatabase != null ? iconDatabase.GetIcon(entry.itemType) : null;

                slotData.itemIcon.sprite = icon;
                slotData.itemIcon.enabled = icon != null;
                slotData.itemType = entry.itemType;

                if (slotData.amountText != null)
                {
                    slotData.amountText.text = entry.amount.ToString();
                    slotData.amountText.gameObject.SetActive(true);
                }
            }
            else
            {
                slotData.itemIcon.sprite = null;
                slotData.itemIcon.enabled = false;
                slotData.itemType = null;

                if (slotData.amountText != null)
                {
                    slotData.amountText.text = "";
                    slotData.amountText.gameObject.SetActive(false);
                }
            }
        }
    }

    private void SetupHotbarDrag()
    {
        if (hotbarUI == null) return;

        hotbarUI.OnSlotDragStart += OnHotbarDragStart;
        hotbarUI.OnSlotDragEnd += OnHotbarDragEnd;
    }

    private void OnHotbarDragStart(int slotIndex, ItemType itemType)
    {
        draggedItemType = itemType;
        isDraggingFromHotbar = true;
        CreateHotbarDragIcon(itemType);
    }

    private void OnHotbarDragEnd(ItemType itemType, int sourceSlot)
    {
        if (draggedIcon == null || draggedItemType == null) return;

        bool droppedOnHotbar = TrySwapHotbarSlots(sourceSlot);

        if (!droppedOnHotbar)
        {
            bool droppedOnInventory = TryMoveToInventory(itemType);
            if (droppedOnInventory && hotbarUI != null)
            {
                hotbarUI.SetItemToSkipFromAssignment(itemType);
                hotbarUI.ClearSlot(sourceSlot);
            }
        }

        DestroyDraggedIcon();
        draggedItemType = null;
        isDraggingFromHotbar = false;
    }

    private bool TrySwapHotbarSlots(int sourceSlot)
    {
        if (hotbarUI == null) return false;

        Vector2 mousePos = Input.mousePosition;
        PointerEventData pointerData = new PointerEventData(EventSystem.current);
        pointerData.position = mousePos;

        var results = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);

        foreach (var result in results)
        {
            Button button = result.gameObject.GetComponent<Button>();
            if (button != null)
            {
                int targetSlot = hotbarUI.GetSlotIndex(button);
                if (targetSlot >= 0 && targetSlot != sourceSlot)
                {
                    hotbarUI.SwapOrMoveSlot(sourceSlot, targetSlot);
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryMoveToInventory(ItemType itemType)
    {
        if (inventory == null) return false;

        Vector2 mousePos = Input.mousePosition;
        PointerEventData pointerData = new PointerEventData(EventSystem.current);
        pointerData.position = mousePos;

        var results = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);

        foreach (var result in results)
        {
            if (result.gameObject.name.StartsWith("Slot_"))
            {
                Transform parent = result.gameObject.transform.parent;
                while (parent != null)
                {
                    if (parent.name == "Items")
                    {
                        Transform grandParent = parent.parent;
                        if (grandParent != null && grandParent.name.Contains("Inventory"))
                        {
                            int currentAmount = inventory.GetAmount(itemType);
                            if (currentAmount <= 0)
                            {
                                inventory.AddItem(itemType, 1);
                            }
                            return true;
                        }
                        break;
                    }
                    parent = parent.parent;
                }
            }
        }
        return false;
    }

    private void CreateHotbarDragIcon(ItemType itemType)
    {
        if (draggedIcon != null) DestroyDraggedIcon();

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;

        Sprite icon = iconDatabase != null ? iconDatabase.GetIcon(itemType) : null;
        if (icon == null) return;

        draggedIcon = new GameObject("Hotbar Dragged Icon", typeof(RectTransform), typeof(Image));
        draggedIcon.transform.SetParent(canvas.transform, false);
        draggedIcon.transform.SetAsLastSibling();

        RectTransform rect = draggedIcon.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(50f, 50f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        draggedImage = draggedIcon.GetComponent<Image>();
        draggedImage.sprite = icon;
        draggedImage.preserveAspect = true;
    }

    private Transform FindDeepChild(Transform parent, string childName)
    {
        if (parent == null || string.IsNullOrEmpty(childName)) return null;

        foreach (Transform child in parent)
        {
            if (child.name == childName) return child;

            Transform found = FindDeepChild(child, childName);
            if (found != null) return found;
        }

        return null;
    }

    private Transform FindItemsTransform()
    {
        Canvas[] canvases = FindObjectsOfType<Canvas>(true);
        foreach (Canvas canvas in canvases)
        {
            Transform invUI = FindDeepChild(canvas.transform, "Inventory UI");
            if (invUI != null)
            {
                Transform items = FindDeepChild(invUI, "Items");
                if (items != null) return items;
            }
        }

        Transform[] allTransforms = FindObjectsOfType<Transform>(true);
        foreach (Transform t in allTransforms)
        {
            if (t.name == "Items")
            {
                Transform parent = t.parent;
                if (parent != null && parent.name.Contains("Inventory"))
                {
                    return t;
                }
            }
        }

        return null;
    }
}
