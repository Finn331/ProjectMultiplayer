using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class FusionPlayerSpawner : MonoBehaviour
{
    [SerializeField] private NetworkPrefabRef playerPrefab;

    private const float SpawnClearance = 1.2f;

    private void Start()
    {
        var runner = FindObjectOfType<NetworkRunner>();
        if (runner != null && runner.IsRunning)
        {
            SpawnLocalPlayer(runner);
        }
    }

    private void OnEnable()
    {
        PhotonFusionBootstrap bootstrap = PhotonFusionBootstrap.Instance;
        if (bootstrap == null)
        {
            bootstrap = FindObjectOfType<PhotonFusionBootstrap>(true);
        }

        if (bootstrap != null)
        {
            bootstrap.RunnerStarted -= HandleRunnerStarted;
            bootstrap.RunnerStarted += HandleRunnerStarted;
        }
    }

    private void OnDisable()
    {
        PhotonFusionBootstrap bootstrap = PhotonFusionBootstrap.Instance;
        if (bootstrap == null)
        {
            bootstrap = FindObjectOfType<PhotonFusionBootstrap>(true);
        }

        if (bootstrap != null)
        {
            bootstrap.RunnerStarted -= HandleRunnerStarted;
        }
    }

    private void HandleRunnerStarted(NetworkRunner runner)
    {
        TrySpawnLocalPlayer(runner);
    }

    public void TrySpawnLocalPlayer(NetworkRunner runner)
    {
        if (runner == null || !runner.IsRunning || !playerPrefab.IsValid)
        {
            return;
        }

        SpawnLocalPlayer(runner);
    }

    private async void SpawnLocalPlayer(NetworkRunner runner)
    {
        if (!playerPrefab.IsValid)
        {
            Debug.LogWarning("Cannot spawn Fusion player because playerPrefab is not assigned.", this);
            return;
        }

        PlayerRef localPlayer = runner.LocalPlayer;
        if (localPlayer.IsNone) return;

        // Cek apakah player sudah spawn (menghindari double spawn)
        foreach (var no in FindObjectsOfType<NetworkObject>())
        {
            if (no.HasStateAuthority && no.GetComponent<FusionPlayerMovement>() != null)
            {
                return;
            }
        }

        Transform spawnPoint = GetSpawnPoint(localPlayer);
        Vector3 position = spawnPoint != null ? spawnPoint.position : new Vector3(0f, 1.2f, -8f);
        Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

        position = SnapToGround(position);

        // Di Shared Mode, setiap client men-spawn karakternya sendiri
        NetworkObject playerObject = await runner.SpawnAsync(playerPrefab, position, rotation, localPlayer);

        if (playerObject != null)
        {
            ApplySpawnTransform(playerObject, position, rotation);
            // Daftarkan sebagai Player Object
            runner.SetPlayerObject(localPlayer, playerObject);
            RestorePersistedPlayerState(runner, playerObject);
        }
    }

    private static void RestorePersistedPlayerState(NetworkRunner runner, NetworkObject playerObject)
    {
        string roomCode = PhotonFusionSessionState.HasSession
            ? PhotonFusionSessionState.Active.RoomCode
            : string.Empty;

        PlayerInventory inventory = playerObject.GetComponent<PlayerInventory>();
        PlayerSurvivalSystem survival = playerObject.GetComponent<PlayerSurvivalSystem>();
#if UNITY_6000_5_OR_NEWER
        FusionPlayerPersistence.TryRestore(roomCode, (int)UnityEngine.EntityId.ToULong(runner.GetEntityId()), inventory, survival);
#else
        FusionPlayerPersistence.TryRestore(roomCode, runner.GetInstanceID(), inventory, survival);
#endif

        // Lobby (Gameplay) adalah zona aman: karakter yang baru dibuat di sini
        // selalu lahir sehat, terlepas dari sisa state jaringan sesi sebelumnya
        // (mis. tepat setelah wipe-return dari forest, objek pengganti bisa
        // mewarisi snapshot hp=0/downed dan player terkunci hingga timer
        // respawn 20 detik habis).
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Gameplay")
        {
            FusionPlayerSurvival fusionSurvival = playerObject.GetComponent<FusionPlayerSurvival>();
            if (fusionSurvival != null && fusionSurvival.IsDowned)
            {
                fusionSurvival.ResetForRespawn();
            }
        }
    }

    private static void ApplySpawnTransform(NetworkObject playerObject, Vector3 position, Quaternion rotation)
    {
        Transform target = playerObject.transform;
        CharacterController controller = target.GetComponent<CharacterController>();
        if (controller != null && controller.enabled)
        {
            controller.enabled = false;
            target.SetPositionAndRotation(position, rotation);
            controller.enabled = true;
        }
        else
        {
            target.SetPositionAndRotation(position, rotation);
        }
    }

    public void TeleportPlayerToSpawnPoint(PlayerRef player, Transform playerTransform)
    {
        if (playerTransform == null)
        {
            return;
        }

        Transform spawnPoint = GetSpawnPoint(player);
        Vector3 position = spawnPoint != null ? spawnPoint.position : new Vector3(0f, 1.2f, -8f);
        Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;
        CharacterController controller = playerTransform.GetComponent<CharacterController>();
        if (controller != null && controller.enabled)
        {
            controller.enabled = false;
            playerTransform.SetPositionAndRotation(position, rotation);
            controller.enabled = true;
        }
        else
        {
            playerTransform.SetPositionAndRotation(position, rotation);
        }
    }

    private static Vector3 SnapToGround(Vector3 position)
    {
        Terrain[] terrains = UnityEngine.Object.FindObjectsOfType<Terrain>();
        if (terrains == null || terrains.Length == 0)
        {
            return position;
        }

        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain terrain = terrains[i];
            if (terrain == null || terrain.terrainData == null)
            {
                continue;
            }

            Vector3 local = position - terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            if (local.x < 0f || local.z < 0f || local.x > size.x || local.z > size.z)
            {
                continue;
            }

            float surfaceY = terrain.SampleHeight(position);
            float groundedY = surfaceY + SpawnClearance;
            return new Vector3(position.x, Mathf.Max(position.y, groundedY), position.z);
        }

        return position;
    }

    public Transform GetSpawnPoint(PlayerRef player)
    {
        FusionSpawnPoint[] points = FindObjectsOfType<FusionSpawnPoint>(true);
        if (points == null || points.Length == 0)
        {
            return null;
        }

        System.Array.Sort(points, CompareSpawnPoints);
        int index = (int)((uint)player.PlayerId % (uint)points.Length);
        return points[index].transform;
    }

    private static int CompareSpawnPoints(FusionSpawnPoint left, FusionSpawnPoint right)
    {
        int indexComparison = left.Index.CompareTo(right.Index);
        if (indexComparison != 0)
        {
            return indexComparison;
        }

        int pathComparison = string.CompareOrdinal(GetHierarchyPath(left.transform), GetHierarchyPath(right.transform));
#if UNITY_6000_5_OR_NEWER
        return pathComparison != 0 ? pathComparison : (int)UnityEngine.EntityId.ToULong(left.GetEntityId()).CompareTo((int)UnityEngine.EntityId.ToULong(right.GetEntityId()));
#else
        return pathComparison != 0 ? pathComparison : left.GetInstanceID().CompareTo(right.GetInstanceID());
#endif
    }

    private static string GetHierarchyPath(Transform transform)
    {
        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }

        return path;
    }
}
