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

            // ParseInt handles primitives (regression guard for the cloud-load cast bug).
            ok &= PlayerStatsPersistence.ParseIntForTest("7") == 7;
            ok &= PlayerStatsPersistence.ParseIntForTest((long)9) == 9;
            ok &= PlayerStatsPersistence.ParseIntForTest(4.0) == 4;
            ok &= PlayerStatsPersistence.ParseIntForTest(null) == 0;

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