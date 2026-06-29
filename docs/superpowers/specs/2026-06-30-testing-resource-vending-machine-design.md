# Testing Resource Vending Machine Design

## Goal

Add a scene testing helper named `vending_food` that lets the player dispense common crafting resources quickly during multiplayer testing.

The vending machine should help test crafting and placement flows without requiring repeated gathering.

## User Decisions

- Use a vending machine GameObject named `vending_food`.
- Provide four separate resource actions.
- Dispense amount is `x5` per tap.
- Resources:
  - Wood / Log -> `ItemType.Wood`
  - Fiber -> `ItemType.Fiber`
  - Rock -> `ItemType.Stone`
  - Cloth -> `ItemType.Cloth`

## Current Project Context

The project already has these relevant systems:

- `PlayerInteractionSystem` detects `Interactable` objects and calls interaction logic from the mobile `PICK` / interaction button.
- `PlayerInventory` owns local inventory slots and supports adding items by `ItemType`.
- `FusionPlayerInventory` handles networked pickup/drop/placement flows, but direct world spawn is unnecessary for this helper.
- Existing gameplay testing features should remain mobile-first and should not depend on keyboard-only input.

Context7 Photon Fusion documentation confirms that runtime `NetworkObject` spawning should be done through `Runner.Spawn`, and in Host/Server mode clients must request spawns through RPCs. This feature intentionally avoids network spawning because it is only a local inventory testing helper. It does not need to create persistent replicated world objects.

Unity MCP was used to verify the active Unity project/editor state and available project-specific Unity tools before designing the feature.

## Feature Scope

Add a simple testing vending machine in the `Gameplay` scene:

- GameObject name: `vending_food`.
- Add a visible vending-machine-like primitive object.
- Add a collider on the interactable layer.
- Add `Interactable` so the existing `PlayerInteractionSystem` can target it.
- Add a new script that opens a local UI panel when interacted with.
- The panel contains four buttons:
  - `WOOD x5`
  - `FIBER x5`
  - `STONE x5`
  - `CLOTH x5`
- Tapping a button adds the selected resource to the interacting player's `PlayerInventory`.
- The panel can be closed.

## Architecture

Create one focused script:

- `Assets/Scripts/Testing/TestingResourceVendingMachine.cs`

Responsibilities:

- Track the current interacting player.
- Build a small runtime UI if no panel reference is assigned.
- Show four resource buttons.
- Add the selected item to the current player's `PlayerInventory`.
- Hide the UI after close or when no player is available.

The script should not modify crafting recipes, item enum values, Fusion network state, or drop spawning.

## Interaction Flow

1. Player looks at or targets `vending_food` within interaction range.
2. Existing mobile interaction button becomes available through `PlayerInteractionSystem`.
3. Player taps interact.
4. `vending_food` opens a local vending UI panel.
5. Player taps one of four resource buttons.
6. Script calls `PlayerInventory.AddItem(itemType, 5)` for that player.
7. If inventory accepts fewer than 5, the script leaves remaining items undistributed and optionally logs or shows a short message.

## Networking Model

This helper is intentionally not a networked dispenser.

Rules:

- Do not call `Runner.Spawn`.
- Do not create or register a Fusion prefab.
- Do not replicate vending UI state.
- Do not mutate world state.
- Only the local interacting player's inventory receives items.

This keeps the feature low-risk and avoids introducing test-only replicated objects.

## Scene Object

Create or update a GameObject in `Assets/Scenes/Gameplay.unity`:

- Name: `vending_food`
- Components:
  - `Transform`
  - visible primitive renderers or child mesh primitives
  - `BoxCollider`
  - `Interactable`
  - `TestingResourceVendingMachine`
- Layer: the same layer currently used by interactable scene objects.

The visual can be simple: a rectangular vending-machine body with four small colored buttons or labels. The exact art is non-goal; usability for testing matters more.

## UI

The UI is local-only and runtime-generated unless a prefab/panel is assigned later.

Minimum UI requirements:

- Large enough buttons for mobile tapping.
- Four resource buttons.
- Close button.
- Panel appears only after interaction.
- Panel does not block existing inventory/crafting UI permanently.

Button labels:

- `WOOD x5`
- `FIBER x5`
- `STONE x5`
- `CLOTH x5`

## Error Handling

- If no player is currently interacting, buttons do nothing.
- If no `PlayerInventory` is found on the interactor, log a warning and keep the panel closed.
- If inventory is full, add as much as `PlayerInventory.AddItem` accepts and log/optionally display `Inventory Full`.
- If the scene already contains a `vending_food` object, update it instead of creating a duplicate.

## Non-Goals

This feature will not add:

- Currency or payment costs.
- Networked resource drops.
- Fusion network prefab registration.
- Persistent vending machine state.
- Save/load support.
- Item selection beyond Wood, Fiber, Stone, and Cloth.
- Production balancing.

## Validation

Required checks:

- Unity scripts compile with no new errors.
- `TestingResourceVendingMachine.cs` exists under `Assets/Scripts/Testing/`.
- `Gameplay.unity` contains exactly one GameObject named `vending_food`.
- `vending_food` has `Interactable`, `BoxCollider`, and `TestingResourceVendingMachine`.
- Interacting with `vending_food` opens a local panel.
- Each button adds exactly `x5` of the expected item when inventory has room:
  - Wood button adds `ItemType.Wood`.
  - Fiber button adds `ItemType.Fiber`.
  - Stone button adds `ItemType.Stone`.
  - Cloth button adds `ItemType.Cloth`.
- The UI can be closed.
- No Fusion prefab table changes are required.
- No new Unity console errors are introduced.
