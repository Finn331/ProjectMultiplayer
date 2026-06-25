# Placeable Ghost Prefabs Design

## Goal

Make placement previews use item-shaped 3D ghost prefabs instead of the generic cube preview.

## Design

Use separate local-only ghost prefabs for placeable items:

- `GhostCraftingTable`
- `GhostCampfire`

Ghost prefabs contain only visual geometry. They must not include `NetworkObject`, `FusionPlaceableObject`, `CraftingTableStation`, gameplay scripts, or enabled colliders.

`PlaceableItemSystem` owns a small serializable mapping from `ItemType` to ghost prefab and preview bounds. When placement mode starts or the selected item changes, it instantiates the matching ghost prefab locally. The final placed object still spawns through `FusionPlayerInventory` and registered Fusion network prefabs.

This follows the Fusion rule that persistent network objects use `Runner.Spawn`, while local-only preview objects are ordinary Unity GameObjects.

## Behavior

- Selecting `CraftingTable` and pressing `PLACE` shows a table-shaped ghost.
- Selecting `Campfire` and pressing `PLACE` shows a campfire-shaped ghost.
- Ghost renderers receive the existing valid/invalid preview materials.
- Ghost colliders are disabled if present.
- Ghost objects are destroyed when placement mode exits.
- If a ghost prefab is missing, `PlaceableItemSystem` falls back to the previous cube preview.

## Acceptance Criteria

- Crafting Table preview is shaped like the crafting table object.
- Campfire preview is shaped like the campfire object.
- Ghost previews are not networked and do not interact with crafting stations or colliders.
- Final placement still uses the networked placed prefabs.
- Existing Crafting Table and Campfire placement still works.
