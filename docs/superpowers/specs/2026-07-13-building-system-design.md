# Building System — Design

**Date**: 2026-07-13  
**Status**: Approved

## Summary
Snap-to-grid building system: Wall, Floor, Roof, Door. Craft first → place from hotbar → networked via Photon Fusion → has HP → demolishable.

---

## Building Piece Types

| Type | Primitive | Size | Craft Cost |
|---|---|---|---|
| Wall | Cube | 1 x 2 x 0.2 | 10 Wood |
| Floor | Cube (flattened) | 1 x 0.1 x 1 | 8 Wood |
| Roof | Cube (45° tilted) | 1 x 0.1 x 1.5 | 12 Wood |
| Door | Cube | 0.8 x 2 x 0.1 | 15 Wood + 2 Iron |

Grid cell: 1x1 meter. Snap position = round world position to nearest integer (Vector3Int).

---

## ItemType Additions
`WallItem`, `FloorItem`, `RoofItem`, `DoorItem` — each maps to a placeable prefab.

---

## BuildingPiece (NetworkBehaviour)

### Networked State
| Field | Type | Default |
|---|---|---|
| `Health` | float | 100 |
| `MaxHealth` | float | 100 (const) |
| `PieceType` | int (enum) | set at spawn |
| `GridPosition` | Vector3Int | set at spawn |
| `RotationY` | int (0/90/180/270) | set at spawn |

### Methods
- `TakeDamage(float amount)` — RPC to StateAuthority, reduces Health, destroys at 0
- `Repair(float amount)` — RPC to StateAuthority, restores Health
- `Demolish()` — RPC to StateAuthority, despawns and drops 50% resources

### Render
- Color tint based on HP ratio: green (>66%) → yellow (33-66%) → red (<33%)

---

## Placement System

### Ghost Preview
- Shows at snapped grid position
- Green = valid (no overlap), Red = invalid (overlap)
- Overlap detection: `Physics.OverlapBox` at grid cell
- Rotation: 0°/90°/180°/270° (Y axis)

### Place Confirm
1. Player confirms placement
2. Client sends RPC to StateAuthority
3. State authority spawns `BuildingPiece` NetworkObject
4. Consumes item from player inventory

---

## Demolish Interaction
- Look at building piece → show HP bar UI
- Hold interact (1.5s) → demolish
- Drops 50% of craft cost back to player inventory

---

## Files
- **New**: `Assets/Scripts/PhotonFusion/BuildingPiece.cs`
- **New**: `Assets/Scripts/Building/BuildingPieceType.cs`
- **Update**: `Assets/Scripts/Player/Survival/PlaceableItemSystem.cs` — snap-to-grid
- **Update**: `Assets/Scripts/Player/Survival/BandageCraftingSystem.cs` — 4 new recipes
- **Update**: `Assets/Scripts/Player/Survival/PlayerInteractionSystem.cs` — demolish
- **Update**: `Assets/Scripts/Object/Item/PickableItem.cs` — 4 new ItemType
- **Update**: `Assets/Scripts/PhotonFusion/FusionPlayerInventory.cs` — prefab bindings
