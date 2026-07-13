# Building System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Snap-to-grid building system: 4 piece types (Wall, Floor, Roof, Door), networked via Fusion, with HP and demolish.

**Architecture:** New `BuildingPiece` NetworkBehaviour handles networked state (HP, type, grid position) and RPC for damage/demolish. `PlaceableItemSystem` extended with snap-to-grid logic (round to nearest integer). `BandageCraftingSystem` gets 4 new recipes. `PlayerInteractionSystem` adds demolish with hold interaction and HP bar display.

**Tech Stack:** Photon Fusion (NetworkBehaviour, [Networked], RPC), Unity UI (procedural HP bar)

---

### Task 1: Add ItemType Values + Update IsPlaceable

**Files:**
- Modify: `Assets/Scripts/Object/Item/PickableItem.cs`
- Modify: `Assets/Scripts/Player/Survival/PlaceableItemSystem.cs`

- [ ] **Step 1: Add 4 new ItemType values to PickableItem.cs**

Add after `Fertilizer`:
```csharp
    Fertilizer,
    WallItem,
    FloorItem,
    RoofItem,
    DoorItem
```

- [ ] **Step 2: Add building types to PlaceableItemSystem.IsPlaceable()**

```csharp
public static bool IsPlaceable(ItemType itemType)
{
    return itemType == ItemType.CraftingTable
        || itemType == ItemType.Campfire
        || itemType == ItemType.StorageChest
        || itemType == ItemType.Furnace
        || itemType == ItemType.WallItem
        || itemType == ItemType.FloorItem
        || itemType == ItemType.RoofItem
        || itemType == ItemType.DoorItem;
}
```

- [ ] **Step 3: Wait for compilation, check console, commit**

---

### Task 2: Add Snap-to-Grid to Placement

**Files:**
- Modify: `Assets/Scripts/Player/Survival/PlaceableItemSystem.cs`

- [ ] **Step 1: Add grid snapping fields and method**

Add to the class:
```csharp
private const float GridSize = 1f;
private const float GridYOffset = 0f;

private Vector3 SnapToGrid(Vector3 worldPosition)
{
    float snappedX = Mathf.Round(worldPosition.x / GridSize) * GridSize;
    float snappedY = Mathf.Round(worldPosition.y / GridSize) * GridSize + GridYOffset;
    float snappedZ = Mathf.Round(worldPosition.z / GridSize) * GridSize;
    return new Vector3(snappedX, snappedY, snappedZ);
}
```

- [ ] **Step 2: Update UpdatePreview to snap building pieces to grid**

In `UpdatePreview()`, after `targetPosition = hit.point + hit.normal * groundOffset;`, add:
```csharp
    if (IsBuildingItem(selectedItemType))
    {
        targetPosition = SnapToGrid(targetPosition);
    }
```

- [ ] **Step 3: Add IsBuildingItem helper**

```csharp
private static bool IsBuildingItem(ItemType itemType)
{
    return itemType == ItemType.WallItem
        || itemType == ItemType.FloorItem
        || itemType == ItemType.RoofItem
        || itemType == ItemType.DoorItem;
}
```

- [ ] **Step 4: Wait for compilation, check console, commit**

---

### Task 3: Create BuildingPieceType Enum

**Files:**
- Create: `Assets/Scripts/Building/BuildingPieceType.cs`

```csharp
public enum BuildingPieceType
{
    Wall,
    Floor,
    Roof,
    Door
}
```

Also create the directory:
```bash
mkdir -p "Assets/Scripts/Building"
```

- [ ] **Step 5: Wait for compilation, check console, commit**

---

### Task 4: Create BuildingPiece NetworkBehaviour

**Files:**
- Create: `Assets/Scripts/PhotonFusion/BuildingPiece.cs`

```csharp
using Fusion;
using UnityEngine;

public class BuildingPiece : NetworkBehaviour
{
    public const float DefaultMaxHealth = 100f;

    [Networked] private float Health { get; set; }
    [Networked] private int PieceTypeValue { get; set; }
    [Networked] private Vector3Int GridPosition { get; set; }
    [Networked] private int RotationIndex { get; set; }

    public BuildingPieceType PieceType => (BuildingPieceType)PieceTypeValue;
    public float HealthValue => Health;
    public float MaxHealthValue => DefaultMaxHealth;
    public float HealthRatio => Health / DefaultMaxHealth;
    public bool IsDestroyed => Health <= 0f;

    private MeshRenderer meshRenderer;
    private Material instanceMaterial;
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

    public void Initialize(BuildingPieceType pieceType, Vector3Int gridPos, int rotationIndex)
    {
        PieceTypeValue = (int)pieceType;
        GridPosition = gridPos;
        RotationIndex = rotationIndex;
        Health = DefaultMaxHealth;
        CreateModel(pieceType);
    }

    public void TakeDamage(float amount)
    {
        if (HasStateAuthority) ApplyDamage(amount);
        else RPC_RequestDamage(amount);
    }

    public void Demolish()
    {
        if (HasStateAuthority) ApplyDemolish();
        else RPC_RequestDemolish();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDamage(float amount)
    {
        ApplyDamage(amount);
    }

    private void ApplyDamage(float amount)
    {
        Health = Mathf.Max(0f, Health - amount);
        if (Health <= 0f && Object != null && Object.IsValid)
        {
            DropDemolishResources();
            Runner.Despawn(Object);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDemolish()
    {
        ApplyDemolish();
    }

    private void ApplyDemolish()
    {
        DropDemolishResources();
        if (Object != null && Object.IsValid)
        {
            Runner.Despawn(Object);
        }
    }

    private void DropDemolishResources()
    {
        if (!HasStateAuthority) return;
        var recipe = GetCraftRecipe(PieceType);
        if (recipe == null) return;

        for (int i = 0; i < recipe.ingredients.Count; i++)
        {
            var ingredient = recipe.ingredients[i];
            int refund = Mathf.Max(1, ingredient.Amount / 2);
            Vector3 dropPos = transform.position + Vector3.up * 0.5f + Random.insideUnitSphere * 0.3f;
            SpawnResourceDrop(ingredient.itemType, refund, dropPos);
        }
    }

    private void SpawnResourceDrop(ItemType itemType, int amount, Vector3 position)
    {
        var handler = FindObjectOfType<FusionPlayerInventory>();
        if (handler == null) return;
        handler.SpawnTreeDropsFromData(position, position, Vector3.forward, itemType, 1, amount, 0.2f);
    }

    private CraftingRecipe GetCraftRecipe(BuildingPieceType pieceType)
    {
        var system = FindObjectOfType<BandageCraftingSystem>();
        if (system == null) return null;
        ItemType outputType = pieceType switch
        {
            BuildingPieceType.Wall => ItemType.WallItem,
            BuildingPieceType.Floor => ItemType.FloorItem,
            BuildingPieceType.Roof => ItemType.RoofItem,
            BuildingPieceType.Door => ItemType.DoorItem,
            _ => default
        };
        var recipes = system.GetAvailableRecipes(CraftingContext.CraftingTable);
        for (int i = 0; i < recipes.Count; i++)
        {
            if (recipes[i].outputItemType == outputType)
                return recipes[i];
        }
        return null;
    }

    public override void Spawned()
    {
        meshRenderer = GetComponentInChildren<MeshRenderer>();
        if (meshRenderer != null)
        {
            instanceMaterial = meshRenderer.material;
        }
    }

    public override void Render()
    {
        if (instanceMaterial == null) return;
        float ratio = HealthRatio;
        Color color;
        if (ratio > 0.66f) color = Color.Lerp(Color.yellow, Color.green, (ratio - 0.66f) / 0.34f);
        else if (ratio > 0.33f) color = Color.Lerp(Color.red, Color.yellow, (ratio - 0.33f) / 0.33f);
        else color = Color.red;
        instanceMaterial.SetColor(ColorPropertyId, color);
    }

    private void CreateModel(BuildingPieceType pieceType)
    {
        GameObject model = null;
        switch (pieceType)
        {
            case BuildingPieceType.Wall:
                model = GameObject.CreatePrimitive(PrimitiveType.Cube);
                model.transform.localScale = new Vector3(1f, 2f, 0.2f);
                break;
            case BuildingPieceType.Floor:
                model = GameObject.CreatePrimitive(PrimitiveType.Cube);
                model.transform.localScale = new Vector3(1f, 0.1f, 1f);
                break;
            case BuildingPieceType.Roof:
                model = GameObject.CreatePrimitive(PrimitiveType.Cube);
                model.transform.localScale = new Vector3(1f, 0.1f, 1.5f);
                model.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);
                break;
            case BuildingPieceType.Door:
                model = GameObject.CreatePrimitive(PrimitiveType.Cube);
                model.transform.localScale = new Vector3(0.8f, 2f, 0.1f);
                break;
        }
        if (model != null)
        {
            model.transform.SetParent(transform, false);
            model.transform.localPosition = Vector3.zero;
            if (pieceType != BuildingPieceType.Floor)
                model.transform.localPosition += Vector3.up * GetModelYOffset(pieceType);
        }
    }

    private static float GetModelYOffset(BuildingPieceType pieceType)
    {
        return pieceType switch
        {
            BuildingPieceType.Wall => 1f,
            BuildingPieceType.Roof => 0.05f,
            BuildingPieceType.Door => 1f,
            _ => 0f
        };
    }
}
```

- [ ] **Step 6: Wait for compilation, check console, commit**

---

### Task 5: Add Crafting Recipes

**Files:**
- Modify: `Assets/Scripts/Player/Survival/BandageCraftingSystem.cs`

- [ ] **Step 1: Add 4 building recipes in EnsureDefaultRecipes()**

After the furnace recipe, add:
```csharp
        AddDefaultRecipeIfMissing(new CraftingRecipe
        {
            recipeId = "wall",
            displayName = "Wall",
            outputItemType = ItemType.WallItem,
            outputAmount = 1,
            context = CraftingContext.CraftingTable,
            ingredients = new List<CraftingIngredient>
            {
                new CraftingIngredient { itemType = ItemType.Wood, amount = 10 }
            }
        });

        AddDefaultRecipeIfMissing(new CraftingRecipe
        {
            recipeId = "floor",
            displayName = "Floor",
            outputItemType = ItemType.FloorItem,
            outputAmount = 1,
            context = CraftingContext.CraftingTable,
            ingredients = new List<CraftingIngredient>
            {
                new CraftingIngredient { itemType = ItemType.Wood, amount = 8 }
            }
        });

        AddDefaultRecipeIfMissing(new CraftingRecipe
        {
            recipeId = "roof",
            displayName = "Roof",
            outputItemType = ItemType.RoofItem,
            outputAmount = 1,
            context = CraftingContext.CraftingTable,
            ingredients = new List<CraftingIngredient>
            {
                new CraftingIngredient { itemType = ItemType.Wood, amount = 12 }
            }
        });

        AddDefaultRecipeIfMissing(new CraftingRecipe
        {
            recipeId = "door",
            displayName = "Door",
            outputItemType = ItemType.DoorItem,
            outputAmount = 1,
            context = CraftingContext.CraftingTable,
            ingredients = new List<CraftingIngredient>
            {
                new CraftingIngredient { itemType = ItemType.Wood, amount = 15 },
                new CraftingIngredient { itemType = ItemType.Iron, amount = 2 }
            }
        });
```

- [ ] **Step 2: Wait for compilation, check console, commit**

---

### Task 6: Update FusionPlayerInventory Prefab Bindings

**Files:**
- Modify: `Assets/Scripts/PhotonFusion/FusionPlayerInventory.cs`

- [ ] **Step 1: Add building types to CanSpawnSceneDrop**

Add building item types to the `CanSpawnSceneDrop` method:
```csharp
            || itemType == ItemType.WallItem
            || itemType == ItemType.FloorItem
            || itemType == ItemType.RoofItem
            || itemType == ItemType.DoorItem;
```

- [ ] **Step 2: Add building types for scene drop spawn**

In `SpawnSceneDropLocal`, add after the Wood case:
```csharp
        else if (itemType == ItemType.WallItem || itemType == ItemType.FloorItem
            || itemType == ItemType.RoofItem || itemType == ItemType.DoorItem)
        {
            droppedObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            droppedObject.transform.localScale = new Vector3(0.3f, 0.15f, 0.3f);
            droppedObject.transform.position = position;
        }
```

- [ ] **Step 3: Wait for compilation, check console, commit**

---

### Task 7: Demolish Interaction + HP Bar

**Files:**
- Modify: `Assets/Scripts/Player/Survival/PlayerInteractionSystem.cs`

- [ ] **Step 1: Add demolish fields**

Add to class:
```csharp
    [Header("Building")]
    [SerializeField] private float demolishHoldTime = 1.5f;
    private float demolishHoldTimer;
    private BuildingPiece currentBuildingTarget;
    private GameObject hpBarObject;
    private UnityEngine.UI.Image hpBarFill;
```

- [ ] **Step 2: Add building interaction detection in Update**

In `Update()`, after `CheckInteractableInFront()`, add:
```csharp
    DetectBuildingPiece();
    if (currentBuildingTarget != null)
    {
        ShowHpBar(currentBuildingTarget);
        if (IsHoldingDemolish())
        {
            demolishHoldTimer += Time.deltaTime;
            if (demolishHoldTimer >= demolishHoldTime)
            {
                PerformDemolish();
            }
        }
        else
        {
            demolishHoldTimer = 0f;
        }
    }
    else
    {
        HideHpBar();
        demolishHoldTimer = 0f;
    }
```

- [ ] **Step 3: Add DetectBuildingPiece method**

```csharp
private void DetectBuildingPiece()
{
    currentBuildingTarget = null;
    if (playerCamera == null) return;

    Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
    if (!Physics.Raycast(ray, out RaycastHit hit, interactDistance)) return;

    BuildingPiece piece = hit.collider.GetComponentInParent<BuildingPiece>();
    if (piece == null) return;
    currentBuildingTarget = piece;
}
```

- [ ] **Step 4: Add HP bar show/hide methods**

```csharp
private void ShowHpBar(BuildingPiece piece)
{
    if (hpBarObject == null)
    {
        hpBarObject = new GameObject("BuildingHpBar", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        hpBarObject.transform.SetParent(FindFirstObjectByType<Canvas>()?.transform, false);
        var bg = hpBarObject.GetComponent<UnityEngine.UI.Image>();
        bg.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
        var bgRt = hpBarObject.GetComponent<RectTransform>();
        bgRt.sizeDelta = new Vector2(200f, 8f);

        var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        fillGo.transform.SetParent(hpBarObject.transform, false);
        hpBarFill = fillGo.GetComponent<UnityEngine.UI.Image>();
        hpBarFill.color = Color.red;
        var fillRt = fillGo.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = new Vector2(1f, 1f);
        fillRt.sizeDelta = Vector2.zero; fillRt.anchoredPosition = Vector2.zero;
        hpBarFill.type = UnityEngine.UI.Image.Type.Filled;
        hpBarFill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
    }

    hpBarObject.SetActive(true);
    if (Camera.main != null)
    {
        Vector3 screenPos = Camera.main.WorldToScreenPoint(piece.transform.position + Vector3.up * 2f);
        ((RectTransform)hpBarObject.transform).position = screenPos;
    }
    if (hpBarFill != null) hpBarFill.fillAmount = piece.HealthRatio;
}

private void HideHpBar()
{
    if (hpBarObject != null) hpBarObject.SetActive(false);
}

private bool IsHoldingDemolish()
{
    return currentBuildingTarget != null && Input.GetMouseButton(0);
}

private void PerformDemolish()
{
    if (currentBuildingTarget != null)
    {
        currentBuildingTarget.Demolish();
        demolishHoldTimer = 0f;
        currentBuildingTarget = null;
    }
}
```

- [ ] **Step 5: Wait for compilation, check console, commit**

---

### Task 8: Add Building Items to Vending Machine

**Files:**
- Modify: `Assets/Scripts/Testing/TestingResourceVendingMachine.cs`

- [ ] **Step 1: Add dispense methods and buttons**

Add dispense methods:
```csharp
private void DispenseWall()
{
    if (currentInventory == null) { ShowInfo("Open vending first"); return; }
    int accepted = currentInventory.AddItem(ItemType.WallItem, 1);
    if (accepted > 0) ShowInfo("Wall +" + accepted);
}
private void DispenseFloor()
{
    if (currentInventory == null) { ShowInfo("Open vending first"); return; }
    int accepted = currentInventory.AddItem(ItemType.FloorItem, 1);
    if (accepted > 0) ShowInfo("Floor +" + accepted);
}
private void DispenseRoof()
{
    if (currentInventory == null) { ShowInfo("Open vending first"); return; }
    int accepted = currentInventory.AddItem(ItemType.RoofItem, 1);
    if (accepted > 0) ShowInfo("Roof +" + accepted);
}
private void DispenseDoor()
{
    if (currentInventory == null) { ShowInfo("Open vending first"); return; }
    int accepted = currentInventory.AddItem(ItemType.DoorItem, 1);
    if (accepted > 0) ShowInfo("Door +" + accepted);
}
```

Add buttons in EnsureUI after CAMPFIRE:
```csharp
CreateButton(contentRect, "WALL x1", Vector2.zero, DispenseWall);
CreateButton(contentRect, "FLOOR x1", Vector2.zero, DispenseFloor);
CreateButton(contentRect, "ROOF x1", Vector2.zero, DispenseRoof);
CreateButton(contentRect, "DOOR x1", Vector2.zero, DispenseDoor);
```

- [ ] **Step 2: Wait for compilation, check console, commit**

---

### Task 9: Integration Test

- [ ] **Step 1: Test flow**
1. Open vending → get building items → equip in hotbar
2. Place in world (should snap to grid)
3. Verify networked (other players can see)
4. Look at piece → HP bar shows
5. Hold to demolish → piece destroyed, resources dropped

- [ ] **Step 2: Final commit**
