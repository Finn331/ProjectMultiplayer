# Fusion Full Item Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate world item pickup/drop/tree resource flow fully to Photon Fusion and remove Unity Netcode gameplay dependencies from Fusion runtime.

**Architecture:** Photon Fusion becomes the authoritative multiplayer layer for world item state. `FusionPickableItem` stores replicated item type/amount and mirrors data into `PickableItem` for existing inventory/UI compatibility. `FusionPlayerInventory` handles all Fusion pickup/drop through `Runner.Spawn`/`Runner.Despawn`; scene-local fallback remains only for pre-existing scene objects during transition.

**Tech Stack:** Unity 2022.3, Photon Fusion Shared Mode, Unity MCP validation, C# MonoBehaviours/NetworkBehaviours, Unity prefabs.

---

## File Structure

- Modify `Assets/Scripts/PhotonFusion/FusionPickableItem.cs`: mirror networked item data to legacy `PickableItem` and support default initialization on spawned prefabs.
- Modify `Assets/Scripts/PhotonFusion/FusionPlayerInventory.cs`: prefer `FusionPickableItem` pickup/drop, remove local inventory mutation before successful network despawn, keep scene fallback isolated.
- Modify `Assets/Scripts/Object/Tree/TreeChoppable.cs`: expose drop metadata and let Fusion combat spawn networked drops instead of local instantiate when in Fusion mode.
- Modify `Assets/Scripts/PhotonFusion/FusionPlayerCombat.cs`: resolve scene tree depletion and request Fusion item drop spawning from the attacking player authority.
- Modify prefabs `Assets/Assets/Prefabs/Wood.prefab` and `Assets/Assets/Prefabs/Stone.prefab`: replace `Unity.Netcode.NetworkObject` with `Fusion.NetworkObject` and add `FusionPickableItem`.
- Modify `Assets/Assets/Prefabs/FusionPlayer.prefab`: assign Fusion drop prefab bindings for Wood/Stone if needed.
- Validate with Unity MCP: compile, console, prefab component checks, and scripted diagnostics.

---

### Task 1: Make Fusion Pickable Items Mirror Legacy Data

**Files:**
- Modify: `Assets/Scripts/PhotonFusion/FusionPickableItem.cs`

- [ ] **Step 1: Update `FusionPickableItem` data mirroring**

Replace the class body with behavior that initializes networked item data and mirrors it to `PickableItem` on `Spawned()` and `Render()`:

```csharp
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class FusionPickableItem : NetworkBehaviour
{
    [Networked] public int ItemTypeValue { get; set; }
    [Networked] public int Amount { get; set; }
    [Networked] public NetworkBool IsInitialized { get; set; }

    [SerializeField] private ItemType defaultItemType;
    [SerializeField] private int defaultAmount = 1;

    private PickableItem pickableItem;

    public ItemType ItemType => IsValidItemTypeValue(ItemTypeValue) ? (ItemType)ItemTypeValue : defaultItemType;
    public int ClampedAmount => Mathf.Max(1, Amount);

    public override void Spawned()
    {
        ResolveReferences();
        if (HasFusionStateAuthority())
        {
            InitializeDefaultsIfNeeded();
        }

        ApplyToPickableItem();
    }

    public override void Render()
    {
        ApplyToPickableItem();
    }

    private void Awake()
    {
        ResolveReferences();
    }

    public bool Initialize(ItemType itemType, int amount)
    {
        if (!HasFusionStateAuthority())
        {
            return false;
        }

        ItemTypeValue = (int)itemType;
        Amount = Mathf.Max(1, amount);
        IsInitialized = true;
        ApplyToPickableItem();
        return true;
    }

    public bool CanPickup(Transform player, float maxDistance)
    {
        return player != null && Vector3.Distance(transform.position, player.position) <= Mathf.Max(0f, maxDistance);
    }

    private void ResolveReferences()
    {
        if (pickableItem == null)
        {
            pickableItem = GetComponent<PickableItem>();
        }
    }

    private void InitializeDefaultsIfNeeded()
    {
        if (!IsInitialized || !IsValidItemTypeValue(ItemTypeValue))
        {
            ItemTypeValue = (int)defaultItemType;
            IsInitialized = true;
        }

        if (Amount < 1)
        {
            Amount = Mathf.Max(1, defaultAmount);
        }
    }

    private void ApplyToPickableItem()
    {
        ResolveReferences();
        if (pickableItem == null)
        {
            return;
        }

        pickableItem.itemType = ItemType;
        pickableItem.amount = ClampedAmount;
        if (string.IsNullOrWhiteSpace(pickableItem.itemName))
        {
            pickableItem.itemName = pickableItem.itemType.ToString();
        }
    }

    private bool HasFusionStateAuthority()
    {
        return Object != null && Object.HasStateAuthority;
    }

    private static bool IsValidItemTypeValue(int value)
    {
        return System.Enum.IsDefined(typeof(ItemType), value);
    }
}
```

- [ ] **Step 2: Validate script**

Run via Unity MCP: `validate_script` on `Assets/Scripts/PhotonFusion/FusionPickableItem.cs`.
Expected: `0` errors.

---

### Task 2: Make Fusion Inventory Use Fusion Pickables First

**Files:**
- Modify: `Assets/Scripts/PhotonFusion/FusionPlayerInventory.cs`

- [ ] **Step 1: Change networked pickup branch**

In `RequestPickup(PickableItem item)`, detect `FusionPickableItem` first. Use `fusionItem.ClampedAmount` and `fusionItem.ItemType`, add to inventory only after distance check, then call despawn RPC for the Fusion `NetworkObject`.

- [ ] **Step 2: Keep scene fallback isolated**

Only call `RequestScenePickup(item)` if no valid `FusionPickableItem`/`Fusion.NetworkObject` exists. This fallback supports old scene objects until all scenes are migrated.

- [ ] **Step 3: Validate script**

Run via Unity MCP: `validate_script` on `Assets/Scripts/PhotonFusion/FusionPlayerInventory.cs`.
Expected: `0` errors.

---

### Task 3: Convert Wood and Stone Prefabs to Fusion

**Files:**
- Modify prefab: `Assets/Assets/Prefabs/Wood.prefab`
- Modify prefab: `Assets/Assets/Prefabs/Stone.prefab`

- [ ] **Step 1: Remove Unity Netcode component from both prefabs**

Use Unity MCP prefab modification or Unity execute code to remove `Unity.Netcode.NetworkObject` from root of Wood and Stone.

- [ ] **Step 2: Add Fusion components to both prefabs**

Add `Fusion.NetworkObject` and `FusionPickableItem` to each root.

- [ ] **Step 3: Set item defaults**

Set Wood `FusionPickableItem.defaultItemType = ItemType.Wood` and Stone `FusionPickableItem.defaultItemType = ItemType.Stone`. Set default amount to 1.

- [ ] **Step 4: Verify prefab components**

Run Unity MCP prefab info or execute code.
Expected for each prefab: `PickableItem=True`, `Interactable=True`, `Fusion.NetworkObject=True`, `Unity.Netcode.NetworkObject=False`, `FusionPickableItem=True`.

---

### Task 4: Register Fusion Item Prefabs With Player Inventory

**Files:**
- Modify prefab: `Assets/Assets/Prefabs/FusionPlayer.prefab`
- Modify Fusion network prefab table if required by Fusion config.

- [ ] **Step 1: Add Wood and Stone to Fusion prefab table**

Use Unity editor/Fusion prefab registration so `NetworkPrefabRef` can reference Wood and Stone.

- [ ] **Step 2: Assign drop bindings on `FusionPlayerInventory`**

Set `dropPrefabs` entries: `Wood -> Wood.prefab`, `Stone -> Stone.prefab`. Keep axe scene fallback unless axe is migrated in a later task.

- [ ] **Step 3: Verify prefab data**

Run Unity MCP execute code checking `FusionPlayerInventory` serialized references are present.
Expected: Wood and Stone bindings exist and prefab refs are valid.

---

### Task 5: Migrate Tree Drops to Fusion Spawn

**Files:**
- Modify: `Assets/Scripts/Object/Tree/TreeChoppable.cs`
- Modify: `Assets/Scripts/PhotonFusion/FusionPlayerCombat.cs`
- Modify: `Assets/Scripts/PhotonFusion/FusionPlayerInventory.cs`

- [ ] **Step 1: Expose tree drop metadata**

Add read-only accessors on `TreeChoppable` for drop prefab item type, amount, stack mode, base position, scatter radius, and forward impulse direction.

- [ ] **Step 2: Prevent local tree drops in Fusion replicated hit path**

`ApplyFusionReplicatedHit` should deplete/destroy tree visuals but not instantiate local non-networked drops.

- [ ] **Step 3: Spawn networked drops from authority**

When `FusionPlayerCombat` detects tree depletion on the attacking player's authority, call a new method on `FusionPlayerInventory` to spawn Wood drops through `Runner.Spawn`.

- [ ] **Step 4: Validate script**

Run Unity MCP validate on the three modified scripts.
Expected: `0` errors.

---

### Task 6: Disable Legacy Netcode Gameplay on Fusion Player

**Files:**
- Modify prefab: `Assets/Assets/Prefabs/FusionPlayer.prefab`
- Possibly modify scripts only if components cannot be removed safely.

- [ ] **Step 1: Confirm no `Unity.Netcode.NetworkObject` on FusionPlayer**

Run execute code. Expected: false.

- [ ] **Step 2: Disable legacy bridge usage**

Ensure `NetworkInventoryBridge` is not present on `FusionPlayer.prefab` or is inert because no `Unity.Netcode.NetworkObject` exists. Keep class in project for old prefabs only.

- [ ] **Step 3: Check UI scripts choose Fusion path first**

`PlayerInteractionSystem` and `MobileHotbarUI` should prefer `FusionPlayerInventory` over `NetworkInventoryBridge` whenever `FusionPlayerInventory` is present.

---

### Task 7: Full Unity MCP Validation

**Files:**
- No source changes expected.

- [ ] **Step 1: Clear console**

Run Unity MCP `read_console` action `clear`.

- [ ] **Step 2: Force refresh and compile**

Run Unity MCP `refresh_unity` with `scope=all`, `compile=request`, `wait_for_ready=true`.

- [ ] **Step 3: Read console**

Run Unity MCP `read_console` for errors/warnings.
Expected: no compile errors. Existing Fusion TickRate warning should be separately tracked if still present.

- [ ] **Step 4: Run prefab audit**

Execute code checking FusionPlayer, Wood, Stone component composition.
Expected: FusionPlayer is Fusion-only, Wood/Stone are Fusion networked pickables.

- [ ] **Step 5: Manual two-client test**

User should test host/client flow: create room, join, start scene, pickup Wood/Stone, drop Wood/Stone, chop tree, pickup tree drops. Expected: no duplicate models, no item disappearing from hotbar on tap, inventory and world state agree across both clients.

---

## Self-Review

Spec coverage: covers full migration of item world state to Fusion, hotbar safety, prefab migration, tree drops, and validation.
Placeholder scan: no TODO/TBD placeholders.
Type consistency: uses existing classes `FusionPickableItem`, `FusionPlayerInventory`, `TreeChoppable`, `FusionPlayerCombat`, `PickableItem`, and known prefab paths.
Scope note: full cleanup of every Unity Netcode script from the repository is intentionally out of scope; this plan removes Unity Netcode from active Fusion gameplay path while leaving legacy scripts available for old prefabs/scenes.
