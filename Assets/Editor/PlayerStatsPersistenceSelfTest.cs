using UnityEditor;
using UnityEngine;

public static class PlayerStatsPersistenceSelfTest
{
    [MenuItem("Project Multiplayer/Run Player Stats Persistence Self Test")]
    public static void Run()
    {
        GameObject go = null;
        try
        {
            go = new GameObject("PlayerStatsPersistenceSelfTest");
            var persistence = go.AddComponent<PlayerStatsPersistence>();
            persistence.ResetForTest();
            persistence.RecordKill();
            persistence.RecordDown();
            bool ok = persistence.TotalKillsForTest == 1 && persistence.TotalDownsForTest == 1;
            if (!ok)
            {
                throw new System.Exception("PlayerStatsPersistenceSelfTest FAILED");
            }
            Debug.Log("PlayerStatsPersistenceSelfTest passed.");
        }
        finally
        {
            if (go != null) Object.DestroyImmediate(go);
        }
    }
}