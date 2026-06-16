# Fusion Player Owner Stabilization Design

## Goal

Stabilize multiplayer player ownership so each client controls only its own player while remote players remain visual-only. This should reduce abnormal controls, duplicate camera/audio behavior, and accidental remote input conflicts during multiplayer QA.

## Current Context

- The project now uses Photon Fusion Shared Mode for gameplay sessions.
- `FusionPlayer.prefab` has `FusionPlayerOwnerSetup`, but its serialized arrays are currently empty.
- `FusionPlayerMovement` already gates movement with Fusion state authority and binds mobile input only for the local authority player.
- `FPSControllerMobile` still exists on the Fusion player prefab for first-person camera stabilization, renderer visibility, and legacy movement fallback. It returns early from movement when a Fusion `NetworkObject` is present, but it still owns local first-person camera/audio behavior.
- The older Netcode `NetworkPlayerOwnerSetup` has a useful pattern: auto-discover local-only camera/audio, toggle owner-only components, and enforce a single active audio listener.

## Problems To Solve

1. Remote player instances must not keep active cameras, audio listeners, mobile input handlers, or local-only HUD objects.
2. Local player instances must have exactly one active first-person camera/audio listener path.
3. Fusion visual sync components must remain enabled for remote players so animation/combat visuals still replicate.
4. The prefab should be self-healing enough that missing serialized owner-only arrays do not silently leave remote cameras/listeners active.
5. Diagnostics should make regressions obvious before multiplayer QA.

## Proposed Approach

Enhance `FusionPlayerOwnerSetup` into the central authority for Fusion local/remote player activation. It will auto-populate missing references from child components, apply the owner state on `Spawned`, `FixedUpdateNetwork` when authority state changes, `OnEnable`, `OnDisable`, and `Despawned`, and expose a clear diagnostic surface for prefab verification.

`FusionPlayerOwnerSetup` should manage only local-only presentation and input helpers. It should not disable network state replication or remote visual components.

## Owner-Only Components

The owner-only set should include:

- `FPSControllerMobile`, for first-person camera stabilization and local-only camera/audio toggling.
- `PlayerInteractionSystem`, if present on the Fusion player.
- `PlayerInventoryUI`, `GridInventoryUI`, `DraggableInventoryUI`, `MobileHotbarUI`, `HotbarConsumeUI`, and related player UI objects if they are children of the player prefab.
- Child `Camera` components.
- Child `AudioListener` components.
- Explicit owner-only GameObjects configured on the prefab.

`FusionPlayerMovement` should stay enabled on all instances because it is a `NetworkBehaviour` and already gates local authority logic internally. Disabling it on remote proxies would risk breaking remote animation state sources or lifecycle callbacks.

`FusionAnimatorSync`, `PlayerAnimatorDriver`, `FusionPlayerCombat`, `FusionPlayerInventory`, `FusionPlayerSurvival`, and other Fusion network components should remain enabled unless a specific component is proven local-only.

## Data Flow

1. Fusion spawns a player object in Shared Mode.
2. `FusionPlayerOwnerSetup.Spawned()` checks `Object.HasStateAuthority`.
3. The setup enables local-only components for the owner and disables them for remote proxies.
4. The owner keeps the first active child camera/audio listener and disables other audio listeners that would conflict.
5. Remote proxies keep renderer, animator, combat visual sync, inventory network state, and transform sync active.

## Error Handling

- If owner-only arrays are empty, auto-discover cameras/audio listeners and known local-only behaviours at runtime.
- If no local camera is found for the owner, log one warning with the player object path.
- If more than one audio listener is active after owner setup, disable extra listeners and log a concise warning.
- If local-only behaviours are missing, skip them safely; not every prefab variant needs every UI component.

## Diagnostics And Verification

Add Unity MCP diagnostics that check:

- `FusionPlayer.prefab` has `FusionPlayerOwnerSetup`.
- `FusionPlayerOwnerSetup` can auto-discover child cameras and audio listeners.
- The prefab does not rely on empty serialized arrays for local-only safety.
- Network visual components remain enabled for remote proxies.
- There is no more than one active audio listener in the loaded gameplay scene after owner setup.

Manual QA should cover:

- Host and client can move only their own player.
- Remote player movement is visible but cannot be controlled locally.
- Only the local player's camera is active on each machine.
- No duplicate audio listener warning appears after both players join.
- Inventory/hotbar/chest UI still works for the local player.

## Out Of Scope

- Rewriting movement input architecture.
- Changing lobby/session creation behavior.
- Adding death/respawn mechanics.
- Replacing `FPSControllerMobile` entirely.
- Building new UI screens.

## Acceptance Criteria

- `FusionPlayerOwnerSetup` automatically handles missing owner-only arrays on `FusionPlayer.prefab`.
- Remote players have no active cameras or audio listeners.
- Local player retains working camera, look, jump, hotbar, inventory, interaction, and combat input.
- Fusion animation and combat visuals still replicate for remote players.
- Unity validation reports zero compile errors for touched scripts.
- A diagnostic command returns PASS for owner-only prefab wiring and active listener safety.
