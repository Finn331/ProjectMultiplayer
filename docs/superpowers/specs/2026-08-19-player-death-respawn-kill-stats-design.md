# Player Death, Respawn, Kill Tracking, and Stats Persistence Design

## Goal

Add a complete death/respawn loop and kill tracking to the multiplayer survival game:

- When a player's health reaches 0, they become **downed** (existing behavior). A **downed event** is recorded to the kill feed.
- While downed, the player can be revived (existing bandage+hold-E flow) or respawn directly.
- Player has an **auto-respawn timer** (~20s, pauses while someone is reviving) plus a **Respawn Now** button.
- On respawn without revive, a **kill event** is recorded to the kill feed, the player's inventory is dropped as a networked **loot bag** at the death location, and the player is teleported to their respawn point with stats reset.
- Kill/downed statistics are persisted per-player via **Unity Gaming Services** (anonymous auth + Cloud Save).

The game is PvE-focused; PvP only happens when friends deliberately hit each other. Kill attribution must handle damage with no attacker (hunger, thirst, fall damage) by falling back to a generic name.

## Context

Current death path:

- `PlayerSurvivalSystem` (plain MonoBehaviour, local to the state authority) tracks health/hunger/thirst, raises `Died` when health reaches 0, and has `Revive(healthPercent)`.
- `FusionPlayerSurvival` (NetworkBehaviour) syncs stats to all clients, exposes `Networked IsDowned`, listens to `Died` -> sets `IsDowned = true`, and runs the revive RPC flow (`RPC_RequestRevive` -> `RPC_ReviveRequestResolved`).
- `FusionPlayerDownedState` blocks movement/combat while downed and shows a revive prompt HUD.
- `FusionPlayerReviveInteractor` lets a nearby alive player hold a key to revive a downed player using a bandage.
- `PlayerInventory` is a **local-owner-only** MonoBehaviour: its `Entries` (`IReadOnlyList<InventoryEntry>`) enumerate item stacks and `RemoveItem`/`AddItem` mutate them. It is not replicated to other clients.
- `FusionPickableItem` is a networked single-item pickup (`Networked ItemTypeValue` + `Amount` + `Initialize(ItemType, int)`); already used for tree drops via `FusionPlayerInventory.SpawnTreeDropsFromData`.
- `FusionPlayerSpawner` picks a `FusionSpawnPoint` per player for initial spawn.
- `FusionPlayerCombat.RequestPlayerDamage` -> `RPC_PlayerDamage` -> `targetSurvival.ApplyDamageForStateAuthority(damage)` already delivers PvP damage.

There is no respawn flow and no kill/downed tracking today. A downed player with no reviver is stuck forever.

Unity Gaming Services: **not yet installed**. The project's `Packages/manifest.json` has no `com.unity.services.core`, `com.unity.services.authentication`, or `com.unity.services.cloudsave`. These must be added. Per context7 docs: `UnityServices.InitializeAsync()` then `AuthenticationService.Instance.SignInAnonymouslyAsync()` (recovering cached player via session token), then `CloudSaveService.Instance.Data.Player.SaveAsync(dictionary)` / `LoadAsync(...)` for stats.

## Chosen Approach

A new network component `FusionPlayerDeath` owns the respawn state machine. It reads `Networked IsDowned` and a new `Networked LastDamagerRef`, and reacts to the existing revive/health flow. A separate local component `PlayerStatsPersistence` handles UGS auth + Cloud Save. A `KillFeedHUD` renders feed messages. Inventory drop reuses the existing networked pickable spawn path.

### Why a new component instead of extending FusionPlayerSurvival

`FusionPlayerSurvival` already handles stat sync and the revive RPC protocol (318 lines). Adding the respawn timer, respawn-now RPC, kill-feed triggering, and loot-drop orchestration there would mix four responsibilities and make edit-mode testing harder. The project already splits player concerns into separate components (`FusionPlayerDownedState`, `FusionPlayerReviveInteractor`), so a dedicated `FusionPlayerDeath` follows the established pattern.

## Components

### FusionPlayerDeath (new NetworkBehaviour, on player prefab)

Owns the respawn state machine and coordinates death/kill events.

State machine over `IsDowned`:

```
Alive (IsDowned=false) -> Downed (IsDowned=true) -> Respawned (IsDowned=false, stats reset)
```

When `IsDowned` transitions false->true (detected in `FixedUpdateNetwork`/`Update` on the state authority):

1. Record a **downed** kill-feed event using `LastDamagerRef`.
2. Begin the auto-respawn countdown (`respawnDelaySeconds` default 20).
3. Trigger the inventory drop (drop all stacks as networked pickables at the death position).

While downed:

- The countdown **pauses** while `FusionPlayerReviveInteractor` is actively holding a revive on this player (progress > 0). The revive interactor exposes `IsRevivingTarget(FusionPlayerDeath)` or we subscribe to the survival's revive state. Simpler: re-use the existing `RPC_RequestRevive`-adjacent path — add a public method on the reviver to query whether a revive is in progress on the target, polled each frame.
- "Respawn Now" button calls `RespawnNow()` RPC (see below).

When the countdown reaches zero (and not currently being revived), the player respawns (see Respawning).

On respawn without revive (`RespawnNow` or timer):

1. Record a **kill** kill-feed event using `LastDamagerRef`.
2. Reset stats on the state authority: `survivalSystem.Revive(1f)`-equivalent full heal + restore hunger/thirst (`RestoreAllNeeds`), then `IsDowned = false`.
3. Teleport the player object to the chosen respawn point.
4. Clear `LastDamagerRef`.
5. Persist totals via `PlayerStatsPersistence` (kill count +1).

Public API:

```csharp
public void RespawnNow()            // local input -> RPC_RespawnNow (InputAuthority -> StateAuthority)
```

`RPC_RespawnNow` only works while `IsDowned`; otherwise ignored.

Respawn point selection: use `FusionPlayerSpawner`'s existing spawn-point picker (spawn index = `playerId % points.Length`), which picks the initial spawn point. Document a seam for a future **bed respawn**: a `RespawnLocationProvider` interface (or a virtual method on `FusionPlayerDeath`) that currently returns the initial spawn point but can later read a "last slept bed" location from the player's persisted state.

### LastDamagerRef (new, on FusionPlayerSurvival)

Add `[Networked] public PlayerRef LastDamagerRef { get; set; }` to `FusionPlayerSurvival`. `PlayerRef` implements `INetworkStruct`, so it is directly networkable as a networked property.

- Set inside `ApplyDamageForStateAuthority(damage)` — the method needs an attacker `PlayerRef` parameter. Currently damage arrives via `RPC_PlayerDamage(targetPosition, damage)` which has `RpcInfo info`; `info.Source` is the attacker. Extend `ApplyDamageForStateAuthority(damage, PlayerRef attacker)` and have `RPC_PlayerDamage` pass `info.Source`.

Also add `[Networked] public NetworkString<_16> DisplayName { get; private set; }` to the same behaviour, set once in `Spawned()` on the state authority from `PhotonFusionSessionState.Active.PlayerName` (clamped to 15 chars). This lets any client derive a player's name from their NetworkObject without a shared registry. (`using Fusion;` already imports `NetworkString`.)
- If damage has no attacker (hunger/thirst/fall), pass `PlayerRef.None`; the kill feed falls back to a generic label (e.g. "Nature").
- Cleared on respawn.

Keep the method name/behavior for existing callers where possible; internal callers already on state authority pass `PlayerRef.None`.

### Kill feed event

Events broadcast to every client with names + kind:

```csharp
[Rpc(RpcSources.StateAuthority, RpcTargets.All)]
private void RPC_KillFeedMessage(string victimName, string killerName, bool isKill)
```

- Each player publishes a lightweight `[Networked] NetworkString<_16> DisplayName` (see LastDamagerRef section) set once at spawn from `PhotonFusionSessionState.Active.PlayerName`. The state authority can thus read the attacker's name directly from `LastDamagerRef`'s object and the victim's from its own display name — no name registry or request RPC needed.
- `killerName` is empty `""` when `LastDamagerRef` is `PlayerRef.None`; the HUD substitutes the localizable string "Nature".
- `victimName` is the downed/killed player's own display name.
- `isKill=true` for the respawn-without-revive event, `false` for downed.

The RPC payload carries strings so every client renders identically without a name registry.

### KillFeedHUD (new UI component)

- Canvas overlay top-right (screen-space overlay), stack of message rows with a fade timer (~5s).
- Downed messages: green tint `"<killer> downed <victim>"`.
- Kill messages: red tint `"<killer> killed <victim>"`.
- Nature fallback: `"Nature downed <victim>"` / `"Nature killed <victim>"`.
- Reuses the project's existing UI conventions (world/screen canvas present in Gameplay scene; TextMeshPro available).

### Loot drop on downed

Inventory is local-owner state, so the **state authority** of the player enumerates its local `PlayerInventory.Entries` and spawns the stacks as networked pickables:

- For each non-empty `InventoryEntry` (item type + amount), spawn one `FusionPickableItem` at the death position using the same drop-prefab binding path already used for tree drops (`FusionPlayerInventory` `TryGetDropPrefab` + `Runner.Spawn` + `Initialize(itemType, amount)`).
- Clear the inventory on spawn success (`inventory.RemoveItem(...)` per stack), so the player respawns empty-handed.
- Despawn each pickable after a timeout (e.g. 120s) being untouched, matching `FusionPickableItem` behavior; reuse whatever cleanup exists.

Edge: if the inventory is full of many stacks, clamp the number of spawned pickables (e.g. cap at 20) and drop remaining stacks implicitly (documented limitation).

### Reset / teleport

On respawn:

- `survivalSystem` full restore: `Heal(MaxHealth)`, `RestoreAllNeeds()` (or a new `FusionPlayerSurvival.ResetForRespawn()` that does both and sets `IsDowned=false`, `IsInitialized=true`).
- Teleport: set `transform.position` to the respawn point on the state authority; `FusionPlayerMovement`/controller snaps on the state authority and replicates position via the networked transform.
- Clear downed control locks via existing `FusionPlayerDownedState` (it already reacts to `IsDowned` transitions).

### PlayerStatsPersistence (new MonoBehaviour)

Per-player runtime persistence of totals:

- On `Awake`: `UnityServices.InitializeAsync()` then `AuthenticationService.Instance.SignInAnonymouslyAsync()` (cached session token reuse).
- Loads `kill_count`, `downed_count` via `CloudSaveService.Instance.Data.Player.LoadAsync(...)` at first sign-in; exposes `TotalKills`, `TotalDowns`.
- Subscribes to the player's local downed/kill events and increments counters; persists via `SaveAsync` (throttled, e.g. save on event + on session end).
- Offline safe: wraps all UGS calls in try/catch; on failure, keeps in-memory counters and retries next event.

Kept out of `FusionPlayerDeath` so the network component stays UI/persistence-agnostic and testable without UGS.

## Data Flow

1. Player takes damage with an attacker -> `RPC_PlayerDamage` -> `FusionPlayerSurvival.ApplyDamageForStateAuthority(damage, info.Source)` -> `PlayerSurvivalSystem.ApplyDamage`.
2. Health reaches 0 -> `PlayerSurvivalSystem.Died` -> `FusionPlayerSurvival` sets `IsDowned=true`, sends snapshot.
3. `FusionPlayerDeath` sees downed transition -> emits downed feed event, starts 20s timer, triggers inventory drop.
4. Reviver holds to revive -> timer pauses (revive in progress). Revive succeeds or fails (existing flow).
5. Timer completes or player presses Respawn Now -> `RPC_RespawnNow` -> respawn: kill feed event, full stat reset, teleport, clear `LastDamagerRef`, persist stats.
6. `KillFeedHUD` renders messages on all clients.

## Edge Cases

- **Nature damage (no attacker)**: `PlayerRef.None` -> feed shows "Nature ...".
- **Damage while downed**: ignore (existing `IsDowned` guards in `PlayerSurvivalSystem` and combat requests).
- **Respawn Now while being revived**: allowed (player chooses to forgo revive); timer-driven respawn is cancelled if a revive completes first (`IsDowned` flips false, countdown resets).
- **Revive right at countdown end**: if a revive completes at the same frame the countdown hits zero, prioritize the revive; respawn cancels.
- **Inventory empty**: drop is skipped; respawn proceeds.
- **Inventory split across many stacks**: pickable cap of 20; overflow stacks are lost (documented).
- **UGS offline / uninitialized**: stats stay in-memory; no crash; retry on next event.
- **Late joiner**: kill feed is transient (session-scoped), UGS totals load on sign-in and reflect prior sessions; no per-session replay is needed.
- **Multiple attackers**: `LastDamagerRef` stores the most recent attacker (simplest); kill credit goes to the last hit. Acceptable for PvE/PvP-casual context.

## Testing

- Edit-mode self-tests:
  - `FusionPlayerDeath` state machine with a fake survival: downed transition starts timer; revive in progress pauses timer; timer completes -> respawn path invoked; Respawn Now works only while downed.
  - `LastDamagerRef` set/clear logic (with a test seam on damage entry).
  - Inventory drop enumeration: given a fake `PlayerInventory`, non-empty stacks are produced for spawning; empty inventory skips.
  - Kill feed message construction (names, nature fallback, isKill tint selection) is pure logic testable via the HUD's view-model.
  - Stats persistence counter logic with an injected fake save/load backend (no real UGS in tests).
- Runtime (single editor): kill player via debug damage -> downed feed appears; inventory drops appear; auto-respawn countdown HUD shows; Respawn Now teleports and clears; stats increments in memory.
- Manual multiplayer (two editors): player A downs player B -> A and B both see the feed; B respawns -> A sees the kill; A picks up B's loot bag. Async persistence checked via UGS dashboard (best-effort; tolerant of sandbox flakiness).

## Limitations

- Pickable spawn cap of 20 stacks per death; overflow stacks lost.
- `LastDamagerRef` credits the most recent attacker, not the largest contributor.
- UGS stats are session-and-cache best-effort; a truly offline run does not backfill totals later.
- Respawn location is the initial spawn point; a future "slept in bed" location is a documented extension seam, not implemented here.