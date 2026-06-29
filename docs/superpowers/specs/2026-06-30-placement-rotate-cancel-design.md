# Placement Rotate & Cancel Design

## Goal

Add **rotate** and **cancel** buttons to the existing placement mode so the player can rotate ghost previews and exit placement without spawning.

## Current State

- `PlaceableItemSystem` has one button `placeButton` that toggles between `EnterPlacementMode()` and `ConfirmPlacement()`.
- `UpdatePreview()` sets ghost rotation to `Quaternion.Euler(0f, playerY, 0f)` and does not expose manual rotation.
- No cancel button; exiting placement requires switching away from the hotbar item.

## Feature Scope

- Add one local `rotateButton` serialized field and one `cancelButton`.
- In placement mode, show/hide/bind both buttons alongside `placeButton`.
- Rotate increments ghost preview Y rotation by 45° per tap.
- Cancel immediately exits placement mode without spawning.
- Buttons resolved via `FindButtonByName("rotate")` and `FindButtonByName("cancel")` fallbacks.
- Scene buttons "rotate" and "cancel" already exist or can be added; if missing, fallback to placement-only UI without those buttons.

## Implementation Plan

Modify only `PlaceableItemSystem.cs`:

1. Add `rotateButton` and `cancelButton` serialized `Button` fields.
2. Add `currentPreviewRotation` float to track accumulated manual rotation.
3. In `RefreshButton()`: show/hide/bind rotate + cancel in placement mode.
4. `RotatePreview()`: increment `currentPreviewRotation` by 45°, wrap 360°.
5. `CancelPlacement()`: call `ExitPlacementMode()`.
6. In `UpdatePreview()`: combine player Y rotation with `currentPreviewRotation`.
7. In `EnterPlacementMode()`: reset `currentPreviewRotation = 0`.
8. In `ExitPlacementMode()`: reset `currentPreviewRotation = 0`.
9. In `ResolveButtonReference()`: resolve rotate/cancel buttons.

No scene changes unless buttons "rotate" or "cancel" are not found in canvas; then skip binding.

## Non-Goals

- No network state for rotation preview.
- No continuous rotation slider or drag.
- No placement preview mesh beyond existing ghost.
- No cancel confirmation dialog.

## Validation

- Unity compile with no errors.
- In placement mode, rotate button increments ghost rotation by 45°.
- Cancel button exits placement mode without spawning.
- Place button still works at final rotation.
