# Local Player Body Visibility Design

## Goal

Make the local player camera stop seeing the player's own head and upper torso while preserving the parts that should remain visible in first person.

The local player should still see:

- Hands and arms.
- Legs.
- Waist / hips area.

The local player should not see:

- Head.
- Hair / face accessories.
- Chest.
- Stomach / upper torso.

Other players must still see the full character model. When the local player looks at remote players, those remote players must also remain full-body.

## Current Project Context

The player prefab is `Assets/Assets/Prefabs/FusionPlayer.prefab`.

Unity MCP prefab inspection found the model under `FusionPlayer/Player Prototype` with these relevant renderer children:

- `Ch28_Body`
- `Ch28_Eyelashes`
- `Ch28_Hair`
- `Ch28_Hoody`
- `Ch28_Pants`
- `Ch28_Sneakers`

The rig hierarchy includes `mixamorig10:Hips`, spine bones, arm bones, leg bones, neck, and head. This means the model supports local visual treatment without changing the network object itself.

The project already has owner/local setup patterns:

- `FusionPlayerOwnerSetup` enables owner-only behaviours, cameras, listeners, and objects.
- `FusionPlayerMovement` uses Fusion local authority checks before local input handling.
- `FPSController` has an older first-person renderer hiding pattern that stores renderer state and restores it later.

Context7 Photon Fusion documentation confirms that local player checks such as `Object.HasInputAuthority` are appropriate for local-only representation logic. Since this feature is visual-only and does not change gameplay state, it must not use networked properties or RPCs.

## Design

Add a new local-only visibility component to the Fusion player prefab.

Suggested script name:

- `FusionLocalBodyVisibility`

The component will be a `NetworkBehaviour` so it can reliably evaluate Fusion authority after spawn.

Responsibilities:

- Detect whether this prefab instance belongs to the local player.
- Hide configured renderers only on the local player instance.
- Leave remote/proxy instances unchanged.
- Store original renderer state before modifying it.
- Restore original state when the component is disabled, despawned, or no longer local.

## Authority Rule

The component will use local Fusion authority to decide whether to apply hiding.

Preferred rule:

- Apply local body hiding when `Object != null && Object.HasInputAuthority`.

This matches the intent: only the client that owns the player input should see the first-person local body treatment.

If the current project mode uses shared-mode ownership patterns where existing local camera setup depends on `HasStateAuthority`, the implementation may support an explicit fallback that mirrors the project's existing local owner logic. Any fallback must remain local-only and must not mutate networked state.

## Renderer Strategy

Primary strategy: manual renderer references.

The component will expose serialized renderer arrays such as:

- `hideForLocalPlayer`
- `keepForLocalPlayer` or optional notes/comments for editor clarity

Initial target renderers to hide locally:

- `Ch28_Hair`
- `Ch28_Eyelashes`
- `Ch28_Hoody`

Renderers to keep visible:

- `Ch28_Pants`
- `Ch28_Sneakers`

`Ch28_Body` requires extra care because it may contain arms, hands, waist, head, and/or torso in a single skinned mesh. The implementation must not blindly disable `Ch28_Body` if doing so removes the hands or waist that the user wants visible.

## Fallback Strategy

If `Ch28_Body` is one combined mesh and hiding it removes required visible parts, the first implementation will avoid disabling it. It will still hide separate head/upper clothing/accessory renderers.

If that is not visually sufficient, the next safe fallback is a local-only body mask approach, such as:

- Local-only duplicate/variant mesh that contains only arms, hands, legs, and hips.
- Bone-scale or renderer-material masking only if it can be proven not to affect remote players or animation quality.
- Camera layer culling only if a dedicated first-person visual layer is needed later.

The first implementation should remain minimal and reversible.

## Data Flow

1. Fusion spawns `FusionPlayer`.
2. `FusionLocalBodyVisibility.Spawned()` runs.
3. The component checks whether the object is locally owned.
4. If local, it stores original renderer states and applies local hiding.
5. If remote, it restores or leaves all renderers unchanged.
6. If authority state changes, despawn happens, or the component disables, it restores original renderer states.

## Non-Goals

This feature will not:

- Change player health, movement, inventory, combat, revive, or crafting.
- Change networked data.
- Spawn or despawn network objects.
- Hide local body parts from other players.
- Rebuild the player model unless the existing renderer layout cannot meet the requested result.

## Validation

Implementation should be validated with these checks:

- Unity compile has no errors.
- Local player does not see head/hair/chest/stomach blocking the camera.
- Local player can still see hands/arms, legs, and waist/hips as much as the current mesh supports.
- Remote players remain full-body when viewed by the local player.
- The local player remains full-body when viewed from another client.
- Disabling/despawning the player restores renderer states cleanly.

## Risks

The main risk is mesh granularity. If `Ch28_Body` contains both hidden and visible requested parts, renderer-level hiding alone cannot perfectly remove only the torso while preserving hands and hips. The design intentionally treats this as a fallback case rather than risking a broad hide that removes too much of the local player model.
