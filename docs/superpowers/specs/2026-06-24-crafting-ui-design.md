# Crafting UI Design

## Purpose

Add a mobile-friendly crafting interface that starts simple, supports the existing bandage recipe, and can grow into crafting table recipes without replacing the UI later.

## Goals

- Add a `Crafting` mode/tab inside the existing inventory UI.
- Show craftable recipes as cards with output, ingredients, owned counts, and a `Craft` button.
- Keep simple crafting available without a crafting table.
- Prepare for crafting table recipes that become available only near a crafting table.
- Avoid keyboard-only interaction because the game targets mobile controls.

## Non-Goals

- Do not add a full crafting table implementation in this first pass.
- Do not create a complex ScriptableObject recipe database unless implementation proves it is needed.
- Do not redesign the entire inventory/hotbar system.
- Do not add network-shared crafting stations or team inventories yet.

## Player Flow

The player opens the existing inventory UI and can switch between `Items` and `Crafting`.

In `Crafting`, recipe cards show the output item, required ingredients, how many of each ingredient the player owns, and whether the recipe can currently be crafted. The first recipe is `Bandage`, using the existing cost of `2 Fiber + 1 Cloth -> 1 Bandage`.

For simple crafting, the player does not need to stand near any object. They can open inventory, switch to `Crafting`, and press the card's `Craft` button.

For future crafting table support, a mobile `CRAFT` button appears near the hotbar or mobile control area only while the local player is near a crafting table. Pressing this button opens the inventory directly to `Crafting` and enables crafting table recipes for that context. There is no keyboard prompt for crafting table use.

## UI Design

The inventory panel gains a small tab/header area with two controls:

- `Items`
- `Crafting`

The existing item list and drop/next behavior remain in the `Items` view. The `Crafting` view replaces the list area with recipe cards.

Each recipe card contains:

- Output name, such as `Bandage`.
- Output amount, such as `x1`.
- Ingredient rows, such as `Fiber 2/2` and `Cloth 1/1`.
- A `Craft` button.
- A disabled or blocked state when requirements are not met.

Recommendation for first implementation: disable the `Craft` button when ingredients are missing or the player is downed, and keep the ingredient rows visible so the player sees why crafting is unavailable.

## Crafting Data Model

Crafting should move from a bandage-only UI path toward a reusable recipe model.

A recipe needs these fields:

- Recipe id or display name.
- Output item type.
- Output amount.
- One or more ingredient item types and amounts.
- Crafting context requirement.

Initial contexts:

- `Simple`: always available to the local player.
- `CraftingTable`: available only when the local player has activated a nearby crafting table context.

The first pass can define recipes in code or serialized inspector fields. The important boundary is that UI works against a recipe list and does not depend directly on a single `TryCraftBandage()` method.

## Components

### Crafting System

Owns recipe evaluation and craft execution for the local player inventory.

Responsibilities:

- Resolve the local `PlayerInventory`.
- Expose available recipes for a requested crafting context.
- Report whether a recipe can currently be crafted.
- Remove ingredients, add output, and roll back ingredients if output cannot be added.
- Show short feedback messages through the existing pickup/info UI.

The existing `BandageCraftingSystem` can be refactored or wrapped to become this general crafting system while preserving the current bandage recipe.

### Inventory Crafting UI

Extends the existing inventory UI with a `Crafting` view.

Responsibilities:

- Switch between `Items` and `Crafting` views.
- Render recipe cards for the current context.
- Refresh when inventory changes.
- Call the crafting system when the player presses `Craft`.
- Keep UI local-authority only, matching existing inventory UI behavior.

### Crafting Table Access Button

Future component for crafting table proximity.

Responsibilities:

- Detect whether the local player is near a crafting table.
- Show a mobile `CRAFT` button near the hotbar/mobile controls while nearby.
- Open inventory directly to `Crafting` with `CraftingTable` context active.
- Hide or disable table recipes when the player leaves range.

This can be implemented after simple crafting UI works.

## Multiplayer Behavior

Crafting operates on the local player's inventory only. Remote players cannot craft from another player's inventory.

The UI should only run for the local authority player, following the same pattern as `PlayerInventoryUI`. If the player object does not have local authority, crafting UI should be disabled.

No shared crafting table inventory is included in this design.

## Error Handling

- Missing ingredients: disable the `Craft` button and show ingredient counts in the card.
- Inventory full: craft fails safely and returns removed ingredients, matching the current bandage crafting behavior.
- Downed player: crafting is blocked and the card button is disabled.
- Table context lost: crafting table recipes disappear or become disabled; simple recipes remain available.

## Verification Plan

- With no ingredients, open `Inventory > Crafting` and confirm the `Bandage` card shows missing `Fiber` and `Cloth`.
- Pick up `2 Fiber` and `1 Cloth`, then confirm the `Bandage` card enables its `Craft` button.
- Press `Craft` and confirm `Fiber` and `Cloth` decrease while `Bandage` increases by `1`.
- Fill inventory enough to force output failure and confirm ingredients are not lost.
- Put the player into downed state and confirm crafting is blocked.
- Later, near a crafting table, confirm the mobile `CRAFT` button opens inventory directly to the `Crafting` tab.

## Open Implementation Notes

- The current `PlayerInventoryUI` is runtime-generated and has existing object names such as `Inventory UI` and `Inventory Toggle Button`; implementation must avoid creating these UI objects in `MainMenu` scene edit mode.
- The first implementation should keep scene/prefab diffs small and prefer script-driven UI changes.
- The existing keyboard `C` bandage crafting path can remain temporarily if it does not conflict, but the UI path should be the primary mobile-friendly path.
