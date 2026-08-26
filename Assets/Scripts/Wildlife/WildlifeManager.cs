using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Spawner wildlife NETWORKED (v2) untuk scene Environment (taiga).
/// HANYA master client yang menjalankan spawn: hewan = instance prefab networked
/// (Resources/Wildlife/ArcticAnimal) dengan state authority di master; seluruh klien
/// melihat hewan yang sama. Gerakan memakai NavMeshAgent pada master — NavMesh dibake
/// runtime dari physics collider scene (multi-tile taiga) sekali per sesi.
/// Mati -> tiap klien spawn kubus daging LOKAL (siapa pun mengambil, miliknya),
/// bangkai tenggelam, authority despawn object networked-nya.
/// </summary>
public class WildlifeManager : MonoBehaviour
{
    public static WildlifeManager Instance { get; private set; }

    private const string ForestSceneName = "Environment";
    private const string AnimalPrefabPath = "Wildlife/ArcticAnimal";

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

    [Header("NavMesh")]
    [SerializeField] private float navMeshVoxelSize = 0.8f;

    private Fusion.NetworkRunner masterRunner;
    private bool spawnedForThisSession;
    private float triggerScanTimer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoBootstrap()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoadedStatic;
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

        if (Instance != null || FindObjectOfType<WildlifeManager>() != null)
        {
            return;
        }

        new GameObject("WildlifeManager").AddComponent<WildlifeManager>();
        Debug.Log("[WildlifeManager] bootstrap di scene forest.");
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

        foreach (Fusion.NetworkRunner runner in FindObjectsOfType<Fusion.NetworkRunner>())
        {
            if (!runner.IsRunning || !runner.IsSharedModeMasterClient)
            {
                continue;
            }

            Transform localCharacter = FindLocalAuthorityCharacter();
            if (localCharacter == null)
            {
                return; // master sudah ada tapi karakter lokal belum — tunggu scan berikutnya.
            }

            spawnedForThisSession = true;
            masterRunner = runner;
            StartCoroutine(BakeNavMeshThenSpawn(localCharacter.position));
            return;
        }
    }

    private static Transform FindLocalAuthorityCharacter()
    {
        foreach (Fusion.NetworkObject networkObject in FindObjectsOfType<Fusion.NetworkObject>())
        {
            if (networkObject.GetComponent<FusionPlayerSurvival>() != null
                && networkObject.HasStateAuthority
                && networkObject.gameObject.activeInHierarchy)
            {
                return networkObject.transform;
            }
        }

        return null;
    }

    private IEnumerator BakeNavMeshThenSpawn(Vector3 center)
    {
        yield return null;

        // Bake NavMesh runtime lewat API modul UnityEngine.AI (selalu tersedia).
        // Pola kanonik: data kosong DIADD DULU di identitas, lalu dibake IN-PLACE
        // dengan sumber ber-koordinat dunia (hindari bug offset BuildNavMeshData).
        NavMeshBuildSettings settings = UnityEngine.AI.NavMesh.GetSettingsByIndex(0);
        settings.overrideVoxelSize = true;
        settings.voxelSize = navMeshVoxelSize;

        Bounds bounds = new Bounds(center + Vector3.up * 30f, new Vector3(520f, 90f, 520f));
        List<UnityEngine.AI.NavMeshBuildSource> buildSources = new List<UnityEngine.AI.NavMeshBuildSource>();
        List<UnityEngine.AI.NavMeshBuildMarkup> markups = new List<UnityEngine.AI.NavMeshBuildMarkup>();
        UnityEngine.AI.NavMeshBuilder.CollectSources(
            bounds,
            UnityEngine.AI.NavMesh.AllAreas,
            UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders,
            0,
            markups,
            buildSources);

        UnityEngine.AI.NavMeshData navData = new UnityEngine.AI.NavMeshData();
        UnityEngine.AI.NavMesh.AddNavMeshData(navData);
        // UpdateNavMeshData bersifat synchronous dan return bool (true = sukses update).
        bool bakeOk = UnityEngine.AI.NavMeshBuilder.UpdateNavMeshData(navData, settings, buildSources, bounds);
        if (!bakeOk)
        {
            Debug.LogWarning("[WildlifeManager] UpdateNavMeshData return false — NavMesh mungkin kosong/tidak ada sumber.");
        }

        int vertexCount = UnityEngine.AI.NavMesh.CalculateTriangulation().vertices.Length;
        if (vertexCount == 0)
        {
            Debug.LogError("[WildlifeManager] bake NavMesh menghasilkan mesh kosong — fallback raycast dipakai.");
            WildlifeTestLog("NavMesh EMPTY - bake failed");
        }
        else
        {
            Debug.Log("[WildlifeManager] NavMesh siap (" + buildSources.Count + " sumber, " + vertexCount + " verts).");
            WildlifeTestLog("NavMesh READY - verts=" + vertexCount + " sources=" + buildSources.Count);
        }

        yield return SpawnAllRoutine(center);
    }

    public static void WildlifeTestLog(string message)
    {
        try
        {
            string path = UnityEngine.Application.persistentDataPath + "/wildlife_test.log";
            System.IO.File.AppendAllText(path, System.DateTime.Now.ToString("HH:mm:ss") + " " + message + "\n");
        }
        catch (System.Exception) { /* ignore */ }
    }

    /// <summary>Drop daging LOKAL per klien saat hewan mati (dipanggil AnimalAI).</summary>
    public static void SpawnLocalMeatCubes(ItemType itemType, int totalAmount, Vector3 worldPosition)
    {
        int remaining = Mathf.Max(1, totalAmount);
        while (remaining > 0)
        {
            int stack = Mathf.Min(remaining, 4);
            remaining -= stack;

            GameObject drop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            drop.name = itemType + " (WildlifeDrop)";
            Vector3 jitter = new Vector3(Random.Range(-0.7f, 0.7f), 0f, Random.Range(-0.7f, 0.7f));
            if (TrySampleGroundForDrop(worldPosition, out Vector3 grounded))
            {
                drop.transform.position = grounded + Vector3.up * 0.25f + jitter;
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

            Rigidbody rb = drop.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = drop.AddComponent<Rigidbody>();
            }
        }
    }

    private static bool TrySampleGroundForDrop(Vector3 sampleAt, out Vector3 grounded)
    {
        grounded = sampleAt;
        RaycastHit[] hits = Physics.RaycastAll(sampleAt + Vector3.up * 5f, Vector3.down, 40f);
        float bestDistance = float.MaxValue;
        bool found = false;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.isTrigger)
            {
                continue;
            }

            if (hit.collider.GetComponentInParent<AnimalAI>() != null
                || hit.collider.GetComponentInParent<FusionPlayerSurvival>() != null)
            {
                continue;
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

    private IEnumerator SpawnAllRoutine(Vector3 center)
    {
        GameObject prefab = Resources.Load<GameObject>(AnimalPrefabPath);
        if (prefab == null)
        {
            Debug.LogError("[WildlifeManager] prefab '" + AnimalPrefabPath + "' tidak ditemukan di Resources.");
            yield break;
        }

        SpeciesConfig bear = GetSpeciesOrDefault(0);
        SpeciesConfig wolf = GetSpeciesOrDefault(1);
        SpeciesConfig deer = GetSpeciesOrDefault(2);
        SpeciesConfig fox = GetSpeciesOrDefault(3);

        for (int i = 0; i < bearCount; i++)
        {
            TrySpawn(masterRunner, prefab, bear, center, minDistanceFromPlayer);
        }

        bool packAnchorDone = false;
        for (int i = 0; i < wolfPackSize; i++)
        {
            float minRange = packAnchorDone ? minDistanceFromPlayer + 8f : minDistanceFromPlayer;
            TrySpawn(masterRunner, prefab, wolf, center, minRange);
            packAnchorDone = true;
        }

        for (int i = 0; i < deerCount; i++)
        {
            TrySpawn(masterRunner, prefab, deer, center, minDistanceFromPlayer);
        }

        for (int i = 0; i < foxCount; i++)
        {
            TrySpawn(masterRunner, prefab, fox, center, minDistanceFromPlayer);
        }

        int animalCount = FindObjectsOfType<AnimalAI>().Length;
        Debug.Log("[WildlifeManager] spawn networked selesai: " + animalCount + " hewan.");
        WildlifeTestLog("SPAWN DONE - animals=" + animalCount);
    }

    private SpeciesConfig GetSpeciesOrDefault(int index)
    {
        List<SpeciesConfig> list = (species != null && species.Count > 0) ? species : BuildDefaultSpecies();
        return list[Mathf.Clamp(index, 0, list.Count - 1)];
    }

    private void TrySpawn(Fusion.NetworkRunner runner, GameObject prefab, SpeciesConfig config, Vector3 center, float minDistance)
    {
        const int MaxAttempts = 24;
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(minDistance, maxDistanceFromPlayer);
            Vector3 candidate = center + new Vector3(offset.x, 0f, offset.y);

            // Wajib titik yang VALID di NavMesh agar NavMeshAgent langsung hidup.
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit navHit, 6f, NavMesh.AllAreas))
            {
                continue;
            }

            Vector3 grounded = navHit.position;
            if (IsBlockedByObstacle(grounded))
            {
                continue;
            }

            Fusion.NetworkObject spawnedObject = runner.Spawn(
                prefab,
                grounded,
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f),
                Fusion.PlayerRef.None);

            if (spawnedObject == null)
            {
                Debug.LogWarning("[WildlifeManager] Runner.Spawn gagal untuk " + config.speciesName);
                return;
            }

            AnimalAI ai = spawnedObject.GetComponent<AnimalAI>();
            if (ai != null)
            {
                ai.speciesName = config.speciesName;
                ai.InitializeFromConfig(config.isPredator, config.maxHealth, config.walkSpeed, config.runSpeed,
                    config.aggroRadius, config.fleeRadius, config.attackDamage, config.meatDropAmount);
            }

            return;
        }
    }

    private static bool IsBlockedByObstacle(Vector3 groundedPosition)
    {
        Collider[] around = Physics.OverlapSphere(groundedPosition + Vector3.up * 1f, 1.2f);
        foreach (Collider candidate in around)
        {
            if (candidate is TerrainCollider)
            {
                continue;
            }

            if (candidate.GetComponentInParent<AnimalAI>() != null)
            {
                continue;
            }

            if (candidate.GetComponentInParent<PlayerSurvivalSystem>() != null)
            {
                continue;
            }

            return true;
        }

        return false;
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
