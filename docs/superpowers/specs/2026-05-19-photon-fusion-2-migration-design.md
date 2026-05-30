# Photon Fusion 2 Migration Design

Date: 2026-05-19
Project: ProjectMultiplayer Unity co-op survival

## Goal

Migrate the multiplayer runtime from Unity Netcode for GameObjects and the custom VPS room directory to Photon Fusion 2. The first Photon version should prioritize stable room creation, joining, scene transitions, player spawning, movement, pickup/drop, inventory, survival, chest storage, and basic combat sync.

## Decision

Use Photon Fusion 2 in Shared Mode for V1.

This is the best fit for the current project because it removes the custom dedicated server deployment loop and room directory mismatch issues while staying more scalable and modern than Photon PUN 2. Fusion Dedicated Server remains a future option after V1 is stable, but it is too large for the first rewrite because it requires stricter server-authoritative versions of every gameplay system.

## Current Context

The project currently depends heavily on Unity Netcode for GameObjects. Existing multiplayer runtime scripts include `CoopNetworkBootstrap`, `OwnerDrivenNetworkTransform`, `NetworkPlayerOwnerSetup`, `NetworkInventoryBridge`, `NetworkSurvivalBridge`, and `NetworkAnimatorStateSync`. These use `NetworkObject`, `NetworkBehaviour`, `NetworkVariable`, `ServerRpc`, and `ClientRpc`.

The current architecture also uses a custom Python room directory service on the VPS and a dedicated Unity server. That setup has produced repeated issues around network config mismatch, scene object hashes, prefab registration, room stage state, and the need to rebuild/deploy server builds after gameplay code changes.

## Scope V1

V1 includes:

- Photon Fusion 2 setup and AppId configuration.
- Photon room create, join, leave, and waiting flow.
- Room code/session name based joining.
- Host/master-only Start Lobby and Start Forest actions.
- Fusion scene transitions from MainMenu to Gameplay and Environment.
- Fusion player spawning.
- Local owner camera, input, joystick, look, UI, and audio setup.
- Player movement and remote movement sync.
- Pickup and drop item sync.
- Player inventory owner-visible sync.
- Shared world item visibility.
- Shared chest/storage state.
- Survival stats sync for health, hunger, thirst, and injured state.
- Basic axe combat and swing animation sync.
- Basic tree/choppable interaction sync.
- Removal or disabling of old NGO runtime flow from Photon V1.

V1 excludes:

- Fusion Dedicated Server.
- Heavy anti-cheat.
- Complex public matchmaking.
- Persistent inventory between sessions.
- Player-to-player inventory looting.
- Complete host migration solution.
- Server database persistence.

## Architecture

### New Photon Layer

Create a separate Fusion runtime layer instead of patching the NGO layer in place. This keeps migration safer and makes it clear which runtime is active.

Core scripts:

- `PhotonFusionBootstrap`
- `PhotonFusionRoomController`
- `PhotonFusionSceneLoader`
- `PhotonFusionSessionState`

Player scripts:

- `FusionPlayerSpawner`
- `FusionPlayerAvatar`
- `FusionPlayerOwnerSetup`
- `FusionPlayerMovement`
- `FusionAnimatorSync`

Gameplay scripts:

- `FusionPlayerInventory`
- `FusionPickableItem`
- `FusionWorldItemSpawner`
- `FusionStorageChest`
- `FusionPlayerSurvival`
- `FusionPlayerCombat`
- `FusionTreeChoppable`

### Legacy Logic Reuse

Keep these as local gameplay/UI logic where practical:

- `FPSControllerMobile`, adapted to Fusion input authority.
- `PlayerInventory`, for slot/stack add/remove logic.
- `PlayerSurvivalSystem`, for health/hunger/thirst logic.
- `PlayerInteractionSystem`, for local raycast/detection logic.
- Inventory, hotbar, survival, and pickup UI scripts if they do not require NGO.
- `PickableItem` as item metadata, or replace with a small Fusion wrapper.

Disable these from Photon runtime:

- `CoopNetworkBootstrap`
- `RoomDirectoryClient`
- `NetworkPlayerOwnerSetup`
- `OwnerDrivenNetworkTransform`
- `NetworkInventoryBridge`
- `NetworkSurvivalBridge`
- `NetworkAnimatorStateSync`
- `CoopNetworkTestUI`
- NGO scene/prefab runtime configuration.

NGO package can remain installed during migration, but the Photon V1 runtime must not depend on NGO.

## Room Flow

`MainMenu` is the multiplayer entry point.

Create Room:

- Creates a Photon Fusion session using the room code/session name.
- The creator becomes the room/master authority for room controls.
- The player remains in the menu waiting/host panel.
- The scene must not automatically change to Gameplay.

Join Room:

- Joins an existing Fusion session by room code/session name.
- The player remains in the waiting panel until the active room scene changes.
- If the room is already in Gameplay or Environment, the player joins that active scene.

Start Lobby:

- Only available to the room creator/master client.
- Uses Fusion scene management to load `Gameplay` for all clients.

Start Forest:

- Only available to the room creator/master client.
- Uses Fusion scene management to load `Environment` for all clients.

## Scene Flow

- `MainMenu` has no active network player object.
- `Gameplay` is the office lobby / initial co-op scene.
- `Environment` is the forest survival scene.
- Scene changes must use Fusion scene manager, not local `SceneManager.LoadScene` calls.
- After a gameplay scene finishes loading, Fusion spawns player prefab at scene spawn points.
- Scene player prototypes should be removed or converted into spawn markers.

## Player And Movement

Create `FusionPlayer.prefab`, likely by duplicating `NetworkPlayer.prefab` and replacing NGO components.

The Fusion player prefab should include:

- Fusion `NetworkObject`.
- Fusion movement sync, either Fusion `NetworkTransform` or a custom networked transform if needed.
- `FusionPlayerOwnerSetup` for local-only camera/input/UI/audio.
- `FusionPlayerMovement` adapted from `FPSControllerMobile`.
- Inventory, survival, combat, and animator Fusion components.

Ownership model:

- Each player has input authority over their own player object.
- Only the local owner reads joystick/look/jump/pick/combat input.
- Remote players receive transform and visual state only.
- Local-only cameras and audio listeners are enabled only for the owner.

Movement behavior:

- `CharacterController` movement can remain owner-driven for V1.
- Gravity and jump stay local to the owner.
- Remote clients see smoothed Fusion transform updates.
- The old NGO owner correction/snapback system is not used.

## Inventory And Looting

Player inventory and world loot are separate concepts.

Player inventory:

- Private to the owning player in V1.
- The owner sees slot details in their UI.
- Other players do not need live access to another player inventory.
- Authority still validates pickup/drop/use transactions to prevent item duplication.

World loot and dropped items:

- Networked and visible to all players.
- Can be picked up by any player.
- When one player takes an item, the item despawns for everyone.
- Drop requests remove an item from the player inventory and spawn a networked item in the world.

Chest/storage:

- Shared and visible to relevant players.
- `FusionStorageChest` owns networked slot state.
- Chest transactions must be processed by one authority source.
- Two players interacting with the same chest must not duplicate items.

Player inventory looting by other players is out of V1. If needed later, dead/downed players can create a public loot bag container from their inventory.

## Pickup And Drop

Pickup flow:

1. Local player detects a `FusionPickableItem` through raycast/detection.
2. Player sends a pickup request via Fusion RPC/state authority path.
3. Authority validates item existence, distance, and inventory capacity.
4. Inventory owner state increases.
5. Item despawns for all players.
6. Owner receives feedback if pickup fails.

Drop flow:

1. Owner requests drop from a slot.
2. Authority removes item from inventory state.
3. Fusion spawns the matching item prefab in front of the player.
4. Item is visible and lootable by all players.

## Survival

`PlayerSurvivalSystem` can remain the local stat logic, wrapped by `FusionPlayerSurvival`.

Networked survival state:

- Health
- Hunger
- Thirst
- Injured state

The authority for the player updates survival drain and applies consumable effects. UI reads the local player's networked state. Other players only need exposed visual/animation state, not necessarily full UI values.

## Combat And Trees

`FusionPlayerCombat` handles axe swing input and replicated swing visuals.

V1 combat scope:

- Local owner triggers swing.
- Remote players see swing animation.
- Tree/choppable object validates hit/chop request.
- Tree/choppable state changes are networked.
- Complex damage/AI/PvP is out of scope.

## Animator Sync

Replace `NetworkAnimatorStateSync` with `FusionAnimatorSync`.

Sync only important state:

- Speed
- Move X/Y if needed
- Grounded
- Injured
- Swing trigger/state

Avoid syncing all animator parameters to reduce traffic.

## Error Handling

Create room failure:

- Show Photon status/error.
- Stay in MainMenu.

Join failure:

- Show room not found, room full, version mismatch, or connection error.
- Stay in MainMenu.

Scene load failure:

- Keep current scene.
- Show error status.

Pickup failure:

- Show item too far, inventory full, or item already taken.

Disconnect:

- Return to MainMenu.
- Clear Photon session state.
- Show disconnect reason.

## Testing Plan

Manual test matrix for V1:

- Create room stays in waiting panel and does not auto-load Gameplay.
- Join room enters waiting panel.
- Only creator/master can click Start Lobby.
- Start Lobby moves all connected clients to Gameplay.
- Two clients spawn at different spawn points.
- Analog moves only the local owner.
- Remote player movement is visible and smooth.
- Pickup by client A removes item for client B.
- Client A inventory increases after pickup.
- Drop item appears for all clients.
- Chest contents update for two clients.
- Survival UI changes after consumable use.
- Axe swing is visible to remote clients.
- Start Forest moves all clients to Environment.
- New joiner enters the active room scene.
- Disconnect returns to MainMenu cleanly.

## Implementation Order

1. Install and configure Photon Fusion 2.
2. Add Photon bootstrap and room create/join/leave flow.
3. Add Fusion scene loading from MainMenu to Gameplay.
4. Create `FusionPlayer.prefab` and player spawner.
5. Port local owner setup, movement, joystick, camera, and look bindings.
6. Add item prefab table and world item spawner.
7. Implement pickup/drop with Fusion.
8. Implement owner-visible player inventory sync.
9. Implement shared chest sync.
10. Implement survival sync.
11. Implement combat/tree sync.
12. Disable old NGO runtime flow in UI/scenes.
13. Run two-client manual test matrix.
14. Clean up obsolete room directory/VPS dependencies from Photon V1 path.

## Risks

- Full rewrite touches many prefabs and scenes.
- Shared Mode requires careful authority decisions for world objects.
- Inventory and chest transactions can duplicate items if not centralized.
- Keeping NGO and Fusion simultaneously can confuse scene references unless old runtime objects are disabled.
- Photon AppId/package setup may require manual Unity Editor configuration.

## Future V2 Options

After V1 is stable:

- Move to Fusion Host Mode or Dedicated Server.
- Add stronger validation and anti-cheat checks.
- Add Photon lobby room list/search.
- Add persistent inventory/database.
- Add dead player loot bags.
- Add host migration or reconnect handling.
- Optimize interest management and network traffic.
