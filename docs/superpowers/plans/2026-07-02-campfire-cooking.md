# Campfire Cooking V1 Implementation Plan

> **For agentic workers:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Add campfire cooking with 4 raw→cooked food pairs ala The Forest.

**Architecture:** Add item types, cooking script on campfire prefab, Interactable wiring. `CampfireCooking.cs` works alongside existing `FusionStorageChest` login.

**Tech Stack:** Unity 2022.3, C#, Photon Fusion, Food model prefabs from `Assets/Simple Foods/Prefabs`.

---

### Task 1: Add RawMeat and CookedMeat Item Types

**Files:** `Assets/Scripts/Object/Item/PickableItem.cs`

Add `RawMeat` and `CookedMeat` after `StorageChest` in the ItemType enum. Compile, commit.

### Task 2: Add CookedMeat Consumable Effect

**Files:** `Assets/Scripts/Player/Survival/ConsumableItemCatalog.cs`

Add case for `ItemType.CookedMeat` returning `HungerAmount = 40f`. Compile, commit.

### Task 3: Create CampfireCooking Script

**Files:** Create `Assets/Scripts/PhotonFusion/CampfireCooking.cs`

NetworkBehaviour with 4 cooking slots. Each slot: timer, raw prefab instance, cooked prefab instance. `FixedUpdateNetwork()` advances timers. Public `TryPlaceRawMeat(PlayerInventory)` and `TryPickupCooked(PlayerInventory, int slot)`. Uses 4 raw→cooked prefab pairs from `Assets/Simple Foods/Prefabs`. Compile, commit.

### Task 4: Wire Campfire Prefab

**Files:** `Assets/Assets/Prefabs/PlacedCampfire.prefab`

Add `Interactable`, `CampfireCooking`, layer `Item`. Wire persistent listener to interact. Compile, commit.

### Task 5: Final Validation

Refresh, check console, diagnostic for item types + consumable effect + script compile. No commit needed.
