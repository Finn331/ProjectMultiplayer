# Chest Split Stack Quantity Design

## Purpose

Allow players to move only part of an item stack between their inventory/hotbar and a chest without changing the existing full-stack drag behavior.

## Approved UX

- Normal drag/drop keeps the current behavior: it moves the full stack.
- Split transfer is intentional and starts with a long-press on a stackable slot.
- Long-press does nothing when the source stack amount is `1`.
- Long-press on a stack amount greater than `1` starts split drag mode.
- Dropping a split drag onto a valid target opens a quantity dialog before any transaction is applied.
- The dialog shows the source item and current maximum quantity.
- The default quantity is `ceil(sourceAmount / 2)` clamped to the target capacity.
- The dialog supports numeric entry plus `-`, `+`, `Half`, `Max`, `Cancel`, and `Confirm` actions.
- `Cancel` closes the dialog and leaves inventory and chest contents unchanged.

## Transfer Scope

- Inventory slot to chest slot supports split transfer.
- Hotbar slot to chest slot supports split transfer.
- Chest slot to inventory slot supports split transfer.
- Chest slot to hotbar slot supports split transfer.
- Inventory panel to hotbar and hotbar to inventory panel keep full-stack behavior for this change.

## Components

### `StorageChestUI`

Owns the user interaction state only:

- Detects long-press on player inventory slots, hotbar slots, and chest slots.
- Records the pending split source, source slot, target slot, and direction.
- Uses the existing pointer/raycast drop target discovery.
- Opens the quantity dialog only after a valid split source is dropped onto a valid transfer target.
- Calls amount-aware transaction methods after confirmation.
- Does not directly mutate inventory or chest contents.

### `StorageChestSlotUI`

Continues to describe slot identity for UI events:

- Slot kind: player inventory, player hotbar, or chest.
- Slot index as used by the current UI.
- Source stack display and drag metadata.

It may expose whether a slot can start split mode, but it should not execute transfers.

### `StorageChest`

Owns local and non-Fusion chest transactions:

- Adds amount-aware deposit and take APIs.
- Validates source slot, target slot, source amount, and target capacity.
- Removes only the confirmed amount from the source.
- Adds only the accepted amount to the target.
- Fails safely without losing items if validation fails.

### `FusionStorageChest`

Owns Fusion multiplayer chest transactions:

- Adds amount-aware request/RPC paths for deposit and take.
- Preserves the existing Shared Mode state-authority handoff.
- Stores pending transaction data with `amount` included.
- Revalidates and clamps amount on the state authority before applying changes.
- Fails safely if source contents, target contents, or authority changes make the transaction invalid.

### `PlayerInventory`

Uses existing amount-aware inventory operations:

- `RemoveItemFromSlot(int slotIndex, int amount, out ItemType removedItemType)` for source removal.
- `AddItemToSlot(ItemType itemType, int amount, int slotIndex)` for target insertion.

No broad inventory rewrite is required.

## Quantity Rules

- Minimum confirmed quantity is `1`.
- Maximum confirmed quantity is the lower of:
  - source stack amount.
  - target slot capacity for that item.
- Empty target slots can accept up to the item max stack.
- Same-item target stacks can accept up to their remaining stack capacity.
- Different-item target slots reject the transfer.
- Full target slots reject the transfer.
- If the source or target changes while the dialog is open, confirmation revalidates against current state and either clamps or fails safely.

## Data Flow

1. Player long-presses a stack with amount greater than `1`.
2. `StorageChestUI` marks the drag as split mode.
3. Player drops onto a valid chest/inventory/hotbar target.
4. `StorageChestUI` computes the current maximum transferable quantity.
5. If maximum is less than `1`, the transfer is rejected and no dialog is shown.
6. Otherwise, the quantity dialog opens with default `ceil(sourceAmount / 2)` clamped to maximum.
7. Player confirms a quantity.
8. `StorageChestUI` calls the amount-aware `StorageChest` or `FusionStorageChest` method.
9. The transaction owner validates again and applies the transfer atomically.
10. UI refreshes from inventory/chest state change events.

## Error Handling

- Invalid source slot: reject transaction, no item changes.
- Invalid target slot: reject transaction, no item changes.
- Requested amount less than `1`: reject transaction.
- Requested amount greater than source amount: clamp to source amount before final target-capacity validation.
- Target capacity lower than requested amount: clamp to capacity if capacity is at least `1`; otherwise reject.
- Fusion authority not owned locally: request authority and replay the pending amount-aware transaction after handoff.
- Fusion handoff fails or object despawns: discard pending transaction and leave items unchanged.

## Testing

Automated Unity diagnostics should cover:

- Long-press split mode ignores single-item stacks.
- Split deposit from inventory to chest moves only the confirmed amount.
- Split deposit from hotbar to chest moves only the confirmed amount.
- Split take from chest to inventory moves only the confirmed amount.
- Split take from chest to hotbar moves only the confirmed amount.
- Dialog maximum respects empty slots, same-item partial stacks, different-item slots, and full slots.
- Cancel leaves all slots unchanged.
- Fusion amount-aware deposit and take preserve Shared Mode authority handoff.
- Fusion rejects stale or invalid pending split transactions without losing items.

Manual multiplayer checks should cover:

- Two players using the same chest while one has a quantity dialog open.
- Split deposit after another player fills the target chest slot.
- Split take after another player changes the source chest slot.
- Normal full-stack drag still works in all existing directions.

## Non-Goals

- Splitting items between player inventory and hotbar without involving a chest.
- Adding keyboard modifier controls for desktop.
- Changing max stack sizes or item definitions.
- Redesigning the existing chest layout.
