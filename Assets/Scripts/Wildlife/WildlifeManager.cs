using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawner wildlife untuk scene Environment (taiga): polar bear, arctic wolf, deer, arctic fox.
/// v1 LOKAL per klien (non-networked) — tiap client punya dunia hewan sendiri.
/// SELF-BOOTSTRAP: dibuat otomatis saat scene "Environment" termuat (tanpa bake objek ke
/// scene file); musnah otomatis saat scene berganti bersama seluruh hewan.
/// Trigger spawn: menunggu karakter player lokal ber-authority muncul, lalu spawn ring
/// 25–60 m di atas tanah (raycast, kompatibel multi-tile taiga).
/// </summary>
public class WildlifeManager : MonoBehaviour
{
    public static WildlifeManager Instance { get; private set; }

    private const string ForestSceneName = "Environment";

    [System.Serializable]
    public struct SpeciesConfig
    {
        public string speciesName;
        public bool isPredator;
        public float maxHealth;
        public float walkSpeed;
        public float runSpeed;
        public float aggroRadius;
        public float fleeRadius;
        public float attackDamage;
        public int meatDropAmount;
        public Color bodyColor;
        public float bodyHeight;
        public GameObject visualPrefab; // opsional: model asli; null = kapsul prosedural.
    }

    [Header("Konfigurasi spesies (kosong = default 4 spesies arktik)")]
    [SerializeField] private List<SpeciesConfig> species = new List<SpeciesConfig>();

    [Header("Jumlah per sesi")]
    [SerializeField] private int bearCount = 2;
    [SerializeField] private int wolfPackSize = 3;
    [SerializeField] private int deerCount = 6;
    [SerializeField] private int foxCount = 4;

    [Header("Spawn")]
    [SerializeField] private float minDistanceFromPlayer = 25f;
    [SerializeField] private float maxDistanceFromPlayer = 60f;
    [SerializeField] private float groundRayLength = 60f;

    private readonly List<AnimalAI> aliveAnimals = new List<AnimalAI>();
    private bool spawnedForThisSession;
    private float triggerScanTimer;

    public static void SpawnPickables(ItemType itemType, int totalAmount, Vector3 worldPosition)
    {
        if (Instance == null)
        {
            return;
        }

        Instance.StartCoroutine(Instance.SpawnPickablesRoutine(itemType, totalAmount, worldPosition));
    }

    private IEnumerator SpawnPickablesRoutine(ItemType itemType, int totalAmount, Vector3 worldPosition)
    {
        yield return null; // tunggu bangkai selesai mematikan collider.

        int remaining = Mathf.Max(1, totalAmount);
        while (remaining > 0)
        {
            int stack = Mathf.Min(remaining, 4);
            remaining -= stack;

            GameObject drop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            drop.name = itemType + " (WildlifeDrop)";
            Vector3 jitter = new Vector3(Random.Range(-0.7f, 0.7f), 0f, Random.Range(-0.7f, 0.7f));
            if (Physics.Raycast(worldPosition + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 40f))
            {
                drop.transform.position = hit.point + Vector3.up * 0.25f + jitter;
            }
            else
            {
                drop.transform.position = worldPosition + Vector3.up * 0.5f + jitter;
            }

            drop.transform.localScale = new Vector3(0.28f, 0.28f, 0.28f);

            Interactable interactable = drop.GetComponent<Interactable>();
            if (interactable == null)
            {
                drop.AddComponent<Interactable>();
            }

            PickableItem pickable = drop.GetComponent<PickableItem>();
            if (pickable == null)
            {
                pickable = drop.AddComponent<PickableItem>();
            }

            pickable.itemType = itemType;
            pickable.itemName = itemType.ToString();
            pickable.amount = stack;

            if (drop.GetComponent<Rigidbody>() == null)
            {
                drop.AddComponent<Rigidbody>();
            }
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoBootstrap()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoadedStatic;
        // Jika editor play dimulai langsung di dalam forest:
        TryCreateForActiveScene();
    }

    private static void OnSceneLoadedStatic(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        TryCreateForActiveScene();
    }

    private static void TryCreateForActiveScene()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        UnityEngine.SceneManagement.Scene active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!active.IsValid() || active.name != ForestSceneName)
        {
            return;
        }

        if (Instance != null || FindExistingManager() != null)
        {
            return;
        }

        new GameObject("WildlifeManager").AddComponent<WildlifeManager>();
        Debug.Log("[WildlifeManager] bootstrap di scene forest.");
    }

    private static WildlifeManager FindExistingManager()
    {
        return FindObjectOfType<WildlifeManager>();
    }

    private void Awake()
    {
        Instance = this;
        if (species == null || species.Count == 0)
        {
            species = BuildDefaultSpecies();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (spawnedForThisSession)
        {
            return;
        }

        triggerScanTimer -= Time.deltaTime;
        if (triggerScanTimer > 0f)
        {
            return;
        }

        triggerScanTimer = 0.5f;
        Transform localPlayer = FindLocalAuthorityCharacter();
        if (localPlayer != null)
        {
            spawnedForThisSession = true;
            StartCoroutine(SpawnAllRoutine(localPlayer.position));
        }
    }

    private static Transform FindLocalAuthorityCharacter()
    {
        foreach (var networkObject in FindObjectsOfType<Fusion.NetworkObject>())
        {
            var survival = networkObject.GetComponent<FusionPlayerSurvival>();
            if (survival != null && networkObject.HasStateAuthority && networkObject.gameObject.activeInHierarchy)
            {
                return networkObject.transform;
            }
        }

        return null;
    }

    /// <summary>Hook publik untuk alur lain (mis. pintu/panggilan manual).</summary>
    public static void RequestSpawnAround(Vector3 localPlayerPosition)
    {
        if (Instance != null && !Instance.spawnedForThisSession)
        {
            Instance.spawnedForThisSession = true;
            Instance.StartCoroutine(Instance.SpawnAllRoutine(localPlayerPosition));
        }
    }

    private IEnumerator SpawnAllRoutine(Vector3 center)
    {
        yield return null; // satu frame agar terrain & collider siap.

        SpeciesConfig bear = GetSpeciesOrDefault(0);
        SpeciesConfig wolf = GetSpeciesOrDefault(1);
        SpeciesConfig deer = GetSpeciesOrDefault(2);
        SpeciesConfig fox = GetSpeciesOrDefault(3);

        for (int i = 0; i < bearCount; i++) TrySpawn(bear, center);
        bool packAnchorDone = false;
        for (int i = 0; i < wolfPackSize; i++)
        {
            TrySpawn(wolf, center, !packAnchorDone ? minDistanceFromPlayer : minDistanceFromPlayer + 8f);
            packAnchorDone = true;
        }
        for (int i = 0; i < deerCount; i++) TrySpawn(deer, center);
        for (int i = 0; i < foxCount; i++) TrySpawn(fox, center);

        Debug.Log("[WildlifeManager] spawn selesai: " + aliveAnimals.Count + " hewan.");
    }

    private SpeciesConfig GetSpeciesOrDefault(int index)
    {
        List<SpeciesConfig> list = (species != null && species.Count > 0) ? species : BuildDefaultSpecies();
        return list[Mathf.Clamp(index, 0, list.Count - 1)];
    }

    private void TrySpawn(SpeciesConfig config, Vector3 center, float? minDistanceOverride = null)
    {
        const int MaxAttempts = 24;
        float minDistance = minDistanceOverride ?? minDistanceFromPlayer;
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(minDistance, maxDistanceFromPlayer);
            Vector3 candidate = center + new Vector3(offset.x, 0f, offset.y);

            if (!TrySampleGround(candidate, out Vector3 grounded))
            {
                continue;
            }

            if (IsBlockedByObstacle(grounded))
            {
                continue;
            }

            CreateAnimal(config, grounded);
            return;
        }
    }

    private static bool IsBlockedByObstacle(Vector3 groundedPosition)
    {
        // Cek manual: jangan pakai CheckSphere dengan mask penuh karena terrain sendiri
        // selalu terkena -> spawn tak pernah berhasil.
        Collider[] around = Physics.OverlapSphere(groundedPosition + Vector3.up * 1f, 1.2f);
        foreach (Collider candidate in around)
        {
            if (candidate is TerrainCollider)
            {
                continue;
            }

            if (candidate.GetComponentInParent<AnimalAI>() != null)
            {
                continue; // hewan lain boleh berdekatan.
            }

            return true; // pohon/batu/player menghalangi titik ini.
        }

        return false;
    }

    private bool TrySampleGround(Vector3 sampleAt, out Vector3 grounded)
    {
        grounded = sampleAt;
        RaycastHit[] hits = Physics.RaycastAll(sampleAt + Vector3.up * 8f, Vector3.down, groundRayLength);
        float bestDistance = float.MaxValue;
        bool found = false;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.isTrigger)
            {
                continue;
            }

            if (hit.collider.GetComponentInParent<AnimalAI>() != null || hit.collider.GetComponentInParent<FusionPlayerSurvival>() != null)
            {
                continue; // jangan gunjang hewan/player lain sebagai "tanah".
            }

            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                grounded = new Vector3(sampleAt.x, hit.point.y, sampleAt.z);
                found = true;
            }
        }

        return found;
    }

    private void CreateAnimal(SpeciesConfig config, Vector3 position)
    {
        GameObject root = new GameObject("Wildlife_" + config.speciesName);
        root.transform.position = position;
        root.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        if (config.visualPrefab != null)
        {
            Object.Instantiate(config.visualPrefab, root.transform);
        }
        else
        {
            BuildProceduralBody(root.transform, config);
        }

        CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
        collider.height = Mathf.Max(1.4f, config.bodyHeight);
        collider.radius = 0.55f;
        collider.center = new Vector3(0f, collider.height * 0.5f, 0f);

        AnimalAI ai = root.AddComponent<AnimalAI>();
        ai.speciesName = config.speciesName;
        ai.maxHealth = config.maxHealth;
        SetPrivateField(ai, "walkSpeed", config.walkSpeed);
        SetPrivateField(ai, "runSpeed", config.runSpeed);
        SetPrivateField(ai, "aggroRadius", config.aggroRadius);
        SetPrivateField(ai, "fleeRadius", config.fleeRadius);
        SetPrivateField(ai, "attackDamage", config.attackDamage);
        SetPrivateField(ai, "meatDropAmount", config.meatDropAmount);
        SetPrivateField(ai, "isPredator", config.isPredator);

        aliveAnimals.Add(ai);
    }

    private static void BuildProceduralBody(Transform parent, SpeciesConfig config)
    {
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Object.Destroy(body.GetComponent<Collider>()); // fisika cukup dari collider root.
        body.name = "Body";
        body.transform.SetParent(parent, false);
        body.transform.localScale = new Vector3(0.7f, Mathf.Max(0.4f, config.bodyHeight * 0.5f), 1.1f);
        body.transform.localPosition = new Vector3(0f, config.bodyHeight * 0.55f, 0f);

        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(head.GetComponent<Collider>());
        head.name = "Head";
        head.transform.SetParent(parent, false);
        head.transform.localScale = new Vector3(0.42f, 0.42f, 0.55f);
        head.transform.localPosition = new Vector3(0f, config.bodyHeight * 0.75f, 0.85f);

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        if (shader != null)
        {
            Material material = new Material(shader);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", config.bodyColor);
            }
            else if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", config.bodyColor);
            }

            body.GetComponent<MeshRenderer>().sharedMaterial = material;
            head.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        if (field != null)
        {
            field.SetValue(target, value);
        }
    }

    private static List<SpeciesConfig> BuildDefaultSpecies()
    {
        return new List<SpeciesConfig>
        {
            new SpeciesConfig { speciesName = "PolarBear", isPredator = true, maxHealth = 140f,
                walkSpeed = 1.6f, runSpeed = 5.0f, aggroRadius = 9f, fleeRadius = 0f,
                attackDamage = 18f, meatDropAmount = 4, bodyColor = new Color(0.91f, 0.93f, 0.95f), bodyHeight = 2.2f },
            new SpeciesConfig { speciesName = "ArcticWolf", isPredator = true, maxHealth = 70f,
                walkSpeed = 1.9f, runSpeed = 5.6f, aggroRadius = 12f, fleeRadius = 0f,
                attackDamage = 10f, meatDropAmount = 2, bodyColor = new Color(0.62f, 0.66f, 0.70f), bodyHeight = 1.5f },
            new SpeciesConfig { speciesName = "Deer", isPredator = false, maxHealth = 50f,
                walkSpeed = 1.8f, runSpeed = 6.0f, aggroRadius = 0f, fleeRadius = 10f,
                attackDamage = 0f, meatDropAmount = 2, bodyColor = new Color(0.54f, 0.42f, 0.31f), bodyHeight = 1.8f },
            new SpeciesConfig { speciesName = "ArcticFox", isPredator = false, maxHealth = 35f,
                walkSpeed = 2.0f, runSpeed = 6.4f, aggroRadius = 0f, fleeRadius = 13f,
                attackDamage = 0f, meatDropAmount = 1, bodyColor = new Color(0.88f, 0.90f, 0.92f), bodyHeight = 1.0f }
        };
    }
}
