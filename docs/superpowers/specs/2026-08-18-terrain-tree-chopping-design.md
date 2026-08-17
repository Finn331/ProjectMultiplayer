# Terrain Tree Chopping Design

## Goal

Make every Terrain tree in `Assets/Scenes/Environment.unity` choppable in multiplayer. When a player cuts a tree down, the tree disappears for all players, plays a falling animation using LeanTween, and produces shared wood drops that cannot be duplicated by other players.

## Context

The forest scene currently uses Terrain tree instances rather than placed `TreeChoppable` GameObjects. The existing axe system already supports chopping `TreeChoppable` components, and the existing Fusion inventory/drop path can spawn and claim wood drops. The missing piece is an adapter between axe hits and Terrain tree instances.

Context7 MCP was used for LeanTween API verification. The relevant API shape is `LeanTween.rotate(...)` with chained `.setEase(...)` and `.setOnComplete(...)`, which is suitable for a temporary visual tree proxy that falls over and then cleans itself up.

Context7 MCP was also used earlier for Unity Terrain/raycast guidance. The important constraint is that Terrain trees are not ordinary per-tree GameObjects, so treating every tree as a persistent collider/proxy would be costly for mobile.

## Chosen Approach

Use a runtime Terrain tree chopping service instead of pre-generating GameObjects for every tree.

When a player swings the axe and no normal `TreeChoppable` is hit, the axe system asks a Terrain-tree service for the best tree candidate in front of the player. If the candidate is valid, the player sends a Fusion request to state authority. State authority validates the tree, applies chop damage, and replicates the depletion event. On depletion, every client hides that Terrain tree instance, spawns a temporary visual proxy at the same position, animates the proxy falling with LeanTween, and the authoritative side spawns wood drops.

This keeps the forest mobile-friendly because only actively chopped trees get proxy GameObjects.

## Alternatives Considered

1. Pre-generate colliders/proxies for every Terrain tree.
   - Easier raycast targeting.
   - Rejected because the forest may contain many Terrain trees and this would add collider, Transform, and GameObject overhead on mobile.

2. Hide the Terrain tree and spawn wood without a visual proxy.
   - Lightest implementation.
   - Rejected because the requested falling-tree animation would be missing.

3. Convert the whole forest from Terrain trees to placed tree prefabs.
   - Gives full authoring control.
   - Rejected for this pass because it is a large art/scene migration and risks undoing recent forest mobile optimization work.

## Components

### Terrain Tree Registry

A scene-level registry scans all active Terrain objects in `Environment.unity` at runtime. For each Terrain tree instance it records:

- Terrain reference and Terrain instance index.
- Prototype prefab reference.
- World position, approximate height, and approximate trunk/chop radius.
- Stable tree id derived from Terrain identity and tree index.
- Runtime health/depleted state.

The registry should rebuild after scene load and should not create a GameObject for each tree. It should keep the data in arrays/lists and use simple distance/dot-product filtering for chop targeting.

### Axe Integration

`PlayerAxeCombat` remains the entry point for player swings. The existing `TreeChoppable` path stays intact. Terrain tree chopping is only attempted after normal hit detection and existing tree assist fail.

The terrain-tree candidate search should use:

- Player camera or axe direction.
- A max distance close to the current axe hit distance/fallback camera hit distance.
- A forward dot threshold so trees behind or far to the side are ignored.
- A nearest-valid-candidate preference.

This preserves current combat behavior and adds Terrain tree support as a fallback, not a rewrite.

### Fusion Authority

The local player with input authority requests a Terrain tree chop. State authority validates:

- The tree id exists in the current registry.
- The tree is not already depleted.
- The requesting player is close enough to the tree.
- The damage value is within the allowed per-chop range.

State authority applies damage and sends the replicated depletion event when health reaches zero. The tree removal and falling proxy must happen on all clients from the same tree id, so every player sees the same tree disappear.

### Terrain Tree Removal

When a tree is depleted, the registry hides it from the runtime Terrain data on each client. The implementation should avoid permanently modifying source assets. Runtime changes are acceptable for the loaded scene/session only.

The removal strategy should keep an original snapshot of each Terrain's tree instances and a runtime hidden-tree id set. When a tree is depleted, the affected Terrain receives a rebuilt runtime `treeInstances` array that excludes hidden ids, then refreshes Terrain tree rendering. Stable ids are always resolved against the original snapshot, not against the shortened runtime array.

### Falling Proxy Animation

On depletion, each client instantiates a temporary proxy from the Terrain tree prototype at the tree's recorded world position and scale. The proxy exists only for visual feedback and should not be networked.

The proxy animation uses LeanTween:

- Pick fall direction from the chopper-to-tree vector when available; otherwise use tree forward or a deterministic fallback from tree id.
- Create a temporary pivot GameObject at the recorded tree base, instantiate the prototype as a child, and rotate the pivot so the tree appears to fall from the base instead of spinning around its visual center.
- Use an ease-out or ease-in-back style curve for a quick, readable fall.
- Use `.setOnComplete(...)` to schedule cleanup or leave the fallen proxy briefly before destroying it.

If a prototype cannot be instantiated safely, the system still removes the Terrain tree and spawns drops, then logs a warning instead of blocking chopping.

### Wood Drops

Wood drops should use the existing Fusion drop/inventory path. The authoritative side spawns wood after depletion. The drop position should be grounded near the original tree base, with existing deterministic scatter behavior when possible.

The default item is `ItemType.Wood`, which is defined in `Assets/Scripts/Object/Item/PickableItem.cs`. The existing `Assets/Assets/Prefabs/Wood.prefab` is the preferred shared drop prefab because it already has `PickableItem`, `FusionPickableItem`, and Fusion components.

## Data Flow

1. Player presses attack while axe is equipped.
2. `PlayerAxeCombat` runs existing hit checks.
3. If no normal tree hit is found, it queries the Terrain tree registry for a target.
4. The local player sends a Fusion chop request with the tree id, hit position, and damage.
5. State authority validates and applies damage.
6. On depletion, all clients receive the same tree id removal event.
7. Each client hides the Terrain tree and plays the LeanTween falling proxy animation.
8. State authority spawns shared wood drops.
9. Players pick up wood through the existing Fusion inventory path.

## Error Handling

- If the Terrain registry is unavailable, the axe swing behaves like it does today and logs only when debug logging is enabled.
- If the tree id cannot be found on state authority, the request is ignored.
- If a client receives a depletion event for an already-hidden tree, the event is ignored to keep replication idempotent.
- If the tree prototype prefab is missing, removal and drops still proceed without the falling proxy.
- If LeanTween is unavailable at compile time, implementation must stop at compile verification and either add/confirm the package or replace the animation call only after approval.

## Tuning Defaults

- Tree health: default to 3 axe hits per Terrain tree.
- Max chop distance: default to 2.25 meters from the player or axe origin.
- Wood amount: default to 3 total `Wood` per depleted tree, using current inventory stack behavior.
- Proxy lifetime: default to 6 seconds after the fall animation completes.
- Fall duration: default to 1.1 seconds.

Final numeric values should be serialized fields so they can be adjusted in the Unity Inspector without code changes.

## Non-Goals

- No persistent save/load of chopped Terrain trees across separate game sessions.
- No stump replacement in this pass.
- No regrowth system in this pass.
- No migration of Terrain trees into authored prefab forests.
- No changes to crafting recipes or wood economy beyond making Terrain trees produce wood.

## Verification

- Compile scripts and check Unity console for errors.
- Run existing EditMode tests that cover forest/player systems.
- Add or update an EditMode self-test for the registry id mapping and tree depletion idempotency.
- Runtime test in `Environment.unity` through the MainMenu/Fusion flow:
  - Swing axe at a Terrain tree.
  - Tree disappears for the local player.
  - LeanTween falling proxy plays.
  - Wood drop appears and can be picked up.
  - Repeated swings do not duplicate drops.
- Manual multiplayer validation with a second client when available. If a second client is not available during implementation verification, record that limitation and still validate the host/Fusion RPC path in solo-host mode:
  - One client chops a tree.
  - Other client sees the same tree removed and the falling proxy animation.
  - Wood pickup is shared/claimed once.
