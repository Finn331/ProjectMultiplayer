# Terrain Tree Chopping Implementation Plan

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Make every Terrain tree in `Assets/Scenes/Environment.unity` choppable in multiplayer, with synced tree removal, LeanTween falling animation, and shared wood drops.

**Architecture:** Add a runtime `TerrainTreeChoppingRegistry` scene service that snapshots Terrain tree instances without creating per-tree GameObjects. Extend `PlayerAxeCombat` to target Terrain trees only after existing `TreeChoppable` paths fail. Extend `FusionPlayerCombat` to route authoritative Terrain tree chop requests, replicate depletion to all clients, and spawn wood through existing `FusionPlayerInventory.SpawnTreeDropsFromData`.

**Tech Stack:** Unity 6 C#, Terrain API, Photon Fusion RPCs, LeanTween, existing `PlayerAxeCombat`, `FusionPlayerCombat`, and `FusionPlayerInventory` systems.

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
- Use Context7 MCP before implementation if LeanTween or Unity Terrain API details need another lookup.
- Use unityMCP resource-first workflow before scene or prefab mutation:
  - Read `mcpforunity://editor/state`.
  - Wait until `data.advice.ready_for_tools` is true.
  - Check `read_console` after script changes.

---

### Task 1: Add Terrain Tree Registry Tests

**Objective:** Create failing EditMode coverage for stable Terrain tree id mapping, candidate search, and idempotent depletion.

**Files:**
- Create: `Assets/Editor/TerrainTreeChoppingRegistrySelfTest.cs`
- Later implementation target: `Assets/Scripts/Environment/TerrainTreeChoppingRegistry.cs`

**Step 1: Write failing test**

Create `Assets/Editor/TerrainTreeChoppingRegistrySelfTest.cs`:

```csharp
using UnityEditor;
using UnityEngine;

public static class TerrainTreeChoppingRegistrySelfTest
{
    [MenuItem("Project Multiplayer/Run Terrain Tree Chopping Registry Self Test")]
    public static void Run()
    {
        GameObject terrainObject = new GameObject("TerrainTreeChoppingRegistrySelfTest_Terrain");
        GameObject prototype = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        prototype.name = "TerrainTreeChoppingRegistrySelfTest_Prototype";

        try
        {
            TerrainData data = new TerrainData();
            data.heightmapResolution = 33;
            data.size = new Vector3(20f, 4f, 20f);
            data.treePrototypes = new[] { new TreePrototype { prefab = prototype } };
            data.treeInstances = new[]
            {
                new TreeInstance
                {
                    prototypeIndex = 0,
                    position = new Vector3(0.5f, 0f, 0.5f),
                    widthScale = 1f,
                    heightScale = 1f,
                    color = Color.white,
                    lightmapColor = Color.white
                },
                new TreeInstance
                {
                    prototypeIndex = 0,
                    position = new Vector3(0.75f, 0f, 0.5f),
                    widthScale = 1f,
                    heightScale = 1f,
                    color = Color.white,
                    lightmapColor = Color.white
                }
            };

            Terrain terrain = terrainObject.AddComponent<Terrain>();
            terrain.terrainData = data;
            terrainObject.AddComponent<TerrainCollider>().terrainData = data;

            TerrainTreeChoppingRegistry registry = terrainObject.AddComponent<TerrainTreeChoppingRegistry>();
            registry.RebuildForTests(new[] { terrain });

            Expect(registry.TreeCount == 2, "Registry should snapshot both Terrain tree instances.");
            Expect(registry.TryFindBestTreeForChop(new Vector3(10f, 1f, 5f), Vector3.left, 20f, 0.2f, out TerrainTreeChoppingRegistry.TreeHit hit),
                "Registry should find a tree in front of the player.");
            Expect(hit.TreeId != 0, "Tree id should be stable and non-zero.");

            int beforeRemovalCount = terrain.terrainData.treeInstanceCount;
            Expect(registry.TryApplyDamageForTests(hit.TreeId, 3f, out bool depleted), "First damage application should be accepted.");
            Expect(depleted, "Tree should deplete after lethal test damage.");
            Expect(terrain.terrainData.treeInstanceCount == beforeRemovalCount - 1, "Runtime Terrain tree instance array should hide one depleted tree.");
            Expect(!registry.TryApplyDamageForTests(hit.TreeId, 3f, out _), "Repeated damage against the same depleted id should be ignored.");
            Expect(terrain.terrainData.treeInstanceCount == beforeRemovalCount - 1, "Repeated depletion should not remove another tree.");

            Debug.Log("TerrainTreeChoppingRegistrySelfTest passed.");
        }
        finally
        {
            Object.DestroyImmediate(terrainObject);
            Object.DestroyImmediate(prototype);
        }
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

**Step 2: Run test to verify failure**

Run through Unity menu or MCP:

```csharp
TerrainTreeChoppingRegistrySelfTest.Run();
```

Expected: compile failure or menu execution failure because `TerrainTreeChoppingRegistry` does not exist yet.

**Step 3: Keep the failing test unstaged until Task 2 passes**

Do not commit this compile-breaking state. Commit the test together with the passing registry implementation in Task 2.

---

### Task 2: Implement Terrain Tree Registry Core

**Objective:** Add the runtime service that snapshots Terrain trees, finds chop candidates, tracks health, and hides depleted runtime tree instances.

**Files:**
- Create: `Assets/Scripts/Environment/TerrainTreeChoppingRegistry.cs`
- Test: `Assets/Editor/TerrainTreeChoppingRegistrySelfTest.cs`

**Step 1: Implement minimal registry API**

Create `Assets/Scripts/Environment/TerrainTreeChoppingRegistry.cs` with this shape:

```csharp
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class TerrainTreeChoppingRegistry : MonoBehaviour
{
    public struct TreeHit
    {
        public int TreeId;
        public Vector3 WorldPosition;
        public Vector3 HitPoint;
        public GameObject PrototypePrefab;
    }

    private sealed class TerrainSnapshot
    {
        public int TerrainOrdinal;
        public Terrain Terrain;
        public TreeInstance[] OriginalInstances;
        public HashSet<int> HiddenTreeIds = new HashSet<int>();
    }

    private sealed class TreeRecord
    {
        public int TreeId;
        public TerrainSnapshot Snapshot;
        public int OriginalIndex;
        public Vector3 WorldPosition;
        public GameObject PrototypePrefab;
        public float Health;
        public bool Depleted;
    }

    [SerializeField] private float defaultTreeHealth = 3f;
    [SerializeField] private float approximateChopRadius = 1.1f;

    private readonly List<TerrainSnapshot> snapshots = new List<TerrainSnapshot>();
    private readonly List<TreeRecord> records = new List<TreeRecord>();
    private readonly Dictionary<int, TreeRecord> recordsById = new Dictionary<int, TreeRecord>();

    public int TreeCount => records.Count;

    private void Awake()
    {
        Rebuild(Terrain.activeTerrains);
    }

    public void RebuildForTests(Terrain[] terrains)
    {
        Rebuild(terrains);
    }

    public bool TryApplyDamageForTests(int treeId, float damage, out bool depleted)
    {
        return TryApplyDamage(treeId, damage, out depleted, out _);
    }

    public void Rebuild(Terrain[] terrains)
    {
        snapshots.Clear();
        records.Clear();
        recordsById.Clear();

        if (terrains == null)
        {
            return;
        }

        System.Array.Sort(terrains, CompareTerrainsForStableIds);

        for (int ti = 0; ti < terrains.Length; ti++)
        {
            Terrain terrain = terrains[ti];
            if (terrain == null || terrain.terrainData == null)
            {
                continue;
            }

            TerrainSnapshot snapshot = new TerrainSnapshot
            {
                TerrainOrdinal = ti,
                Terrain = terrain,
                OriginalInstances = terrain.terrainData.treeInstances
            };
            snapshots.Add(snapshot);

            TreePrototype[] prototypes = terrain.terrainData.treePrototypes;
            Vector3 terrainPosition = terrain.transform.position;
            Vector3 terrainSize = terrain.terrainData.size;

            for (int treeIndex = 0; treeIndex < snapshot.OriginalInstances.Length; treeIndex++)
            {
                TreeInstance instance = snapshot.OriginalInstances[treeIndex];
                int treeId = ComputeTreeId(ti, treeIndex);
                GameObject prototype = instance.prototypeIndex >= 0 && instance.prototypeIndex < prototypes.Length
                    ? prototypes[instance.prototypeIndex].prefab
                    : null;

                Vector3 worldPosition = terrainPosition + Vector3.Scale(instance.position, terrainSize);
                TreeRecord record = new TreeRecord
                {
                    TreeId = treeId,
                    Snapshot = snapshot,
                    OriginalIndex = treeIndex,
                    WorldPosition = worldPosition,
                    PrototypePrefab = prototype,
                    Health = Mathf.Max(1f, defaultTreeHealth)
                };
                records.Add(record);
                recordsById[treeId] = record;
            }
        }
    }

    public bool TryFindBestTreeForChop(Vector3 origin, Vector3 direction, float maxDistance, float minForwardDot, out TreeHit hit)
    {
        hit = default;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        Vector3 forward = direction.normalized;
        float maxSqr = Mathf.Max(0.1f, maxDistance) * Mathf.Max(0.1f, maxDistance);
        float bestSqr = float.MaxValue;
        TreeRecord best = null;

        for (int i = 0; i < records.Count; i++)
        {
            TreeRecord record = records[i];
            if (record.Depleted)
            {
                continue;
            }

            Vector3 toTree = record.WorldPosition - origin;
            float sqr = toTree.sqrMagnitude;
            if (sqr > maxSqr)
            {
                continue;
            }

            Vector3 toTreeDirection = sqr > 0.0001f ? toTree.normalized : forward;
            if (Vector3.Dot(forward, toTreeDirection) < minForwardDot)
            {
                continue;
            }

            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = record;
            }
        }

        if (best == null)
        {
            return false;
        }

        hit = new TreeHit
        {
            TreeId = best.TreeId,
            WorldPosition = best.WorldPosition,
            HitPoint = best.WorldPosition + Vector3.up * 0.75f,
            PrototypePrefab = best.PrototypePrefab
        };
        return true;
    }

    public bool TryApplyDamage(int treeId, float damage, out bool depleted, out TreeHit hit)
    {
        depleted = false;
        hit = default;
        if (!recordsById.TryGetValue(treeId, out TreeRecord record) || record.Depleted || damage <= 0f)
        {
            return false;
        }

        record.Health -= damage;
        hit = new TreeHit
        {
            TreeId = record.TreeId,
            WorldPosition = record.WorldPosition,
            HitPoint = record.WorldPosition + Vector3.up * 0.75f,
            PrototypePrefab = record.PrototypePrefab
        };

        if (record.Health > 0f)
        {
            return true;
        }

        record.Depleted = true;
        depleted = true;
        HideTree(record);
        return true;
    }

    public bool TryHideTree(int treeId, out TreeHit hit)
    {
        hit = default;
        if (!recordsById.TryGetValue(treeId, out TreeRecord record))
        {
            return false;
        }

        hit = new TreeHit
        {
            TreeId = record.TreeId,
            WorldPosition = record.WorldPosition,
            HitPoint = record.WorldPosition + Vector3.up * 0.75f,
            PrototypePrefab = record.PrototypePrefab
        };

        if (record.Depleted)
        {
            return false;
        }

        record.Depleted = true;
        HideTree(record);
        return true;
    }

    private static int ComputeTreeId(int terrainOrdinal, int treeIndex)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + terrainOrdinal;
            hash = hash * 31 + treeIndex;
            return hash == 0 ? 1 : hash;
        }
    }

    private void HideTree(TreeRecord record)
    {
        if (record.Snapshot.HiddenTreeIds.Add(record.TreeId))
        {
            RebuildRuntimeTreeInstances(record.Snapshot);
        }
    }

    private void RebuildRuntimeTreeInstances(TerrainSnapshot snapshot)
    {
        List<TreeInstance> visible = new List<TreeInstance>(snapshot.OriginalInstances.Length);
        for (int i = 0; i < snapshot.OriginalInstances.Length; i++)
        {
            int treeId = ComputeTreeId(snapshot.TerrainOrdinal, i);
            if (!snapshot.HiddenTreeIds.Contains(treeId))
            {
                visible.Add(snapshot.OriginalInstances[i]);
            }
        }

        snapshot.Terrain.terrainData.treeInstances = visible.ToArray();
        snapshot.Terrain.Flush();
    }

    private static int CompareTerrainsForStableIds(Terrain a, Terrain b)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }

        if (a == null)
        {
            return 1;
        }

        if (b == null)
        {
            return -1;
        }

        int nameCompare = string.CompareOrdinal(a.name, b.name);
        if (nameCompare != 0)
        {
            return nameCompare;
        }

        Vector3 ap = a.transform.position;
        Vector3 bp = b.transform.position;
        int xCompare = ap.x.CompareTo(bp.x);
        if (xCompare != 0) return xCompare;
        int zCompare = ap.z.CompareTo(bp.z);
        if (zCompare != 0) return zCompare;
        return ap.y.CompareTo(bp.y);
    }
}
```

Keep ids deterministic across clients. Do not use `GetInstanceID()` for tree ids because Unity instance ids can differ per client/editor session.

**Step 2: Run test to verify pass**

Run:

```csharp
TerrainTreeChoppingRegistrySelfTest.Run();
```

Expected: Unity console logs `TerrainTreeChoppingRegistrySelfTest passed.`

**Step 3: Check console**

Use unityMCP:

```text
read_console(action="get", types=["error", "warning"], count="20", format="detailed")
```

Expected: no new compile errors. Existing known warning about extra `AudioListener` may still appear during runtime tests.

**Step 4: Commit**

```bash
git add Assets/Scripts/Environment/TerrainTreeChoppingRegistry.cs Assets/Editor/TerrainTreeChoppingRegistrySelfTest.cs
git commit -m "feat: add terrain tree chopping registry"
```

---

### Task 3: Add LeanTween Falling Proxy Animation

**Objective:** Make depleted Terrain trees play a local non-networked falling animation using LeanTween.

**Files:**
- Modify: `Assets/Scripts/Environment/TerrainTreeChoppingRegistry.cs`
- Test: `Assets/Editor/TerrainTreeChoppingRegistrySelfTest.cs`

**Step 1: Add serialized animation settings and helper method**

Modify `TerrainTreeChoppingRegistry`:

```csharp
[Header("Falling Proxy")]
[SerializeField] private float fallDurationSeconds = 1.1f;
[SerializeField] private float fallenProxyLifetimeSeconds = 6f;
[SerializeField] private LeanTweenType fallEase = LeanTweenType.easeInBack;
```

Add a method shaped like:

```csharp
public bool TryPlayFallingProxy(int treeId, Vector3 fallDirection)
{
    if (!recordsById.TryGetValue(treeId, out TreeRecord record) || record.PrototypePrefab == null)
    {
        return false;
    }

    Vector3 direction = fallDirection.sqrMagnitude > 0.0001f
        ? fallDirection.normalized
        : DeterministicFallDirection(treeId);

    GameObject pivot = new GameObject("FallingTerrainTree_" + treeId);
    pivot.transform.position = record.WorldPosition;
    GameObject visual = Instantiate(record.PrototypePrefab, record.WorldPosition, Quaternion.identity, pivot.transform);
    DisableProxyColliders(visual);

    Vector3 axis = Vector3.Cross(Vector3.up, direction);
    if (axis.sqrMagnitude <= 0.0001f)
    {
        axis = Vector3.right;
    }
    axis.Normalize();

    Quaternion targetRotation = Quaternion.AngleAxis(88f, axis) * pivot.transform.rotation;
    LeanTween.rotate(pivot, targetRotation.eulerAngles, Mathf.Max(0.1f, fallDurationSeconds))
        .setEase(fallEase)
        .setOnComplete(() => Destroy(pivot, Mathf.Max(0.1f, fallenProxyLifetimeSeconds)));

    return true;
}
```

Add private helpers:

```csharp
private static Vector3 DeterministicFallDirection(int treeId)
{
    float angle = Mathf.Abs(treeId % 360) * Mathf.Deg2Rad;
    return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)).normalized;
}

private static void DisableProxyColliders(GameObject proxy)
{
    if (proxy == null)
    {
        return;
    }
    Collider[] colliders = proxy.GetComponentsInChildren<Collider>(true);
    for (int i = 0; i < colliders.Length; i++)
    {
        colliders[i].enabled = false;
    }
}
```

**Step 2: Extend self-test for missing-prototype safety**

Add a second test method or section that creates a Terrain tree with no valid prototype and asserts depletion still hides the tree. Avoid requiring LeanTween animation to run in EditMode.

Expected assertion:

```csharp
Expect(!registry.TryPlayFallingProxy(123456789, Vector3.forward), "Missing tree id should not spawn a proxy.");
```

**Step 3: Run registry test**

Run:

```csharp
TerrainTreeChoppingRegistrySelfTest.Run();
```

Expected: pass and no compile errors. If `LeanTweenType` is not found, stop and inspect existing LeanTween installation before changing the animation design.

**Step 4: Commit**

```bash
git add Assets/Scripts/Environment/TerrainTreeChoppingRegistry.cs Assets/Editor/TerrainTreeChoppingRegistrySelfTest.cs
git commit -m "feat: animate chopped terrain trees"
```

---

### Task 4: Wire Axe Fallback Targeting

**Objective:** Let axe swings target Terrain trees after existing normal tree hit paths fail.

**Files:**
- Modify: `Assets/Scripts/Player/Combat/PlayerAxeCombat.cs:34-45,283-317,517-555`
- Modify later: `Assets/Scripts/PhotonFusion/FusionPlayerCombat.cs`

**Step 1: Add serialized terrain-tree assist settings**

In `PlayerAxeCombat`, near the existing `Melee Assist` fields, add:

```csharp
[Header("Terrain Tree Assist")]
[SerializeField] private bool enableTerrainTreeAssist = true;
[SerializeField] private float terrainTreeAssistDistance = 2.25f;
[SerializeField, Range(-1f, 1f)] private float terrainTreeAssistForwardDot = 0.15f;
```

**Step 2: Add fallback call after existing tree assist**

In `ResolveHitAfterDelay`, after the `TryGetNearestTreeAssist` block and before debug logging, add:

```csharp
if (enableTerrainTreeAssist && this.TryGetTerrainTreeAssist(out TerrainTreeChoppingRegistry.TreeHit terrainTreeHit))
{
    this.ApplyTerrainTreeHit(terrainTreeHit);
    yield break;
}
```

**Step 3: Add helper methods**

Add methods near `TryGetNearestTreeAssist`:

```csharp
private bool TryGetTerrainTreeAssist(out TerrainTreeChoppingRegistry.TreeHit hit)
{
    hit = default;
    TerrainTreeChoppingRegistry registry = FindObjectOfType<TerrainTreeChoppingRegistry>();
    if (registry == null)
    {
        return false;
    }

    Vector3 origin = this.GetHitOrigin();
    Vector3 direction = playerCamera != null ? playerCamera.transform.forward : this.GetHitDirection();
    float distance = Mathf.Max(0.1f, terrainTreeAssistDistance);
    return registry.TryFindBestTreeForChop(origin, direction, distance, terrainTreeAssistForwardDot, out hit);
}

private void ApplyTerrainTreeHit(TerrainTreeChoppingRegistry.TreeHit hit)
{
    float appliedTreeDamage = Mathf.Max(0f, treeDamagePerHit * runtimeTreeDamageMultiplier);
    FusionPlayerCombat fusionCombat = GetComponent<FusionPlayerCombat>();
    Vector3 chopperPosition = transform.position;

    if (fusionCombat != null && fusionCombat.RequestTerrainTreeHit(hit.TreeId, hit.WorldPosition, chopperPosition, appliedTreeDamage))
    {
        return;
    }

    TerrainTreeChoppingRegistry registry = FindObjectOfType<TerrainTreeChoppingRegistry>();
    if (registry != null && registry.TryApplyDamage(hit.TreeId, appliedTreeDamage, out bool depleted, out TerrainTreeChoppingRegistry.TreeHit depletedHit) && depleted)
    {
        registry.TryPlayFallingProxy(depletedHit.TreeId, depletedHit.WorldPosition - chopperPosition);
    }
}
```

**Step 4: Compile and inspect console**

Use unityMCP to wait for compile, then:

```text
read_console(action="get", types=["error"], count="20", format="detailed", include_stacktrace=true)
```

Expected: no compile errors.

**Step 5: Commit**

```bash
git add Assets/Scripts/Player/Combat/PlayerAxeCombat.cs
git commit -m "feat: target terrain trees with axe"
```

---

### Task 5: Add Fusion Terrain Tree Chop RPC

**Objective:** Sync Terrain tree health/depletion across players and spawn authoritative wood drops once.

**Files:**
- Modify: `Assets/Scripts/PhotonFusion/FusionPlayerCombat.cs:60-147`
- Uses: `Assets/Scripts/PhotonFusion/FusionPlayerInventory.cs:272-365`

**Step 1: Add public request method**

In `FusionPlayerCombat`, add next to `RequestSceneTreeHit`:

```csharp
public bool RequestTerrainTreeHit(int treeId, Vector3 treePosition, Vector3 chopperPosition, float damage)
{
    if (IsDowned() || !IsNetworkReady() || !HasFusionInputAuthority() || treeId == 0 || damage <= 0f)
    {
        return false;
    }

    RPC_TerrainTreeHit(treeId, treePosition, chopperPosition, damage);
    return true;
}
```

**Step 2: Add RPC implementation**

Add below `RPC_SceneTreeHit`:

```csharp
[Rpc(RpcSources.InputAuthority, RpcTargets.All)]
private void RPC_TerrainTreeHit(int treeId, Vector3 treePosition, Vector3 chopperPosition, float damage, RpcInfo info = default)
{
    TerrainTreeChoppingRegistry registry = FindObjectOfType<TerrainTreeChoppingRegistry>();
    if (registry == null)
    {
        return;
    }

    if (!registry.TryApplyDamage(treeId, damage, out bool depleted, out TerrainTreeChoppingRegistry.TreeHit hit))
    {
        return;
    }

    if (!depleted)
    {
        return;
    }

    Vector3 fallDirection = hit.WorldPosition - chopperPosition;
    registry.TryPlayFallingProxy(treeId, fallDirection);

    if (Object != null && Object.HasStateAuthority)
    {
        FusionPlayerInventory fusionInventory = GetComponent<FusionPlayerInventory>();
        if (fusionInventory != null)
        {
            fusionInventory.SpawnTreeDropsFromData(hit.WorldPosition, hit.WorldPosition, fallDirection, ItemType.Wood, 1, 3, 0.75f);
        }
    }
}
```

**Step 3: Validate local fallback still exists**

Confirm `PlayerAxeCombat.ApplyTerrainTreeHit` still has the non-Fusion fallback for editor/manual non-network testing.

**Step 4: Compile and inspect console**

Expected: no Fusion RPC signature compile errors. If Fusion rejects `TerrainTreeChoppingRegistry.TreeHit` use only primitive/vector args inside RPCs and keep nested struct local to non-RPC code.

**Step 5: Commit**

```bash
git add Assets/Scripts/PhotonFusion/FusionPlayerCombat.cs Assets/Scripts/Player/Combat/PlayerAxeCombat.cs
git commit -m "feat: sync terrain tree chopping"
```

---

### Task 6: Add Registry To Environment Scene

**Objective:** Ensure `Environment.unity` has one scene-level `TerrainTreeChoppingRegistry` service.

**Files:**
- Modify: `Assets/Scenes/Environment.unity`
- Optionally create prefab later only if scene-level object is not suitable.

**Step 1: Use unityMCP to inspect scene readiness**

Read:

```text
mcpforunity://editor/state
```

Expected: `data.advice.ready_for_tools == true`.

**Step 2: Find existing registry**

Use:

```text
find_gameobjects(search_term="TerrainTreeChoppingRegistry", search_method="by_component", include_inactive=true)
```

Expected before implementation: no registry in scene.

**Step 3: Create scene service GameObject**

Use unityMCP:

```text
manage_gameobject(action="create", name="TerrainTreeChoppingRegistry", components_to_add=["TerrainTreeChoppingRegistry"])
```

Set serialized defaults if needed:

```text
manage_components(action="set_property", target="TerrainTreeChoppingRegistry", component_type="TerrainTreeChoppingRegistry", properties={"defaultTreeHealth":3,"fallDurationSeconds":1.1,"fallenProxyLifetimeSeconds":6})
```

If private serialized names are not settable by MCP, leave Inspector defaults from the script.

**Step 4: Save scene**

Use:

```text
manage_scene(action="save")
```

**Step 5: Commit**

```bash
git add Assets/Scenes/Environment.unity
git commit -m "feat: add terrain tree chopping service to forest"
```

---

### Task 7: Add Environment Scene Self-Test

**Objective:** Verify the forest scene has Terrain trees and exactly one chopping registry service.

**Files:**
- Modify: `Assets/Editor/TerrainTreeChoppingRegistrySelfTest.cs`
- Test scene: `Assets/Scenes/Environment.unity`

**Step 1: Extend self-test**

Append an environment validation method:

```csharp
private const string EnvironmentScenePath = "Assets/Scenes/Environment.unity";

[MenuItem("Project Multiplayer/Run Terrain Tree Chopping Environment Self Test")]
public static void RunEnvironmentSceneValidation()
{
    UnityEngine.SceneManagement.Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
    if (!string.Equals(activeScene.path, EnvironmentScenePath, System.StringComparison.OrdinalIgnoreCase))
    {
        if (UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(EnvironmentScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
        }
    }

    Terrain[] terrains = Object.FindObjectsOfType<Terrain>(true);
    Expect(terrains.Length > 0, "Environment scene should contain Terrain objects.");

    int treeCount = 0;
    for (int i = 0; i < terrains.Length; i++)
    {
        if (terrains[i] != null && terrains[i].terrainData != null)
        {
            treeCount += terrains[i].terrainData.treeInstanceCount;
        }
    }
    Expect(treeCount > 0, "Environment scene should contain Terrain tree instances.");

    TerrainTreeChoppingRegistry[] registries = Object.FindObjectsOfType<TerrainTreeChoppingRegistry>(true);
    Expect(registries.Length == 1, "Environment scene should contain exactly one TerrainTreeChoppingRegistry, got " + registries.Length + ".");
    Debug.Log("TerrainTreeChoppingRegistry environment validation passed.");
}
```

**Step 2: Run both self-tests**

Run:

```csharp
TerrainTreeChoppingRegistrySelfTest.Run();
TerrainTreeChoppingRegistrySelfTest.RunEnvironmentSceneValidation();
```

Expected: both pass.

**Step 3: Commit**

```bash
git add Assets/Editor/TerrainTreeChoppingRegistrySelfTest.cs
git commit -m "test: validate terrain tree chopping scene setup"
```

---

### Task 8: Runtime Verification In Solo Host Flow

**Objective:** Prove the feature works through the real MainMenu to Environment Fusion flow.

**Files:**
- No expected code changes.
- Scene/runtime verification only.

**Step 1: Clear console**

Use unityMCP:

```text
read_console(action="clear")
```

**Step 2: Enter Play Mode from MainMenu**

Use existing tested flow:

```csharp
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
UnityEditor.EditorApplication.isPlaying = true;
```

Then use the existing runtime control path that was verified in earlier forest work:

```csharp
Object.FindObjectOfType<MainMenuController>().OpenRoomFlow();
Object.FindObjectOfType<MainMenuController>().PlaySolo();
Object.FindObjectOfType<PhotonFusionRoomController>().HostStartForest();
```

Adjust the exact runtime calls if the MainMenu controller method names differ at implementation time.

**Step 3: Chop a nearby Terrain tree**

Use either manual controls or a small temporary `execute_code` snippet that finds the local `PlayerAxeCombat`, equips the axe if needed through existing methods, positions the player near a tree candidate, and invokes attack.

Expected runtime behavior:

- Axe swing resolves to a Terrain tree candidate.
- Tree disappears from Terrain rendering.
- A LeanTween falling proxy plays.
- One shared wood drop appears.
- Repeated swings against the same id do not spawn duplicate wood.

**Step 4: Check console**

Use:

```text
read_console(action="get", types=["error", "warning"], count="50", format="detailed", include_stacktrace=true)
```

Expected: no new errors. Known existing warning about extra `AudioListener` may appear and is acceptable if unchanged.

**Step 5: Capture evidence screenshot**

Use:

```text
manage_camera(action="screenshot", capture_source="game_view", include_image=true, max_resolution=512, screenshot_file_name="terrain-tree-chopping-runtime")
```

Do not commit screenshots unless the user asks.

---

### Task 9: Final Verification And Commit Hygiene

**Objective:** Verify final code state and keep commits limited to intended files.

**Files:**
- Review all changed files.

**Step 1: Run Unity self-tests**

Run via unityMCP `execute_code`:

```csharp
ForestMobileOptimizationSelfTest.Run();
TerrainTreeChoppingRegistrySelfTest.Run();
TerrainTreeChoppingRegistrySelfTest.RunEnvironmentSceneValidation();
FusionPlayerOwnerSetupSelfTest.Run();
```

Expected:

- `ForestMobileOptimizationSelfTest passed.`
- `TerrainTreeChoppingRegistrySelfTest passed.`
- `TerrainTreeChoppingRegistry environment validation passed.`
- `FusionPlayerOwnerSetupSelfTest passed.`

**Step 2: Check Unity console**

Expected: no errors.

**Step 3: Inspect git status and diffs**

Run:

```bash
git status --short
git diff -- Assets/Scripts/Environment/TerrainTreeChoppingRegistry.cs Assets/Scripts/Player/Combat/PlayerAxeCombat.cs Assets/Scripts/PhotonFusion/FusionPlayerCombat.cs Assets/Editor/TerrainTreeChoppingRegistrySelfTest.cs Assets/Scenes/Environment.unity
```

Expected: only intended code/scene changes plus pre-existing unrelated dirty files.

**Step 4: Final commit if needed**

If any final cleanup changes remain:

```bash
git add Assets/Scripts/Environment/TerrainTreeChoppingRegistry.cs Assets/Scripts/Player/Combat/PlayerAxeCombat.cs Assets/Scripts/PhotonFusion/FusionPlayerCombat.cs Assets/Editor/TerrainTreeChoppingRegistrySelfTest.cs Assets/Scenes/Environment.unity
git commit -m "fix: stabilize terrain tree chopping"
```

**Step 5: Report actual verification evidence**

Report only commands that were actually run and their observed output. Do not claim multiplayer two-client verification unless it was actually performed.
