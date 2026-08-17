using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class FusionPlayerSpawnerSelfTest
{
    [MenuItem("Project Multiplayer/Run Fusion Player Spawner Self Test")]
    public static void Run()
    {
        MethodInfo snapMethod = typeof(FusionPlayerSpawner).GetMethod("SnapToGround", BindingFlags.Static | BindingFlags.NonPublic);
        if (snapMethod == null)
        {
            throw new System.InvalidOperationException("FusionPlayerSpawner.SnapToGround method not found.");
        }

        System.Collections.Generic.List<Terrain> createdTerrains = new System.Collections.Generic.List<Terrain>();
        try
        {
            Terrain isolated = CreateTerrain(new Vector3(5000f, 0f, 5000f), new Vector3(100f, 100f, 100f), 0.5f);
            createdTerrains.Add(isolated);

            Vector3 highSpawn = new Vector3(5050f, 1.2f, 5050f);
            Vector3 snapped = (Vector3)snapMethod.Invoke(null, new object[] { highSpawn });
            if (!Mathf.Approximately(snapped.y, 51.2f))
            {
                throw new System.Exception("SnapToGround should lift spawn Y to terrain surface + clearance; expected 51.2, got " + snapped.y);
            }
            Debug.Log("SnapToGround lift verified (" + snapped.y + ").");

            Vector3 aboveSpawn = new Vector3(5050f, 60f, 5050f);
            Vector3 kept = (Vector3)snapMethod.Invoke(null, new object[] { aboveSpawn });
            if (!Mathf.Approximately(kept.y, 60f))
            {
                throw new System.Exception("SnapToGround should keep spawn Y above terrain unchanged; expected 60, got " + kept.y);
            }
            Debug.Log("SnapToGround keep-above verified (" + kept.y + ").");
        }
        finally
        {
            for (int i = 0; i < createdTerrains.Count; i++)
            {
                if (createdTerrains[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(createdTerrains[i].gameObject);
                }
            }
        }

        Debug.Log("FusionPlayerSpawnerSelfTest passed.");
    }

    private static Terrain CreateTerrain(Vector3 position, Vector3 size, float flatHeightNormalized)
    {
        TerrainData data = new TerrainData();
        data.heightmapResolution = 17;
        data.size = size;

        float[,] heights = new float[17, 17];
        for (int i = 0; i < 17; i++)
        {
            for (int j = 0; j < 17; j++)
            {
                heights[i, j] = flatHeightNormalized;
            }
        }

        data.SetHeights(0, 0, heights);

        GameObject go = new GameObject("SpawnerSelfTestTerrain");
        Terrain terrain = go.AddComponent<Terrain>();
        terrain.terrainData = data;
        go.AddComponent<TerrainCollider>();
        go.transform.position = position;
        return terrain;
    }
}