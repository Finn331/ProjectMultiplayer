using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class FusionPlayerSpawner : MonoBehaviour
{
    [SerializeField] private NetworkPrefabRef playerPrefab;

    private void Start()
    {
        var runner = FindObjectOfType<NetworkRunner>();
        if (runner != null && runner.IsRunning)
        {
            SpawnLocalPlayer(runner);
        }
    }

    private void SpawnLocalPlayer(NetworkRunner runner)
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

        // Di Shared Mode, setiap client men-spawn karakternya sendiri
        NetworkObject playerObject = runner.Spawn(playerPrefab, position, rotation, localPlayer);
        
        // Daftarkan sebagai Player Object
        runner.SetPlayerObject(localPlayer, playerObject);
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
