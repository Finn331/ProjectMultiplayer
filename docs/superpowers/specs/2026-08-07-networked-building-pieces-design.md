# Networked Building Pieces Design

## Goal

Make Wall, Floor, Roof, and Door placement fully synchronized through Photon Fusion while keeping the current snap-to-grid placement feel.

Players should be able to:

- Place building pieces from hotbar on one client and have every player see the same piece.
- Join an active room and receive the existing building pieces from the Fusion session snapshot.
- See building health changes synchronized across clients.
- Demolish or damage a building piece through state-authoritative requests.

This is session-only persistence. Building pieces live while the Photon room/session lives. They do not save to disk or cloud after the room closes.

## Current Project Context

The project already has the local building workflow:

- `PlaceableItemSystem.cs` handles local placement preview, snap-to-grid, proximity snap, and building rotation snapping.
- `FusionPlayerInventory.cs` sends placement requests to state authority through `RPC_RequestPlace`.
- Non-building placeables use `Runner.Spawn` through `TryGetPlaceablePrefab`.
- Building items currently fall back to `PlaceBuildingPiece`, which creates a local-only `GameObject` with `new GameObject`.
- `BuildingPiece.cs` stores piece type and health locally, creates primitive visuals, shows damage tint, and destroys itself locally.
- `PlayerInteractionSystem.cs` detects `BuildingPiece`, shows a local HP bar, and currently calls `Demolish()` directly.

The intended architecture is to remove the local-only building fallback and route building pieces through the same Fusion spawning pattern as other multiplayer world objects.

Context7 MCP was requested for Photon Fusion documentation, but the `context7` MCP server is not exposed as an available tool/resource in this session. The design therefore follows the existing project Fusion patterns already present in `FusionPlayerInventory`, `FusionStorageChest`, `FusionPlaceableObject`, and `CampfireCooking`. The implementation plan must still verify Fusion API usage through Unity compile checks and available project package sources.

## Selected Approach

Use one generic networked building prefab.

The prefab represents any building piece. Its `BuildingPiece` component receives networked state for type, grid position, rotation, health, and placer. Each client builds the matching primitive visual locally from that networked state.

This fits the current procedural building implementation and avoids four separate prefab pipelines while the art is still prototype-quality.

## Networked Building Prefab

Add one prefab, for example:

- `Assets/Assets/Prefabs/NetworkBuildingPiece.prefab`

It should include:

- `NetworkObject`
- `BuildingPiece`
- A root `BoxCollider`, configured by `BuildingPiece` after initialization

It should not include a prebuilt wall/floor/roof/door mesh. `BuildingPiece` remains responsible for procedural model creation so the same prefab can serve every building item.

It should not include `NetworkTransform` in the first version. Building pieces are static after spawn, and their initial position/rotation are provided through `Runner.Spawn` plus explicit grid/rotation state.

## BuildingPiece Networked State

Convert `BuildingPiece` from `MonoBehaviour` to `NetworkBehaviour`.

Networked fields:

- `PieceTypeValue`: int enum value for `BuildingPieceType`.
- `Health`: float, initialized to `DefaultMaxHealth`.
- `GridX`, `GridY`, `GridZ`: int grid coordinates.
- `RotationIndex`: int, 0-3 for 0/90/180/270 degrees.
- `Placer`: `PlayerRef`, the player who placed the piece.

Runtime fields remain local:

- mesh renderer/material cache
- generated primitive child model
- root collider reference
- last visual state key to prevent duplicate model creation

`Spawned()` builds or refreshes the model from networked state. `Render()` updates material tint and can rebuild the visual if the state was initialized after spawn.

## Placement Flow

The local preview remains local-only.

Flow:

1. Player selects `WallItem`, `FloorItem`, `RoofItem`, or `DoorItem`.
2. `PlaceableItemSystem` previews the snap position and 90-degree building rotation locally.
3. Player confirms placement.
4. `FusionPlayerInventory.RequestPlaceFromSlot` sends slot, item type, position, and rotation to `RPC_RequestPlace`.
5. State authority validates the request: player alive, item still in slot, distance, ground, and collision.
6. State authority removes one item from the slot only after validation passes.
7. State authority spawns `NetworkBuildingPiece` with `Runner.Spawn`.
8. State authority initializes `BuildingPiece` with piece type, grid position, rotation index, health, and placer.
9. If spawn or initialization fails, state authority restores the consumed item.

Building items should no longer use `new GameObject` in multiplayer placement.

## Validation Rules

State authority must re-check placement instead of trusting the client preview.

Rules:

- Item type must match the current inventory slot.
- Player must be alive and not downed.
- Requested position must be within `maxPlacementDistance` of the player.
- Ground raycast must pass using the existing placement surface mask/tolerance.
- Blocking overlap must use the building-specific bounds and the same contact-skin behavior needed for adjacent pieces.
- Rotation must be normalized to 90-degree increments for building pieces.
- Piece type must map only from the four building item types.

Client preview and server validation must share the same building bounds and rotation normalization rules. If full helper reuse is not practical, duplicate constants must be kept in one small clearly named helper to avoid drift.

## Damage And Demolish Flow

Damage and demolish must be state-authoritative.

Flow:

1. Local player looks at a `BuildingPiece`; HP bar uses networked `HealthRatio`.
2. Player holds/taps the existing interaction for demolish.
3. `PlayerInteractionSystem` calls a request method on `BuildingPiece` instead of directly destroying it.
4. If the local peer has state authority, the piece applies the change immediately.
5. Otherwise, the piece sends an RPC to state authority.
6. State authority validates range where player object data is available, applies damage or demolish, and updates `Health`.
7. When `Health <= 0`, state authority drops refund resources and despawns the network object with `Runner.Despawn`.

Initial version can keep one demolish action. General combat/tool damage can reuse the same request path later.

## Refund Drops

Refund drops stay at 50% of craft cost, matching the current local behavior.

The state authority must spawn drops through the existing Fusion drop path:

- Reuse `FusionPlayerInventory.SpawnTreeDropsFromData` from the state-authoritative player inventory when it supports the item.
- Avoid client-local primitive drops for networked demolish unless no Fusion runner exists.

If reliable refund spawning requires more wiring than expected, stop implementation and ask before changing the refund requirement. The default target remains networked refund drops.

## Session-Only Persistence

No save/load system is added.

Expected behavior:

- Existing room members see building pieces as soon as they are spawned.
- Late joiners receive spawned building pieces from Fusion while the session remains alive.
- Pieces disappear when the Photon room is shut down.

Non-goals:

- Cloud persistence.
- Local save persistence.
- Offline base restoration.
- Ownership permissions/locks.
- Team/base authorization.
- Repair mechanics.
- Upgrading tiers/materials.

## Files

Expected changes:

- Update `Assets/Scripts/PhotonFusion/BuildingPiece.cs` to become a networked, state-authoritative building object.
- Update `Assets/Scripts/PhotonFusion/FusionPlayerInventory.cs` to spawn the generic building prefab instead of local-only objects.
- Update `Assets/Scripts/Player/Survival/PlayerInteractionSystem.cs` to request networked demolish/damage.
- Update `Assets/Scripts/Player/Survival/PlaceableItemSystem.cs` only to expose/reuse building bounds helpers if server validation would otherwise duplicate placement dimensions.
- Add `Assets/Assets/Prefabs/NetworkBuildingPiece.prefab` and register it in Fusion prefab configuration.

## Risks

Prefab registration is the highest risk. Fusion spawned prefabs must be valid network prefabs; Unity scene/prefab setup must be verified in the editor, not assumed from C# alone.

Another risk is duplicate visual/collider creation if networked state arrives after `Spawned()`. `BuildingPiece` must clear and rebuild generated children/colliders idempotently.

Server validation can drift from local preview if it uses different bounds or overlap filtering. The implementation should reuse the same dimensions and contact-skin assumptions used by the current placement preview.

Refund drops may need additional wiring because current drop spawning helpers are player-inventory-centric. The implementation should prefer existing Fusion drop behavior and keep local fallback only for non-networked/editor situations.

## Validation

Required checks:

- Unity scripts compile with no new errors.
- `NetworkBuildingPiece` prefab has a valid `NetworkObject` and `BuildingPiece`, and does not require `NetworkTransform`.
- Fusion prefab configuration contains the building prefab or the scene/player bindings reference a valid network prefab object.
- Host/client can place Wall, Floor, Roof, and Door from hotbar.
- Both players see each placed piece at the same position and rotation.
- Late joiner sees existing pieces while the room is still active.
- Adjacent wall/floor/roof/door placement remains flush and not falsely blocked.
- Non-building placeables still spawn as before.
- Demolish/damage from one player updates the HP bar/tint on all clients.
- Destroyed building pieces despawn for all clients.
- Refund resources spawn once, not once per client.
- Dead or downed players cannot place or demolish pieces.
