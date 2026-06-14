using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class StorageChestUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private MobileHotbarUI hotbarUI;
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private RectTransform panelRoot;
    [SerializeField] private RectTransform playerSlotsRoot;
    [SerializeField] private RectTransform chestSlotsRoot;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private Button closeButton;
    [SerializeField] private ItemIconDatabase iconDatabase;

    [Header("Behavior")]
    [SerializeField] private float autoCloseDistancePadding = 0.35f;
    [SerializeField] private float splitLongPressSeconds = 0.45f;
    [SerializeField] private Color splitHighlightColor = new Color(0.25f, 0.7f, 1f, 1f);

    [Header("Layout")]
    [SerializeField] private Vector2 panelSize = new Vector2(760f, 440f);
    [SerializeField] private Vector2 slotSize = new Vector2(58f, 58f);
    [SerializeField] private float slotSpacing = 8f;

    [Header("Style")]
    [SerializeField] private Color panelColor = new Color(0f, 0f, 0f, 0.72f);
    [SerializeField] private Color slotColor = new Color(0.16f, 0.16f, 0.16f, 0.95f);
    [SerializeField] private Color slotHighlightColor = new Color(0.95f, 0.78f, 0.25f, 1f);

    private readonly List<StorageChestSlotUI> playerSlots = new List<StorageChestSlotUI>();
    private readonly List<StorageChestSlotUI> chestSlots = new List<StorageChestSlotUI>();
    private readonly List<GameObject> generatedPanelChildren = new List<GameObject>();
    private StorageChest activeChest;
    private FusionStorageChest activeFusionChest;
    private StorageChestSlotUI dragSourceSlot;
    private GameObject dragIconObject;
    private Image dragIconImage;
    private PlayerInventory subscribedInventory;
    private MobileHotbarUI subscribedHotbar;
    private int hotbarDragSourceGlobalSlot = -1;
    private ItemType? hotbarDragItemType;
    private bool dragDropHandled;
    private bool createdPanelRoot;
    private bool initialized;
    private StorageChestSlotUI pointerDownSlot;
    private float pointerDownStartedAt;
    private bool splitDragMode;
    private bool hotbarSplitArmed;
    private int hotbarSplitSlot = -1;
    private RectTransform splitDialogRoot;
    private TextMeshProUGUI splitDialogTitle;
    private TextMeshProUGUI splitDialogInfoLabel;
    private TMP_InputField splitQuantityInput;
    private Button splitMinusButton;
    private Button splitPlusButton;
    private Button splitHalfButton;
    private Button splitMaxButton;
    private Button splitCancelButton;
    private Button splitConfirmButton;
    private SplitTransfer pendingSplitTransfer;

    private enum SplitTransferDirection
    {
        None,
        PlayerToChest,
        ChestToPlayer
    }

    private struct SplitTransfer
    {
        public SplitTransferDirection Direction;
        public int PlayerSlot;
        public int ChestSlot;
        public int MaxAmount;
        public int SourceAmount;
        public ItemType SourceItemType;
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        EnsureUI();
        BindPlayerInventory();
        BindHotbarDrag();
        SetVisible(false);
    }

    private void OnDisable()
    {
        CleanupRuntimeUI();
    }

    private void OnDestroy()
    {
        CleanupRuntimeUI();
    }

    private void Update()
    {
        if (!ShouldKeepChestOpen())
        {
            CloseChest();
            return;
        }

        UpdateSplitLongPress();
    }

    public void HandleSlotPointerDown(StorageChestSlotUI slot)
    {
        if (slot == null || !SlotHasItem(slot) || GetSlotAmount(slot) <= 1)
        {
            return;
        }

        pointerDownSlot = slot;
        pointerDownStartedAt = Time.unscaledTime;
    }

    public void HandleSlotPointerUp(StorageChestSlotUI slot)
    {
        if (pointerDownSlot == slot)
        {
            if (splitDragMode && dragSourceSlot == null)
            {
                ClearSplitSlotState();
                return;
            }

            pointerDownSlot = null;
        }
    }

    private void UpdateSplitLongPress()
    {
        if (pointerDownSlot == null || splitDragMode)
        {
            return;
        }

        if (Time.unscaledTime - pointerDownStartedAt < splitLongPressSeconds)
        {
            return;
        }

        if (!SlotHasItem(pointerDownSlot) || GetSlotAmount(pointerDownSlot) <= 1)
        {
            pointerDownSlot = null;
            return;
        }

        splitDragMode = true;
        pointerDownSlot.SetHighlight(true, slotColor, splitHighlightColor);
    }

    private void ClearSplitSlotState()
    {
        if (pointerDownSlot != null)
        {
            pointerDownSlot.SetHighlight(false, slotColor, slotHighlightColor);
        }

        pointerDownSlot = null;
        splitDragMode = false;
    }

    private void ClearHotbarSplitState()
    {
        hotbarSplitArmed = false;
        hotbarSplitSlot = -1;
    }

    public void OpenChest(StorageChest chest)
    {
        if (chest == null)
        {
            return;
        }

        EnsureUI();
        if (activeChest != chest)
        {
            HideSplitDialog();
            ClearSplitSlotState();
            ClearHotbarSplitState();
            UnbindChest();
            activeChest = chest;
            activeChest.ChestChanged += Refresh;
            RebuildChestSlots();
        }

        SetVisible(true);
        Refresh();
    }

    public void OpenChest(FusionStorageChest chest)
    {
        if (chest == null)
        {
            return;
        }

        EnsureUI();
        if (activeFusionChest != chest)
        {
            HideSplitDialog();
            ClearSplitSlotState();
            ClearHotbarSplitState();
            UnbindChest();
            activeFusionChest = chest;
            activeFusionChest.ChestChanged += Refresh;
            RebuildChestSlots();
        }

        SetVisible(true);
        Refresh();
    }

    public void CloseChest()
    {
        HideSplitDialog();
        ClearSplitSlotState();
        ClearHotbarSplitState();
        DestroyDragIcon();
        UnbindChest();
        SetVisible(false);
    }

    public void BeginSlotDrag(StorageChestSlotUI slot, PointerEventData eventData)
    {
        if (slot == null || !SlotHasItem(slot))
        {
            return;
        }

        dragSourceSlot = slot;
        if (pointerDownSlot != slot)
        {
            splitDragMode = false;
        }

        slot.SetHighlight(true, slotColor, slotHighlightColor);
        CreateDragIcon(GetSlotSprite(slot));
        UpdateSlotDrag(eventData);
    }

    public void UpdateSlotDrag(PointerEventData eventData)
    {
        if (dragIconObject != null && eventData != null)
        {
            dragIconObject.transform.position = eventData.position;
        }
    }

    public void EndSlotDrag(StorageChestSlotUI slot, PointerEventData eventData)
    {
        if (!dragDropHandled)
        {
            StorageChestSlotUI targetSlot = FindSlotUnderPointer(eventData);
            if (targetSlot != null)
            {
                HandleSlotDrop(targetSlot);
            }

            if (!dragDropHandled && dragSourceSlot != null && dragSourceSlot.Kind == StorageChestSlotKind.Chest)
            {
                int targetHotbarSlot = FindHotbarSlotUnderPointer(eventData);
                if (targetHotbarSlot >= 0)
                {
                    int targetPlayerSlot = hotbarUI != null ? hotbarUI.GetHotbarGlobalSlotIndex(targetHotbarSlot) : -1;
                    if (splitDragMode)
                    {
                        TryOpenSplitDialog(SplitTransferDirection.ChestToPlayer, targetPlayerSlot, dragSourceSlot.SlotIndex);
                        dragDropHandled = true;
                    }
                    else
                    {
                        TakeChestSlotToHotbar(dragSourceSlot.SlotIndex, targetHotbarSlot);
                        dragDropHandled = true;
                    }
                }
            }

            if (!dragDropHandled && dragSourceSlot != null && dragSourceSlot.Kind == StorageChestSlotKind.PlayerInventory)
            {
                int targetHotbarSlot = FindHotbarSlotUnderPointer(eventData);
                if (targetHotbarSlot >= 0)
                {
                    MovePlayerSlotToHotbar(dragSourceSlot.SlotIndex, targetHotbarSlot);
                    dragDropHandled = true;
                }
            }
        }

        DestroyDragIcon();
        if (dragSourceSlot != null)
        {
            dragSourceSlot.SetHighlight(false, slotColor, slotHighlightColor);
        }

        dragSourceSlot = null;
        dragDropHandled = false;
        ClearSplitSlotState();
        Refresh();
    }

    public void HandleSlotDrop(StorageChestSlotUI targetSlot)
    {
        if (dragSourceSlot == null || targetSlot == null || dragSourceSlot == targetSlot)
        {
            return;
        }

        if (dragSourceSlot.Kind == StorageChestSlotKind.PlayerInventory && targetSlot.Kind == StorageChestSlotKind.Chest)
        {
            dragDropHandled = true;
            if (splitDragMode)
            {
                TryOpenSplitDialog(SplitTransferDirection.PlayerToChest, dragSourceSlot.SlotIndex, targetSlot.SlotIndex);
                return;
            }

            DepositPlayerSlotToChest(dragSourceSlot.SlotIndex, targetSlot.SlotIndex);
            return;
        }

        if (dragSourceSlot.Kind == StorageChestSlotKind.Chest && targetSlot.Kind == StorageChestSlotKind.PlayerInventory)
        {
            dragDropHandled = true;
            if (splitDragMode)
            {
                TryOpenSplitDialog(SplitTransferDirection.ChestToPlayer, targetSlot.SlotIndex, dragSourceSlot.SlotIndex);
                return;
            }

            TakeChestSlotToPlayer(dragSourceSlot.SlotIndex, targetSlot.SlotIndex);
        }
    }

    private void ResolveReferences()
    {
        if (playerInventory == null)
        {
            playerInventory = GetComponent<PlayerInventory>();
        }

        if (iconDatabase == null)
        {
            iconDatabase = Resources.Load<ItemIconDatabase>("ItemIconDB");
            if (iconDatabase == null)
            {
                ItemIconDatabase[] databases = Resources.FindObjectsOfTypeAll<ItemIconDatabase>();
                if (databases != null && databases.Length > 0)
                {
                    iconDatabase = databases[0];
                }
            }
        }

        if (hotbarUI == null)
        {
            hotbarUI = GetComponent<MobileHotbarUI>();
        }
    }

    private Canvas ResolveTargetCanvas()
    {
        Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
        if (canvases == null)
        {
            return null;
        }

        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas != null && canvas.gameObject.scene.IsValid())
            {
                return canvas;
            }
        }

        return null;
    }

    private void EnsureUI()
    {
        if (initialized)
        {
            return;
        }

        ResolveReferences();
        if (targetCanvas == null)
        {
            targetCanvas = ResolveTargetCanvas();
        }

        if (targetCanvas == null)
        {
            return;
        }

        if (panelRoot == null)
        {
            panelRoot = CreatePanel(targetCanvas.transform as RectTransform);
            createdPanelRoot = true;
        }

        titleText = CreateLabel("Title", panelRoot, 24f, FontStyles.Bold, new Color(1f, 0.85f, 0.4f, 1f), TextAlignmentOptions.Center);
        TrackGeneratedPanelChild(titleText.gameObject);
        titleText.rectTransform.anchorMin = new Vector2(0f, 1f);
        titleText.rectTransform.anchorMax = new Vector2(1f, 1f);
        titleText.rectTransform.pivot = new Vector2(0.5f, 1f);
        titleText.rectTransform.anchoredPosition = new Vector2(0f, -12f);
        titleText.rectTransform.sizeDelta = new Vector2(-80f, 34f);

        closeButton = CreateButton("Close Chest Button", panelRoot, new Vector2(-28f, -14f), "X", CloseChest);
        TrackGeneratedPanelChild(closeButton.gameObject);
        RectTransform closeRect = closeButton.GetComponent<RectTransform>();
        closeRect.anchorMin = new Vector2(1f, 1f);
        closeRect.anchorMax = new Vector2(1f, 1f);
        closeRect.pivot = new Vector2(1f, 1f);
        closeRect.sizeDelta = new Vector2(44f, 34f);

        playerSlotsRoot = CreateSection("Inventory Slots", panelRoot, new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(20f, 24f), new Vector2(-12f, -60f));
        chestSlotsRoot = CreateSection("Chest Slots", panelRoot, new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(12f, 24f), new Vector2(-20f, -60f));
        TrackGeneratedPanelChild(playerSlotsRoot.gameObject);
        TrackGeneratedPanelChild(chestSlotsRoot.gameObject);

        BuildSlotGrids();
        initialized = true;
        BindPlayerInventory();
        BindHotbarDrag();
    }

    private RectTransform CreatePanel(RectTransform parent)
    {
        GameObject panelObject = new GameObject("Storage Chest UI", typeof(RectTransform), typeof(Image));
        RectTransform rect = panelObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = panelSize;
        rect.anchoredPosition = Vector2.zero;
        panelObject.GetComponent<Image>().color = panelColor;
        return rect;
    }

    private RectTransform CreateSection(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject sectionObject = new GameObject(name, typeof(RectTransform));
        RectTransform rect = sectionObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        return rect;
    }

    private void BuildSlotGrids()
    {
        playerSlots.Clear();
        chestSlots.Clear();

        int playerSlotCount = playerInventory != null ? playerInventory.InventorySlotCount : 12;
        for (int i = 0; i < playerSlotCount; i++)
        {
            playerSlots.Add(CreateSlot(playerSlotsRoot, StorageChestSlotKind.PlayerInventory, i));
        }

        int chestSlotCount = Mathf.Max(1, GetActiveSlotCountFallback());
        for (int i = 0; i < chestSlotCount; i++)
        {
            chestSlots.Add(CreateSlot(chestSlotsRoot, StorageChestSlotKind.Chest, i));
        }
    }

    private int GetActiveSlotCountFallback()
    {
        if (activeFusionChest != null)
        {
            return activeFusionChest.Slots;
        }

        if (activeChest != null)
        {
            return activeChest.SlotCount;
        }

        return 12;
    }

    private StorageChestSlotUI CreateSlot(RectTransform parent, StorageChestSlotKind kind, int slotIndex)
    {
        GameObject slotObject = new GameObject(string.Format("{0} Slot {1}", kind, slotIndex), typeof(RectTransform), typeof(Image), typeof(StorageChestSlotUI));
        RectTransform rect = slotObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = slotSize;

        int columns = kind == StorageChestSlotKind.PlayerInventory ? 4 : 3;
        int row = slotIndex / columns;
        int column = slotIndex % columns;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(column * (slotSize.x + slotSpacing), -row * (slotSize.y + slotSpacing));

        Image background = slotObject.GetComponent<Image>();
        background.color = slotColor;

        GameObject iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        RectTransform iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.SetParent(rect, false);
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = new Vector2(8f, 8f);
        iconRect.offsetMax = new Vector2(-8f, -8f);
        Image icon = iconObject.GetComponent<Image>();
        icon.preserveAspect = true;
        icon.enabled = false;
        icon.raycastTarget = false;

        TextMeshProUGUI countText = CreateLabel("Count", rect, 16f, FontStyles.Bold, Color.white, TextAlignmentOptions.BottomRight);
        countText.rectTransform.anchorMin = Vector2.zero;
        countText.rectTransform.anchorMax = Vector2.one;
        countText.rectTransform.offsetMin = new Vector2(4f, 2f);
        countText.rectTransform.offsetMax = new Vector2(-4f, -2f);
        countText.gameObject.SetActive(false);

        StorageChestSlotUI slot = slotObject.GetComponent<StorageChestSlotUI>();
        slot.Initialize(this, kind, slotIndex, icon, countText, background);
        return slot;
    }

    private Button CreateButton(string name, RectTransform parent, Vector2 anchoredPosition, string label, UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchoredPosition = anchoredPosition;
        buttonObject.GetComponent<Image>().color = new Color(0.18f, 0.25f, 0.18f, 0.95f);
        Button button = buttonObject.GetComponent<Button>();
        button.onClick.AddListener(action);

        TextMeshProUGUI labelText = CreateLabel("Label", rect, 18f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        labelText.rectTransform.anchorMin = Vector2.zero;
        labelText.rectTransform.anchorMax = Vector2.one;
        labelText.rectTransform.offsetMin = new Vector2(4f, 2f);
        labelText.rectTransform.offsetMax = new Vector2(-4f, -2f);
        labelText.text = label;
        return button;
    }

    private TextMeshProUGUI CreateLabel(string objectName, RectTransform parent, float fontSize, FontStyles fontStyle, Color color, TextAlignmentOptions alignment)
    {
        GameObject labelObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rect = labelObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.fontSize = fontSize;
        label.fontStyle = fontStyle;
        label.color = color;
        label.alignment = alignment;
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        return label;
    }

    private void TrackGeneratedPanelChild(GameObject child)
    {
        if (!createdPanelRoot && child != null)
        {
            generatedPanelChildren.Add(child);
        }
    }

    private void RebuildChestSlots()
    {
        if (chestSlotsRoot == null)
        {
            return;
        }

        for (int i = chestSlotsRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(chestSlotsRoot.GetChild(i).gameObject);
        }

        chestSlots.Clear();
        int count = GetActiveSlotCount();
        for (int i = 0; i < count; i++)
        {
            chestSlots.Add(CreateSlot(chestSlotsRoot, StorageChestSlotKind.Chest, i));
        }
    }

    private void Refresh()
    {
        if (!initialized || titleText == null)
        {
            return;
        }

        int slotCount = GetActiveSlotCount();
        titleText.text = string.Format("{0} ({1}/{2})", GetActiveChestName(), GetActiveUsedSlotCount(), slotCount);

        for (int i = 0; i < playerSlots.Count; i++)
        {
            ItemType? itemType = playerInventory != null ? playerInventory.GetSlotItemType(i) : null;
            int amount = playerInventory != null ? playerInventory.GetSlotAmount(i) : 0;
            Sprite sprite = itemType != null && iconDatabase != null ? iconDatabase.GetIcon(itemType.Value) : null;
            playerSlots[i].SetItem(sprite, amount);
            playerSlots[i].SetHighlight(false, slotColor, slotHighlightColor);
        }

        for (int i = 0; i < chestSlots.Count; i++)
        {
            ItemType? itemType = GetActiveSlotItemType(i);
            int amount = GetActiveSlotAmount(i);
            Sprite sprite = itemType != null && iconDatabase != null ? iconDatabase.GetIcon(itemType.Value) : null;
            chestSlots[i].SetItem(sprite, amount);
            chestSlots[i].SetHighlight(false, slotColor, slotHighlightColor);
        }
    }

    private void BindPlayerInventory()
    {
        ResolveReferences();
        if (subscribedInventory == playerInventory)
        {
            return;
        }

        UnbindPlayerInventory();
        if (playerInventory != null)
        {
            subscribedInventory = playerInventory;
            subscribedInventory.InventoryChanged += Refresh;
        }
    }

    private void UnbindPlayerInventory()
    {
        if (subscribedInventory != null)
        {
            subscribedInventory.InventoryChanged -= Refresh;
            subscribedInventory = null;
        }
    }

    private void CleanupRuntimeUI()
    {
        DestroyDragIcon();
        if (splitDialogRoot != null)
        {
            Destroy(splitDialogRoot.gameObject);
            splitDialogRoot = null;
        }

        UnbindChest();
        UnbindPlayerInventory();
        UnbindHotbarDrag();

        dragSourceSlot = null;
        hotbarDragSourceGlobalSlot = -1;
        hotbarDragItemType = null;
        dragDropHandled = false;
        pointerDownSlot = null;
        splitDragMode = false;
        ClearHotbarSplitState();
        pendingSplitTransfer = default;
        playerSlots.Clear();
        chestSlots.Clear();

        if (panelRoot != null)
        {
            if (createdPanelRoot)
            {
                Destroy(panelRoot.gameObject);
                panelRoot = null;
            }
            else
            {
                for (int i = generatedPanelChildren.Count - 1; i >= 0; i--)
                {
                    if (generatedPanelChildren[i] != null)
                    {
                        Destroy(generatedPanelChildren[i]);
                    }
                }

                panelRoot.gameObject.SetActive(false);
            }
        }

        generatedPanelChildren.Clear();
        playerSlotsRoot = null;
        chestSlotsRoot = null;
        titleText = null;
        closeButton = null;
        initialized = false;
        createdPanelRoot = false;
    }

    private void DepositPlayerSlotToChest(int playerSlot, int chestSlot)
    {
        if (playerInventory == null)
        {
            return;
        }

        if (activeFusionChest != null)
        {
            Fusion.NetworkObject playerObject = playerInventory.GetComponent<Fusion.NetworkObject>();
            activeFusionChest.RequestDepositToChest(playerObject, playerSlot, chestSlot);
            return;
        }

        activeChest?.TryRequestStore(playerInventory, playerSlot, chestSlot);
    }

    private void DepositPlayerSlotToChest(int playerSlot, int chestSlot, int amount)
    {
        if (playerInventory == null || amount <= 0)
        {
            return;
        }

        if (activeFusionChest != null)
        {
            Fusion.NetworkObject playerObject = playerInventory.GetComponent<Fusion.NetworkObject>();
            activeFusionChest.RequestDepositToChest(playerObject, playerSlot, chestSlot, amount);
            return;
        }

        activeChest?.TryRequestStore(playerInventory, playerSlot, chestSlot, amount);
    }

    private void OnHotbarDragStart(int hotbarSlotIndex, ItemType itemType)
    {
        if (!IsChestVisible() || hotbarUI == null || playerInventory == null)
        {
            return;
        }

        int globalSlotIndex = hotbarUI.GetHotbarGlobalSlotIndex(hotbarSlotIndex);
        if (globalSlotIndex < 0 || playerInventory.GetSlotAmount(globalSlotIndex) <= 0)
        {
            return;
        }

        hotbarDragSourceGlobalSlot = globalSlotIndex;
        hotbarDragItemType = itemType;
    }

    private void OnHotbarDragEnd(ItemType itemType, int sourceHotbarSlot)
    {
        if (hotbarDragItemType == null || hotbarDragSourceGlobalSlot < 0 || !IsChestVisible())
        {
            ClearHotbarDragState();
            return;
        }

        StorageChestSlotUI targetSlot = FindSlotUnderPointer(Input.mousePosition);
        if (targetSlot != null && targetSlot.Kind == StorageChestSlotKind.Chest)
        {
            bool shouldSplit = hotbarSplitArmed && hotbarSplitSlot == sourceHotbarSlot;
            if (shouldSplit)
            {
                TryOpenSplitDialog(SplitTransferDirection.PlayerToChest, hotbarDragSourceGlobalSlot, targetSlot.SlotIndex);
                ClearHotbarDragState();
                return;
            }

            DepositPlayerSlotToChest(hotbarDragSourceGlobalSlot, targetSlot.SlotIndex);
            Refresh();
        }
        else if (targetSlot != null && targetSlot.Kind == StorageChestSlotKind.PlayerInventory)
        {
            MoveHotbarSlotToPlayerSlot(sourceHotbarSlot, targetSlot.SlotIndex);
            Refresh();
        }

        ClearHotbarDragState();
    }

    private void ClearHotbarDragState()
    {
        hotbarDragSourceGlobalSlot = -1;
        hotbarDragItemType = null;
        ClearHotbarSplitState();
    }

    private bool OnHotbarLongPressForSplit(int hotbarSlotIndex, ItemType itemType)
    {
        if (!IsChestVisible() || hotbarUI == null || playerInventory == null)
        {
            return false;
        }

        int globalSlotIndex = hotbarUI.GetHotbarGlobalSlotIndex(hotbarSlotIndex);
        if (globalSlotIndex < 0 || playerInventory.GetSlotAmount(globalSlotIndex) <= 1)
        {
            return false;
        }

        hotbarSplitArmed = true;
        hotbarSplitSlot = hotbarSlotIndex;
        return true;
    }

    private void OnHotbarPointerUpForSplit(int hotbarSlotIndex)
    {
        if (hotbarSplitArmed && hotbarSplitSlot == hotbarSlotIndex && hotbarDragSourceGlobalSlot < 0)
        {
            ClearHotbarSplitState();
        }
    }

    private void TakeChestSlotToPlayer(int chestSlot, int playerSlot)
    {
        if (playerInventory == null)
        {
            return;
        }

        if (activeFusionChest != null)
        {
            Fusion.NetworkObject playerObject = playerInventory.GetComponent<Fusion.NetworkObject>();
            activeFusionChest.RequestTakeFromChest(playerObject, chestSlot, playerSlot);
            return;
        }

        activeChest?.TryRequestTake(playerInventory, chestSlot, playerSlot);
    }

    private void TakeChestSlotToPlayer(int chestSlot, int playerSlot, int amount)
    {
        if (playerInventory == null || amount <= 0)
        {
            return;
        }

        if (activeFusionChest != null)
        {
            Fusion.NetworkObject playerObject = playerInventory.GetComponent<Fusion.NetworkObject>();
            activeFusionChest.RequestTakeFromChest(playerObject, chestSlot, playerSlot, amount);
            return;
        }

        activeChest?.TryRequestTake(playerInventory, chestSlot, playerSlot, amount);
    }

    private void TakeChestSlotToHotbar(int chestSlot, int hotbarSlot)
    {
        if (hotbarUI == null || playerInventory == null)
        {
            return;
        }

        int targetPlayerSlot = hotbarUI.GetHotbarGlobalSlotIndex(hotbarSlot);
        if (targetPlayerSlot < 0)
        {
            return;
        }

        TakeChestSlotToPlayer(chestSlot, targetPlayerSlot);
        hotbarUI.SelectSlot(hotbarSlot);
        hotbarUI.Refresh();
    }

    private void MovePlayerSlotToHotbar(int playerSlot, int hotbarSlot)
    {
        if (hotbarUI == null || playerInventory == null)
        {
            return;
        }

        hotbarUI.AssignInventorySlotToHotbar(playerSlot, hotbarSlot);
        Refresh();
    }

    private void MoveHotbarSlotToPlayerSlot(int hotbarSlot, int playerSlot)
    {
        if (hotbarUI == null || playerInventory == null)
        {
            return;
        }

        hotbarUI.MoveHotbarSlotToInventory(hotbarSlot, playerSlot);
        Refresh();
    }

    private StorageChestSlotUI FindSlotUnderPointer(PointerEventData eventData)
    {
        if (EventSystem.current == null || eventData == null)
        {
            return null;
        }

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);
        for (int i = 0; i < results.Count; i++)
        {
            if (results[i].gameObject == null)
            {
                continue;
            }

            StorageChestSlotUI slot = results[i].gameObject.GetComponentInParent<StorageChestSlotUI>();
            if (slot != null)
            {
                return slot;
            }
        }

        return null;
    }

    private int FindHotbarSlotUnderPointer(PointerEventData eventData)
    {
        if (EventSystem.current == null || eventData == null || hotbarUI == null)
        {
            return -1;
        }

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);
        for (int i = 0; i < results.Count; i++)
        {
            if (results[i].gameObject == null)
            {
                continue;
            }

            HotbarSlotUI hotbarSlot = results[i].gameObject.GetComponentInParent<HotbarSlotUI>();
            if (hotbarSlot != null)
            {
                return hotbarSlot.slotIndex;
            }

            Button button = results[i].gameObject.GetComponentInParent<Button>();
            int slotIndex = hotbarUI.GetSlotIndex(button);
            if (slotIndex >= 0)
            {
                return slotIndex;
            }
        }

        return -1;
    }

    private StorageChestSlotUI FindSlotUnderPointer(Vector2 pointerPosition)
    {
        if (EventSystem.current == null)
        {
            return null;
        }

        PointerEventData pointerData = new PointerEventData(EventSystem.current)
        {
            position = pointerPosition
        };
        return FindSlotUnderPointer(pointerData);
    }

    private void CreateDragIcon(Sprite sprite)
    {
        DestroyDragIcon();
        if (sprite == null || targetCanvas == null)
        {
            return;
        }

        dragIconObject = new GameObject("Chest Drag Icon", typeof(RectTransform), typeof(Image));
        dragIconObject.transform.SetParent(targetCanvas.transform, false);
        dragIconObject.transform.SetAsLastSibling();
        RectTransform rect = dragIconObject.GetComponent<RectTransform>();
        rect.sizeDelta = slotSize;
        dragIconImage = dragIconObject.GetComponent<Image>();
        dragIconImage.sprite = sprite;
        dragIconImage.preserveAspect = true;
        dragIconImage.raycastTarget = false;
    }

    private void DestroyDragIcon()
    {
        if (dragIconObject != null)
        {
            Destroy(dragIconObject);
            dragIconObject = null;
            dragIconImage = null;
        }
    }

    private bool SlotHasItem(StorageChestSlotUI slot)
    {
        if (slot.Kind == StorageChestSlotKind.PlayerInventory)
        {
            return playerInventory != null && playerInventory.GetSlotAmount(slot.SlotIndex) > 0;
        }

        return GetActiveSlotAmount(slot.SlotIndex) > 0;
    }

    private int GetSlotAmount(StorageChestSlotUI slot)
    {
        if (slot == null)
        {
            return 0;
        }

        return slot.Kind == StorageChestSlotKind.PlayerInventory
            ? playerInventory != null ? playerInventory.GetSlotAmount(slot.SlotIndex) : 0
            : GetActiveSlotAmount(slot.SlotIndex);
    }

    private int GetActiveMaxStackPerSlot()
    {
        if (activeFusionChest != null)
        {
            return activeFusionChest.MaxStackPerSlot;
        }

        return activeChest != null ? activeChest.MaxStackPerSlot : 1;
    }

    private int GetSplitMaxAmount(SplitTransferDirection direction, int playerSlot, int chestSlot)
    {
        if (playerInventory == null)
        {
            return 0;
        }

        if (direction == SplitTransferDirection.PlayerToChest)
        {
            if (activeChest != null)
            {
                return activeChest.GetStoreCapacity(playerInventory, playerSlot, chestSlot);
            }

            if (playerSlot < 0 || playerSlot >= playerInventory.TotalSlotCount || chestSlot < 0 || chestSlot >= GetActiveSlotCount())
            {
                return 0;
            }

            ItemType? sourceItemType = playerInventory.GetSlotItemType(playerSlot);
            int sourceAmount = playerInventory.GetSlotAmount(playerSlot);
            if (sourceItemType == null || sourceAmount <= 0)
            {
                return 0;
            }

            ItemType? chestItemType = GetActiveSlotItemType(chestSlot);
            int chestAmount = GetActiveSlotAmount(chestSlot);
            if (chestItemType != null && chestItemType.Value != sourceItemType.Value)
            {
                return 0;
            }

            return Mathf.Clamp(GetActiveMaxStackPerSlot() - chestAmount, 0, sourceAmount);
        }

        if (direction == SplitTransferDirection.ChestToPlayer)
        {
            if (activeChest != null)
            {
                return activeChest.GetTakeCapacity(playerInventory, chestSlot, playerSlot);
            }

            if (chestSlot < 0 || chestSlot >= GetActiveSlotCount())
            {
                return 0;
            }

            ItemType? chestItemType = GetActiveSlotItemType(chestSlot);
            int chestAmount = GetActiveSlotAmount(chestSlot);
            if (chestItemType == null || chestAmount <= 0)
            {
                return 0;
            }

            if (playerSlot < 0 || playerSlot >= playerInventory.TotalSlotCount)
            {
                return 0;
            }

            ItemType? targetItemType = playerInventory.GetSlotItemType(playerSlot);
            int targetAmount = playerInventory.GetSlotAmount(playerSlot);
            if (targetItemType != null && targetItemType.Value != chestItemType.Value)
            {
                return 0;
            }

            return Mathf.Clamp(playerInventory.MaxStackPerSlot - targetAmount, 0, chestAmount);
        }

        return 0;
    }

    private int GetSplitSourceAmount(SplitTransferDirection direction, int playerSlot, int chestSlot)
    {
        if (direction == SplitTransferDirection.PlayerToChest)
        {
            return playerInventory != null ? playerInventory.GetSlotAmount(playerSlot) : 0;
        }

        return direction == SplitTransferDirection.ChestToPlayer ? GetActiveSlotAmount(chestSlot) : 0;
    }

    private ItemType? GetSplitSourceItemType(SplitTransferDirection direction, int playerSlot, int chestSlot)
    {
        if (direction == SplitTransferDirection.PlayerToChest)
        {
            return playerInventory != null ? playerInventory.GetSlotItemType(playerSlot) : null;
        }

        return direction == SplitTransferDirection.ChestToPlayer ? GetActiveSlotItemType(chestSlot) : null;
    }

    private Sprite GetSlotSprite(StorageChestSlotUI slot)
    {
        ItemType? itemType = slot.Kind == StorageChestSlotKind.PlayerInventory
            ? playerInventory?.GetSlotItemType(slot.SlotIndex)
            : GetActiveSlotItemType(slot.SlotIndex);
        return itemType != null && iconDatabase != null ? iconDatabase.GetIcon(itemType.Value) : null;
    }

    private bool TryOpenSplitDialog(SplitTransferDirection direction, int playerSlot, int chestSlot)
    {
        int maxAmount = GetSplitMaxAmount(direction, playerSlot, chestSlot);
        if (maxAmount < 1)
        {
            return false;
        }

        ItemType? sourceItemType = GetSplitSourceItemType(direction, playerSlot, chestSlot);
        if (sourceItemType == null)
        {
            return false;
        }

        int sourceAmount = GetSplitSourceAmount(direction, playerSlot, chestSlot);
        if (sourceAmount <= 0)
        {
            return false;
        }

        pendingSplitTransfer = new SplitTransfer
        {
            Direction = direction,
            PlayerSlot = playerSlot,
            ChestSlot = chestSlot,
            MaxAmount = maxAmount,
            SourceAmount = sourceAmount,
            SourceItemType = sourceItemType.Value
        };

        EnsureSplitDialog();
        if (splitDialogRoot == null)
        {
            pendingSplitTransfer = default;
            return false;
        }

        int defaultAmount = Mathf.Clamp(Mathf.CeilToInt(sourceAmount * 0.5f), 1, maxAmount);
        splitDialogTitle.text = direction == SplitTransferDirection.PlayerToChest ? "Move to Chest" : "Take from Chest";
        if (splitDialogInfoLabel != null)
        {
            splitDialogInfoLabel.text = string.Format("{0} - Max {1}", sourceItemType.Value, maxAmount);
        }

        SetSplitQuantity(defaultAmount);
        splitDialogRoot.gameObject.SetActive(true);
        splitDialogRoot.SetAsLastSibling();
        return true;
    }

    private void EnsureSplitDialog()
    {
        if (splitDialogRoot != null || targetCanvas == null)
        {
            return;
        }

        GameObject dialogObject = new GameObject("Split Quantity Dialog", typeof(RectTransform), typeof(Image));
        splitDialogRoot = dialogObject.GetComponent<RectTransform>();
        splitDialogRoot.SetParent(targetCanvas.transform, false);
        splitDialogRoot.anchorMin = new Vector2(0.5f, 0.5f);
        splitDialogRoot.anchorMax = new Vector2(0.5f, 0.5f);
        splitDialogRoot.pivot = new Vector2(0.5f, 0.5f);
        splitDialogRoot.sizeDelta = new Vector2(360f, 230f);
        splitDialogRoot.anchoredPosition = Vector2.zero;
        dialogObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.88f);

        splitDialogTitle = CreateLabel("Split Title", splitDialogRoot, 22f, FontStyles.Bold, new Color(1f, 0.85f, 0.4f, 1f), TextAlignmentOptions.Center);
        splitDialogTitle.rectTransform.anchorMin = new Vector2(0f, 1f);
        splitDialogTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
        splitDialogTitle.rectTransform.pivot = new Vector2(0.5f, 1f);
        splitDialogTitle.rectTransform.anchoredPosition = new Vector2(0f, -14f);
        splitDialogTitle.rectTransform.sizeDelta = new Vector2(-24f, 30f);

        splitDialogInfoLabel = CreateLabel("Split Info", splitDialogRoot, 16f, FontStyles.Normal, Color.white, TextAlignmentOptions.Center);
        splitDialogInfoLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
        splitDialogInfoLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
        splitDialogInfoLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
        splitDialogInfoLabel.rectTransform.anchoredPosition = new Vector2(0f, -42f);
        splitDialogInfoLabel.rectTransform.sizeDelta = new Vector2(-24f, 24f);

        GameObject inputObject = new GameObject("Quantity Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        RectTransform inputRect = inputObject.GetComponent<RectTransform>();
        inputRect.SetParent(splitDialogRoot, false);
        inputRect.anchorMin = new Vector2(0.5f, 1f);
        inputRect.anchorMax = new Vector2(0.5f, 1f);
        inputRect.pivot = new Vector2(0.5f, 1f);
        inputRect.anchoredPosition = new Vector2(0f, -72f);
        inputRect.sizeDelta = new Vector2(140f, 42f);
        inputObject.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.12f, 1f);
        splitQuantityInput = inputObject.GetComponent<TMP_InputField>();
        splitQuantityInput.contentType = TMP_InputField.ContentType.IntegerNumber;

        TextMeshProUGUI inputText = CreateLabel("Text", inputRect, 22f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        inputText.rectTransform.anchorMin = Vector2.zero;
        inputText.rectTransform.anchorMax = Vector2.one;
        inputText.rectTransform.offsetMin = new Vector2(8f, 2f);
        inputText.rectTransform.offsetMax = new Vector2(-8f, -2f);
        splitQuantityInput.textComponent = inputText;

        splitMinusButton = CreateButton("Minus", splitDialogRoot, new Vector2(-120f, -112f), "-", () => AdjustSplitQuantity(-1));
        splitPlusButton = CreateButton("Plus", splitDialogRoot, new Vector2(120f, -112f), "+", () => AdjustSplitQuantity(1));
        splitHalfButton = CreateButton("Half", splitDialogRoot, new Vector2(-56f, -112f), "Half", () => SetSplitQuantity(Mathf.CeilToInt(pendingSplitTransfer.MaxAmount * 0.5f)));
        splitMaxButton = CreateButton("Max", splitDialogRoot, new Vector2(56f, -112f), "Max", () => SetSplitQuantity(pendingSplitTransfer.MaxAmount));
        splitCancelButton = CreateButton("Cancel", splitDialogRoot, new Vector2(-78f, -170f), "Cancel", CancelSplitDialog);
        splitConfirmButton = CreateButton("Confirm", splitDialogRoot, new Vector2(78f, -170f), "Confirm", ConfirmSplitDialog);

        ResizeDialogButton(splitMinusButton, 48f, 36f);
        ResizeDialogButton(splitPlusButton, 48f, 36f);
        ResizeDialogButton(splitHalfButton, 86f, 36f);
        ResizeDialogButton(splitMaxButton, 86f, 36f);
        ResizeDialogButton(splitCancelButton, 120f, 38f);
        ResizeDialogButton(splitConfirmButton, 120f, 38f);
        splitDialogRoot.gameObject.SetActive(false);
    }

    private void ResizeDialogButton(Button button, float width, float height)
    {
        if (button == null)
        {
            return;
        }

        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(width, height);
    }

    private void AdjustSplitQuantity(int delta)
    {
        SetSplitQuantity(GetSplitQuantityInputValue() + delta);
    }

    private void SetSplitQuantity(int value)
    {
        if (splitQuantityInput == null)
        {
            return;
        }

        int clamped = Mathf.Clamp(value, 1, Mathf.Max(1, pendingSplitTransfer.MaxAmount));
        splitQuantityInput.SetTextWithoutNotify(clamped.ToString());
    }

    private int GetSplitQuantityInputValue()
    {
        if (splitQuantityInput == null || !int.TryParse(splitQuantityInput.text, out int value))
        {
            return 1;
        }

        return value;
    }

    private void CancelSplitDialog()
    {
        HideSplitDialog();
    }

    private void ConfirmSplitDialog()
    {
        ItemType? currentSourceItemType = GetSplitSourceItemType(pendingSplitTransfer.Direction, pendingSplitTransfer.PlayerSlot, pendingSplitTransfer.ChestSlot);
        int currentSourceAmount = GetSplitSourceAmount(pendingSplitTransfer.Direction, pendingSplitTransfer.PlayerSlot, pendingSplitTransfer.ChestSlot);
        if (currentSourceItemType == null
            || currentSourceItemType.Value != pendingSplitTransfer.SourceItemType
            || currentSourceAmount < 1
            || currentSourceAmount != pendingSplitTransfer.SourceAmount)
        {
            HideSplitDialog();
            Refresh();
            return;
        }

        int maxAmount = GetSplitMaxAmount(pendingSplitTransfer.Direction, pendingSplitTransfer.PlayerSlot, pendingSplitTransfer.ChestSlot);
        if (maxAmount < 1 || maxAmount != pendingSplitTransfer.MaxAmount)
        {
            HideSplitDialog();
            Refresh();
            return;
        }

        int amount = Mathf.Clamp(GetSplitQuantityInputValue(), 1, maxAmount);
        if (amount <= 0)
        {
            HideSplitDialog();
            return;
        }

        if (pendingSplitTransfer.Direction == SplitTransferDirection.PlayerToChest)
        {
            DepositPlayerSlotToChest(pendingSplitTransfer.PlayerSlot, pendingSplitTransfer.ChestSlot, amount);
        }
        else if (pendingSplitTransfer.Direction == SplitTransferDirection.ChestToPlayer)
        {
            TakeChestSlotToPlayer(pendingSplitTransfer.ChestSlot, pendingSplitTransfer.PlayerSlot, amount);
        }

        HideSplitDialog();
        Refresh();
    }

    private void HideSplitDialog()
    {
        if (splitDialogRoot != null)
        {
            splitDialogRoot.gameObject.SetActive(false);
        }

        pendingSplitTransfer = default;
    }

    private void SetVisible(bool visible)
    {
        if (panelRoot != null)
        {
            panelRoot.gameObject.SetActive(visible);
        }
    }

    private bool IsChestVisible()
    {
        return panelRoot != null && panelRoot.gameObject.activeSelf && (activeChest != null || activeFusionChest != null);
    }

    private void BindHotbarDrag()
    {
        ResolveReferences();
        if (subscribedHotbar == hotbarUI)
        {
            return;
        }

        UnbindHotbarDrag();
        if (hotbarUI != null)
        {
            subscribedHotbar = hotbarUI;
            subscribedHotbar.OnSlotDragStart -= OnHotbarDragStart;
            subscribedHotbar.OnSlotDragEnd -= OnHotbarDragEnd;
            subscribedHotbar.OnSlotDragStart += OnHotbarDragStart;
            subscribedHotbar.OnSlotDragEnd += OnHotbarDragEnd;
            subscribedHotbar.OnSlotLongPressForSplit = OnHotbarLongPressForSplit;
            subscribedHotbar.OnSlotPointerUpForSplit = OnHotbarPointerUpForSplit;
        }
    }

    private void UnbindHotbarDrag()
    {
        if (subscribedHotbar != null)
        {
            subscribedHotbar.OnSlotDragStart -= OnHotbarDragStart;
            subscribedHotbar.OnSlotDragEnd -= OnHotbarDragEnd;
            if (subscribedHotbar.OnSlotLongPressForSplit == OnHotbarLongPressForSplit)
            {
                subscribedHotbar.OnSlotLongPressForSplit = null;
            }

            if (subscribedHotbar.OnSlotPointerUpForSplit == OnHotbarPointerUpForSplit)
            {
                subscribedHotbar.OnSlotPointerUpForSplit = null;
            }

            subscribedHotbar = null;
        }
    }

    private bool ShouldKeepChestOpen()
    {
        if ((activeChest == null && activeFusionChest == null) || panelRoot == null || !panelRoot.gameObject.activeSelf)
        {
            return true;
        }

        if (playerInventory == null)
        {
            playerInventory = GetComponent<PlayerInventory>();
        }

        if (playerInventory == null)
        {
            return false;
        }

        Transform chestTransform = activeFusionChest != null ? activeFusionChest.transform : activeChest.transform;
        float interactDistance = activeFusionChest != null ? activeFusionChest.InteractDistance : activeChest.InteractDistance;
        float maxDistance = interactDistance + Mathf.Max(0.05f, autoCloseDistancePadding);
        return Vector3.Distance(playerInventory.transform.position, chestTransform.position) <= maxDistance;
    }

    private void UnbindChest()
    {
        if (activeChest != null)
        {
            activeChest.ChestChanged -= Refresh;
            activeChest = null;
        }

        if (activeFusionChest != null)
        {
            activeFusionChest.ChestChanged -= Refresh;
            activeFusionChest = null;
        }
    }

    private string GetActiveChestName()
    {
        if (activeFusionChest != null)
        {
            return activeFusionChest.ChestName;
        }

        return activeChest != null ? activeChest.ChestName : "Storage Chest";
    }

    private int GetActiveSlotCount()
    {
        if (activeFusionChest != null)
        {
            return activeFusionChest.Slots;
        }

        return activeChest != null ? activeChest.SlotCount : 0;
    }

    private int GetActiveUsedSlotCount()
    {
        if (activeFusionChest != null)
        {
            return activeFusionChest.UsedSlotCount;
        }

        return activeChest != null ? activeChest.UsedSlotCount : 0;
    }

    private ItemType? GetActiveSlotItemType(int slotIndex)
    {
        if (activeFusionChest != null)
        {
            return activeFusionChest.TryReadSlot(slotIndex, out ItemType itemType, out _) ? itemType : (ItemType?)null;
        }

        return activeChest != null ? activeChest.GetSlotItemType(slotIndex) : null;
    }

    private int GetActiveSlotAmount(int slotIndex)
    {
        if (activeFusionChest != null)
        {
            return activeFusionChest.TryReadSlot(slotIndex, out _, out int amount) ? amount : 0;
        }

        return activeChest != null ? activeChest.GetSlotAmount(slotIndex) : 0;
    }
}
