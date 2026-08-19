# Terrain Tree Depletion Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix late-join desync so a player who joins after trees were chopped sees those trees as already depleted, matching the host.

**Architecture:** Add a scene-level `FusionTerrainTreeDepletionState` NetworkBehaviour holding a `[Networked] NetworkArray<int>` of depleted Terrain tree ids. When the host depletes a tree, it adds the id to the array; Fusion replicates it. Each client (and late joiners via `Spawned`/`Render`) re-applies the id set to the local `TerrainTreeChoppingRegistry`.

**Tech Stack:** Unity 6 C#, Photon Fusion 2.0.12 (embedded at `Assets/Photon/Fusion`), existing `TerrainTreeChoppingRegistry`, `FusionPlayerCombat`, `FusionStorageChest` (ChangeDetector pattern reference).

---

## Preconditions

- Keep the unrelated dirty files out of all commits unless the user explicitly requests otherwise:
  - `Assets/Assets/Prefabs/PlacedCraftingTable.prefab`
  - `Assets/Screenshots/forest-mobile-optimization-runtime.png`
  - `Assets/Screenshots/forest-mobile-optimization-runtime.png.meta`
  - `Assets/Screenshots/forest-mobile-optimization-sceneview.png`
  - `Assets/Screenshots/forest-mobile-optimization-sceneview.png.meta`
  - `Assets/Screenshots/screenshot-20260817-171433.png`
  - `Assets/Screenshots/screenshot-20260817-171433.png.meta`
- Use Context7 MCP before implementation if Fusion `NetworkArray`/`Capacity` details need another lookup.
- Use unityMCP resource-first workflow before scene or prefab mutation:
  - Read `mcpforunity://editor/state`.
  - Wait until `data.advice.ready_for_tools` is true.
  - Check `read_console` after script changes.
- Spec: `docs/superpowers/specs/2026-08-19-terrain-tree-depletion-sync-design.md`

---

### Task 1: Add Registry Depletion Application Method

**Objective:** Give `TerrainTreeChoppingRegistry` a public, idempotent method to mark a set of tree ids as depleted and hide them.

**Files:**
- Modify: `Assets/Scripts/Environment/TerrainTreeChoppingRegistry.cs`
- Test: `Assets/Editor/TerrainTreeChoppingRegistrySelfTest.cs`

- [ ] **Step 1: Add failing test for `ApplyNetworkedDepletion`**

Append to the existing `Run()` method in `Assets/Editor/TerrainTreeChoppingRegistrySelfTest.cs`, right after the line that asserts `!registry.TryPlayFallingProxy(123456789, Vector3.forward)`:

```csharp
            int beforeApplyCount = terrain.terrainData.treeInstanceCount;
            registry.ApplyNetworkedDepletion(new[] { hit.TreeId, 999999999 });
            Expect(terrain.terrainData.treeInstanceCount == beforeApplyCount - 1,
                "ApplyNetworkedDepletion should hide matching trees and ignore unknown ids.");
            registry.ApplyNetworkedDepletion(new[] { hit.TreeId });
            Expect(terrain.terrainData.treeInstanceCount == beforeApplyCount - 1,
                "ApplyNetworkedDepletion should be idempotent (re-applying an already-depleted id must not hide twice).");
```

- [ ] **Step 2: Run test to verify it fails**

Run via menu `Project Multiplayer/Run Terrain Tree Chopping Registry Self Test`.
Expected: FAIL with "ApplyNetworkedDepletion should hide matching trees and ignore unknown ids." (method does not exist yet).

- [ ] **Step 3: Implement `ApplyNetworkedDepletion`**

In `TerrainTreeChoppingRegistry.cs`, add a public method after `TryHideTree`:

```csharp
    public void ApplyNetworkedDepletion(System.Collections.Generic.IEnumerable<int> treeIds)
    {
        if (treeIds == null)
        {
            return;
        }

        foreach (int treeId in treeIds)
        {
            if (!recordsById.TryGetValue(treeId, out TreeRecord record) || record.Depleted)
            {
                continue;
            }

            record.Depleted = true;
            HideTree(record);
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run via menu `Project Multiplayer/Run Terrain Tree Chopping Registry Self Test`.
Expected: PASS ("TerrainTreeChoppingRegistrySelfTest passed.").

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Environment/TerrainTreeChoppingRegistry.cs Assets/Editor/TerrainTreeChoppingRegistrySelfTest.cs
git commit -m "feat: add networked depletion application to registry"
```

---

### Task 2: Add FusionTerrainTreeDepletionState Behaviour

**Objective:** Create the `NetworkBehaviour` that holds and replicates the depleted tree id set.

**Files:**
- Create: `Assets/Scripts/PhotonFusion/FusionTerrainTreeDepletionState.cs`
- Test: `Assets/Editor/FusionTerrainTreeDepletionStateSelfTest.cs`

- [ ] **Step 1: Write failing test for the state object's logic**

Create `Assets/Editor/FusionTerrainTreeDepletionStateSelfTest.cs`:

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class FusionTerrainTreeDepletionStateSelfTest
{
    [MenuItem("Project Multiplayer/Run Fusion Terrain Tree Depletion State Self Test")]
    public static void Run()
    {
        // Pure-logic seam: validate AddDepletedTree de-duplication and ordering
        var state = ScriptableObject.CreateInstance<DepletionIdBuffer>();
        state.ResetForTest();

        Expect(!state.Contains(5), "Buffer should start empty.");
        Expect(state.Add(5), "First add should return true.");
        Expect(!state.Add(5), "Duplicate add should return false.");
        Expect(state.Add(7), "Second distinct add should return true.");
        Expect(state.Count == 2, "Buffer should contain two distinct ids.");

        ScriptableObject.DestroyImmediate(state);
        Debug.Log("FusionTerrainTreeDepletionStateSelfTest passed.");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new System.InvalidOperationException(message);
        }
    }
}
```

Note: `DepletionIdBuffer` is a test seam ScriptableObject used by both the test and the NetworkBehaviour. Create it in Task 3 if the test fails to compile; alternatively write this test after Task 3. The intent is that the ordering/de-dupe logic is testable outside Fusion's IL weaving.

- [ ] **Step 2: Run test (after Task 3 creates DepletionIdBuffer)**

Run via menu `Project Multiplayer/Run Fusion Terrain Tree Depletion State Self Test`.
Expected: PASS.

- [ ] **Step 3: Commit (test + seam together)**

```bash
git add Assets/Editor/FusionTerrainTreeDepletionStateSelfTest.cs Assets/Scripts/PhotonFusion/DepletionIdBuffer.cs
git commit -m "test: cover depletion id buffer de-duplication"
```

---

### Task 3: Create DepletionIdBuffer Seam

**Objective:** Isolate the de-duplication + ordering logic so it is testable without Fusion's IL weaving.

**Files:**
- Create: `Assets/Scripts/PhotonFusion/DepletionIdBuffer.cs`

- [ ] **Step 1: Implement the seam**

```csharp
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "DepletionIdBuffer", menuName = "Multiplayer/DepletionIdBuffer")]
public class DepletionIdBuffer : ScriptableObject
{
    [SerializeField] private List<int> ids = new List<int>();
    private readonly HashSet<int> set = new HashSet<int>();

    public int Count => ids.Count;

    public void ResetForTest()
    {
        ids.Clear();
        set.Clear();
    }

    public void Load(IEnumerable<int> values)
    {
        ids.Clear();
        set.Clear();
        if (values == null)
        {
            return;
        }

        foreach (int value in values)
        {
            if (value != 0 && set.Add(value))
            {
                ids.Add(value);
            }
        }
    }

    public bool Contains(int treeId)
    {
        return set.Contains(treeId);
    }

    public bool Add(int treeId)
    {
        if (treeId == 0 || !set.Add(treeId))
        {
            return false;
        }

        ids.Add(treeId);
        return true;
    }

    public int[] ToArray()
    {
        return ids.ToArray();
    }
}
```

- [ ] **Step 2: Run the Task 2 test**

Run via menu `Project Multiplayer/Run Fusion Terrain Tree Depletion State Self Test`.
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/PhotonFusion/DepletionIdBuffer.cs Assets/Editor/FusionTerrainTreeDepletionStateSelfTest.cs
git commit -m "feat: add depletion id buffer seam"
```

---

### Task 4: Implement FusionTerrainTreeDepletionState

**Objective:** Wire the buffer into a `NetworkBehaviour` with a replicated `NetworkArray<int>`, syncing into the registry.

**Files:**
- Modify: `Assets/Scripts/PhotonFusion/FusionTerrainTreeDepletionState.cs` (create)
- Modify: `Assets/Scripts/PhotonFusion/FusionPlayerCombat.cs:149-179`
- Test: `Assets/Editor/FusionTerrainTreeDepletionStateSelfTest.cs` (unchanged from Task 2)

- [ ] **Step 1: Implement the NetworkBehaviour**

Create `Assets/Scripts/PhotonFusion/FusionTerrainTreeDepletionState.cs`:

```csharp
using System.Collections.Generic;
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class FusionTerrainTreeDepletionState : NetworkBehaviour
{
    private const int MaxDepletedTrees = 512;

    [Networked, Capacity(MaxDepletedTrees)]
    private NetworkArray<int> DepletedTreeIds { get; }

    private DepletionIdBuffer buffer;
    private TerrainTreeChoppingRegistry registry;
    private bool hasAppliedInitialState;
    private bool warnedMissingRegistry;

    public override void Spawned()
    {
        ResolveReferences();
        SyncToRegistry();
    }

    public override void Render()
    {
        if (!hasAppliedInitialState)
        {
            hasAppliedInitialState = true;
            SyncToRegistry();
            return;
        }

        if (HasStateAuthority)
        {
            return;
        }

        SyncToRegistry();
    }

    public void AddDepletedTree(int treeId)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority || treeId == 0)
        {
            return;
        }

        if (buffer == null)
        {
            buffer = new DepletionIdBuffer();
        }

        if (!buffer.Add(treeId))
        {
            return;
        }

        WriteBufferToNetwork();
    }

    public int[] GetDepletedTreeIds()
    {
        if (buffer == null)
        {
            buffer = new DepletionIdBuffer();
        }

        return buffer.ToArray();
    }

    private void ResolveReferences()
    {
        if (registry == null)
        {
            registry = FindObjectOfType<TerrainTreeChoppingRegistry>();
        }
    }

    private void SyncToRegistry()
    {
        if (buffer == null)
        {
            buffer = new DepletionIdBuffer();
        }

        buffer.Load(ReadBufferFromNetwork());

        if (registry == null)
        {
            ResolveReferences();
        }

        if (registry == null)
        {
            if (!warnedMissingRegistry)
            {
                warnedMissingRegistry = true;
                Debug.LogWarning("[FusionTerrainTreeDepletionState] TerrainTreeChoppingRegistry not found; skipping depletion sync.");
            }

            return;
        }

        registry.ApplyNetworkedDepletion(buffer.ToArray());
    }

    private int[] ReadBufferFromNetwork()
    {
        List<int> result = new List<int>();
        for (int i = 0; i < DepletedTreeIds.Length; i++)
        {
            int value = DepletedTreeIds[i];
            if (value != 0)
            {
                result.Add(value);
            }
        }

        return result.ToArray();
    }

    private void WriteBufferToNetwork()
    {
        int[] values = buffer.ToArray();
        for (int i = 0; i < values.Length && i < MaxDepletedTrees; i++)
        {
            DepletedTreeIds.Set(i, values[i]);
        }

        for (int i = values.Length; i < MaxDepletedTrees; i++)
        {
            DepletedTreeIds.Set(i, 0);
        }
    }
}
```

- [ ] **Step 2: Wire `RPC_TerrainTreeHit` to add depleted ids**

In `Assets/Scripts/PhotonFusion/FusionPlayerCombat.cs`, inside `RPC_TerrainTreeHit`, after `registry.TryPlayFallingProxy(treeId, fallDirection);` (and only when `Object != null && Object.HasStateAuthority`), add:

```csharp
        if (Object != null && Object.HasStateAuthority)
        {
            FusionTerrainTreeDepletionState depletionState = FindObjectOfType<FusionTerrainTreeDepletionState>();
            if (depletionState != null)
            {
                depletionState.AddDepletedTree(treeId);
            }
        }
```

Place this block inside the existing state-authority guard (before or after `SpawnTreeDropsFromData`; the ordering only affects whether the id replicates before or after drops spawn, which is not load-bearing).

- [ ] **Step 3: Verify compile + run registry self test**

Run via menu `Project Multiplayer/Run Terrain Tree Chopping Registry Self Test`.
Expected: PASS. Also confirm `read_console` has no new errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/PhotonFusion/FusionTerrainTreeDepletionState.cs Assets/Scripts/PhotonFusion/FusionPlayerCombat.cs
git commit -m "feat: replicate terrain tree depletion state for late join"
```

---

### Task 5: Add Scene Object to Environment

**Objective:** Place `FusionTerrainTreeDepletionState` in the Environment scene as a NetworkObject so it is present from session start.

**Files:**
- Modify: `Assets/Scenes/Environment.unity`

- [ ] **Step 1: Create the scene GameObject via unityMCP**

Use `manage_gameobject` (or `execute_code` with UnityEditor API) to:

1. Create GameObject named `FusionTerrainTreeDepletionState`.
2. Add `Fusion.NetworkObject` component.
3. Add `FusionTerrainTreeDepletionState` component.
4. Save the scene.

Confirm via `manage_scene(action="save")` and `manage_gameobject`/`execute_code` that the object exists with both components and is active.

- [ ] **Step 2: Run environment validation self test**

Run via menu `Project Multiplayer/Run Terrain Tree Chopping Environment Self Test`.
Expected: PASS (registry count still exactly 1). Confirm the new scene object does not break the validation.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scenes/Environment.unity
git commit -m "feat: add terrain tree depletion state to environment scene"
```

---

### Task 6: Runtime Verification (Single Client / Shared Host)

**Objective:** Verify chopping pushes depleted ids into the networked array and the registry applies them.

**Files:** none (verification only).

- [ ] **Step 1: Enter play mode via unityMCP**

Run `manage_editor(action="play")`. Wait for the session to be ready (read `mcpforunity://editor/state` until `data.advice.ready_for_tools`).

- [ ] **Step 2: Chop a tree via axe**

Using the pattern from prior chopping verification:
1. Find the player (`FusionPlayerCombat`).
2. Position it in range of a tree (`registry.TryFindBestTreeForChop`).
3. Call `PlayerAxeCombat.TryAttack()` three times with `Start-Sleep -Milliseconds 800` between.
4. After the third hit, query `FusionTerrainTreeDepletionState.GetDepletedTreeIds()` and the registry's tree count.

Expected:
- `treeCount` decreases by 1.
- `GetDepletedTreeIds()` contains the chopped tree id.
- `registry` marks that id depleted (verify via `TryApplyDamage` returning false for it).

- [ ] **Step 3: Verify re-application is idempotent**

Call `registry.ApplyNetworkedDepletion(GetDepletedTreeIds())` again and confirm `treeCount` does not decrease further.

- [ ] **Step 4: Exit play mode and confirm cleanup**

Run `manage_editor(action="stop")`. Confirm `read_console` shows no new errors and that `treeCount` returns to baseline (registry restore on disable works).

---

### Task 7: Manual Multiplayer Note (Documentation)

**Objective:** Document the two-client late-join verification step (cannot be automated in a single editor).

**Files:**
- Modify: `docs/superpowers/specs/2026-08-19-terrain-tree-depletion-sync-design.md`

- [ ] **Step 1: Append manual test section**

Append to the spec under Testing:

```markdown
### Manual Multiplayer Verification (Two Editors)

1. Launch Editor A -> Enter play mode on Environment (shared host session).
2. Chop 2-3 trees in Editor A.
3. Launch Editor B -> Enter play mode on Environment and join the same room.
4. In Editor B, confirm the same 2-3 trees are already depleted (absent) near the chopped locations.
5. Chop a new tree in Editor B; confirm Editor A also sees it disappear.
```

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/specs/2026-08-19-terrain-tree-depletion-sync-design.md
git commit -m "docs: add manual multiplayer verification for depletion sync"
```

---

## Self-Review Notes

- Spec coverage: NetworkArray depleted-id sync (Tasks 2/4), registry application (Task 1), scene object (Task 5), runtime verify (Task 6), manual multiplayer note (Task 7).
- Placeholder scan: all code blocks are complete; no TBD/TODO.
- Type consistency: `AddDepletedTree`, `GetDepletedTreeIds`, `ApplyNetworkedDepletion`, `DepletionIdBuffer` names are used consistently across tasks.
