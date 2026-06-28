# Storage Chest Craft And Place Design

## Goal

Add a craftable and placeable Storage Chest that fits the existing survival loop.

Players should be able to:

- Craft a Storage Chest near a Crafting Table.
- Receive the chest as an inventory/hotbar item.
- Place the chest in the world using the existing mobile `PLACE` flow.
- Open the placed chest when nearby.
- Deposit and take items with state synchronized across multiplayer clients.

## Current Project Context

The project already has most storage building blocks:

- `FusionStorageChest.cs` provides a Photon Fusion storage chest with networked item type and amount arrays.
- `StorageChestUI.cs` and `StorageChestSlotUI.cs` provide chest UI and slot interactions.
- `StorageChest.cs` is the older Netcode storage implementation and should not be extended for the Fusion path.
- `PlaceableItemSystem.cs` handles local ghost preview and placement requests.
- `FusionPlayerInventory.cs` validates placement requests and spawns placeable prefabs.
- `FusionPlaceableObject.cs` stores placeable item metadata.
- Craftable placeables already exist for `CraftingTable` and `Campfire`.

Context7 Photon Fusion documentation confirms the intended networking model:

- Networked state should be changed by state authority.
- Clients/input authority should send requests to state authority.
- Persistent world objects should be spawned with `Runner.Spawn`.
- Visual/local preview objects should remain local-only and non-networked.

## Feature Scope

Add a new `StorageChest` item type and make it craftable/placeable.

The first version will use the existing storage UI and Fusion chest state. It will not add locks, ownership permissions, item filters, chest naming UI, or persistence across sessions.

## Recipe

Initial recipe:

- `Wood x12`
- `Stone x2`
- `Fiber x4`
- Output: `StorageChest x1`
- Crafting context: `CraftingTable`

This makes Storage Chest a Crafting Table unlock and keeps it slightly more expensive than the table itself.

## Assets And Prefabs

Add a networked placed prefab:

- `Assets/Assets/Prefabs/PlacedStorageChest.prefab`

It should include:

- `NetworkObject`
- `NetworkTransform`
- `FusionPlaceableObject`
- `FusionStorageChest`
- Collider for interaction/range detection
- Simple visual mesh assembled from primitives or existing materials

Add a local-only ghost prefab:

- `Assets/Assets/Prefabs/GhostStorageChest.prefab`

It should include:

- Visual mesh matching the storage chest shape closely enough for placement preview
- Renderer(s)
- Collider(s) disabled or absent
- No `NetworkObject`
- No `FusionPlaceableObject`
- No `FusionStorageChest`
- No Fusion prefab label

## Data Flow

Crafting flow:

1. Player opens Crafting Table recipes.
2. Player crafts Storage Chest.
3. Crafting backend validates ingredients.
4. `StorageChest` item is added to inventory/hotbar.

Placement flow:

1. Player selects Storage Chest in hotbar.
2. `PlaceableItemSystem` enters placement mode.
3. Local ghost preview updates on valid ground.
4. Player taps `PLACE`.
5. `FusionPlayerInventory.RequestPlaceFromSlot` sends the requested position and rotation.
6. State authority validates item, range, ground, and collision.
7. State authority removes one Storage Chest item from the slot.
8. State authority spawns `PlacedStorageChest` with `Runner.Spawn`.
9. `FusionPlaceableObject.Initialize` stores the item type and placer.
10. All clients receive the spawned chest and transform through Fusion.

Storage flow:

1. Player interacts with nearby placed chest.
2. `FusionStorageChest.TryInteract` opens `StorageChestUI` on the local player.
3. Deposit/take actions request changes from the chest.
4. Chest state authority validates range, player inventory, chest slot, item type, and amount.
5. `FusionStorageChest` mutates its networked arrays.
6. UI refreshes when chest networked state changes.

## Authority And Validation

Placement validation remains in `FusionPlayerInventory`.

Chest inventory validation remains in `FusionStorageChest`.

Rules:

- Clients never directly mutate chest networked slots unless they have state authority.
- Requests target state authority through existing Fusion RPC/request patterns.
- Range checks use the player object position against the chest position.
- Invalid slots, invalid item types, empty source stacks, full destination stacks, and out-of-range requests are rejected.

## UI

Reuse `StorageChestUI`.

The first version will not add a dedicated `OPEN CHEST` mobile button unless current interaction already cannot open Fusion chests reliably. If the existing `PlayerInteractionSystem` supports interacting with `FusionStorageChest`, reuse it. If it only supports non-Fusion `StorageChest`, add the smallest interaction bridge needed.

## Non-Goals

This feature will not add:

- Chest locks or permissions.
- Chest ownership restrictions.
- Session persistence/save-load.
- Chest renaming.
- Item sorting.
- Item filters.
- New art beyond a simple prototype chest mesh.
- Remote UI; chest UI remains local to the interacting player.

## Risks

The main risk is integration overlap between the older `StorageChest` Netcode path and the Fusion `FusionStorageChest` path. The implementation should prefer the Fusion path and avoid extending the Netcode chest for multiplayer survival.

Another risk is interaction discovery. If current interaction code does not detect `FusionStorageChest`, the chest may spawn correctly but not open. The implementation must verify interaction flow after prefab wiring.

## Validation

Required checks:

- Unity scripts compile with no new errors.
- `ItemType.StorageChest` exists and does not break existing item mappings.
- Storage Chest recipe appears only in Crafting Table context.
- Crafting consumes `Wood x12`, `Stone x2`, `Fiber x4` and adds one Storage Chest item.
- Storage Chest can be selected in hotbar and placed with `PLACE`.
- Host and client see the chest in the same position.
- Placed chest has `NetworkObject` and `NetworkTransform`.
- Ghost chest has no `NetworkObject`.
- Nearby player can open chest UI.
- Depositing an item updates chest contents for other clients.
- Taking an item updates chest contents and player inventory correctly.
- Downed/dead players cannot place a Storage Chest.
