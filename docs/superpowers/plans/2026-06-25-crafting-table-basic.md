# Crafting Table Basic Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a mobile-first, multiplayer-safe Crafting Table loop where players craft a table item, place it from the hotbar, use placed tables to unlock table recipes, craft Campfire, and place Campfire.

**Architecture:** Extend the existing crafting and inventory model, then add focused station and placement components. Crafting stays in `BandageCraftingSystem` and `PlayerInventoryUI`; world placement is handled by new local preview and Fusion authority request code. Placed objects are simple networked prefabs with explicit components.

**Tech Stack:** Unity C#, Photon Fusion 2, Unity UI/Button/TMP, existing `PlayerInventory`, `MobileHotbarUI`, `PlayerInventoryUI`, and Unity MCP diagnostics.

---

## File Map

- Modify: `Assets/Scripts/Object/Item/PickableItem.cs`
  - Append `CraftingTable` and `Campfire` to `ItemType`.
- Modify: `Assets/Scripts/Player/Survival/BandageCraftingSystem.cs`
  - Add default recipes for Crafting Table, Axe, and Campfire without overwriting inspector recipes.
- Modify: `Assets/Scripts/Player/Survival/PlayerInventory.cs`
  - Prefer hotbar insertion for `Axe`, `CraftingTable`, and `Campfire`.
- Modify: `Assets/Scripts/Player/Survival/PlayerInventoryUI.cs`
  - Add current crafting context and `OpenCrafting(CraftingContext context)` API.
  - Fix Fusion UI authority to allow `HasInputAuthority`.
- Create: `Assets/Scripts/Player/Survival/CraftingTableStation.cs`
  - Marker/proximity component for placed tables.
- Create: `Assets/Scripts/Player/Survival/CraftingStationInteractor.cs`
  - Local player station detection and mobile `CRAFT` button binding.
- Create: `Assets/Scripts/Player/Survival/PlaceableItemSystem.cs`
  - Local place button, ghost preview, placement validation, and authority-safe request entrypoint.
- Create: `Assets/Scripts/PhotonFusion/FusionPlaceableObject.cs`
  - Optional metadata on placed objects: placed item type and placer.
- Modify: `Assets/Scripts/PhotonFusion/FusionPlayerInventory.cs`
  - Add placeable prefab bindings and RPC request validation/spawn.
- Create/update scene/prefab assets through Unity MCP:
  - Add simple network prefabs for placed Crafting Table and Campfire.
  - Attach new player components to `Assets/Assets/Prefabs/FusionPlayer.prefab`.
  - Add or bind mobile `CRAFT` and `PLACE` buttons in `Assets/Scenes/Gameplay.unity`.

## Context7 And Unity MCP Requirements

- Before implementing Fusion spawn/RPC code, query Context7 for current Photon Fusion RPC and `Runner.Spawn` patterns.
- Use Unity MCP for Unity asset/prefab/scene operations, script validation, console checks, and diagnostics.
- Do not claim complete without a fresh Unity script compile/validation pass.

---

### Task 1: Add Item Types And Recipe Defaults

**Files:**
- Modify: `Assets/Scripts/Object/Item/PickableItem.cs`
- Modify: `Assets/Scripts/Player/Survival/BandageCraftingSystem.cs`
- Modify: `Assets/Scripts/Player/Survival/PlayerInventory.cs`

- [ ] **Step 1: Append item types only**

Change `ItemType` to append new values at the end:

```csharp
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
    Campfire
}
```

- [ ] **Step 2: Add recipe helpers in `BandageCraftingSystem`**

Replace `EnsureDefaultRecipes()` with an implementation that adds missing default recipes by `recipeId`, without replacing inspector recipes:

```csharp
private void EnsureDefaultRecipes()
{
    if (recipes == null)
    {
        recipes = new List<CraftingRecipe>();
    }

    AddDefaultRecipeIfMissing(new CraftingRecipe
    {
        recipeId = "bandage",
        displayName = "Bandage",
        outputItemType = ItemType.Bandage,
        outputAmount = BandageOutput,
        context = CraftingContext.Simple,
        ingredients = new List<CraftingIngredient>
        {
            new CraftingIngredient { itemType = ItemType.Fiber, amount = FiberCost },
            new CraftingIngredient { itemType = ItemType.Cloth, amount = ClothCost }
        }
    });

    AddDefaultRecipeIfMissing(new CraftingRecipe
    {
        recipeId = "crafting_table",
        displayName = "Crafting Table",
        outputItemType = ItemType.CraftingTable,
        outputAmount = 1,
        context = CraftingContext.Simple,
        ingredients = new List<CraftingIngredient>
        {
            new CraftingIngredient { itemType = ItemType.Wood, amount = 8 },
            new CraftingIngredient { itemType = ItemType.Stone, amount = 2 },
            new CraftingIngredient { itemType = ItemType.Fiber, amount = 2 }
        }
    });

    AddDefaultRecipeIfMissing(new CraftingRecipe
    {
        recipeId = "axe",
        displayName = "Axe",
        outputItemType = ItemType.Axe,
        outputAmount = 1,
        context = CraftingContext.CraftingTable,
        ingredients = new List<CraftingIngredient>
        {
            new CraftingIngredient { itemType = ItemType.Wood, amount = 3 },
            new CraftingIngredient { itemType = ItemType.Stone, amount = 2 }
        }
    });

    AddDefaultRecipeIfMissing(new CraftingRecipe
    {
        recipeId = "campfire",
        displayName = "Campfire",
        outputItemType = ItemType.Campfire,
        outputAmount = 1,
        context = CraftingContext.CraftingTable,
        ingredients = new List<CraftingIngredient>
        {
            new CraftingIngredient { itemType = ItemType.Wood, amount = 4 },
            new CraftingIngredient { itemType = ItemType.Stone, amount = 4 }
        }
    });
}

private void AddDefaultRecipeIfMissing(CraftingRecipe defaultRecipe)
{
    if (defaultRecipe == null || string.IsNullOrWhiteSpace(defaultRecipe.recipeId))
    {
        return;
    }

    for (int i = 0; i < recipes.Count; i++)
    {
        CraftingRecipe recipe = recipes[i];
        if (recipe != null && recipe.recipeId == defaultRecipe.recipeId)
        {
            return;
        }
    }

    recipes.Add(defaultRecipe);
}
```

- [ ] **Step 3: Prefer hotbar for placeables**

In `PlayerInventory.AddItem`, replace the special-case condition:

```csharp
if (itemType == ItemType.Axe)
```

with:

```csharp
if (ShouldPreferHotbar(itemType))
```

Add this helper near other private helpers:

```csharp
private static bool ShouldPreferHotbar(ItemType itemType)
{
    return itemType == ItemType.Axe
        || itemType == ItemType.CraftingTable
        || itemType == ItemType.Campfire;
}
```

- [ ] **Step 4: Validate scripts**

Run Unity MCP script validation for:

```text
Assets/Scripts/Object/Item/PickableItem.cs
Assets/Scripts/Player/Survival/BandageCraftingSystem.cs
Assets/Scripts/Player/Survival/PlayerInventory.cs
```

Expected: no compile errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Object/Item/PickableItem.cs Assets/Scripts/Player/Survival/BandageCraftingSystem.cs Assets/Scripts/Player/Survival/PlayerInventory.cs
git commit -m "Add crafting table and campfire item recipes"
```

---

### Task 2: Add Crafting Context UI API

**Files:**
- Modify: `Assets/Scripts/Player/Survival/PlayerInventoryUI.cs`

- [ ] **Step 1: Add context state**

Add a field near `activeView`:

```csharp
private CraftingContext currentCraftingContext = CraftingContext.Simple;
```

- [ ] **Step 2: Add public API for station crafting**

Add methods after `SetVisible(bool visible)`:

```csharp
public void OpenCrafting(CraftingContext context)
{
    currentCraftingContext = context;
    activeView = InventoryView.Crafting;
    this.EnsureUI();
    this.SetVisible(true);
    this.Refresh();
}

public void SetCraftingContext(CraftingContext context)
{
    if (currentCraftingContext == context)
    {
        return;
    }

    currentCraftingContext = context;
    if (activeView == InventoryView.Crafting)
    {
        this.Refresh();
    }
}

public CraftingContext CurrentCraftingContext => currentCraftingContext;
```

- [ ] **Step 3: Make normal inventory default to simple context**

In `Toggle()`, when opening the panel, reset context to simple before refresh:

```csharp
bool visible = !panelRoot.gameObject.activeSelf;
panelRoot.gameObject.SetActive(visible);
if (visible)
{
    currentCraftingContext = CraftingContext.Simple;
    this.Refresh();
}
```

- [ ] **Step 4: Use current context in crafting refresh**

Find `RefreshCraftingView()` and replace the hard-coded call:

```csharp
craftingSystem.GetAvailableRecipes(CraftingContext.Simple)
```

with:

```csharp
craftingSystem.GetAvailableRecipes(currentCraftingContext)
```

If the method sets title/header text, set it to show context:

```csharp
titleText.text = currentCraftingContext == CraftingContext.CraftingTable ? "Crafting Table" : "Crafting";
```

- [ ] **Step 5: Fix Fusion UI authority**

In `HasLocalInventoryAuthority()`, replace:

```csharp
return fusionObject.HasStateAuthority;
```

with:

```csharp
return fusionObject.HasStateAuthority || fusionObject.HasInputAuthority;
```

- [ ] **Step 6: Validate script**

Run Unity MCP script validation for:

```text
Assets/Scripts/Player/Survival/PlayerInventoryUI.cs
```

Expected: no compile errors.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Player/Survival/PlayerInventoryUI.cs
git commit -m "Add crafting context inventory UI API"
```

---

### Task 3: Add Crafting Table Station Interaction

**Files:**
- Create: `Assets/Scripts/Player/Survival/CraftingTableStation.cs`
- Create: `Assets/Scripts/Player/Survival/CraftingStationInteractor.cs`

- [ ] **Step 1: Create station marker**

Create `Assets/Scripts/Player/Survival/CraftingTableStation.cs`:

```csharp
using UnityEngine;

[DisallowMultipleComponent]
public class CraftingTableStation : MonoBehaviour
{
    [SerializeField] private float interactionRadius = 3f;

    public float InteractionRadius => Mathf.Max(0.5f, interactionRadius);

    public bool IsInRange(Vector3 worldPosition)
    {
        return Vector3.Distance(transform.position, worldPosition) <= InteractionRadius;
    }

    private void OnValidate()
    {
        interactionRadius = Mathf.Max(0.5f, interactionRadius);
    }
}
```

- [ ] **Step 2: Create player interactor**

Create `Assets/Scripts/Player/Survival/CraftingStationInteractor.cs`:

```csharp
using Fusion;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class CraftingStationInteractor : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerInventoryUI inventoryUI;
    [SerializeField] private FusionPlayerSurvival survival;
    [SerializeField] private Button craftButton;

    [Header("Detection")]
    [SerializeField] private float scanRadius = 3f;
    [SerializeField] private float scanInterval = 0.2f;
    [SerializeField] private bool hideWhenUnavailable = true;

    private CraftingTableStation currentStation;
    private float nextScanTime;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        BindButton();
        RefreshButton();
    }

    private void OnDisable()
    {
        if (craftButton != null)
        {
            craftButton.onClick.RemoveListener(OpenCraftingTable);
        }
    }

    private void Update()
    {
        if (!HasLocalAuthority())
        {
            SetButtonVisible(false);
            return;
        }

        if (Time.time >= nextScanTime)
        {
            nextScanTime = Time.time + Mathf.Max(0.05f, scanInterval);
            currentStation = FindNearestStation();
            RefreshButton();
        }
    }

    public void OpenCraftingTable()
    {
        if (!CanUseCurrentStation())
        {
            return;
        }

        inventoryUI.OpenCrafting(CraftingContext.CraftingTable);
    }

    private void ResolveReferences()
    {
        if (inventoryUI == null) inventoryUI = GetComponent<PlayerInventoryUI>();
        if (survival == null) survival = GetComponent<FusionPlayerSurvival>();
        if (craftButton == null) craftButton = FindButtonByName("craft");
    }

    private void BindButton()
    {
        if (craftButton == null) return;
        craftButton.onClick.RemoveListener(OpenCraftingTable);
        craftButton.onClick.AddListener(OpenCraftingTable);
    }

    private CraftingTableStation FindNearestStation()
    {
        CraftingTableStation[] stations = FindObjectsOfType<CraftingTableStation>();
        CraftingTableStation nearest = null;
        float bestDistance = Mathf.Max(0.5f, scanRadius);

        for (int i = 0; i < stations.Length; i++)
        {
            CraftingTableStation station = stations[i];
            if (station == null) continue;

            float distance = Vector3.Distance(transform.position, station.transform.position);
            if (distance <= bestDistance && station.IsInRange(transform.position))
            {
                bestDistance = distance;
                nearest = station;
            }
        }

        return nearest;
    }

    private bool CanUseCurrentStation()
    {
        return inventoryUI != null
            && currentStation != null
            && !IsDowned()
            && HasLocalAuthority()
            && currentStation.IsInRange(transform.position);
    }

    private void RefreshButton()
    {
        if (craftButton == null) return;
        bool canUse = CanUseCurrentStation();
        craftButton.interactable = canUse;
        if (hideWhenUnavailable) SetButtonVisible(canUse);
    }

    private void SetButtonVisible(bool visible)
    {
        if (craftButton != null) craftButton.gameObject.SetActive(visible);
    }

    private bool IsDowned()
    {
        return survival != null && survival.IsDowned;
    }

    private bool HasLocalAuthority()
    {
        Fusion.NetworkObject fusionObject = GetComponent<Fusion.NetworkObject>();
        if (fusionObject != null && fusionObject.IsValid)
        {
            return fusionObject.HasStateAuthority || fusionObject.HasInputAuthority;
        }

        Unity.Netcode.NetworkObject netcodeObject = GetComponent<Unity.Netcode.NetworkObject>();
        if (netcodeObject != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            return netcodeObject.IsSpawned && netcodeObject.IsOwner;
        }

        return true;
    }

    private static Button FindButtonByName(string keyword)
    {
        Button[] buttons = FindObjectsOfType<Button>(true);
        string lowered = keyword.ToLowerInvariant();
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button != null && button.name.ToLowerInvariant().Contains(lowered))
            {
                return button;
            }
        }

        return null;
    }
}
```

- [ ] **Step 3: Validate scripts**

Run Unity MCP validation for both new scripts.

Expected: no compile errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Player/Survival/CraftingTableStation.cs Assets/Scripts/Player/Survival/CraftingStationInteractor.cs
git commit -m "Add crafting table station interaction"
```

---

### Task 4: Add Placeable Item System And Local Preview

**Files:**
- Create: `Assets/Scripts/Player/Survival/PlaceableItemSystem.cs`

- [ ] **Step 1: Create placement component**

Create `Assets/Scripts/Player/Survival/PlaceableItemSystem.cs`:

```csharp
using Fusion;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class PlaceableItemSystem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private MobileHotbarUI hotbarUI;
    [SerializeField] private FusionPlayerInventory fusionInventory;
    [SerializeField] private FusionPlayerSurvival survival;
    [SerializeField] private Button placeButton;

    [Header("Placement")]
    [SerializeField] private float placementDistance = 2.2f;
    [SerializeField] private float placementUpOffset = 0.05f;
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private LayerMask blockedMask = ~0;
    [SerializeField] private Vector3 defaultBounds = new Vector3(1.2f, 1.2f, 1.2f);
    [SerializeField] private Material validPreviewMaterial;
    [SerializeField] private Material invalidPreviewMaterial;
    [SerializeField] private bool hideWhenUnavailable = true;

    private GameObject previewObject;
    private Renderer[] previewRenderers;
    private bool placementMode;
    private bool currentPlacementValid;
    private Vector3 currentPlacementPosition;
    private Quaternion currentPlacementRotation;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeEvents();
        BindButton();
        RefreshButton();
    }

    private void OnDisable()
    {
        UnsubscribeEvents();
        if (placeButton != null) placeButton.onClick.RemoveListener(OnPlaceButtonPressed);
        DestroyPreview();
    }

    private void Update()
    {
        if (!HasLocalAuthority() || IsDowned())
        {
            CancelPlacement();
            RefreshButton();
            return;
        }

        if (placementMode)
        {
            UpdatePreview();
        }
    }

    public void OnPlaceButtonPressed()
    {
        if (!CanUseSelectedPlaceable())
        {
            ShowInfo("Pilih item placeable");
            return;
        }

        if (!placementMode)
        {
            StartPlacement();
            return;
        }

        ConfirmPlacement();
    }

    private void StartPlacement()
    {
        placementMode = true;
        EnsurePreview();
        UpdatePreview();
    }

    private void ConfirmPlacement()
    {
        if (!currentPlacementValid)
        {
            ShowInfo("Tidak bisa ditempatkan di sini");
            return;
        }

        int hotbarSlot = hotbarUI.SelectedSlotIndex;
        int globalSlot = hotbarUI.GetHotbarGlobalSlotIndex(hotbarSlot);
        ItemType? itemType = inventory.GetSlotItemType(globalSlot);
        if (itemType == null || !IsPlaceable(itemType.Value))
        {
            CancelPlacement();
            return;
        }

        bool requested = fusionInventory != null
            ? fusionInventory.RequestPlaceFromSlot(globalSlot, currentPlacementPosition, currentPlacementRotation)
            : TryPlaceLocal(globalSlot, itemType.Value, currentPlacementPosition, currentPlacementRotation);

        if (requested)
        {
            CancelPlacement();
        }
    }

    private bool TryPlaceLocal(int slotIndex, ItemType itemType, Vector3 position, Quaternion rotation)
    {
        if (!inventory.RemoveItemFromSlot(slotIndex, 1, out ItemType removedItemType))
        {
            return false;
        }

        if (removedItemType != itemType)
        {
            inventory.AddItemToSlot(removedItemType, 1, slotIndex);
            return false;
        }

        GameObject primitive = GameObject.CreatePrimitive(itemType == ItemType.Campfire ? PrimitiveType.Cylinder : PrimitiveType.Cube);
        primitive.name = itemType + " (Placed Local)";
        primitive.transform.SetPositionAndRotation(position, rotation);
        if (itemType == ItemType.CraftingTable)
        {
            primitive.AddComponent<CraftingTableStation>();
        }

        return true;
    }

    private void UpdatePreview()
    {
        currentPlacementRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        Vector3 target = transform.position + transform.forward * Mathf.Max(0.5f, placementDistance);
        currentPlacementValid = TryFindGround(target, out currentPlacementPosition)
            && !IsBlocked(currentPlacementPosition);

        if (previewObject == null)
        {
            EnsurePreview();
        }

        previewObject.transform.SetPositionAndRotation(currentPlacementPosition, currentPlacementRotation);
        SetPreviewMaterial(currentPlacementValid ? validPreviewMaterial : invalidPreviewMaterial);
    }

    private bool TryFindGround(Vector3 target, out Vector3 position)
    {
        Vector3 origin = target + Vector3.up * 2f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 6f, groundMask, QueryTriggerInteraction.Ignore))
        {
            position = hit.point + Vector3.up * Mathf.Max(0f, placementUpOffset);
            return true;
        }

        position = target;
        return false;
    }

    private bool IsBlocked(Vector3 position)
    {
        Vector3 halfExtents = defaultBounds * 0.5f;
        Collider[] hits = Physics.OverlapBox(position + Vector3.up * halfExtents.y, halfExtents, currentPlacementRotation, blockedMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit != null && !hit.transform.IsChildOf(transform))
            {
                return true;
            }
        }

        return false;
    }

    private void EnsurePreview()
    {
        if (previewObject != null) return;
        previewObject = GameObject.CreatePrimitive(SelectedItemOrDefault() == ItemType.Campfire ? PrimitiveType.Cylinder : PrimitiveType.Cube);
        previewObject.name = "Place Preview";
        Collider previewCollider = previewObject.GetComponent<Collider>();
        if (previewCollider != null) previewCollider.enabled = false;
        previewRenderers = previewObject.GetComponentsInChildren<Renderer>();
    }

    private void SetPreviewMaterial(Material material)
    {
        if (material == null || previewRenderers == null) return;
        for (int i = 0; i < previewRenderers.Length; i++)
        {
            if (previewRenderers[i] != null) previewRenderers[i].material = material;
        }
    }

    private void DestroyPreview()
    {
        if (previewObject != null) Destroy(previewObject);
        previewObject = null;
        previewRenderers = null;
        placementMode = false;
    }

    private void CancelPlacement()
    {
        DestroyPreview();
        RefreshButton();
    }

    private bool CanUseSelectedPlaceable()
    {
        if (inventory == null || hotbarUI == null || IsDowned() || !HasLocalAuthority()) return false;
        int globalSlot = hotbarUI.GetHotbarGlobalSlotIndex(hotbarUI.SelectedSlotIndex);
        ItemType? itemType = inventory.GetSlotItemType(globalSlot);
        return itemType != null && inventory.GetSlotAmount(globalSlot) > 0 && IsPlaceable(itemType.Value);
    }

    private ItemType SelectedItemOrDefault()
    {
        if (inventory == null || hotbarUI == null) return ItemType.CraftingTable;
        int globalSlot = hotbarUI.GetHotbarGlobalSlotIndex(hotbarUI.SelectedSlotIndex);
        return inventory.GetSlotItemType(globalSlot) ?? ItemType.CraftingTable;
    }

    public static bool IsPlaceable(ItemType itemType)
    {
        return itemType == ItemType.CraftingTable || itemType == ItemType.Campfire;
    }

    private void RefreshButton()
    {
        if (placeButton == null) return;
        bool canUse = CanUseSelectedPlaceable();
        placeButton.interactable = canUse;
        if (hideWhenUnavailable) placeButton.gameObject.SetActive(canUse || placementMode);
    }

    private void ResolveReferences()
    {
        if (inventory == null) inventory = GetComponent<PlayerInventory>();
        if (hotbarUI == null) hotbarUI = GetComponent<MobileHotbarUI>();
        if (fusionInventory == null) fusionInventory = GetComponent<FusionPlayerInventory>();
        if (survival == null) survival = GetComponent<FusionPlayerSurvival>();
        if (placeButton == null) placeButton = FindButtonByName("place");
    }

    private void SubscribeEvents()
    {
        if (hotbarUI != null) hotbarUI.SelectedSlotChanged += OnSelectedSlotChanged;
        if (inventory != null) inventory.InventoryChanged += RefreshButton;
    }

    private void UnsubscribeEvents()
    {
        if (hotbarUI != null) hotbarUI.SelectedSlotChanged -= OnSelectedSlotChanged;
        if (inventory != null) inventory.InventoryChanged -= RefreshButton;
    }

    private void OnSelectedSlotChanged(int slotIndex, ItemType? itemType)
    {
        if (placementMode && (itemType == null || !IsPlaceable(itemType.Value))) CancelPlacement();
        RefreshButton();
    }

    private void BindButton()
    {
        if (placeButton == null) return;
        placeButton.onClick.RemoveListener(OnPlaceButtonPressed);
        placeButton.onClick.AddListener(OnPlaceButtonPressed);
    }

    private bool IsDowned()
    {
        return survival != null && survival.IsDowned;
    }

    private bool HasLocalAuthority()
    {
        Fusion.NetworkObject fusionObject = GetComponent<Fusion.NetworkObject>();
        if (fusionObject != null && fusionObject.IsValid)
        {
            return fusionObject.HasStateAuthority || fusionObject.HasInputAuthority;
        }

        Unity.Netcode.NetworkObject netcodeObject = GetComponent<Unity.Netcode.NetworkObject>();
        if (netcodeObject != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            return netcodeObject.IsSpawned && netcodeObject.IsOwner;
        }

        return true;
    }

    private static Button FindButtonByName(string keyword)
    {
        Button[] buttons = FindObjectsOfType<Button>(true);
        string lowered = keyword.ToLowerInvariant();
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button != null && button.name.ToLowerInvariant().Contains(lowered)) return button;
        }

        return null;
    }

    private static void ShowInfo(string message)
    {
        if (PickupUIManager.instance != null) PickupUIManager.instance.ShowInfo(message);
    }
}
```

- [ ] **Step 2: Validate script**

Run Unity MCP validation for `PlaceableItemSystem.cs`.

Expected: no compile errors. If `FusionPlayerInventory.RequestPlaceFromSlot` is not yet defined, keep this task uncommitted until Task 5 adds it, or temporarily add the method signature in Task 5 before validating both together.

- [ ] **Step 3: Commit with Task 5 if needed**

If validation depends on Task 5, commit Task 4 and Task 5 together after both compile.

---

### Task 5: Add Fusion Placement Request And Spawn

**Files:**
- Create: `Assets/Scripts/PhotonFusion/FusionPlaceableObject.cs`
- Modify: `Assets/Scripts/PhotonFusion/FusionPlayerInventory.cs`

- [ ] **Step 1: Query Context7 before code**

Use Context7 for Photon Fusion docs with this query:

```text
Photon Fusion 2 Unity RPC input authority state authority Runner.Spawn NetworkObject prefab validation
```

Confirm the implementation still matches: input-authority request, state-authority validation, `Runner.Spawn` after validation.

- [ ] **Step 2: Create placeable metadata**

Create `Assets/Scripts/PhotonFusion/FusionPlaceableObject.cs`:

```csharp
using Fusion;
using UnityEngine;

public class FusionPlaceableObject : NetworkBehaviour
{
    [Networked] public int ItemTypeValue { get; private set; }
    [Networked] public PlayerRef Placer { get; private set; }

    public ItemType ItemType => System.Enum.IsDefined(typeof(ItemType), ItemTypeValue)
        ? (ItemType)ItemTypeValue
        : ItemType.CraftingTable;

    public void Initialize(ItemType itemType, PlayerRef placer)
    {
        if (Object != null && Object.IsValid && !Object.HasStateAuthority)
        {
            return;
        }

        ItemTypeValue = (int)itemType;
        Placer = placer;
    }
}
```

- [ ] **Step 3: Add placeable prefab binding to `FusionPlayerInventory`**

Add this nested class near `DropPrefabBinding`:

```csharp
[System.Serializable]
private class PlaceablePrefabBinding
{
    public ItemType itemType;
    public NetworkPrefabRef prefab;
    public GameObject prefabObject;
    public Vector3 bounds = new Vector3(1.2f, 1.2f, 1.2f);
}
```

Add fields near drop settings:

```csharp
[Header("Placement Settings")]
[SerializeField] private PlaceablePrefabBinding[] placeablePrefabs;
[SerializeField] private float maxPlacementDistance = 3.5f;
[SerializeField] private LayerMask placementBlockedMask = ~0;
```

- [ ] **Step 4: Add public request method**

Add after `RequestConsumeFromSlot`:

```csharp
public bool RequestPlaceFromSlot(int slotIndex, Vector3 position, Quaternion rotation)
{
    if (!IsNetworkReady() || !HasFusionInputAuthority() || inventory == null)
    {
        return false;
    }

    ItemType? itemType = inventory.GetSlotItemType(slotIndex);
    if (itemType == null || !PlaceableItemSystem.IsPlaceable(itemType.Value))
    {
        return false;
    }

    RPC_RequestPlace(slotIndex, (int)itemType.Value, position, rotation);
    return true;
}
```

- [ ] **Step 5: Add RPC implementation**

Add before `ResolveReferences()`:

```csharp
[Rpc(RpcSources.StateAuthority, RpcTargets.StateAuthority)]
private void RPC_RequestPlace(int slotIndex, int expectedItemTypeValue, Vector3 position, Quaternion rotation)
{
    ResolveReferences();

    if (inventory == null || Runner == null || Object == null || !System.Enum.IsDefined(typeof(ItemType), expectedItemTypeValue))
    {
        return;
    }

    if (survivalSystem != null && survivalSystem.IsDead)
    {
        return;
    }

    FusionPlayerSurvival fusionSurvival = GetComponent<FusionPlayerSurvival>();
    if (fusionSurvival != null && fusionSurvival.IsDowned)
    {
        return;
    }

    ItemType itemType = (ItemType)expectedItemTypeValue;
    if (!PlaceableItemSystem.IsPlaceable(itemType) || inventory.GetSlotItemType(slotIndex) != itemType)
    {
        return;
    }

    if (!TryGetPlaceablePrefab(itemType, out NetworkPrefabRef prefab, out GameObject prefabObject, out Vector3 bounds))
    {
        return;
    }

    if (!IsPlacementPositionValid(position, rotation, bounds))
    {
        return;
    }

    if (!inventory.RemoveItemFromSlot(slotIndex, 1, out ItemType removedItemType) || removedItemType != itemType)
    {
        if (removedItemType == itemType)
        {
            inventory.AddItemToSlot(removedItemType, 1, slotIndex);
        }

        return;
    }

    NetworkObject placedObject = prefabObject != null
        ? Runner.Spawn(prefabObject, position, rotation, Object.InputAuthority)
        : Runner.Spawn(prefab, position, rotation, Object.InputAuthority);

    if (placedObject == null)
    {
        inventory.AddItemToSlot(itemType, 1, slotIndex);
        return;
    }

    FusionPlaceableObject placeable = placedObject.GetComponent<FusionPlaceableObject>();
    if (placeable != null)
    {
        placeable.Initialize(itemType, Object.InputAuthority);
    }
}
```

- [ ] **Step 6: Add helper methods**

Add near `TryGetDropPrefab`:

```csharp
private bool TryGetPlaceablePrefab(ItemType itemType, out NetworkPrefabRef prefab, out GameObject prefabObject, out Vector3 bounds)
{
    prefabObject = null;
    bounds = new Vector3(1.2f, 1.2f, 1.2f);
    if (placeablePrefabs != null)
    {
        for (int i = 0; i < placeablePrefabs.Length; i++)
        {
            PlaceablePrefabBinding binding = placeablePrefabs[i];
            if (binding == null || binding.itemType != itemType)
            {
                continue;
            }

            bounds = new Vector3(
                Mathf.Max(0.2f, binding.bounds.x),
                Mathf.Max(0.2f, binding.bounds.y),
                Mathf.Max(0.2f, binding.bounds.z));

            if (binding.prefabObject != null && binding.prefabObject.GetComponent<NetworkObject>() != null)
            {
                prefabObject = binding.prefabObject;
                prefab = default;
                return true;
            }

            if (binding.prefab.IsValid)
            {
                prefab = binding.prefab;
                return true;
            }
        }
    }

    prefab = default;
    return false;
}

private bool IsPlacementPositionValid(Vector3 position, Quaternion rotation, Vector3 bounds)
{
    if (Vector3.Distance(transform.position, position) > Mathf.Max(0.5f, maxPlacementDistance))
    {
        return false;
    }

    Vector3 halfExtents = bounds * 0.5f;
    Collider[] hits = Physics.OverlapBox(position + Vector3.up * halfExtents.y, halfExtents, rotation, placementBlockedMask, QueryTriggerInteraction.Ignore);
    for (int i = 0; i < hits.Length; i++)
    {
        Collider hit = hits[i];
        if (hit != null && !hit.transform.IsChildOf(transform))
        {
            return false;
        }
    }

    return true;
}
```

- [ ] **Step 7: Validate scripts**

Run Unity MCP validation for:

```text
Assets/Scripts/PhotonFusion/FusionPlaceableObject.cs
Assets/Scripts/PhotonFusion/FusionPlayerInventory.cs
Assets/Scripts/Player/Survival/PlaceableItemSystem.cs
```

Expected: no compile errors.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/PhotonFusion/FusionPlaceableObject.cs Assets/Scripts/PhotonFusion/FusionPlayerInventory.cs Assets/Scripts/Player/Survival/PlaceableItemSystem.cs
git commit -m "Add Fusion placeable item requests"
```

---

### Task 6: Create Prefabs And Wire Player/Scene UI

**Files/Assets:**
- Modify via Unity MCP: `Assets/Assets/Prefabs/FusionPlayer.prefab`
- Modify via Unity MCP: `Assets/Scenes/Gameplay.unity`
- Create via Unity MCP: placed Crafting Table prefab asset
- Create via Unity MCP: placed Campfire prefab asset
- Optional create: preview materials for valid/invalid placement

- [ ] **Step 1: Create placed Crafting Table prefab**

Using Unity MCP, create a simple prefab with:

```text
Name: PlacedCraftingTable
Components: NetworkObject, FusionPlaceableObject, CraftingTableStation, BoxCollider
Visual: table-like cube composition or one cube for MVP
Layer: Default or an existing world/object layer, not Item
```

Expected: prefab has `NetworkObject` and can be assigned to `FusionPlayerInventory.placeablePrefabs`.

- [ ] **Step 2: Create placed Campfire prefab**

Using Unity MCP, create a simple prefab with:

```text
Name: PlacedCampfire
Components: NetworkObject, FusionPlaceableObject, Collider
Visual: simple cylinder/cone/cube composition for MVP
Layer: Default or an existing world/object layer, not Item
```

- [ ] **Step 3: Attach player components**

Using Unity MCP, add these components to `FusionPlayer.prefab` if missing:

```text
CraftingStationInteractor
PlaceableItemSystem
```

Ensure existing components remain unchanged.

- [ ] **Step 4: Bind placeable prefab references**

Using Unity MCP, set `FusionPlayerInventory.placeablePrefabs`:

```text
CraftingTable -> PlacedCraftingTable prefab object, bounds approx (1.2, 1.0, 1.2)
Campfire -> PlacedCampfire prefab object, bounds approx (1.0, 1.0, 1.0)
```

- [ ] **Step 5: Add or bind mobile buttons**

In `Gameplay.unity` under `/====Canvas====`, ensure buttons exist:

```text
CRAFT button: name contains "Craft", initially hidden/disabled
PLACE button: name contains "Place", initially hidden/disabled
```

If adding new buttons, match the style of existing mobile UI buttons.

- [ ] **Step 6: Save scene and prefab assets**

Use Unity MCP scene/prefab save operations.

- [ ] **Step 7: Check Unity console**

Use Unity MCP console read.

Expected: no new errors from missing scripts, missing prefab refs, or serialization failures.

- [ ] **Step 8: Commit intended assets**

Inspect `git status` and `git diff --stat`. Stage only the player prefab, Gameplay scene, new prefabs/materials, and related `.meta` files.

```bash
git add Assets/Assets/Prefabs/FusionPlayer.prefab Assets/Scenes/Gameplay.unity
git add <new-prefab-and-meta-files>
git commit -m "Wire crafting table placement prefabs and UI"
```

---

### Task 7: Add Targeted Unity Diagnostics

**Files:**
- Use Unity MCP `execute_code` for temporary diagnostics. Do not create permanent test files unless the project already has a test assembly.

- [ ] **Step 1: Validate recipe contexts**

Run an edit-mode diagnostic that creates a temporary GameObject with `PlayerInventory` and `BandageCraftingSystem`, then asserts:

```text
Simple context contains Bandage and CraftingTable.
CraftingTable context contains Axe and Campfire.
```

Expected output:

```text
PASS recipe contexts include simple and table recipes
```

- [ ] **Step 2: Validate craft output goes to inventory**

Run an edit-mode diagnostic that adds required materials to inventory, crafts Crafting Table, and asserts:

```text
Inventory has CraftingTable x1.
No world CraftingTableStation was created by crafting.
```

Expected output:

```text
PASS crafting table craft adds item only
```

- [ ] **Step 3: Validate placeable detection**

Run an edit-mode diagnostic that asserts:

```text
PlaceableItemSystem.IsPlaceable(ItemType.CraftingTable) == true
PlaceableItemSystem.IsPlaceable(ItemType.Campfire) == true
PlaceableItemSystem.IsPlaceable(ItemType.Bandage) == false
```

Expected output:

```text
PASS placeable item detection
```

- [ ] **Step 4: Validate script compile**

Run Unity MCP refresh with script compilation and read console errors.

Expected: no new compile errors.

- [ ] **Step 5: Commit only if permanent files changed**

If no permanent diagnostic files were created, do not commit. If permanent test files were created, commit them:

```bash
git add <test-files>
git commit -m "Add crafting table diagnostics"
```

---

### Task 8: Manual Gameplay Verification And Final Push Prep

**Files:**
- No intended code changes unless verification finds a bug.

- [ ] **Step 1: Run local Gameplay scene checklist**

Use Unity play mode if available. Verify:

```text
Craft Bandage still works.
Craft Crafting Table from simple context.
Crafting Table appears in inventory/hotbar, not world.
Select Crafting Table and see PLACE.
Valid placement consumes one item and creates table.
Invalid placement consumes nothing.
Approach table and see CRAFT.
CRAFT opens inventory Crafting tab with Axe and Campfire.
Craft Campfire from table context.
Campfire appears in inventory/hotbar, not world.
Select Campfire and see PLACE.
Valid placement consumes one item and creates campfire.
Downed player cannot craft/place.
```

- [ ] **Step 2: Run multiplayer check if feasible**

If host/client can be launched, verify:

```text
Both players see placed Crafting Table.
Both players see placed Campfire.
Client cannot place without item.
Client cannot place too far away.
Simultaneous placement does not make inventory negative.
```

If not feasible, document this exact residual risk in the final response.

- [ ] **Step 3: Final script/console verification**

Use Unity MCP refresh/compile and console read.

Expected: no new errors. Existing unrelated warnings may remain but must be listed.

- [ ] **Step 4: Inspect git state**

```bash
git status --short --branch
git diff --stat
git log --oneline -10
```

Expected: only intended changes are present.

- [ ] **Step 5: Push only after explicit confirmation or if user requested push**

If pushing is requested:

```bash
git push origin main
```

Expected: push succeeds. If rejected, fetch/rebase carefully and preserve unrelated user changes.
