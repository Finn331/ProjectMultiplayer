using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Membangun collider tabrakan untuk setiap pohon/batu terrain (tree instance)
/// saat runtime. Diperlukan karena di Unity 2022.3, terrain tree dari prefab
/// biasa TIDAK otomatis menghasilkan collider (hanya Tree Editor asset yang
/// bisa), sehingga pemain bisa menembus semua pohon.
///
/// Collider dibuat dalam satu GameObject induk per terrain dengan
/// HideFlags.DontSaveInEditor agar tidak pernah ter-bake ke file scene, dan
/// dikelompokkan per chunk grid agar query fisika tetap murah.
/// Saat pohon ditebang (depleted), collider-nya dimatikan bersamaan dengan
/// penyembunyian visual oleh registry.
/// </summary>
[DisallowMultipleComponent]
public class TerrainTreeColliderGenerator : MonoBehaviour
{
    private const float ChunkSize = 64f;

    private readonly Dictionary<int, GameObject> chunks = new Dictionary<int, GameObject>();
    private readonly Dictionary<int, Collider> collidersById = new Dictionary<int, Collider>();
    private bool built;

    public static TerrainTreeColliderGenerator EnsureFor(TerrainTreeChoppingRegistry registry)
    {
        if (registry == null)
        {
            return null;
        }

        TerrainTreeColliderGenerator generator = registry.GetComponent<TerrainTreeColliderGenerator>();
        if (generator == null)
        {
            generator = registry.gameObject.AddComponent<TerrainTreeColliderGenerator>();
        }

        return generator;
    }

    public void BuildFromRecords(
        List<TerrainTreeChoppingRegistry.TreeRecordView> records,
        float trunkRadius,
        float trunkHeight,
        bool addRockBoxColliders)
    {
        ClearChunks();

        if (records == null || records.Count == 0)
        {
            return;
        }

        Transform parent = transform;

        for (int i = 0; i < records.Count; i++)
        {
            var view = records[i];

            if (view.WorldPosition == default(Vector3) && view.TreeId <= 0)
            {
                continue;
            }

            int chunkKey = ComputeChunkKey(view.WorldPosition);
            GameObject chunk;
            if (!chunks.TryGetValue(chunkKey, out chunk))
            {
                chunk = new GameObject("TerrainTreeColliders_" + chunkKey);
                chunk.transform.SetParent(parent, false);
                chunk.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor;
                chunks.Add(chunkKey, chunk);
            }

            GameObject node = new GameObject("TreeCol_" + view.TreeId);
            node.transform.SetParent(chunk.transform, false);
            node.transform.position = view.WorldPosition;
            node.layer = gameObject.layer;

            bool isRock = view.PrototypePrefab != null && view.PrototypePrefab.name.Contains("Rock");
            Collider col;
            if (isRock && addRockBoxColliders)
            {
                BoxCollider box = node.AddComponent<BoxCollider>();
                box.size = new Vector3(4.3f, 3.5f, 3.6f);
                box.center = new Vector3(0f, 1.75f, 0f);
                col = box;
            }
            else
            {
                CapsuleCollider capsule = node.AddComponent<CapsuleCollider>();
                capsule.radius = trunkRadius;
                capsule.height = Mathf.Max(trunkHeight, trunkRadius * 2f);
                capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);
                col = capsule;
            }

            node.AddComponent<TerrainTreeColliderMarker>().TreeId = view.TreeId;
            collidersById[view.TreeId] = col;

            if (view.Depleted)
            {
                col.enabled = false;
            }
        }

        built = true;
    }

    public void SetTreeEnabled(int treeId, bool enabled)
    {
        Collider col;
        if (collidersById.TryGetValue(treeId, out col) && col != null)
        {
            col.enabled = enabled;
        }
    }

    public void ClearChunks()
    {
        foreach (KeyValuePair<int, GameObject> kv in chunks)
        {
            if (kv.Value != null)
            {
                Destroy(kv.Value);
            }
        }

        chunks.Clear();
        collidersById.Clear();
        built = false;
    }

    private static int ComputeChunkKey(Vector3 worldPosition)
    {
        unchecked
        {
            int cx = Mathf.FloorToInt(worldPosition.x / ChunkSize);
            int cz = Mathf.FloorToInt(worldPosition.z / ChunkSize);
            return (cx * 73856093) ^ (cz * 19349663);
        }
    }

    public bool IsBuilt => built;
}
