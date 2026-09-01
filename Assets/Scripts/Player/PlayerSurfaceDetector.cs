using UnityEngine;

/// <summary>
/// Deteksi permukaan di bawah player secara otomatis, urutan prioritas:
/// 0. TAG OVERRIDE (tertinggi): tag collider / induknya "Wood"/"Snow"/"Ice"
///    -> langsung dipakai, melewati semua heuristik. Pakai ini untuk lantai
///    buatan (base kayu, dermaga, dsb.) yang menumpuk terrain.
/// 1. Raycast ke bawah dari kaki player (difilter via LayerMask hitMask).
/// 2. Kena Terrain -> baca splat weight dominan (TerrainData.GetAlphamaps)
///    -> nama Terrain Layer dipetakan ke surface ID.
/// 3. Kena object lain -> cek nama material (misal mengandung "ice") -> surface ID.
/// Hasil diteruskan ke PlayerFootstepAudio.SetSurface().
/// Lookup di-throttle (default 0.25s) karena GetAlphamaps mengalokasikan array.
/// </summary>
[RequireComponent(typeof(PlayerFootstepAudio))]
public class PlayerSurfaceDetector : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private PlayerFootstepAudio footstepAudio;
    [SerializeField] private CharacterController controller;

    [Header("Raycast")]
    [SerializeField] private float raycastOriginHeight = 1.5f; // tinggi start ray dari pivot
    [SerializeField] private float raycastMaxDistance = 4f;
    [SerializeField] private LayerMask hitMask = ~0;
    [Tooltip("Hit ber-tag surface (Wood/Snow/Ice) yang sedikit lebih bawah dari hit terdekat tetap diutamakan (meter). Berguna saat lantai buatan tertimbun sedikit di bawah terrain yang menonjol.")]
    [SerializeField] private float tagPreferenceWindow = 0.5f;

    [Header("Throttle")]
    [SerializeField] private float detectInterval = 0.25f;

    [Header("Mapping: Tag override -> surface ID (prioritas tertinggi, dicek ke collider & seluruh induknya)")]
    [SerializeField] private string[] woodTagNames = { "Wood" };
    [SerializeField] private string[] snowTagNames = { "Snow" };
    [SerializeField] private string[] iceTagNames = { "Ice" };
    [SerializeField] private string[] grassTagNames = { "Grass" };
    [SerializeField] private string[] metalTagNames = { "Metal" };
    [SerializeField] private string[] mudTagNames = { "Mud" };

    [Header("Mapping: nama Terrain Layer -> surface ID (0=wood,1=snow,2=ice,3=grass,4=metal,5=mud)")]
    [SerializeField] private string[] snowLayerNames = { "Snow" };
    [SerializeField] private string[] woodLayerNames = { "Rock", "Dirt", "Ground" };
    [SerializeField] private string[] iceLayerNames = { "Ice" };
    [SerializeField] private string[] grassLayerNames = { "Grass" };
    [SerializeField] private string[] metalLayerNames = { "Metal", "Iron", "Steel" };
    [SerializeField] private string[] mudLayerNames = { "Mud" };

    [Header("Mapping non-terrain: substring nama material -> surface")]
    [SerializeField] private string iceMaterialKeyword = "ice";
    [SerializeField] private string snowMaterialKeyword = "snow";
    [SerializeField] private string grassMaterialKeyword = "grass";
    [SerializeField] private string metalMaterialKeyword = "metal";
    [SerializeField] private string mudMaterialKeyword = "mud";

    private float nextDetectTime;
    private int lastSurface = -1;

    /// <summary>ID permukaan aktif terakhir (0=wood,1=snow,2=ice,3=grass,4=metal,5=mud; -1=belum terdeteksi).</summary>
    public int CurrentSurface => lastSurface;

    /// <summary>Dipanggil saat permukaan berganti (argumen = ID surface baru).</summary>
    public event System.Action<int> SurfaceChanged;

    private void Awake()
    {
        if (footstepAudio == null) footstepAudio = GetComponent<PlayerFootstepAudio>();
        if (controller == null) controller = GetComponent<CharacterController>();
    }

    private void Update()
    {
        if (footstepAudio == null || Time.time < nextDetectTime) return;
        nextDetectTime = Time.time + detectInterval;

        Vector3 origin = transform.position + Vector3.up * raycastOriginHeight;
        RaycastHit hit;
        if (!TryRaycastGround(origin, out hit))
        {
            return; // di udara / belum ada ground — pertahankan surface terakhir
        }

        int surface = ResolveSurface(hit);
        if (surface != lastSurface)
        {
            lastSurface = surface;
            footstepAudio.SetSurface(surface);
            SurfaceChanged?.Invoke(surface);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[SurfaceDetector] " + hit.collider.name + " -> surface " + surface + " (" + SurfaceName(surface) + ")");
#endif
        }
    }

    /// <summary>
    /// Raycast ke bawah, melewati collider milik player sendiri.
    /// Prioritas: hit terdekat; TAPI hit ber-tag surface (Wood/Snow/Ice) yang
    /// berada dalam jendela tagPreferenceWindow di bawah hit terdekat diutamakan —
    /// menangani lantai buatan yang sedikit tertimbun di bawah terrain menonjol.
    /// </summary>
    private bool TryRaycastGround(Vector3 origin, out RaycastHit groundHit)
    {
        var hits = Physics.RaycastAll(origin, Vector3.down, raycastOriginHeight + raycastMaxDistance, hitMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        int firstIdx = -1;
        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i].collider;
            if (col == null) continue;
            if (col.transform == transform || col.transform.IsChildOf(transform)) continue; // skip diri sendiri
            if (controller != null && col == controller) continue;
            if (firstIdx < 0) firstIdx = i;

            // Hit ber-tag surface dalam jendela toleransi mengalahkan hit terdekat non-tag.
            if (i > firstIdx && (hits[i].distance - hits[firstIdx].distance) <= tagPreferenceWindow && HasSurfaceTag(hits[i].collider.transform))
            {
                groundHit = hits[i];
                return true;
            }
        }

        if (firstIdx >= 0)
        {
            groundHit = hits[firstIdx];
            return true;
        }
        groundHit = default(RaycastHit);
        return false;
    }

    /// <summary>True jika collider / induknya membawa salah satu tag surface.</summary>
    private bool HasSurfaceTag(Transform t)
    {
        while (t != null)
        {
            if (ContainsAny(t.tag, woodTagNames) || ContainsAny(t.tag, snowTagNames) || ContainsAny(t.tag, iceTagNames)
                || ContainsAny(t.tag, grassTagNames) || ContainsAny(t.tag, metalTagNames) || ContainsAny(t.tag, mudTagNames)) return true;
            t = t.parent;
        }
        return false;
    }

    private int ResolveSurface(RaycastHit hit)
    {
        // 0) TAG OVERRIDE — prioritas tertinggi: cek tag collider dan seluruh induknya.
        // Berguna untuk lantai buatan (base kayu dsb.) yang menumpuk terrain snow.
        Transform t = hit.collider.transform;
        while (t != null)
        {
            if (ContainsAny(t.tag, woodTagNames)) return 0;
            if (ContainsAny(t.tag, snowTagNames)) return 1;
            if (ContainsAny(t.tag, iceTagNames)) return 2;
            if (ContainsAny(t.tag, grassTagNames)) return 3;
            if (ContainsAny(t.tag, metalTagNames)) return 4;
            if (ContainsAny(t.tag, mudTagNames)) return 5;
            t = t.parent;
        }

        // 1) Terrain splatmap dominan
        Terrain terrain = hit.collider.GetComponent<Terrain>();
        if (terrain != null && terrain.terrainData != null)
        {
            string layerName = GetDominantTerrainLayerName(terrain, hit.point);
            if (layerName != null)
            {
                if (ContainsAny(layerName, iceLayerNames)) return 2;
                if (ContainsAny(layerName, snowLayerNames)) return 1;
                if (ContainsAny(layerName, grassLayerNames)) return 3;
                if (ContainsAny(layerName, metalLayerNames)) return 4;
                if (ContainsAny(layerName, mudLayerNames)) return 5;
                if (ContainsAny(layerName, woodLayerNames)) return 0;
                return 0; // layer tak dikenal -> default wood
            }
        }

        // 2) Non-terrain: nama material
        var renderer = hit.collider.GetComponentInChildren<Renderer>();
        if (renderer != null && renderer.sharedMaterials != null)
        {
            for (int i = 0; i < renderer.sharedMaterials.Length; i++)
            {
                var mat = renderer.sharedMaterials[i];
                if (mat == null || string.IsNullOrEmpty(mat.name)) continue;
                string n = mat.name.ToLowerInvariant();
                if (n.Contains(iceMaterialKeyword)) return 2;
                if (!string.IsNullOrEmpty(snowMaterialKeyword) && n.Contains(snowMaterialKeyword)) return 1;
                if (!string.IsNullOrEmpty(grassMaterialKeyword) && n.Contains(grassMaterialKeyword)) return 3;
                if (!string.IsNullOrEmpty(metalMaterialKeyword) && n.Contains(metalMaterialKeyword)) return 4;
                if (!string.IsNullOrEmpty(mudMaterialKeyword) && n.Contains(mudMaterialKeyword)) return 5;
            }
        }

        return 0; // default
    }

    /// <returns>Nama Terrain Layer dengan splat weight tertinggi pada titik dunia; null jika di luar terrain.</returns>
    private string GetDominantTerrainLayerName(Terrain terrain, Vector3 worldPoint)
    {
        TerrainData td = terrain.terrainData;
        Vector3 tPos = terrain.transform.position;
        Vector3 size = td.size;

        float nx = Mathf.Clamp01((worldPoint.x - tPos.x) / Mathf.Max(0.001f, size.x));
        float nz = Mathf.Clamp01((worldPoint.z - tPos.z) / Mathf.Max(0.001f, size.z));

        int mapX = Mathf.FloorToInt(nx * (td.alphamapWidth - 1));
        int mapY = Mathf.FloorToInt(nz * (td.alphamapHeight - 1));
        mapX = Mathf.Clamp(mapX, 0, td.alphamapWidth - 1);
        mapY = Mathf.Clamp(mapY, 0, td.alphamapHeight - 1);

        float[,,] alpha = td.GetAlphamaps(mapX, mapY, 1, 1);
        if (alpha == null || alpha.Length == 0) return null;

        int layerCount = alpha.GetLength(2);
        int best = 0;
        for (int i = 1; i < layerCount; i++)
        {
            if (alpha[0, 0, i] > alpha[0, 0, best]) best = i;
        }

        var layers = td.terrainLayers;
        if (best >= layers.Length || layers[best] == null) return null;
        return layers[best].name;
    }

    private bool ContainsAny(string name, string[] keywords)
    {
        if (keywords == null) return false;
        for (int i = 0; i < keywords.Length; i++)
        {
            if (!string.IsNullOrEmpty(keywords[i]) &&
                name.IndexOf(keywords[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    private static string SurfaceName(int id)
    {
        switch (id)
        {
            case 1: return "snow";
            case 2: return "ice";
            case 3: return "grass";
            case 4: return "metal";
            case 5: return "mud";
            default: return "wood";
        }
    }
}
