# Forest Mobile Optimization Design

## Goal

Improve `Assets/Scenes/Environment.unity` performance on low-end and mid-range phones without adding a graphics settings panel yet. The scene should keep the forest playable while avoiding the current expensive far-distance tree rendering.

## Context

Context7 Unity Manual confirms Unity occlusion culling uses baked editor data, static occluders/occludees, and camera-side occlusion queries at runtime. Dynamic objects can be occludees but cannot be baked as occluders.

UnityMCP inspection of `Environment.unity` showed:

- `Main Camera.useOcclusionCulling` is already enabled.
- The scene has 9 Terrain objects.
- Terrain tree distance is `5000` on every Terrain, which is too high for mobile.
- There are only 2 active MeshRenderers, no LODGroups, and no current occluder/occludee static flags.

## Approach

Use a fixed mobile-safe forest profile in the scene now, not runtime graphics quality levels. Later, when a settings panel exists, these values can become the default `Low` or `Medium` preset.

## Changes

1. Apply mobile-safe Terrain settings to every Terrain in `Environment.unity`:
   - Reduce `treeDistance` from `5000` to `250`.
   - Keep details conservative; no detail prototypes currently exist.
   - Keep terrain height/position/spawn points unchanged.

2. Apply mobile-safe camera settings:
   - Keep `useOcclusionCulling = true`.
   - Reduce `farClipPlane` from `1000` to `500` if the active scene camera exists.

3. Prepare scene occlusion where it is useful:
   - Mark existing solid MeshRenderers as `Occludee Static`.
   - Mark only reasonably large solid MeshRenderers as `Occluder Static`.
   - Do not mark Terrain trees as occluders; foliage is not a strong occluder and will not solve mobile forest cost by itself.

4. Add an editor self-test:
   - Fails if any Terrain in `Environment.unity` has `treeDistance` above `350`.
   - Fails if scene camera occlusion culling is disabled.
   - Verifies `farClipPlane` is no higher than `600`.

## Non-Goals

- No MainMenu graphics settings panel.
- No runtime quality switching.
- No tree prefab replacement or impostor generation in this pass.
- No destructive changes to terrain layout, terrain data, spawn points, inventory, networking, or UI.

## Verification

- Run the new forest mobile optimization self-test.
- Check Unity console for compile/runtime errors.
- Use UnityMCP to inspect Terrain tree distances and camera occlusion/far clip after applying the profile.
