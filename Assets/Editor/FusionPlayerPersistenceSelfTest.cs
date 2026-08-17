using System.IO;
using UnityEditor;
using UnityEngine;

public static class FusionPlayerPersistenceSelfTest
{
    [MenuItem("Project Multiplayer/Run Fusion Player Persistence Self Test")]
    public static void Run()
    {
        FusionPlayerPersistence.Clear();

        GameObject source = new GameObject("PersistenceSource");
        GameObject destination = new GameObject("PersistenceDestination");

        try
        {
            PlayerInventory sourceInventory = source.AddComponent<PlayerInventory>();
            PlayerSurvivalSystem sourceSurvival = source.AddComponent<PlayerSurvivalSystem>();
            sourceInventory.AddItem(ItemType.Wood, 5);
            sourceInventory.AddItem(ItemType.Stone, 2);
            sourceSurvival.ApplyNetworkSnapshot(72f, 61f, 33f);

            FusionPlayerPersistence.Capture("ROOM42", 12345, sourceInventory, sourceSurvival);
            Expect(FusionPlayerPersistence.HasPendingForSession("ROOM42", 12345), "Snapshot should be pending for the same room and runner.");
            Expect(!FusionPlayerPersistence.HasPendingForSession("ROOM42", 67890), "Snapshot should not match a different runner.");

            PlayerInventory destinationInventory = destination.AddComponent<PlayerInventory>();
            PlayerSurvivalSystem destinationSurvival = destination.AddComponent<PlayerSurvivalSystem>();
            bool restored = FusionPlayerPersistence.TryRestore("ROOM42", 12345, destinationInventory, destinationSurvival);

            Expect(restored, "Snapshot restore should succeed for the matching room and runner.");
            Expect(sourceInventory.BuildSnapshotString() == destinationInventory.BuildSnapshotString(), "Inventory snapshot should round-trip exactly.");
            Expect(Mathf.Approximately(destinationSurvival.CurrentHealth, 72f), "Health should be restored.");
            Expect(Mathf.Approximately(destinationSurvival.CurrentHunger, 61f), "Hunger should be restored.");
            Expect(Mathf.Approximately(destinationSurvival.CurrentThirst, 33f), "Thirst should be restored.");
            Expect(!FusionPlayerPersistence.HasPendingForSession("ROOM42", 12345), "Snapshot should clear after restore.");

            string prefabPath = "Assets/Assets/Prefabs/FusionPlayer.prefab";
            Expect(File.Exists(prefabPath), "FusionPlayer prefab should exist at " + prefabPath);
            string bridgeGuid = GetScriptGuid(typeof(FusionPlayerPersistenceBridge));
            string prefabContents = File.ReadAllText(prefabPath);
            Expect(!string.IsNullOrEmpty(bridgeGuid) && prefabContents.Contains(bridgeGuid), "FusionPlayer prefab should reference FusionPlayerPersistenceBridge.");
        }
        finally
        {
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(destination);
            FusionPlayerPersistence.Clear();
        }

        Debug.Log("FusionPlayerPersistenceSelfTest passed.");
    }

    private static string GetScriptGuid(System.Type scriptType)
    {
        string[] guids = AssetDatabase.FindAssets(scriptType.Name + " t:MonoScript");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (path.EndsWith(scriptType.Name + ".cs", System.StringComparison.OrdinalIgnoreCase))
            {
                return guids[i];
            }
        }

        return string.Empty;
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new System.InvalidOperationException(message);
        }
    }
}
