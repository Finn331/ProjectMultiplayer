using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DraggableInventoryUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private ItemIconDatabase iconDatabase;
    [SerializeField] private MobileHotbarUI hotbarUI;
    [SerializeField] private NetworkInventoryBridge networkBridge;

    [Header("Settings")]
    [SerializeField] private int maxSlots = 12;

    private readonly List<InventorySlotData> slotDataList = new List<InventorySlotData>();
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
        if (networkBridge == null)
        {
            networkBridge = GetComponent<NetworkInventoryBridge>();
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

        if (hotbarUI != null)
        {
            hotbarUI.OnSlotDragStart -= OnHotbarDragStart;
            hotbarUI.OnSlotDragEnd -= OnHotbarDragEnd;
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
        if (initialized || iconDatabase == null)
        {
            return;
        }

        slotDataList.Clear();

        Transform itemsTransform = FindItemsTransform();
        if (itemsTransform == null)
        {
            return;
        }

        int slotLimit = inventory != null ? Mathf.Min(maxSlots, inventory.InventorySlotCount) : maxSlots;
        for (int i = 0; i < slotLimit; i++)
        {
            string slotName = $"Slot_{i}";
            Transform slotTransform = FindDeepChild(itemsTransform, slotName);
            if (slotTransform == null)
            {
                continue;
            }

            InventorySlotData slotData = new InventorySlotData
            {
                slotObject = slotTransform.gameObject,
                slotIndex = i
            };

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
                        slotData.amountText.text = string.Empty;
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
        if (slotData.slotObject == null)
        {
            return;
        }

        EventTrigger trigger = slotData.slotObject.GetComponent<EventTrigger>();
        if (trigger == null)
        {
            trigger = slotData.slotObject.AddComponent<EventTrigger>();
        }

        EventTrigger.Entry pointerDown = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        pointerDown.callback.AddListener(_ => OnSlotPointerDown(slotData));
        trigger.triggers.Add(pointerDown);

        EventTrigger.Entry pointerUp = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        pointerUp.callback.AddListener(_ => OnSlotPointerUp(slotData));
        trigger.triggers.Add(pointerUp);
    }

    private void OnSlotPointerDown(InventorySlotData slotData)
    {
        if (slotData.itemType == null || slotData.itemIcon == null)
        {
            return;
        }

        draggedItemType = slotData.itemType;
        draggedFromSlotIndex = slotData.slotIndex;
        isDraggingFromHotbar = false;
        CreateDraggedIcon(slotData.itemIcon.sprite);
    }

    private void OnSlotPointerUp(InventorySlotData slotData)
    {
        if (draggedItemType == null || isDraggingFromHotbar || draggedFromSlotIndex < 0)
        {
            return;
        }

        bool handled = TryMoveToHotbar(draggedFromSlotIndex);
        if (!handled)
        {
            int targetInventorySlot = GetInventorySlotFromMouse();
            if (targetInventorySlot >= 0 && inventory != null)
            {
                handled = networkBridge != null && networkBridge.UseNetworkedInventory
                    ? networkBridge.TryRequestMoveSlot(draggedFromSlotIndex, targetInventorySlot)
                    : inventory.MoveOrSwapSlot(draggedFromSlotIndex, targetInventorySlot);
            }
        }

        DestroyDraggedIcon();
        draggedItemType = null;
        draggedFromSlotIndex = -1;
        if (handled)
        {
            RefreshUI();
        }
    }

    private int GetInventorySlotFromMouse()
    {
        if (EventSystem.current == null)
        {
            return -1;
        }

        PointerEventData pointerData = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);

        foreach (RaycastResult result in results)
        {
            if (result.gameObject == null)
            {
                continue;
            }

            Transform current = result.gameObject.transform;
            while (current != null && !current.name.StartsWith("Slot_"))
            {
                current = current.parent;
            }

            if (current == null)
            {
                continue;
            }

            Transform parent = current.parent;
            while (parent != null)
            {
                if (parent.name == "Items")
                {
                    Transform grandParent = parent.parent;
                    if (grandParent != null && grandParent.name.Contains("Inventory"))
                    {
                        return GetSlotIndexFromName(current.name);
                    }
                    break;
                }
                parent = parent.parent;
            }
        }

        return -1;
    }

    private int GetSlotIndexFromName(string name)
    {
        if (!name.StartsWith("Slot_"))
        {
            return -1;
        }

        return int.TryParse(name.Substring(5), out int index) ? index : -1;
    }

    private bool TryMoveToHotbar(int sourceInventorySlot)
    {
        if (draggedItemType == null || hotbarUI == null)
        {
            return false;
        }

        if (EventSystem.current == null)
        {
            return false;
        }

        PointerEventData pointerData = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);

        foreach (RaycastResult result in results)
        {
            if (result.gameObject == null)
            {
                continue;
            }

            Button button = result.gameObject.GetComponentInParent<Button>();
            if (button == null)
            {
                continue;
            }

            int hotbarSlotIndex = hotbarUI.GetSlotIndex(button);
            if (hotbarSlotIndex >= 0)
            {
                return hotbarUI.AssignInventorySlotToHotbar(sourceInventorySlot, hotbarSlotIndex);
            }
        }

        return false;
    }

    private void CreateDraggedIcon(Sprite sprite)
    {
        if (draggedIcon != null)
        {
            DestroyDraggedIcon();
        }

        if (sprite == null)
        {
            // Tetap izinkan drag logic berjalan untuk item tanpa icon.
            return;
        }

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            return;
        }

        draggedIcon = new GameObject("Dragged Icon", typeof(RectTransform), typeof(Image));
        draggedIcon.transform.SetParent(canvas.transform, false);
        draggedIcon.transform.SetAsLastSibling();

        RectTransform rect = draggedIcon.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(50f, 50f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        draggedImage = draggedIcon.GetComponent<Image>();
        draggedImage.sprite = sprite;
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
        if (!initialized || slotDataList.Count == 0 || inventory == null)
        {
            return;
        }

        for (int i = 0; i < slotDataList.Count; i++)
        {
            InventorySlotData slotData = slotDataList[i];
            if (slotData.itemIcon == null)
            {
                continue;
            }

            ItemType? itemType = inventory.GetSlotItemType(i);
            int amount = inventory.GetSlotAmount(i);
            if (itemType != null && amount > 0)
            {
                Sprite icon = iconDatabase != null ? iconDatabase.GetIcon(itemType.Value) : null;
                slotData.itemIcon.sprite = icon;
                slotData.itemIcon.enabled = icon != null;
                slotData.itemType = itemType;

                if (slotData.amountText != null)
                {
                    slotData.amountText.text = amount.ToString();
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
                    slotData.amountText.text = string.Empty;
                    slotData.amountText.gameObject.SetActive(false);
                }
            }
        }
    }

    private void SetupHotbarDrag()
    {
        if (hotbarUI == null)
        {
            return;
        }

        hotbarUI.OnSlotDragStart -= OnHotbarDragStart;
        hotbarUI.OnSlotDragEnd -= OnHotbarDragEnd;
        hotbarUI.OnSlotDragStart += OnHotbarDragStart;
        hotbarUI.OnSlotDragEnd += OnHotbarDragEnd;
    }

    private void OnHotbarDragStart(int slotIndex, ItemType itemType)
    {
        draggedItemType = itemType;
        draggedFromSlotIndex = slotIndex;
        isDraggingFromHotbar = true;
        CreateDraggedIcon(iconDatabase != null ? iconDatabase.GetIcon(itemType) : null);
    }

    private void OnHotbarDragEnd(ItemType itemType, int sourceHotbarSlot)
    {
        if (draggedItemType == null || hotbarUI == null || sourceHotbarSlot < 0)
        {
            return;
        }

        bool handled = TrySwapHotbarSlots(sourceHotbarSlot);
        if (!handled)
        {
            int inventoryTarget = GetInventorySlotFromMouse();
            if (inventoryTarget >= 0)
            {
                handled = hotbarUI.MoveHotbarSlotToInventory(sourceHotbarSlot, inventoryTarget);
            }
        }

        DestroyDraggedIcon();
        draggedItemType = null;
        isDraggingFromHotbar = false;
        draggedFromSlotIndex = -1;
        if (handled)
        {
            RefreshUI();
        }
    }

    private bool TrySwapHotbarSlots(int sourceHotbarSlot)
    {
        if (hotbarUI == null)
        {
            return false;
        }

        if (EventSystem.current == null)
        {
            return false;
        }

        PointerEventData pointerData = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);

        foreach (RaycastResult result in results)
        {
            if (result.gameObject == null)
            {
                continue;
            }

            Button button = result.gameObject.GetComponentInParent<Button>();
            if (button == null)
            {
                continue;
            }

            int targetHotbarSlot = hotbarUI.GetSlotIndex(button);
            if (targetHotbarSlot >= 0 && targetHotbarSlot != sourceHotbarSlot)
            {
                hotbarUI.SwapOrMoveSlot(sourceHotbarSlot, targetHotbarSlot);
                return true;
            }
        }

        return false;
    }

    private Transform FindDeepChild(Transform parent, string childName)
    {
        if (parent == null || string.IsNullOrEmpty(childName))
        {
            return null;
        }

        foreach (Transform child in parent)
        {
            if (child.name == childName)
            {
                return child;
            }

            Transform found = FindDeepChild(child, childName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private Transform FindItemsTransform()
    {
        Canvas[] canvases = FindObjectsOfType<Canvas>(true);
        foreach (Canvas canvas in canvases)
        {
            Transform inventoryUI = FindDeepChild(canvas.transform, "Inventory UI");
            if (inventoryUI != null)
            {
                Transform items = FindDeepChild(inventoryUI, "Items");
                if (items != null)
                {
                    return items;
                }
            }
        }

        Transform[] allTransforms = FindObjectsOfType<Transform>(true);
        foreach (Transform current in allTransforms)
        {
            if (current.name == "Items")
            {
                Transform parent = current.parent;
                if (parent != null && parent.name.Contains("Inventory"))
                {
                    return current;
                }
            }
        }

        return null;
    }
}
