# Minecraft-Style Chest Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a side-by-side drag-and-drop chest UI that moves full stacks between player inventory and chest slots, while keeping existing legacy and Photon Fusion storage authority flows.

**Architecture:** Replace the current text/button `StorageChestUI` interaction layer with runtime slot grids and a small `StorageChestSlotUI` helper. Keep `StorageChest`, `FusionStorageChest`, and `PlayerInventory` as the transaction owners; UI sends slot transfer requests and refreshes from existing state/events.

**Tech Stack:** Unity C#, uGUI, TextMeshPro, Unity EventSystem drag interfaces, Photon Fusion `NetworkBehaviour`, Unity MCP verification.

---

## File Structure

- Create: `Assets/Scripts/Object/Storage/StorageChestSlotUI.cs`
  - Runtime slot view component for player/chest slot metadata and drag callbacks.
- Modify: `Assets/Scripts/Object/Storage/StorageChestUI.cs`
  - Replace text-list panel with side-by-side grid panel, drag state, slot creation, refresh, and transfer delegation.
- Modify only if diagnostics show it is necessary: `Assets/Scripts/Object/Storage/StorageChest.cs`
  - Keep existing methods unless full-stack behavior needs amount-explicit overloads.
- Modify only if diagnostics show it is necessary: `Assets/Scripts/PhotonFusion/FusionStorageChest.cs`
  - Keep existing RPC methods unless full-stack behavior needs amount-explicit overloads.
- Verify: `Assets/Scenes/Gameplay.unity`
  - Scene should validate without broken prefabs or missing scripts after script changes.

## Task 1: RED Diagnostics For Existing Chest UI Gap

**Files:**
- Inspect: `Assets/Scripts/Object/Storage/StorageChestUI.cs`
- Inspect: `Assets/Scripts/PhotonFusion/FusionStorageChest.cs`
- Inspect: `Assets/Scripts/Object/Storage/StorageChest.cs`

- [ ] **Step 1: Run Unity MCP diagnostic that must fail before implementation**

Run this with `unityMCP_execute_code`:

```csharp
var errors = new System.Collections.Generic.List<string>();
var slotType = System.Type.GetType("StorageChestSlotUI");
if (slotType == null)
{
    errors.Add("StorageChestSlotUI type is missing");
}

var uiType = typeof(StorageChestUI);
if (uiType.GetMethod("HandleSlotDrop", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic) == null)
{
    errors.Add("StorageChestUI.HandleSlotDrop is missing");
}

if (uiType.GetMethod("CreateSlot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic) == null)
{
    errors.Add("StorageChestUI.CreateSlot is missing");
}

return errors.Count == 0
    ? "PASS: drag-grid chest UI exists"
    : "FAIL: " + string.Join("; ", errors.ToArray());
```

Expected: `FAIL` with missing `StorageChestSlotUI`, `HandleSlotDrop`, and `CreateSlot`.

- [ ] **Step 2: Record RED result in work notes**

Expected note:

```text
RED confirmed: current chest UI has no slot helper or drag/drop transfer handler.
```

## Task 2: Add Slot View Helper

**Files:**
- Create: `Assets/Scripts/Object/Storage/StorageChestSlotUI.cs`

- [ ] **Step 1: Create `StorageChestSlotUI.cs`**

Use `apply_patch` to add:

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum StorageChestSlotKind
{
    PlayerInventory,
    Chest
}

public class StorageChestSlotUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text countText;
    [SerializeField] private Image background;

    private StorageChestUI owner;

    public StorageChestSlotKind Kind { get; private set; }
    public int SlotIndex { get; private set; }

    public void Initialize(StorageChestUI ownerUI, StorageChestSlotKind kind, int slotIndex, Image iconImage, TMP_Text amountText, Image backgroundImage)
    {
        owner = ownerUI;
        Kind = kind;
        SlotIndex = slotIndex;
        icon = iconImage;
        countText = amountText;
        background = backgroundImage;
    }

    public void SetItem(Sprite sprite, int amount)
    {
        bool hasItem = sprite != null && amount > 0;
        if (icon != null)
        {
            icon.sprite = hasItem ? sprite : null;
            icon.enabled = hasItem;
            icon.raycastTarget = false;
        }

        if (countText != null)
        {
            countText.text = hasItem ? amount.ToString() : string.Empty;
            countText.gameObject.SetActive(hasItem);
            countText.raycastTarget = false;
        }
    }

    public void SetHighlight(bool highlighted, Color normalColor, Color highlightColor)
    {
        if (background != null)
        {
            background.color = highlighted ? highlightColor : normalColor;
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        owner?.BeginSlotDrag(this, eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        owner?.UpdateSlotDrag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        owner?.EndSlotDrag(this, eventData);
    }

    public void OnDrop(PointerEventData eventData)
    {
        owner?.HandleSlotDrop(this);
    }
}
```

- [ ] **Step 2: Validate helper script**

Run: `unityMCP_validate_script` on `Assets/Scripts/Object/Storage/StorageChestSlotUI.cs` with level `standard`.

Expected: `errors=0`, `warnings=0`.

## Task 3: Replace Chest UI With Side-By-Side Runtime Grids

**Files:**
- Modify: `Assets/Scripts/Object/Storage/StorageChestUI.cs`

- [ ] **Step 1: Replace using section**

Ensure the top of `StorageChestUI.cs` contains:

```csharp
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
```

- [ ] **Step 2: Replace serialized UI fields and runtime state fields**

Inside `StorageChestUI`, replace the old text/button fields with this focused field set:

```csharp
[Header("References")]
[SerializeField] private PlayerInventory playerInventory;
[SerializeField] private Canvas targetCanvas;
[SerializeField] private RectTransform panelRoot;
[SerializeField] private RectTransform playerSlotsRoot;
[SerializeField] private RectTransform chestSlotsRoot;
[SerializeField] private TextMeshProUGUI titleText;
[SerializeField] private Button closeButton;
[SerializeField] private ItemIconDatabase iconDatabase;

[Header("Behavior")]
[SerializeField] private float autoCloseDistancePadding = 0.35f;

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
private StorageChest activeChest;
private FusionStorageChest activeFusionChest;
private StorageChestSlotUI dragSourceSlot;
private GameObject dragIconObject;
private Image dragIconImage;
private bool initialized;
```

- [ ] **Step 3: Update `Awake` reference resolution**

Use this body:

```csharp
private void Awake()
{
    ResolveReferences();
}
```

Add this helper:

```csharp
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
}
```

- [ ] **Step 4: Replace `EnsureUI` with side-by-side panel creation**

Implement `EnsureUI` and its creation helpers:

```csharp
private void EnsureUI()
{
    if (initialized)
    {
        return;
    }

    ResolveReferences();
    if (targetCanvas == null)
    {
        targetCanvas = FindObjectOfType<Canvas>(true);
    }

    if (targetCanvas == null)
    {
        return;
    }

    if (panelRoot == null)
    {
        panelRoot = CreatePanel(targetCanvas.transform as RectTransform);
    }

    titleText = CreateLabel("Title", panelRoot, 24f, FontStyles.Bold, new Color(1f, 0.85f, 0.4f, 1f), TextAlignmentOptions.Center);
    titleText.rectTransform.anchorMin = new Vector2(0f, 1f);
    titleText.rectTransform.anchorMax = new Vector2(1f, 1f);
    titleText.rectTransform.pivot = new Vector2(0.5f, 1f);
    titleText.rectTransform.anchoredPosition = new Vector2(0f, -12f);
    titleText.rectTransform.sizeDelta = new Vector2(-80f, 34f);

    closeButton = CreateButton("Close Chest Button", panelRoot, new Vector2(-28f, -14f), "X", CloseChest);
    RectTransform closeRect = closeButton.GetComponent<RectTransform>();
    closeRect.anchorMin = new Vector2(1f, 1f);
    closeRect.anchorMax = new Vector2(1f, 1f);
    closeRect.pivot = new Vector2(1f, 1f);
    closeRect.sizeDelta = new Vector2(44f, 34f);

    playerSlotsRoot = CreateSection("Inventory Slots", panelRoot, new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(20f, 24f), new Vector2(-12f, -60f));
    chestSlotsRoot = CreateSection("Chest Slots", panelRoot, new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(12f, 24f), new Vector2(-20f, -60f));

    BuildSlotGrids();
    initialized = true;
}
```

Add helpers:

```csharp
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
```

- [ ] **Step 5: Implement slot grid creation**

Add:

```csharp
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
    GameObject slotObject = new GameObject(kind + " Slot " + slotIndex, typeof(RectTransform), typeof(Image), typeof(StorageChestSlotUI));
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
```

- [ ] **Step 6: Keep button/label helpers**

Replace old `CreateButton` and `CreateLabel` with:

```csharp
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
```

## Task 4: Implement Refresh And Drag Transfer Logic

**Files:**
- Modify: `Assets/Scripts/Object/Storage/StorageChestUI.cs`

- [ ] **Step 1: Replace `OpenChest` methods to rebuild grids for active chest**

Use:

```csharp
public void OpenChest(StorageChest chest)
{
    if (chest == null)
    {
        return;
    }

    EnsureUI();
    if (activeChest != chest)
    {
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
        UnbindChest();
        activeFusionChest = chest;
        activeFusionChest.ChestChanged += Refresh;
        RebuildChestSlots();
    }

    SetVisible(true);
    Refresh();
}
```

Add:

```csharp
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
```

- [ ] **Step 2: Replace `Refresh` to update grids**

Use:

```csharp
private void Refresh()
{
    if (!initialized || titleText == null)
    {
        return;
    }

    int slotCount = GetActiveSlotCount();
    titleText.text = GetActiveChestName() + " (" + GetActiveUsedSlotCount() + "/" + slotCount + ")";

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
```

- [ ] **Step 3: Add drag callbacks consumed by `StorageChestSlotUI`**

Add:

```csharp
public void BeginSlotDrag(StorageChestSlotUI slot, PointerEventData eventData)
{
    if (slot == null || !SlotHasItem(slot))
    {
        return;
    }

    dragSourceSlot = slot;
    slot.SetHighlight(true, slotColor, slotHighlightColor);
    CreateDragIcon(GetSlotSprite(slot));
    UpdateSlotDrag(eventData);
}

public void UpdateSlotDrag(PointerEventData eventData)
{
    if (dragIconObject != null)
    {
        dragIconObject.transform.position = eventData.position;
    }
}

public void EndSlotDrag(StorageChestSlotUI slot, PointerEventData eventData)
{
    StorageChestSlotUI targetSlot = FindSlotUnderPointer(eventData);
    if (targetSlot != null)
    {
        HandleSlotDrop(targetSlot);
    }

    DestroyDragIcon();
    if (dragSourceSlot != null)
    {
        dragSourceSlot.SetHighlight(false, slotColor, slotHighlightColor);
    }

    dragSourceSlot = null;
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
        DepositPlayerSlotToChest(dragSourceSlot.SlotIndex, targetSlot.SlotIndex);
        return;
    }

    if (dragSourceSlot.Kind == StorageChestSlotKind.Chest && targetSlot.Kind == StorageChestSlotKind.PlayerInventory)
    {
        TakeChestSlotToPlayer(dragSourceSlot.SlotIndex, targetSlot.SlotIndex);
    }
}
```

- [ ] **Step 4: Add transaction helpers**

Add:

```csharp
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
```

- [ ] **Step 5: Add drag icon and pointer raycast helpers**

Add:

```csharp
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

private Sprite GetSlotSprite(StorageChestSlotUI slot)
{
    ItemType? itemType = slot.Kind == StorageChestSlotKind.PlayerInventory
        ? playerInventory?.GetSlotItemType(slot.SlotIndex)
        : GetActiveSlotItemType(slot.SlotIndex);
    return itemType != null && iconDatabase != null ? iconDatabase.GetIcon(itemType.Value) : null;
}
```

- [ ] **Step 6: Keep active chest accessor helpers**

Ensure these helpers exist and remain compatible with Fusion and legacy chest:

```csharp
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
```

## Task 5: Validate Local Transfer Behavior With Unity MCP

**Files:**
- Validate: `Assets/Scripts/Object/Storage/StorageChestUI.cs`
- Validate: `Assets/Scripts/Object/Storage/StorageChestSlotUI.cs`
- Validate: `Assets/Scripts/Object/Storage/StorageChest.cs`

- [ ] **Step 1: Validate scripts**

Run `unityMCP_validate_script` for:

```text
Assets/Scripts/Object/Storage/StorageChestUI.cs
Assets/Scripts/Object/Storage/StorageChestSlotUI.cs
Assets/Scripts/Object/Storage/StorageChest.cs
Assets/Scripts/PhotonFusion/FusionStorageChest.cs
```

Expected for each: `errors=0`, `warnings=0`.

- [ ] **Step 2: Run local storage transaction diagnostic**

Run this with `unityMCP_execute_code`:

```csharp
var errors = new System.Collections.Generic.List<string>();
var player = new UnityEngine.GameObject("ChestLocalPlayerDiagnostic");
var chestObject = new UnityEngine.GameObject("ChestLocalDiagnostic");
try
{
    var inventory = player.AddComponent<PlayerInventory>();
    var chest = chestObject.AddComponent<StorageChest>();
    inventory.AddItemToSlot(ItemType.Wood, 5, 0);

    if (!chest.TryRequestStore(inventory, 0, 0))
    {
        errors.Add("deposit returned false");
    }

    if (inventory.GetSlotAmount(0) != 0 || chest.GetSlotItemType(0) != ItemType.Wood || chest.GetSlotAmount(0) != 5)
    {
        errors.Add("deposit state is incorrect");
    }

    if (!chest.TryRequestTake(inventory, 0, 1))
    {
        errors.Add("take returned false");
    }

    if (inventory.GetSlotItemType(1) != ItemType.Wood || inventory.GetSlotAmount(1) != 5 || chest.GetSlotAmount(0) != 0)
    {
        errors.Add("take state is incorrect");
    }
}
finally
{
    UnityEngine.Object.DestroyImmediate(player);
    UnityEngine.Object.DestroyImmediate(chestObject);
}

return errors.Count == 0 ? "PASS: local chest deposit/take full-stack behavior works" : "FAIL: " + string.Join("; ", errors.ToArray());
```

Expected: `PASS: local chest deposit/take full-stack behavior works`.

- [ ] **Step 3: Run UI metadata diagnostic**

Run this with `unityMCP_execute_code`:

```csharp
var errors = new System.Collections.Generic.List<string>();
if (System.Type.GetType("StorageChestSlotUI") == null)
{
    errors.Add("StorageChestSlotUI missing");
}

var uiType = typeof(StorageChestUI);
foreach (string method in new [] { "BeginSlotDrag", "UpdateSlotDrag", "EndSlotDrag", "HandleSlotDrop" })
{
    if (uiType.GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public) == null)
    {
        errors.Add(method + " missing");
    }
}

return errors.Count == 0 ? "PASS: chest UI drag metadata API exists" : "FAIL: " + string.Join("; ", errors.ToArray());
```

Expected: `PASS: chest UI drag metadata API exists`.

## Task 6: Scene And Console Verification

**Files:**
- Verify: `Assets/Scenes/Gameplay.unity`
- Verify: changed scripts

- [ ] **Step 1: Refresh Unity and wait for readiness**

Run `unityMCP_refresh_unity` with:

```text
mode=if_dirty
scope=scripts
compile=request
wait_for_ready=true
```

Expected: Unity ready for tools.

- [ ] **Step 2: Validate Gameplay scene**

Run `unityMCP_manage_scene` with:

```text
action=validate
scene_path=Assets/Scenes/Gameplay.unity
```

Expected:

```text
totalIssues=0
missingScripts=0
brokenPrefabs=0
```

- [ ] **Step 3: Clear and read console**

Run `unityMCP_read_console` with `action=clear`, then run `unityMCP_read_console` with:

```text
action=get
count=20
format=detailed
include_stacktrace=true
```

Expected: `Retrieved 0 log entries.`

- [ ] **Step 4: Run git diff check**

Run:

```bash
git diff --check -- "Assets/Scripts/Object/Storage/StorageChestUI.cs" "Assets/Scripts/Object/Storage/StorageChestSlotUI.cs" "Assets/Scripts/Object/Storage/StorageChest.cs" "Assets/Scripts/PhotonFusion/FusionStorageChest.cs"
```

Expected: no whitespace errors. CRLF warnings are acceptable in this repository.

## Task 7: Manual Play Test Checklist

**Files:**
- Runtime verification in Unity Play Mode.

- [ ] **Step 1: Test deposit**

Manual steps:

```text
1. Start/host gameplay.
2. Pick up Wood or Stone.
3. Open Storage Chest Prototype.
4. Drag item from left inventory grid to a right chest slot.
5. Confirm item disappears from inventory slot and appears in chest slot.
```

Expected: full stack moves into the chest.

- [ ] **Step 2: Test take**

Manual steps:

```text
1. With an item in chest, drag the chest slot to an empty inventory slot.
2. Confirm chest slot empties and inventory slot receives the stack.
```

Expected: full stack returns to inventory.

- [ ] **Step 3: Test invalid target**

Manual steps:

```text
1. Put Wood in one chest slot.
2. Try dropping Stone onto the Wood chest slot.
3. Confirm neither item disappears or duplicates.
```

Expected: invalid mixed-item stack is rejected safely.

- [ ] **Step 4: Test distance auto-close**

Manual steps:

```text
1. Open chest UI.
2. Move player away from chest beyond interact range.
3. Confirm chest panel closes.
```

Expected: panel closes automatically.

## Commit Guidance

Do not commit unrelated dirty files. Intended files for this feature:

```text
Assets/Scripts/Object/Storage/StorageChestUI.cs
Assets/Scripts/Object/Storage/StorageChestSlotUI.cs
docs/superpowers/specs/2026-06-12-minecraft-style-chest-design.md
docs/superpowers/plans/2026-06-12-minecraft-style-chest.md
```

Include `StorageChest.cs` or `FusionStorageChest.cs` only if implementation required amount-explicit overloads.

Suggested commit message after successful verification:

```text
Add drag and drop chest UI
```
