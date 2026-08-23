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
        public Vector3 InstanceScale;
    }

    private sealed class TerrainSnapshot
    {
        public int TerrainOrdinal;
        public Terrain Terrain;
        public TreeInstance[] OriginalInstances;
        public readonly HashSet<int> HiddenTreeIds = new HashSet<int>();
    }

    private sealed class TreeRecord
    {
        public int TreeId;
        public TerrainSnapshot Snapshot;
        public Vector3 WorldPosition;
        public GameObject PrototypePrefab;
        public Vector3 InstanceScale;
        public float Health;
        public bool Depleted;
    }

    [SerializeField] private float defaultTreeHealth = 3f;
    [SerializeField] private float approximateChopRadius = 1.1f;

    [Header("Falling Proxy")]
    [SerializeField] private float fallDurationSeconds = 1.1f;
    [SerializeField] private float fallenProxyLifetimeSeconds = 6f;
    [SerializeField] private LeanTweenType fallEase = LeanTweenType.easeInBack;

    [Header("Depletion Sync")]
    [SerializeField] private GameObject depletionStatePrefab;

    private FusionTerrainTreeDepletionState spawnedDepletionState;
    private float nextSpawnRetryTime;
    private const float SpawnRetryInterval = 0.5f;

    private readonly List<TerrainSnapshot> snapshots = new List<TerrainSnapshot>();
    private readonly List<TreeRecord> records = new List<TreeRecord>();
    private readonly Dictionary<int, TreeRecord> recordsById = new Dictionary<int, TreeRecord>();
    private TerrainTreeColliderGenerator colliderGenerator;

    [Header("Tree Colliders")]
    [SerializeField] private bool generateTreeColliders = true;
    [SerializeField] private float trunkColliderRadius = 0.45f;
    [SerializeField] private float trunkColliderHeight = 3.5f;
    [SerializeField] private bool addRockBoxColliders = true;

    /// <summary>Tampilan read-only satu pohon untuk generator collider.</summary>
    public struct TreeRecordView
    {
        public int TreeId;
        public Vector3 WorldPosition;
        public GameObject PrototypePrefab;
        public bool Depleted;
    }

    public int TreeCount => records.Count;

    public bool HasUniqueTreeIds()
    {
        return records.Count == recordsById.Count;
    }

    /// <summary>Ambil data hit pohon berdasarkan ID (untuk jalur collider fisika).</summary>
    public bool TryGetTreeHit(int treeId, out TreeHit hit)
    {
        if (recordsById.TryGetValue(treeId, out TreeRecord record) && !record.Depleted)
        {
            hit = CreateHit(record);
            return true;
        }

        hit = default;
        return false;
    }

    private void Awake()
    {
        Rebuild(Terrain.activeTerrains);
    }

    private void OnDisable()
    {
        RestoreAllRuntimeTreeInstances();
        if (colliderGenerator != null)
        {
            colliderGenerator.ClearChunks();
        }
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
        RestoreAllRuntimeTreeInstances();
        snapshots.Clear();
        records.Clear();
        recordsById.Clear();

        if (terrains == null)
        {
            return;
        }

        System.Array.Sort(terrains, CompareTerrainsForStableIds);

        for (int terrainIndex = 0; terrainIndex < terrains.Length; terrainIndex++)
        {
            Terrain terrain = terrains[terrainIndex];
            if (terrain == null || terrain.terrainData == null)
            {
                continue;
            }

            TerrainSnapshot snapshot = new TerrainSnapshot
            {
                TerrainOrdinal = terrainIndex,
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
                int treeId = ComputeTreeId(terrainIndex, treeIndex);
                GameObject prototype = instance.prototypeIndex >= 0 && instance.prototypeIndex < prototypes.Length
                    ? prototypes[instance.prototypeIndex].prefab
                    : null;

                TreeRecord record = new TreeRecord
                {
                    TreeId = treeId,
                    Snapshot = snapshot,
                    WorldPosition = terrainPosition + Vector3.Scale(instance.position, terrainSize),
                    PrototypePrefab = prototype,
                    InstanceScale = new Vector3(instance.widthScale, instance.heightScale, instance.widthScale),
                    Health = Mathf.Max(1f, defaultTreeHealth)
                };
                records.Add(record);
                recordsById[treeId] = record;
            }
        }

        BuildTreeColliders();
    }

    private void BuildTreeColliders()
    {
        if (!generateTreeColliders)
        {
            return;
        }

        if (colliderGenerator == null)
        {
            colliderGenerator = TerrainTreeColliderGenerator.EnsureFor(this);
        }

        List<TreeRecordView> views = new List<TreeRecordView>(records.Count);
        for (int i = 0; i < records.Count; i++)
        {
            TreeRecord record = records[i];
            views.Add(new TreeRecordView
            {
                TreeId = record.TreeId,
                WorldPosition = record.WorldPosition,
                PrototypePrefab = record.PrototypePrefab,
                Depleted = record.Depleted
            });
        }

        colliderGenerator.BuildFromRecords(views, trunkColliderRadius, trunkColliderHeight, addRockBoxColliders);
    }

    public bool TryFindBestTreeForChop(Vector3 origin, Vector3 direction, float maxDistance, float minForwardDot, out TreeHit hit)
    {
        hit = default;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        Vector3 forward = direction.normalized;
        float searchDistance = Mathf.Max(0.1f, maxDistance + Mathf.Max(0f, approximateChopRadius));
        float maxSqr = searchDistance * searchDistance;
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

        hit = CreateHit(best);
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
        hit = CreateHit(record);

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

        hit = CreateHit(record);
        if (record.Depleted)
        {
            return false;
        }

        record.Depleted = true;
        HideTree(record);
        return true;
    }

    public void ApplyNetworkedDepletion(IEnumerable<int> treeIds)
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
        visual.transform.localScale = Vector3.Scale(visual.transform.localScale, record.InstanceScale);
        DisableProxyColliders(visual);

        Vector3 fallAxis = Vector3.Cross(Vector3.up, direction);
        if (fallAxis.sqrMagnitude <= 0.0001f)
        {
            fallAxis = Vector3.right;
        }

        fallAxis.Normalize();
        Quaternion targetRotation = Quaternion.AngleAxis(88f, fallAxis) * pivot.transform.rotation;
        LeanTween.rotate(pivot, targetRotation.eulerAngles, Mathf.Max(0.1f, fallDurationSeconds))
            .setEase(fallEase)
            .setOnComplete(() => Destroy(pivot, Mathf.Max(0.1f, fallenProxyLifetimeSeconds)));

        return true;
    }

    private static TreeHit CreateHit(TreeRecord record)
    {
        return new TreeHit
        {
            TreeId = record.TreeId,
            WorldPosition = record.WorldPosition,
            HitPoint = record.WorldPosition + Vector3.up * 0.75f,
            PrototypePrefab = record.PrototypePrefab,
            InstanceScale = record.InstanceScale
        };
    }

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
            if (colliders[i] != null)
            {
                colliders[i].enabled = false;
            }
        }
    }

    private static int ComputeTreeId(int terrainOrdinal, int treeIndex)
    {
        unchecked
        {
            return (terrainOrdinal * 1_000_000) + treeIndex + 1;
        }
    }

    private void HideTree(TreeRecord record)
    {
        if (record.Snapshot.HiddenTreeIds.Add(record.TreeId))
        {
            RebuildRuntimeTreeInstances(record.Snapshot);
            if (colliderGenerator != null)
            {
                colliderGenerator.SetTreeEnabled(record.TreeId, false);
            }
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

    private void RestoreAllRuntimeTreeInstances()
    {
        for (int i = 0; i < snapshots.Count; i++)
        {
            TerrainSnapshot snapshot = snapshots[i];
            if (snapshot == null || snapshot.Terrain == null || snapshot.Terrain.terrainData == null || snapshot.OriginalInstances == null)
            {
                continue;
            }

            if (snapshot.HiddenTreeIds.Count == 0)
            {
                continue;
            }

            snapshot.Terrain.terrainData.treeInstances = snapshot.OriginalInstances;
            snapshot.Terrain.Flush();
            snapshot.HiddenTreeIds.Clear();
        }
    }

    private void Update()
    {
        TrySpawnDepletionState();
    }

    private void TrySpawnDepletionState()
    {
        if (FusionTerrainTreeDepletionState.Instance != null || spawnedDepletionState != null)
        {
            return;
        }

        Fusion.NetworkRunner runner = FindObjectOfType<Fusion.NetworkRunner>();
        if (runner == null || !runner.IsRunning || !runner.IsSharedModeMasterClient)
        {
            return;
        }

        if (Time.unscaledTime < nextSpawnRetryTime)
        {
            return;
        }

        nextSpawnRetryTime = Time.unscaledTime + SpawnRetryInterval;
        if (depletionStatePrefab == null)
        {
            return;
        }

        try
        {
            Fusion.NetworkObject spawnedObject = runner.Spawn(depletionStatePrefab);
            if (spawnedObject != null)
            {
                spawnedDepletionState = spawnedObject.GetComponent<FusionTerrainTreeDepletionState>();
            }
        }
        catch (System.Exception exception)
        {
            // NRE di sini biasanya race startup: Spawn dipanggil ketika scene jaringan
            // Fusion belum selesai dimuat. Retry otomatis (SpawnRetryInterval) — log
            // exception PENUH supaya penyebab asli tidak hilang.
            Debug.LogWarning("[TerrainTreeChoppingRegistry] Depletion state spawn deferred (akan retry tiap "
                + SpawnRetryInterval + "s): " + exception);
        }
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
        if (xCompare != 0)
        {
            return xCompare;
        }

        int zCompare = ap.z.CompareTo(bp.z);
        if (zCompare != 0)
        {
            return zCompare;
        }

        return ap.y.CompareTo(bp.y);
    }
}
