# Minecraft-Style Chest Design

Date: 2026-06-12

## Goal

Replace the current text/list-based chest UI with a Minecraft-style drag-and-drop chest interface. The new UI should let the player move items between their inventory and an opened chest by dragging slots, while keeping the existing multiplayer chest authority model intact.

## Scope

Implement now:

- Side-by-side chest UI for mobile/landscape use.
- Player inventory grid on the left.
- Chest grid on the right.
- Drag full stacks from player inventory to chest.
- Drag full stacks from chest to player inventory.
- Slot icon and amount display using the existing item icon database.
- Existing close behavior and auto-close by distance.
- Split-stack-ready transaction shape, without adding split input yet.

Do not implement now:

- Long-press split stack.
- Shift-drag split stack.
- Quantity dialog.
- Sorting/filtering.
- Storage model rewrite.

## Current Context

The project already has:

- `StorageChest` for legacy/NGO chest flow.
- `FusionStorageChest` for Photon Fusion multiplayer chest state.
- `StorageChestUI` that opens from player interaction and currently creates a text/list panel with `Store` and `Take` buttons.
- `PlayerInventory` with slot-level methods such as `GetSlotItemType`, `GetSlotAmount`, `RemoveItemFromSlot`, `AddItemToSlot`, and `FindPreferredInventorySlot`.
- `ItemIconDatabase` for item icons.
- Existing inventory/hotbar drag logic using Unity `EventSystem.current.RaycastAll`.

Context7 Unity documentation confirms the UI should use Unity pointer/drag/drop event callbacks and EventSystem raycasts to identify drop targets. This matches existing project patterns in `DraggableInventoryUI` and `MobileHotbarUI`.

## Recommended Approach

Use the existing storage APIs and replace the chest UI interaction layer.

`StorageChestUI` becomes a side-by-side runtime grid UI. It owns drag state, creates slot views, resolves drop targets, and delegates actual item movement to `StorageChest` or `FusionStorageChest`.

This avoids rewriting multiplayer storage state and keeps authority rules in `FusionStorageChest`.

## UI Design

The chest panel appears when a player interacts with a chest.

Layout:

- Left panel: player inventory slots.
- Right panel: chest slots.
- Header shows chest name and used/total slots.
- Close button remains available.

Each slot shows:

- Item icon when occupied.
- Stack count when occupied.
- Empty visual when no item is present.

Interaction:

- Drag a player inventory slot onto a chest slot to deposit the full stack.
- Drag a chest slot onto a player inventory slot to take the full stack or as much as fits.
- Dragging onto incompatible/full slots leaves both source and target unchanged unless existing storage logic can accept a partial stack.

## Data Flow

Deposit flow:

1. User begins drag from player inventory slot.
2. User releases over a chest slot.
3. `StorageChestUI` calls the active chest deposit API.
4. Legacy chest path calls `StorageChest.TryRequestStore(playerInventory, playerSlot, chestSlot)`.
5. Fusion chest path calls `FusionStorageChest.RequestDepositToChest(playerNetworkObject, playerSlot, chestSlot)`.
6. Chest validates distance, source slot, target slot, stack compatibility, and capacity.
7. Chest state changes and `ChestChanged` refreshes UI.

Take flow:

1. User begins drag from chest slot.
2. User releases over a player inventory slot.
3. `StorageChestUI` calls the active chest take API.
4. Legacy chest path calls `StorageChest.TryRequestTake(playerInventory, chestSlot, targetPlayerSlot)`.
5. Fusion chest path calls `FusionStorageChest.RequestTakeFromChest(playerNetworkObject, chestSlot, targetPlayerSlot)`.
6. Chest validates distance and target capacity.
7. Chest state changes and `ChestChanged` refreshes UI.

## Components

### `StorageChestUI`

Responsibilities:

- Build the side-by-side runtime panel.
- Create and refresh player inventory slots.
- Create and refresh chest slots.
- Track drag source type and source index.
- Resolve drop targets via EventSystem raycasts.
- Delegate deposit/take requests to the active chest.
- Keep current close and distance auto-close behavior.

### Chest Slot View Helper

Add a small helper component, likely `StorageChestSlotUI`, to runtime slot objects.

Responsibilities:

- Store slot owner kind: player inventory or chest.
- Store slot index.
- Hold icon/count references.
- Forward pointer/drag events back to `StorageChestUI`.

### `FusionStorageChest`

Keep the existing NetworkArray model and RPC authority flow. Only add minimal overloads if implementation needs amount-explicit calls for future split-stack support.

### `StorageChest`

Keep the existing local/legacy storage behavior. Only add minimal amount-explicit overloads if needed.

## Error Handling

- Invalid source slot: ignore the drop and refresh UI.
- Invalid target slot: ignore the drop and refresh UI.
- Different item type in target slot: reject unless current chest logic can stack it.
- Full target stack: reject or partially accept according to existing chest logic.
- Player too far from chest: reject and auto-close if distance exceeds allowed range.
- Slot changed before network request arrives: authoritative chest rejects the request.

## Testing

Use Unity MCP diagnostics and script validation.

RED diagnostics before implementation:

- `StorageChestUI` does not expose grid drag slot behavior.
- Full-stack inventory-to-chest transfer through drag target is not available.
- Full-stack chest-to-inventory transfer through drag target is not available.

GREEN diagnostics after implementation:

- Local `StorageChest` deposit moves an item from player slot to chest slot.
- Local `StorageChest` take moves an item from chest slot to player inventory slot.
- Incompatible target slot does not delete or duplicate items.
- `StorageChestUI` creates player and chest slot UI metadata.
- `StorageChestUI`, `StorageChest`, and `FusionStorageChest` validate with zero script errors.
- `Gameplay` scene validates with no missing scripts or broken prefabs.
- Unity console has no errors or warnings after verification.

## Open Decisions

All decisions are resolved for this phase:

- Transfer mode: hybrid-ready, but full-stack only now.
- Layout: side-by-side.
- Split stack input: deferred.
