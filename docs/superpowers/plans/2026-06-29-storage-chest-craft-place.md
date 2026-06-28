# Storage Chest Craft Place Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a craftable, inventory-held, placeable, network-synchronized Storage Chest for the Fusion survival loop.

**Architecture:** Reuse the existing crafting, placement, Fusion placeable, and Fusion storage systems. Add `ItemType.StorageChest`, add one Crafting Table recipe, allow `StorageChest` through the existing placeable validation paths, then create and wire a networked placed prefab plus a local-only ghost prefab.

**Tech Stack:** Unity, C#, Photon Fusion, Unity prefabs, MCP for Unity diagnostics.

---

## File Structure

- Modify: `Assets/Scripts/Object/Item/PickableItem.cs`
  - Responsibility: define the shared `ItemType` enum used by inventory, crafting, placement, and storage UI.
- Modify: `Assets/Scripts/Player/Survival/BandageCraftingSystem.cs`
  - Responsibility: provide default craft recipes and context filtering.
- Modify: `Assets/Scripts/Player/Survival/PlaceableItemSystem.cs`
  - Responsibility: decide which inventory item types can enter placement mode, show local ghosts, and submit placement requests.
- Modify: `Assets/Assets/Prefabs/FusionPlayer.prefab`
  - Responsibility: serialize the new Storage Chest placeable prefab binding on `FusionPlayerInventory` and ghost binding on `PlaceableItemSystem`.
- Create: `Assets/Assets/Prefabs/PlacedStorageChest.prefab`
  - Responsibility: networked, persistent, interactable Fusion chest spawned by placement.
- Create: `Assets/Assets/Prefabs/GhostStorageChest.prefab`
  - Responsibility: local-only placement preview visual for Storage Chest.
- Modify if Unity requires it: `Assets/DefaultNetworkPrefabs.asset`
  - Responsibility: Fusion prefab registry entry for `PlacedStorageChest` when the prefab table does not auto-register from the prefab reference.
- Modify if Unity requires it: `Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion`
  - Responsibility: Fusion network project config prefab table import data.
- Do not modify: `Assets/Scripts/Object/Storage/StorageChest.cs`
  - Reason: this is the older Netcode path and is outside this Fusion feature.

## Task 1: Add StorageChest Item Type

**Files:**
- Modify: `Assets/Scripts/Object/Item/PickableItem.cs:3-17`

- [ ] **Step 1: Inspect the enum before editing**

Run:

```powershell
rg -n "public enum ItemType|CraftingTable|Campfire|StorageChest" "Assets/Scripts/Object/Item/PickableItem.cs"
```

Expected before this task:

```text
3:public enum ItemType
15:    CraftingTable,
16:    Campfire
```

- [ ] **Step 2: Add `StorageChest` after `Campfire`**

Change `Assets/Scripts/Object/Item/PickableItem.cs` to:

```csharp
using UnityEngine;

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
    Campfire,
    StorageChest
}

public class PickableItem : MonoBehaviour
{
    public ItemType itemType;
    public string itemName;
    public int amount = 1;

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            itemName = itemType.ToString();
        }

        if (amount < 1)
        {
            amount = 1;
        }
    }
}
```

- [ ] **Step 3: Compile-check the enum reference**

Use Unity MCP:

```text
refresh_unity(mode="if_dirty", scope="scripts", compile="request", wait_for_ready=true)
read_console(action="get", types=["error"], count="20")
```

Expected: no new compiler errors mentioning `ItemType` or `StorageChest`.

- [ ] **Step 4: Commit**

Run:

```powershell
git status --short
git add "Assets/Scripts/Object/Item/PickableItem.cs"
git commit -m "Add storage chest item type"
```

Expected: commit succeeds and only `PickableItem.cs` is included.

## Task 2: Add Crafting Table Storage Chest Recipe

**Files:**
- Modify: `Assets/Scripts/Player/Survival/BandageCraftingSystem.cs:166-229`

- [ ] **Step 1: Inspect existing default recipes**

Run:

```powershell
rg -n "recipeId = \"crafting_table\"|recipeId = \"axe\"|recipeId = \"campfire\"|Storage Chest|StorageChest" "Assets/Scripts/Player/Survival/BandageCraftingSystem.cs"
```

Expected before this task: existing recipe IDs are found, and no `StorageChest` recipe is found.

- [ ] **Step 2: Add the default recipe after the campfire recipe**

In `EnsureDefaultRecipes()`, keep the existing recipes and add this block immediately after the campfire `AddDefaultRecipeIfMissing(...)` call:

```csharp
        AddDefaultRecipeIfMissing(new CraftingRecipe
        {
            recipeId = "storage_chest",
            displayName = "Storage Chest",
            outputItemType = ItemType.StorageChest,
            outputAmount = 1,
            context = CraftingContext.CraftingTable,
            ingredients = new List<CraftingIngredient>
            {
                new CraftingIngredient { itemType = ItemType.Wood, amount = 12 },
                new CraftingIngredient { itemType = ItemType.Stone, amount = 2 },
                new CraftingIngredient { itemType = ItemType.Fiber, amount = 4 }
            }
        });
```

The end of `EnsureDefaultRecipes()` should contain these two consecutive placeable recipes:

```csharp
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

        AddDefaultRecipeIfMissing(new CraftingRecipe
        {
            recipeId = "storage_chest",
            displayName = "Storage Chest",
            outputItemType = ItemType.StorageChest,
            outputAmount = 1,
            context = CraftingContext.CraftingTable,
            ingredients = new List<CraftingIngredient>
            {
                new CraftingIngredient { itemType = ItemType.Wood, amount = 12 },
                new CraftingIngredient { itemType = ItemType.Stone, amount = 2 },
                new CraftingIngredient { itemType = ItemType.Fiber, amount = 4 }
            }
        });
```

- [ ] **Step 3: Compile-check recipe code**

Use Unity MCP:

```text
refresh_unity(mode="if_dirty", scope="scripts", compile="request", wait_for_ready=true)
read_console(action="get", types=["error"], count="20")
```

Expected: no new compiler errors mentioning `StorageChest`, `CraftingRecipe`, or `CraftingIngredient`.

- [ ] **Step 4: Runtime diagnostic for context filtering**

Use Unity MCP `execute_code(action="execute", safety_checks=true)` with this code:

```csharp
var go = new UnityEngine.GameObject("StorageChestRecipeDiagnostic");
var inventory = go.AddComponent<PlayerInventory>();
var crafting = go.AddComponent<BandageCraftingSystem>();
var simple = crafting.GetAvailableRecipes(CraftingContext.Simple);
var table = crafting.GetAvailableRecipes(CraftingContext.CraftingTable);
bool simpleHasChest = false;
bool tableHasChest = false;
CraftingRecipe chestRecipe = null;

for (int i = 0; i < simple.Count; i++)
{
    if (simple[i] != null && simple[i].outputItemType == ItemType.StorageChest)
    {
        simpleHasChest = true;
    }
}

for (int i = 0; i < table.Count; i++)
{
    if (table[i] != null && table[i].outputItemType == ItemType.StorageChest)
    {
        tableHasChest = true;
        chestRecipe = table[i];
    }
}

UnityEngine.Object.DestroyImmediate(go);

if (simpleHasChest)
{
    return "FAIL: Storage Chest appeared in Simple crafting context.";
}

if (!tableHasChest || chestRecipe == null)
{
    return "FAIL: Storage Chest recipe missing from CraftingTable context.";
}

if (chestRecipe.OutputAmount != 1 || chestRecipe.ingredients.Count != 3)
{
    return "FAIL: Storage Chest recipe output or ingredient count is wrong.";
}

bool hasWood = false;
bool hasStone = false;
bool hasFiber = false;
for (int i = 0; i < chestRecipe.ingredients.Count; i++)
{
    CraftingIngredient ingredient = chestRecipe.ingredients[i];
    hasWood |= ingredient.itemType == ItemType.Wood && ingredient.Amount == 12;
    hasStone |= ingredient.itemType == ItemType.Stone && ingredient.Amount == 2;
    hasFiber |= ingredient.itemType == ItemType.Fiber && ingredient.Amount == 4;
}

return hasWood && hasStone && hasFiber
    ? "PASS: Storage Chest recipe is CraftingTable-only with Wood x12, Stone x2, Fiber x4."
    : "FAIL: Storage Chest recipe ingredients are wrong.";
```

Expected:

```text
PASS: Storage Chest recipe is CraftingTable-only with Wood x12, Stone x2, Fiber x4.
```

- [ ] **Step 5: Commit**

Run:

```powershell
git status --short
git add "Assets/Scripts/Player/Survival/BandageCraftingSystem.cs"
git commit -m "Add storage chest crafting recipe"
```

Expected: commit succeeds and only `BandageCraftingSystem.cs` is included.

## Task 3: Allow StorageChest Placement Mode

**Files:**
- Modify: `Assets/Scripts/Player/Survival/PlaceableItemSystem.cs:96-99`

- [ ] **Step 1: Inspect current placeable predicate**

Run:

```powershell
rg -n "public static bool IsPlaceable|CraftingTable|Campfire|StorageChest" "Assets/Scripts/Player/Survival/PlaceableItemSystem.cs"
```

Expected before this task: `IsPlaceable` returns true only for `CraftingTable` and `Campfire`.

- [ ] **Step 2: Add `StorageChest` to `IsPlaceable`**

Replace `IsPlaceable` with:

```csharp
    public static bool IsPlaceable(ItemType itemType)
    {
        return itemType == ItemType.CraftingTable
            || itemType == ItemType.Campfire
            || itemType == ItemType.StorageChest;
    }
```

- [ ] **Step 3: Compile-check placement code**

Use Unity MCP:

```text
refresh_unity(mode="if_dirty", scope="scripts", compile="request", wait_for_ready=true)
read_console(action="get", types=["error"], count="20")
```

Expected: no new compiler errors mentioning `PlaceableItemSystem` or `StorageChest`.

- [ ] **Step 4: Runtime diagnostic for placeable predicate**

Use Unity MCP `execute_code(action="execute", safety_checks=true)` with this code:

```csharp
bool table = PlaceableItemSystem.IsPlaceable(ItemType.CraftingTable);
bool campfire = PlaceableItemSystem.IsPlaceable(ItemType.Campfire);
bool chest = PlaceableItemSystem.IsPlaceable(ItemType.StorageChest);
bool wood = PlaceableItemSystem.IsPlaceable(ItemType.Wood);

return table && campfire && chest && !wood
    ? "PASS: StorageChest is placeable and Wood is not."
    : $"FAIL: table={table}, campfire={campfire}, chest={chest}, wood={wood}";
```

Expected:

```text
PASS: StorageChest is placeable and Wood is not.
```

- [ ] **Step 5: Commit**

Run:

```powershell
git status --short
git add "Assets/Scripts/Player/Survival/PlaceableItemSystem.cs"
git commit -m "Allow storage chest placement"
```

Expected: commit succeeds and only `PlaceableItemSystem.cs` is included.

## Task 4: Create PlacedStorageChest Network Prefab

**Files:**
- Create: `Assets/Assets/Prefabs/PlacedStorageChest.prefab`
- Create by Unity: `Assets/Assets/Prefabs/PlacedStorageChest.prefab.meta`
- Modify if Unity updates registration: `Assets/DefaultNetworkPrefabs.asset`
- Modify if Unity updates registration: `Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion`

- [ ] **Step 1: Inspect reference placed prefabs**

Use Unity MCP or file search to inspect these prefabs:

```text
Assets/Assets/Prefabs/PlacedCraftingTable.prefab
Assets/Assets/Prefabs/PlacedCampfire.prefab
```

Required reference components:

```text
NetworkObject
NetworkTransform
FusionPlaceableObject
Collider
```

- [ ] **Step 2: Create the placed chest GameObject in Unity**

Use Unity MCP batch operations or Unity editor automation to create a root object named `PlacedStorageChest` with:

```text
Transform position: 0, 0, 0
Transform rotation: 0, 0, 0
Transform scale: 1, 1, 1
Components:
- NetworkObject
- NetworkTransform
- FusionPlaceableObject
- FusionStorageChest
- BoxCollider
Children visual primitives:
- ChestBody: cube, local scale 1.2, 0.55, 0.75, local position 0, 0.275, 0
- ChestLid: cube, local scale 1.25, 0.18, 0.8, local position 0, 0.65, 0
- ChestLatch: cube, local scale 0.18, 0.18, 0.06, local position 0, 0.47, -0.43
```

Set the root `BoxCollider` approximately to:

```text
center: 0, 0.38, 0
size: 1.25, 0.8, 0.8
isTrigger: false
```

Set visual child primitive colliders disabled or removed so only the root collider controls placement and interaction blocking.

- [ ] **Step 3: Save as prefab**

Save the root object as:

```text
Assets/Assets/Prefabs/PlacedStorageChest.prefab
```

Then remove the temporary scene object if one was created only for prefab authoring.

- [ ] **Step 4: Verify placed prefab components**

Use Unity MCP `manage_prefabs(action="get_hierarchy", prefab_path="Assets/Assets/Prefabs/PlacedStorageChest.prefab")` or equivalent inspection.

Expected:

```text
PlacedStorageChest has NetworkObject
PlacedStorageChest has NetworkTransform
PlacedStorageChest has FusionPlaceableObject
PlacedStorageChest has FusionStorageChest
PlacedStorageChest has BoxCollider
PlacedStorageChest has no missing scripts
```

- [ ] **Step 5: Register Fusion prefab if needed**

If placing the chest later logs a Fusion prefab-id translation error, register/rebuild the prefab table for `PlacedStorageChest` using the same workflow used for `PlacedCraftingTable` and `PlacedCampfire`.

After registration, expected modified files are one or both of:

```text
Assets/DefaultNetworkPrefabs.asset
Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion
```

- [ ] **Step 6: Commit**

Run:

```powershell
git status --short
git add "Assets/Assets/Prefabs/PlacedStorageChest.prefab" "Assets/Assets/Prefabs/PlacedStorageChest.prefab.meta"
git add "Assets/DefaultNetworkPrefabs.asset" "Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion"
git commit -m "Add placed storage chest prefab"
```

Expected: commit includes the new prefab and only includes Fusion registry files if Unity actually changed them.

## Task 5: Create GhostStorageChest Preview Prefab

**Files:**
- Create: `Assets/Assets/Prefabs/GhostStorageChest.prefab`
- Create by Unity: `Assets/Assets/Prefabs/GhostStorageChest.prefab.meta`

- [ ] **Step 1: Inspect reference ghost prefabs**

Inspect:

```text
Assets/Assets/Prefabs/GhostCraftingTable.prefab
Assets/Assets/Prefabs/GhostCampfire.prefab
```

Required pattern:

```text
No NetworkObject
No NetworkTransform
No FusionPlaceableObject
No FusionStorageChest
No Fusion prefab label
Visual renderers only
Colliders absent or disabled
```

- [ ] **Step 2: Create the local-only ghost chest prefab**

Create a root object named `GhostStorageChest` with visual children matching the placed chest shape:

```text
Root: GhostStorageChest
Children visual primitives:
- GhostChestBody: cube, local scale 1.2, 0.55, 0.75, local position 0, 0.275, 0
- GhostChestLid: cube, local scale 1.25, 0.18, 0.8, local position 0, 0.65, 0
- GhostChestLatch: cube, local scale 0.18, 0.18, 0.06, local position 0, 0.47, -0.43
```

Do not add these components:

```text
NetworkObject
NetworkTransform
FusionPlaceableObject
FusionStorageChest
```

Disable or remove all colliders from the root and children.

- [ ] **Step 3: Save as prefab**

Save as:

```text
Assets/Assets/Prefabs/GhostStorageChest.prefab
```

Then remove the temporary scene object if one was created only for prefab authoring.

- [ ] **Step 4: Verify ghost prefab has no network/storage components**

Use Unity MCP inspection.

Expected:

```text
GhostStorageChest has renderers
GhostStorageChest has no enabled colliders
GhostStorageChest has no NetworkObject
GhostStorageChest has no NetworkTransform
GhostStorageChest has no FusionPlaceableObject
GhostStorageChest has no FusionStorageChest
GhostStorageChest has no missing scripts
```

- [ ] **Step 5: Commit**

Run:

```powershell
git status --short
git add "Assets/Assets/Prefabs/GhostStorageChest.prefab" "Assets/Assets/Prefabs/GhostStorageChest.prefab.meta"
git commit -m "Add storage chest placement ghost"
```

Expected: commit includes only the ghost prefab and its `.meta` unless Unity changed required material assets.

## Task 6: Wire Storage Chest Prefabs On FusionPlayer

**Files:**
- Modify: `Assets/Assets/Prefabs/FusionPlayer.prefab`

- [ ] **Step 1: Inspect current serialized bindings**

Search the player prefab for existing bindings:

```powershell
rg -n "placeablePrefabs|ghostPrefabs|CraftingTable|Campfire|PlacedCraftingTable|PlacedCampfire|GhostCraftingTable|GhostCampfire" "Assets/Assets/Prefabs/FusionPlayer.prefab"
```

Expected before this task: Crafting Table and Campfire bindings exist for `FusionPlayerInventory.placeablePrefabs` and `PlaceableItemSystem.ghostPrefabs`.

- [ ] **Step 2: Add a `FusionPlayerInventory.placeablePrefabs` entry**

Use Unity prefab editing, not hand-edited YAML if avoidable. Add one entry to the `placeablePrefabs` array on `FusionPlayerInventory`:

```text
itemType: StorageChest
prefab: leave default unless the project is using NetworkPrefabRef values directly for this array
prefabObject: Assets/Assets/Prefabs/PlacedStorageChest.prefab
bounds: 1.25, 0.8, 0.8
```

Preserve existing Crafting Table and Campfire entries exactly.

- [ ] **Step 3: Add a `PlaceableItemSystem.ghostPrefabs` entry**

Add one entry to the `ghostPrefabs` array on `PlaceableItemSystem`:

```text
itemType: StorageChest
ghostPrefab: Assets/Assets/Prefabs/GhostStorageChest.prefab
previewBounds: 1.25, 0.8, 0.8
```

Preserve existing Crafting Table and Campfire entries exactly.

- [ ] **Step 4: Verify prefab bindings by inspection**

Search again:

```powershell
rg -n "StorageChest|PlacedStorageChest|GhostStorageChest" "Assets/Assets/Prefabs/FusionPlayer.prefab"
```

Expected: references to both new prefabs are present.

- [ ] **Step 5: Unity compile and console check**

Use Unity MCP:

```text
refresh_unity(mode="if_dirty", scope="all", compile="request", wait_for_ready=true)
read_console(action="get", types=["error"], count="30")
```

Expected: no new compiler errors and no missing-script errors tied to `PlacedStorageChest`, `GhostStorageChest`, or `FusionPlayer.prefab`.

- [ ] **Step 6: Commit**

Run:

```powershell
git status --short
git add "Assets/Assets/Prefabs/FusionPlayer.prefab"
git commit -m "Wire storage chest placement prefabs"
```

Expected: commit includes only `FusionPlayer.prefab` unless Unity produced required prefab registry changes not committed in Task 4.

## Task 7: End-To-End Unity Validation

**Files:**
- No intended source edits.
- Possible generated/registration edits only if Unity reports prefab registration problems.

- [ ] **Step 1: Clear old console noise**

Use Unity MCP:

```text
read_console(action="clear")
```

Expected: console clears.

- [ ] **Step 2: Full script and asset refresh**

Use Unity MCP:

```text
refresh_unity(mode="force", scope="all", compile="request", wait_for_ready=true)
read_console(action="get", types=["error"], count="50", include_stacktrace=true)
```

Expected: no new errors. If the known recurring `The referenced script (Unknown) on this Behaviour is missing!` appears without a Storage Chest stack/reference, record it as pre-existing noise and do not claim it is fixed.

- [ ] **Step 3: Prefab component diagnostic**

Use Unity MCP `execute_code(action="execute", safety_checks=true)` with this code:

```csharp
GameObject placed = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Assets/Prefabs/PlacedStorageChest.prefab");
GameObject ghost = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Assets/Prefabs/GhostStorageChest.prefab");

if (placed == null)
{
    return "FAIL: PlacedStorageChest prefab missing.";
}

if (ghost == null)
{
    return "FAIL: GhostStorageChest prefab missing.";
}

bool placedOk = placed.GetComponent<Fusion.NetworkObject>() != null
    && placed.GetComponent<Fusion.NetworkTransform>() != null
    && placed.GetComponent<FusionPlaceableObject>() != null
    && placed.GetComponent<FusionStorageChest>() != null
    && placed.GetComponent<Collider>() != null;

bool ghostOk = ghost.GetComponent<Fusion.NetworkObject>() == null
    && ghost.GetComponent<Fusion.NetworkTransform>() == null
    && ghost.GetComponent<FusionPlaceableObject>() == null
    && ghost.GetComponent<FusionStorageChest>() == null;

Collider[] ghostColliders = ghost.GetComponentsInChildren<Collider>(true);
bool ghostHasEnabledCollider = false;
for (int i = 0; i < ghostColliders.Length; i++)
{
    ghostHasEnabledCollider |= ghostColliders[i] != null && ghostColliders[i].enabled;
}

if (!placedOk)
{
    return "FAIL: PlacedStorageChest is missing required network/placeable/storage/collider components.";
}

if (!ghostOk || ghostHasEnabledCollider)
{
    return $"FAIL: GhostStorageChest has invalid network/storage components or enabled colliders. enabledCollider={ghostHasEnabledCollider}";
}

return "PASS: Storage Chest placed and ghost prefabs have expected components.";
```

Expected:

```text
PASS: Storage Chest placed and ghost prefabs have expected components.
```

- [ ] **Step 4: Manual host/client gameplay validation**

Run a local multiplayer check with one host and one client using the existing project workflow.

Validate these exact outcomes:

```text
Crafting Table context shows Storage Chest recipe.
Simple crafting context does not show Storage Chest recipe.
Crafting Storage Chest consumes Wood x12, Stone x2, Fiber x4.
Crafting Storage Chest adds StorageChest x1 to inventory/hotbar.
Selecting StorageChest shows the PLACE button.
First PLACE tap enters ghost preview mode.
Second PLACE tap sends placement request.
Placed chest appears for host and client at the same position.
Placed chest remains after placement item count decreases by one.
Nearby player can open StorageChestUI from the placed chest.
Depositing one item updates the chest contents for the other peer.
Taking one item updates the chest contents and player inventory correctly.
Downed player cannot place StorageChest.
```

- [ ] **Step 5: Fix only Storage Chest regressions found by validation**

If validation fails, apply the smallest targeted fix:

```text
Recipe missing: revisit Task 2 only.
PLACE button missing: revisit Task 3 or Task 6 ghost binding only.
Spawn prefab-id translation error: revisit Task 4 Fusion prefab registration only.
Chest spawns but does not open: inspect PlayerInteractionSystem FusionStorageChest path before changing code.
Deposit/take desync: inspect FusionStorageChest authority/range rejection logs before changing code.
```

Do not change `StorageChest.cs` unless the root cause proves the active Fusion flow incorrectly depends on the older Netcode chest.

- [ ] **Step 6: Final commit if validation produced fixes**

Run:

```powershell
git status --short
git diff -- "Assets/Scripts" "Assets/Assets/Prefabs" "Assets/DefaultNetworkPrefabs.asset" "Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion"
git add <only files intentionally changed for the validation fix>
git commit -m "Harden storage chest placement integration"
```

Expected: commit is skipped if validation required no edits.

## Final Verification

- [ ] Run Unity MCP script/asset refresh:

```text
refresh_unity(mode="force", scope="all", compile="request", wait_for_ready=true)
read_console(action="get", types=["error"], count="50", include_stacktrace=true)
```

- [ ] Confirm git status contains no unintended scene or unrelated prefab changes:

```powershell
git status --short
git diff --stat
```

- [ ] Confirm recent commits are the intended Storage Chest commits:

```powershell
git log --oneline -8
```

Expected final state:

```text
Storage Chest recipe exists only in Crafting Table context.
StorageChest is placeable.
PlacedStorageChest is networked and contains FusionStorageChest.
GhostStorageChest is local-only and non-networked.
FusionPlayer has placed and ghost prefab bindings.
No new Unity compiler errors.
Host/client manual validation passes or any remaining issue is documented with exact console output.
```
