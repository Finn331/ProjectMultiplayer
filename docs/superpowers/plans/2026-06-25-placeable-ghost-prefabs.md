# Placeable Ghost Prefabs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make placement previews use item-shaped local ghost prefabs for Crafting Table and Campfire.

**Architecture:** Keep final placement networked through `FusionPlayerInventory`. Add a local-only ghost prefab mapping to `PlaceableItemSystem`; ghost prefabs contain only visuals and disabled/no colliders.

**Tech Stack:** Unity C#, Photon Fusion, Unity MCP, uGUI existing placement flow.

---

### Task 1: Add Ghost Prefab Mapping

**Files:**
- Modify: `Assets/Scripts/Player/Survival/PlaceableItemSystem.cs`

- [ ] Add a serializable `GhostPrefabBinding` with `ItemType itemType`, `GameObject ghostPrefab`, and `Vector3 previewBounds`.
- [ ] Add `GhostPrefabBinding[] ghostPrefabs` serialized field.
- [ ] Update `EnsurePreviewObject()` to instantiate the matching ghost prefab when present.
- [ ] Disable all colliders on the instantiated ghost.
- [ ] Fallback to the current cube preview when no ghost prefab exists.
- [ ] Validate `PlaceableItemSystem.cs` with Unity MCP.

### Task 2: Create And Bind Ghost Prefabs

**Files/Assets:**
- Create: `Assets/Assets/Prefabs/GhostCraftingTable.prefab`
- Create: `Assets/Assets/Prefabs/GhostCampfire.prefab`
- Modify: `Assets/Assets/Prefabs/FusionPlayer.prefab`

- [ ] Use Unity MCP to create ghost prefabs with visual-only geometry matching the placed prefabs.
- [ ] Ensure ghost prefabs do not include `NetworkObject`, `FusionPlaceableObject`, or `CraftingTableStation`.
- [ ] Ensure ghost colliders are absent or disabled.
- [ ] Bind `CraftingTable -> GhostCraftingTable` and `Campfire -> GhostCampfire` on `PlaceableItemSystem.ghostPrefabs`.
- [ ] Verify ghost prefabs are not tagged/labeled as Fusion prefabs.
- [ ] Check Unity console for errors.

### Task 3: Verify

**Files:**
- No intended code changes.

- [ ] Run Unity MCP script validation for `PlaceableItemSystem.cs`.
- [ ] Run an editor diagnostic that confirms ghost prefabs do not have `NetworkObject` and that `FusionPlayer.prefab` has two ghost bindings.
- [ ] Check console errors.
- [ ] Commit intended files.
