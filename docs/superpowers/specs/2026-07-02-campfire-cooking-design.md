# Campfire Cooking V1 Design

## Goal
Add campfire cooking ala The Forest: place raw meat on campfire, auto-cook, pickup cooked meat.

## Scope
- New item types: `RawMeat`, `CookedMeat`
- `RawMeat` recipe: simple craft or vending machine dispense for testing
- `CookedMeat`: restores 40 hunger via `ConsumableItemCatalog`
- `PlacedCampfire.prefab`: add `Interactable` + new script `CampfireCooking.cs`
- 4 cooking slots, timer 20 seconds per slot
- Visual: random raw→cooked pair from 4 pasang (Drumstick/Steak/FishFillet/WholeBird)

## Files
- Create: `Assets/Scripts/PhotonFusion/CampfireCooking.cs`
- Modify: `Assets/Scripts/Object/Item/PickableItem.cs` (add `RawMeat`, `CookedMeat`)
- Modify: `Assets/Scripts/Player/Survival/ConsumableItemCatalog.cs` (cooked meat effect)
- Modify: `Assets/Scripts/Testing/TestingResourceVendingMachine.cs` (optional: add raw meat button)
- Modify: `Assets/Assets/Prefabs/PlacedCampfire.prefab` (Interactable + CampfireCooking)
- Modify: `Assets/Scripts/Player/Survival/BandageCraftingSystem.cs` (raw meat recipe)

## Architecture
`CampfireCooking.cs` — NetworkBehaviour:
- 4 cooking slots, each has: state (empty/cooking/cooked), timer, rawPrefab, cookedPrefab
- `TryPlaceRawMeat(PlayerInventory)` — called when player interacts with campfire while holding RawMeat
- `FixedUpdateNetwork()` — advance timers, swap raw→cooked when done
- `TryPickupCooked(PlayerInventory, int slot)` — add CookedMeat to inventory
- Spawn 3D food GameObjects as children of campfire transform for visual
- State authority manages timers and slot state

## Non-Goals
- Cooking pan, recipe book, burnt food, seasoning
- Multi-recipe cooking
- Fuel (campfire burns forever for now)
