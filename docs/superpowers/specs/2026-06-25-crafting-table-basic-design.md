# Crafting Table Basic Design

## Goal

Add a Minecraft-like crafting table loop to the existing survival crafting system. Players craft a Crafting Table as an inventory item, place it from the hotbar, then use the placed table to unlock table-only recipes such as Axe and Campfire. Placeable outputs remain inventory items until the player deliberately places them.

## Scope

This spec covers the first functional version of Crafting Table Basic:

- Craftable `CraftingTable` item.
- Placeable `CraftingTable` world object.
- Crafting table proximity interaction through mobile-first `CRAFT` UI.
- `CraftingContext.CraftingTable` recipe filtering.
- Craftable `Campfire` item from table context.
- Placeable `Campfire` world object.
- Multiplayer-safe placement request validation and network spawning.

This spec does not cover durability, moving placed objects, picking placed objects back up, cooking, heat, fuel, storage, advanced building snapping, or recipe unlock progression.

## Gameplay Flow

Players do not start with a free Crafting Table in the world. They must craft one from the simple crafting context.

The simple crafting context includes:

- `Bandage`
- `Crafting Table`

The `Crafting Table` recipe is:

- `Wood x8`
- `Stone x2`
- `Fiber x2`
- Output: `CraftingTable x1`

Crafting the table adds `CraftingTable` to inventory or hotbar. It does not spawn a world object.

The player selects `CraftingTable` in the hotbar and places it through the placement system. After a valid placement, the item is consumed and a placed Crafting Table appears in the world for all players.

When a player approaches a placed Crafting Table, a mobile `CRAFT` button appears. Pressing it opens the inventory directly to the `Crafting` tab using `CraftingContext.CraftingTable`.

The table crafting context initially includes:

- `Axe`
- `Campfire`

Crafting `Campfire` adds `Campfire x1` to inventory or hotbar. It does not spawn automatically. The player places it from the hotbar with the same placement flow used by Crafting Table.

## Data Model

Add new item types at the end of the existing `ItemType` enum:

- `CraftingTable`
- `Campfire`

These values must be appended only. Existing enum values must not be reordered because Unity scene and prefab serialization stores enum values numerically.

Use the existing `CraftingRecipe` model and `CraftingContext` enum. Recipe definitions should stay explicit and small:

- `Bandage`: `CraftingContext.Simple`
- `CraftingTable`: `CraftingContext.Simple`
- `Axe`: `CraftingContext.CraftingTable`
- `Campfire`: `CraftingContext.CraftingTable`

The current `BandageCraftingSystem` class remains the crafting backend for compatibility with existing prefabs. It should continue to expose recipe filtering by context and should not overwrite inspector-authored recipes unnecessarily.

For the first version, placeable item mapping can be implemented directly in the player placement component rather than with new ScriptableObject assets. The required mapping is:

- `CraftingTable` -> placed Crafting Table network prefab
- `Campfire` -> placed Campfire network prefab

If placeable count grows later, this mapping can be extracted into reusable data assets.

## Components

### PlayerInventoryUI

`PlayerInventoryUI` needs a current crafting context.

Default context is `CraftingContext.Simple`.

When opened from a Crafting Table station, the UI switches to `CraftingContext.CraftingTable` and opens the `Crafting` tab. When the player is no longer in table context, the UI returns to `CraftingContext.Simple` or hides table-only recipes.

### CraftingTableStation

Placed Crafting Tables get a station/proximity component. It is responsible for identifying the table as a valid crafting station. It does not own inventory, recipes, or crafting logic.

Players can detect nearby stations and use them to activate the table crafting context.

### Crafting Station UI Trigger

A player-side component detects the nearest usable `CraftingTableStation`. When a station is in range and the local player is not downed, it shows a mobile `CRAFT` button.

Pressing `CRAFT` opens the existing inventory UI to the `Crafting` tab with `CraftingContext.CraftingTable`.

### PlaceableItemSystem

A player-side placement component controls placeable hotbar items.

Responsibilities:

- Detect whether the selected hotbar slot contains a placeable item.
- Show or hide a mobile `PLACE` button.
- Enter and exit placement mode.
- Create a local-only ghost preview.
- Color the preview green for valid placement and red for invalid placement.
- Submit placement requests to the authority-safe network path.
- Cancel placement mode if the player becomes downed or deselects the item.

The ghost preview is never networked and never changes inventory.

### Placed Crafting Table Prefab

The placed Crafting Table prefab should be a networked object visible to all players. It needs visual geometry, a collider or placement bounds, and `CraftingTableStation`.

### Placed Campfire Prefab

The placed Campfire prefab should be a networked object visible to all players. For this version, it only needs visual geometry and a collider or placement bounds. Cooking, fuel, heat, and damage are out of scope.

## Multiplayer and Fusion Authority

Fusion placement should follow the documented pattern where local input can request an action, but persistent network objects are spawned by the side with state authority.

The local player may:

- Select a placeable item.
- Show and move a ghost preview.
- Press `PLACE`.
- Send a placement request.

The authority path must validate before consuming inventory or spawning anything.

Recommended request model:

- RPC source: input authority.
- RPC target: state authority.
- Request data: item type, selected slot reference, requested position, requested rotation, and request id.

The authority side validates:

- Player is not downed.
- Player still owns the selected item.
- Item type is placeable.
- Requested position is within allowed placement distance from the player.
- Requested position is on valid ground.
- Placement bounds do not overlap blocked geometry, another placed object, or another player.

If validation succeeds:

- Consume one item from the inventory.
- Spawn the corresponding network prefab through `Runner.Spawn`.
- Return or replicate success state as needed for UI feedback.

If validation fails:

- Do not consume the item.
- Do not spawn anything.
- Show a short failure message such as `Tidak bisa ditempatkan di sini`.

Placed objects do not need input authority for this MVP unless later gameplay requires owner-driven input. If ownership tracking is useful, the spawned object can store the placing player's `PlayerRef` separately.

## UI and Controls

The feature is mobile-first. Keyboard and mouse controls may exist as fallback, but mobile buttons must be available.

Inventory keeps the existing tabs:

- `Items`
- `Crafting`

Opening inventory normally uses `CraftingContext.Simple`.

Opening inventory from a nearby Crafting Table uses `CraftingContext.CraftingTable` and selects the `Crafting` tab.

The mobile `CRAFT` button appears only when:

- A placed Crafting Table station is nearby.
- The local player is alive/not downed.
- The local player has usable input authority.

The mobile `PLACE` button appears only when:

- The selected hotbar item is placeable.
- The local player is alive/not downed.

Placement mode shows a local preview in front of the player. The preview uses valid/invalid color feedback.

Suggested short feedback strings:

- `Butuh Crafting Table`
- `Bahan kurang`
- `Tidak bisa ditempatkan di sini`
- `Pilih item placeable`
- `Crafted: Campfire`

## Downed State Rules

Downed players cannot craft through the table and cannot place objects.

If the player becomes downed while placement mode is active, placement mode is cancelled and the ghost preview is removed.

The station `CRAFT` button and placement `PLACE` button should be hidden or disabled while downed.

## Error Handling

Inventory should only be consumed after authority validation succeeds.

Invalid placement must not consume items.

If a local preview is valid but authority rejects the request, the player keeps the item and receives failure feedback.

If the selected item changes or disappears while placement mode is active, placement mode cancels.

If the player walks away from a Crafting Table while the crafting UI is open, table-only recipes should become unavailable by returning to `CraftingContext.Simple` or by refreshing the recipe list to remove table recipes.

## Acceptance Criteria

- Player can craft `CraftingTable x1` from `Wood x8 + Stone x2 + Fiber x2` in simple crafting context.
- Crafting `CraftingTable` adds an item to inventory or hotbar and does not spawn a world object.
- Selecting `CraftingTable` in hotbar shows `PLACE`.
- Valid Crafting Table placement consumes one item and spawns a placed table visible to all players.
- Invalid Crafting Table placement consumes nothing and spawns nothing.
- Nearby placed Crafting Table shows mobile `CRAFT`.
- Pressing `CRAFT` opens inventory to `Crafting` tab with table context.
- Table context shows `Axe` and `Campfire` recipes.
- Simple context still shows `Bandage` and `CraftingTable` recipes.
- Player can craft `Campfire x1` from table context.
- Crafting `Campfire` adds an item to inventory or hotbar and does not spawn a world object.
- Selecting `Campfire` in hotbar shows `PLACE`.
- Valid Campfire placement consumes one item and spawns a placed campfire visible to all players.
- Invalid Campfire placement consumes nothing and spawns nothing.
- Downed players cannot craft through table context.
- Downed players cannot place Crafting Table or Campfire.
- Existing Bandage craft, consume, drop, and revive flows still work.
- MainMenu remains unaffected by gameplay inventory or crafting UI.

## Verification Plan

Before claiming implementation complete:

- Compile Unity scripts without errors.
- Run targeted diagnostics for enum mapping and recipe context filtering.
- Run targeted diagnostics for craft output inventory behavior.
- Run targeted diagnostics for placement validation and invalid-placement no-consume behavior.
- Manually test in `Gameplay` scene: craft table, place table, open table crafting, craft campfire, place campfire, test invalid placement, test downed blocked actions.
- If a multiplayer host/client check is feasible, verify both players see placed Crafting Table and Campfire. If it is not feasible in the current environment, report it as residual manual QA.
