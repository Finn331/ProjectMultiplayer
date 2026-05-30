using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class FusionPlayerSpawner : SimulationBehaviour, IPlayerJoined, IPlayerLeft
{
    [SerializeField] private NetworkPrefabRef playerPrefab;

    private readonly Dictionary<PlayerRef, NetworkObject> spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();

    public void PlayerJoined(PlayerRef player)
    {
        if (Runner == null || !Runner.IsRunning || !Runner.IsSharedModeMasterClient)
        {
            return;
        }

        if (!playerPrefab.IsValid)
        {
            Debug.LogWarning("Cannot spawn Fusion player because playerPrefab is not assigned.", this);
            return;
        }

        if (spawnedPlayers.ContainsKey(player))
        {
            return;
        }

        Transform spawnPoint = GetSpawnPoint(player);
        Vector3 position = spawnPoint != null ? spawnPoint.position : new Vector3(0f, 1.2f, -8f);
        Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

        NetworkObject playerObject = Runner.Spawn(playerPrefab, position, rotation, player);
        spawnedPlayers[player] = playerObject;
        Runner.SetPlayerObject(player, playerObject);
    }

    public void PlayerLeft(PlayerRef player)
    {
        if (Runner == null)
        {
            spawnedPlayers.Remove(player);
            return;
        }

        if (spawnedPlayers.TryGetValue(player, out NetworkObject playerObject) && playerObject != null)
        {
            Runner.SetPlayerObject(player, null);
            Runner.Despawn(playerObject);
        }

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
