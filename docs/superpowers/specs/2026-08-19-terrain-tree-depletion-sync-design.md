# Terrain Tree Depletion Sync Design

## Goal

Fix the late-join desync in Terrain tree chopping: a player who joins a shared Fusion session after trees have already been chopped should see those trees as already depleted on their own client, matching the host and existing players.

## Context

Terrain trees are scene Terrain instances, not per-tree GameObjects. `TerrainTreeChoppingRegistry` is a plain MonoBehaviour that snapshots every Terrain tree at `Awake` and keeps depletion state (health/depleted flags) only in local memory. Chopping damage is replicated through `RPC_TerrainTreeHit` (`RpcSources.InputAuthority -> RpcTargets.All`), so every client that was already in the session applies the same damage and reaches the same depleted state.

A client that joins later rebuilds the registry from a pristine Terrain snapshot, so trees chopped before that client joined remain visible. There is no networked record of "which trees are already depleted."

Fusion 2.0.12 is embedded at `Assets/Photon/Fusion`. The project already uses `[Networked, Capacity(...)] NetworkArray<T>` for networked collections (FusionFurnace, CampfireCooking, FusionStorageChest).

## Chosen Approach

Add a scene-level networked state object, `FusionTerrainTreeDepletionState` (`NetworkBehaviour`), that stores the set of depleted Terrain tree ids in a `[Networked] NetworkArray<int>`. Only depleted ids are replicated, so normal play traffic stays tiny. A late joiner reads the replicated array and applies those ids to its local registry.

### Why NetworkArray of depleted ids instead of full state

- There are 2521 Terrain trees. Replicating health for all of them on every change would be large and wasteful.
- Depleted ids are the only state that must survive a session for late joiners.
- `NetworkArray<int>` with `[Capacity]` matches the existing project pattern and only sends changed elements.

## Components

### FusionTerrainTreeDepletionState (new NetworkBehaviour)

A scene GameObject in `Environment.unity` with a `NetworkObject` + this behaviour.

```csharp
[Networked, Capacity(MaxDepletedTrees)]
private NetworkArray<int> DepletedTreeIds { get; }
```

- `MaxDepletedTrees` constant: the capacity for stored ids. Tune to keep the behaviour within the Fusion state budget. Depleted ids are appended by id, de-duplicated, and new ids overwrite the oldest slot when full (documented limitation).
- `Spawned()`: after spawn, publishes the current set of ids to the local `TerrainTreeChoppingRegistry`.
- `Render()`: watches the replicated array; whenever its contents change, re-applies the full set to the registry (idempotent).
- `AddDepletedTree(int treeId)` (called only by state authority when a tree is depleted): inserts the id into `DepletedTreeIds` if not already present. No-op when Fusion/`Object` is not valid (offline mode keeps current local behavior).
- `GetDepletedTreeIds()`: returns the current ids for the registry to consume.
- Null/registry-missing handling: if the registry is missing, log a warning once and skip; the registry re-syncs on its next rebuild.

### TerrainTreeChoppingRegistry changes

Add `ApplyNetworkedDepletion(IEnumerable<int> treeIds)`:

- For each id present in `recordsById`, mark the record depleted (if not already) and hide the tree via the existing `HideTree` path.
- Idempotent: already-depleted ids are skipped.
- Safe to call before/after `Rebuild`; it resolves ids against the current `recordsById`.

### FusionPlayerCombat changes

In `RPC_TerrainTreeHit`, after a tree is depleted on the state authority, notify the depletion state object so the id is added to the replicated array:

- Locate `FusionTerrainTreeDepletionState` and call `AddDepletedTree(treeId)` when `Object.HasStateAuthority` is true.

This happens inside the existing state-authority guard that already spawns wood drops, so authority rules stay consistent.

### Runtime spawn (revised: replaces scene object)

Initial design placed `FusionTerrainTreeDepletionState` as a scene NetworkObject. Runtime verification showed scene NetworkObjects are NOT initialized in this project: both `DevAutoSessionStarter` and `PhotonFusionBootstrap` start the runner with an empty `NetworkSceneInfo()` and the scene is loaded by the Unity editor Play flow, so `NetworkSceneManagerDefault` never initializes scene-based NetworkObjects (`Object` stays null).

Revised approach: the depletion state is a **runtime-spawned NetworkObject prefab**, matching the proven `FusionPlayerSpawner`/`FusionPlayerInventory` pattern.

- Create prefab `Assets/Prefabs/FusionTerrainTreeDepletionState.prefab` with `NetworkObject` + `FusionTerrainTreeDepletionState` components.
- Register it in `Assets/DefaultNetworkPrefabs.asset` (Fusion prefab table).
- `TerrainTreeChoppingRegistry` spawns it when the runner is ready (only the state authority / session creator spawns once). It keeps a reference to the spawned instance.
- Late joiners receive the object through normal Fusion spawn replication, so `Spawned()`/`Render()` on the spawned object drives the local registry sync.

### Environment scene changes

No scene GameObject needed. The prefab is spawned at runtime by the registry.

## Data Flow

1. On session start, the state authority (host) spawns `FusionTerrainTreeDepletionState` from the registered prefab.
2. Host chops a tree -> `RPC_TerrainTreeHit` -> `TryApplyDamage` depletes -> `AddDepletedTree(treeId)` on state authority.
3. Fusion replicates `DepletedTreeIds` to all clients (including late joiners who receive the spawned object).
4. Each client's `Render()` sees the change -> re-applies id set to local registry -> hides the tree locally.
5. A late joiner spawns -> receives the state object via spawn sync -> `Spawned()` reads replicated ids -> applies them -> the forest already reflects the session state.

## Edge Cases

- **Duplicate chop while RPC is in flight**: `AddDepletedTree` de-duplicates by id; `ApplyNetworkedDepletion` skips already-depleted records.
- **Capacity overflow**: when full, the oldest id is overwritten. This is a documented limitation; typical test sessions chop only a handful of trees.
- **Offline / non-Fusion**: `Object` invalid -> no networked add; local chopping still works through the existing fallback path.
- **Late join with partially applied state**: `Render()` re-applies the full set every change, so partial application self-heals on the next change.
- **Registry not present yet when state arrives**: `ApplyNetworkedDepletion` is a no-op with a one-time warning; the next `Rebuild`/`Render` sync re-applies.
- **Runner not ready at registry Awake**: the registry retries the spawn until the runner is running, then spawns once. A guard prevents duplicate spawns.
- **Two registries or a scene reload mid-session**: the registry re-checks for an existing spawned instance before spawning.

## Testing

- Self-test (edit mode): construct a temp registry + a networked-state simulation (or a test seam) and assert that adding depleted ids updates the registry's depleted set and hides matching records; assert idempotency (double add does not double-hide).
- Runtime (single client / shared host): chop trees in play mode, verify `DepletedTreeIds` grows, verify registry marks the same trees depleted.
- Runtime spawn check: enter play mode, verify `FusionTerrainTreeDepletionState` is spawned once with `Object` non-null and `HasStateAuthority` true on the host.
- Manual multiplayer note: two-client late-join verification is documented as a manual test step (requires a second editor).

## Limitations

- Capacity overflow can evict old depleted ids for very large chopping sessions.
- The state object only records ids; it does not persist across session restarts (fresh session starts with a fresh forest).
