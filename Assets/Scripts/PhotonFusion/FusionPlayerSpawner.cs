using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class FusionPlayerSpawner : SimulationBehaviour, IPlayerJoined, IPlayerLeft, ISpawned
{
    [SerializeField] private NetworkPrefabRef playerPrefab;

    private readonly Dictionary<PlayerRef, NetworkObject> spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();

    public void Spawned()
    {
        if (Runner == null || !Runner.IsRunning)
        {
            return;
        }

        if (!playerPrefab.IsValid)
        {
            Debug.LogWarning("Cannot spawn Fusion player because playerPrefab is not assigned.", this);
            return;
        }

        PlayerRef localPlayer = Runner.LocalPlayer;
        if (spawnedPlayers.ContainsKey(localPlayer))
        {
            return;
        }

        Transform spawnPoint = GetSpawnPoint(localPlayer);
        Vector3 position = spawnPoint != null ? spawnPoint.position : new Vector3(0f, 1.2f, -8f);
        Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

        // Di Shared Mode, setiap client men-spawn karakternya sendiri agar memiliki State Authority
        NetworkObject playerObject = Runner.Spawn(playerPrefab, position, rotation, localPlayer);
        spawnedPlayers[localPlayer] = playerObject;
        
        // Daftarkan sebagai Player Object
        Runner.SetPlayerObject(localPlayer, playerObject);
    }

    public void PlayerJoined(PlayerRef player)
    {
        // Dalam Shared Mode, spawning dilakukan oleh masing-masing client di Spawned().
    }

    public void PlayerLeft(PlayerRef player)
    {
        // Fusion otomatis men-despawn objek milik player yang keluar di Shared Mode.
        spawnedPlayers.Remove(player);
    }

    private static Transform GetSpawnPoint(PlayerRef player)
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
        return pathComparison != 0 ? pathComparison : left.GetInstanceID().CompareTo(right.GetInstanceID());
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
