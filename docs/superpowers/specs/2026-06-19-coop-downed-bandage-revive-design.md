# Co-op Downed Bandage Revive Design

## Goal

Add a cooperative revive mechanic that gives multiplayer players a clear reason to protect and help each other. A player who reaches zero health should become downed instead of instantly dying, and a teammate should be able to revive them by spending a bandage.

## Current Context

- Gameplay uses Photon Fusion Shared Mode.
- `FusionPlayer.prefab` already has Fusion player movement, owner setup, inventory sync, survival sync, combat sync, and animation sync components.
- `PlayerSurvivalSystem` owns local health/survival stats and `FusionPlayerSurvival` mirrors those values for multiplayer state.
- Inventory and hotbar systems already exist, including pickable items, stackable inventory slots, and consumable handling.
- Existing interact systems can show prompts and trigger actions near interactable objects.
- The game already supports scene pickups, tree chopping/resource drops, storage chests, and co-op player spawning.

## Problems To Solve

1. Health reaching zero should not leave the player fully active.
2. Downed state must be visible and consistent for both host and client.
3. Downed players must not move, attack, interact, jump, consume items, or keep using tools.
4. Revive must require a real inventory cost: one `Bandage` from the reviver.
5. Revive must avoid double-consume, revive-from-distance, and revive-after-target-already-up cases.
6. Bandages need a small but useful survival loop: pickups plus crafting.

## Selected Approach

Use a downed state with hold-to-revive and bandage consumption.

When a player's health reaches zero, the player enters `Downed` state. The player remains downed indefinitely; there is no bleed-out timer and no automatic respawn. Another player can revive them by staying within revive range, holding the revive input for five seconds, and having at least one `Bandage` in inventory. When the revive completes, one bandage is consumed from the reviver and the downed target is restored to 25% max health.

Revive progress is local UI state for the reviver. The final revive action is validated against synced state before applying health restoration and bandage consumption.

## Downed State

Downed players should:

- Have movement, jump, look-driven combat actions, interaction, inventory use, consumable use, and tool use blocked.
- Remain physically present in the world so teammates can find and revive them.
- Keep remote visual replication active so other clients can see the downed player.
- Show a local status message such as `Downed - Waiting for revive`.

Downed players should not:

- Automatically respawn.
- Lose inventory.
- Consume survival items.
- Continue chopping, attacking, picking up items, opening chests, or reviving others.

If a downed animation is already available, the mechanic may use it. For MVP, disabling controls and exposing clear UI state is enough.

## Bandage And Crafting

Add three item types:

- `Fiber`
- `Cloth`
- `Bandage`

Recipe:

```text
2 Fiber + 1 Cloth -> 1 Bandage
```

Bandages can be obtained from:

- Direct bandage pickups placed in the scene.
- Crafting from inventory using Fiber and Cloth.

Fiber and Cloth can initially be scene pickups. Later, Fiber can come from bushes/plants and Cloth can come from containers, camps, enemies, or dismantled clothing.

## Revive Interaction

When a living player is near a downed teammate:

- If the reviver has a bandage, show `Hold Interact to Revive (Bandage x1)`.
- If the reviver has no bandage, show `Need Bandage to Revive`.
- On mobile, show a revive button while the target is in range.
- While the revive input is held, show a five-second progress bar.
- Cancel revive if the input is released or the reviver leaves revive range.

MVP cancel conditions:

- Release input.
- Reviver leaves range.
- Target is no longer downed.
- Reviver becomes downed.
- Reviver no longer has a bandage when completion is attempted.

## Networking Model

Synced state should include:

- `IsDowned` for each player.
- Current health and max health, through the existing survival sync path or a small extension to it.

Local-only state should include:

- Revive prompt visibility.
- Revive progress timer.
- Mobile revive button visibility.

The final revive should validate:

- Target is still downed.
- Reviver is not downed.
- Reviver is within revive range.
- Reviver has at least one bandage.

On success:

- Remove one `Bandage` from reviver inventory.
- Set target health to 25% of max health.
- Clear target `IsDowned`.
- Re-enable target controls through the existing owner-local control paths.

## Components

Likely implementation units:

- `FusionPlayerDownedState`: tracks and syncs player downed state, gates local control, and exposes revive eligibility.
- `FusionPlayerReviveInteractor`: detects nearby downed teammates, manages hold progress, shows prompt/button state, and requests revive completion.
- `Bandage` item entries in the item/catalog/inventory data path.
- `Crafting` entry for `2 Fiber + 1 Cloth -> 1 Bandage`.
- Small UI additions for downed status, revive prompt, mobile revive button, and progress bar.

The final implementation may use existing survival/inventory classes directly instead of creating extra wrappers if that is the smaller correct change.

## Error Handling

- If a player reaches zero health but required Fusion state is missing, keep health clamped at zero and log one concise warning.
- If a revive completes but the reviver no longer has a bandage, cancel with `Need Bandage to Revive`.
- If target despawns or leaves range during revive, cancel progress without consuming items.
- If inventory consumption fails after validation, do not restore health.
- Missing optional UI references should degrade gracefully: prompt text can be absent, but revive logic should still work through interaction checks.

## Acceptance Criteria

- When a player reaches zero health, they enter downed state and cannot move, attack, interact, jump, consume, or use tools.
- Downed state is visible to both host and client.
- A teammate with one bandage can hold revive for five seconds and restore the target to 25% max health.
- Revive consumes exactly one bandage from the reviver.
- Revive cancels when input is released or range is broken.
- A teammate without bandage cannot complete revive and sees a clear missing-bandage prompt.
- Bandage can be acquired from scene pickup.
- Bandage can be crafted using `2 Fiber + 1 Cloth`.
- Unity reports zero compile errors after implementation.

## Out Of Scope

- Enemy AI, wave systems, or combat encounter changes.
- Bleed-out timers.
- Inventory loss on downed state.
- Advanced medical item tiers such as medkits, antiseptic, herbs, or splints.
- Full downed animation polish if no suitable animation is already available.
- Rewriting the whole inventory or survival architecture.
