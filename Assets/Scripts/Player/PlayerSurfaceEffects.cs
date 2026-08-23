using UnityEngine;

/// <summary>
/// Efek permukaan untuk player: slowdown salju tebal (+ sedikit licin di es)
/// dan jejak kaki di salju.
///
/// - Slowdown dikirim ke FusionPlayerMovement.SetSurfaceSpeedMultiplier()
///   sehingga kecepatan GERAK dan ANIMASI melambat bersama (blend tree ikut).
/// - Jejak kaki: pool quad transparan yang diletakkan di titik langkah
///   (mengikuti event footfall dari PlayerFootstepAudio), hanya di surface snow,
///   hanya saat benar-benar bergerak. Menyusut/menghilang setelah lifetime —
///   murah, tanpa Decal Projector / Renderer Feature.
/// - Non-networked: tiap client menampilkan jejak dari posisi proxy pemain lain
///   secara lokal (event footfall ikut menyala di proxy karena audio juga jalan).
///
/// Surface ID: 0=wood, 1=snow, 2=ice (konsisten dengan PlayerFootstepAudio).
/// </summary>
[RequireComponent(typeof(PlayerFootstepAudio))]
public class PlayerSurfaceEffects : MonoBehaviour
{
    [Header("Ref (auto jika kosong)")]
    [SerializeField] private PlayerSurfaceDetector surfaceDetector;
    [SerializeField] private PlayerFootstepAudio footstepAudio;
    [SerializeField] private FusionPlayerMovement movement;

    [Header("Slowdown")]
    [Tooltip("Kalikan kecepatan saat berjalan di salju.")]
    [SerializeField] private float snowSpeedMultiplier = 0.72f;
    [Tooltip("Kalikan kecepatan saat berada di es.")]
    [SerializeField] private float iceSpeedMultiplier = 1.06f;

    [Header("Jejak Kaki Salju")]
    [SerializeField] private bool enableSnowTrails = true;
    [SerializeField] private float footprintSize = 0.32f;
    [SerializeField] private float footprintLifetime = 12f;
    [SerializeField] private int maxFootprints = 90;
    [Tooltip("Tinggi quad di atas permukaan tanah (anti z-fighting).")]
    [SerializeField] private float yOffsetAboveGround = 0.03f;
    [Tooltip("Jarak planar minimum antar cetakan agar tidak menumpuk.")]
    [SerializeField] private float minStepSpacing = 0.45f;
    [Tooltip("Kecepatan minimum agar langkah meninggalkan jejak (slide/dorongan dinding tidak).")]
    [SerializeField] private float minTrailSpeed = 0.8f;

    private struct FootprintSlot
    {
        public GameObject Go;
        public Transform Tr;
        public float SpawnTime;
        public bool Active;
    }

    private FootprintSlot[] pool;
    private int nextSlot;
    private bool alternateSide;          // kiri/kanan bergantian seperti pola jalan
    private Vector3 lastPrintPos;
    private bool hasLastPrint;
    private Material sharedPrintMaterial;
    private Mesh quadMesh;
    private bool effectsActive;

    private void Awake()
    {
        if (surfaceDetector == null) surfaceDetector = GetComponent<PlayerSurfaceDetector>();
        if (footstepAudio == null) footstepAudio = GetComponent<PlayerFootstepAudio>();
        if (movement == null) movement = GetComponent<FusionPlayerMovement>();

        BuildResources();
        BuildPool();
    }

    private void OnEnable()
    {
        if (footstepAudio != null)
        {
            footstepAudio.Footfall += HandleFootfall;
        }
        effectsActive = true;
    }

    private void OnDisable()
    {
        if (footstepAudio != null)
        {
            footstepAudio.Footfall -= HandleFootfall;
        }
        effectsActive = false;
        // Jangan biarkan player lambat selamanya kalau komponen dimatikan.
        if (movement != null) movement.SetSurfaceSpeedMultiplier(1f);
    }

    /// <summary>Dipanggil tiap footfall dari jalur sync animasi maupun timer fallback.</summary>
    private void HandleFootfall(int surfaceId, float horizontalSpeed)
    {
        if (!effectsActive || !enableSnowTrails || surfaceDetector == null) return;
        if (surfaceDetector.CurrentSurface != 1) return;            // hanya salju
        if (horizontalSpeed < minTrailSpeed) return;

        Vector3 pos = transform.position;
        Vector3 planar = new Vector3(pos.x - lastPrintPos.x, 0f, pos.z - lastPrintPos.z);
        if (hasLastPrint && planar.magnitude < minStepSpacing) return;

        if (!TryGetGroundPoint(pos, out Vector3 groundPoint, out Vector3 groundNormal)) return;

        PlaceFootprint(groundPoint, groundNormal);
        lastPrintPos = pos;
        hasLastPrint = true;
    }

    private void PlaceFootprint(Vector3 groundPoint, Vector3 groundNormal)
    {
        if (pool == null || pool.Length == 0) return;

        int idx = nextSlot;
        nextSlot = (nextSlot + 1) % pool.Length;
        FootprintSlot slot = pool[idx];
        if (slot.Go == null) return;

        // Offset kiri/kanan relatif arah hadap player supaya jejak terlihat natural.
        alternateSide = !alternateSide;
        float side = alternateSide ? 1f : -1f;
        Vector3 right = Vector3.Cross(groundNormal, transform.forward);
        if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
        right.Normalize();

        Vector3 spawnPos = groundPoint + groundNormal * yOffsetAboveGround + right * (side * footprintSize * 0.55f);
        Quaternion rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, groundNormal).normalized, groundNormal);

        slot.Tr.position = spawnPos;
        slot.Tr.rotation = rot;
        slot.Tr.localScale = new Vector3(footprintSize, footprintSize * 1.35f, 1f); // quad: X lebar, Y panjang
        slot.SpawnTime = Time.time;
        slot.Active = true;
        slot.Go.SetActive(true);

        pool[idx] = slot;
    }

    private void Update()
    {
        if (movement != null && surfaceDetector != null && effectsActive)
        {
            int s = surfaceDetector.CurrentSurface;
            float mult = s == 1 ? snowSpeedMultiplier : (s == 2 ? iceSpeedMultiplier : 1f);
            movement.SetSurfaceSpeedMultiplier(mult);
        }
        DecayFootprints();
    }

    private void DecayFootprints()
    {
        if (pool == null) return;
        for (int i = 0; i < pool.Length; i++)
        {
            if (!pool[i].Active) continue;
            float age = Time.time - pool[i].SpawnTime;
            if (age >= footprintLifetime)
            {
                FootprintSlot slot = pool[i];
                slot.Active = false;
                slot.Go.SetActive(false);
                pool[i] = slot;
                continue;
            }
            // 30% umur terakhir: menyusut ke 0 seolah tertimbun salju.
            float shrinkStart = footprintLifetime * 0.7f;
            if (age > shrinkStart)
            {
                float t = (age - shrinkStart) / (footprintLifetime - shrinkStart);
                float baseScaleX = footprintSize;
                float baseScaleY = footprintSize * 1.35f;
                float k = 1f - t;
                FootprintSlot slot = pool[i];
                slot.Tr.localScale = new Vector3(baseScaleX * k, baseScaleY * k, 1f);
                pool[i] = slot;
            }
        }
    }

    /// <summary>Cari titik tanah tepat di bawah posisi (abaikan collider milik player).</summary>
    private bool TryGetGroundPoint(Vector3 fromPosition, out Vector3 point, out Vector3 normal)
    {
        point = fromPosition;
        normal = Vector3.up;
        Vector3 origin = fromPosition + Vector3.up * 1.6f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 3.2f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null) continue;
            if (col.transform == transform || col.transform.IsChildOf(transform)) continue;
            point = hits[i].point;
            normal = hits[i].normal;
            return true;
        }
        return false;
    }

    private void BuildResources()
    {
        quadMesh = BuildQuadMesh();

        sharedPrintMaterial = TryBuildTransparentMaterial();
        if (sharedPrintMaterial == null)
        {
            Debug.LogWarning("[SurfaceEffects] Tidak menemukan shader transparan yang cocok — jejak kaki dimatikan.", this);
            enableSnowTrails = false;
        }
    }

    /// <summary>Quad 1x1 menghadap +Z (mirip primitive Quad).</summary>
    private static Mesh BuildQuadMesh()
    {
        Mesh mesh = new Mesh { name = "FootprintQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
        };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        return mesh;
    }

    /// <summary>Material transparan putih polos — coba shader URP dulu, fallback legacy.</summary>
    private static Material TryBuildTransparentMaterial()
    {
        Material mat = null;

        Shader urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (urpUnlit != null)
        {
            mat = new Material(urpUnlit);
            mat.name = "FootprintPrint";
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.88f, 0.92f, 1f, 0.75f));
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f); // Transparent
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }

        Shader legacy = Shader.Find("Legacy Shaders/Transparent/Diffuse");
        if (legacy != null)
        {
            mat = new Material(legacy);
            mat.name = "FootprintPrint";
            mat.color = new Color(0.88f, 0.92f, 1f, 0.75f);
            return mat;
        }

        return null;
    }

    private void BuildPool()
    {
        if (!enableSnowTrails || sharedPrintMaterial == null) return;

        pool = new FootprintSlot[maxFootprints];
        for (int i = 0; i < maxFootprints; i++)
        {
            GameObject go = new GameObject("Footprint_" + i);
            go.SetActive(false);
            // Jangan tampil di hierarchy DAN jangan pernah ikut tersimpan ke file
            // scene (pernah menyebabkan 90 quad ter-bake ke Gameplay.unity ketika
            // scene di-save dari sesi probe edit-mode).
            go.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor;

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = quadMesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = sharedPrintMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            pool[i] = new FootprintSlot { Go = go, Tr = go.transform, SpawnTime = 0f, Active = false };
        }
    }
}
