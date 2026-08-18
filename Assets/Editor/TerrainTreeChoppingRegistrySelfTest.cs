using UnityEditor;
using UnityEngine;

public static class TerrainTreeChoppingRegistrySelfTest
{
    [MenuItem("Project Multiplayer/Run Terrain Tree Chopping Registry Self Test")]
    public static void Run()
    {
        GameObject terrainObject = new GameObject("TerrainTreeChoppingRegistrySelfTest_Terrain");
        bool destroyPrototype = false;
        GameObject prototype = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Tom's Terrain Tools/Example Data/Trees/SampleTree1.prefab");
        if (prototype == null)
        {
            prototype = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            prototype.name = "TerrainTreeChoppingRegistrySelfTest_Prototype";
            destroyPrototype = true;
        }

        try
        {
            TerrainData data = new TerrainData();
            data.heightmapResolution = 33;
            data.size = new Vector3(20f, 4f, 20f);
            data.treePrototypes = new[] { new TreePrototype { prefab = prototype } };
            data.treeInstances = new[]
            {
                new TreeInstance
                {
                    prototypeIndex = 0,
                    position = new Vector3(0.5f, 0f, 0.5f),
                    widthScale = 1f,
                    heightScale = 1f,
                    color = Color.white,
                    lightmapColor = Color.white
                },
                new TreeInstance
                {
                    prototypeIndex = 0,
                    position = new Vector3(0.75f, 0f, 0.5f),
                    widthScale = 1f,
                    heightScale = 1f,
                    color = Color.white,
                    lightmapColor = Color.white
                }
            };

            Terrain terrain = terrainObject.AddComponent<Terrain>();
            terrain.terrainData = data;
            terrainObject.AddComponent<TerrainCollider>().terrainData = data;

            TerrainTreeChoppingRegistry registry = terrainObject.AddComponent<TerrainTreeChoppingRegistry>();
            registry.RebuildForTests(new[] { terrain });

            Expect(registry.TreeCount == 2, "Registry should snapshot both Terrain tree instances.");
            Expect(registry.TryFindBestTreeForChop(new Vector3(10f, 1f, 5f), Vector3.forward, 20f, 0.2f, out TerrainTreeChoppingRegistry.TreeHit hit),
                "Registry should find a tree in front of the player.");
            Expect(hit.TreeId != 0, "Tree id should be stable and non-zero.");

            int beforeRemovalCount = terrain.terrainData.treeInstanceCount;
            Expect(registry.TryApplyDamageForTests(hit.TreeId, 3f, out bool depleted), "First damage application should be accepted.");
            Expect(depleted, "Tree should deplete after lethal test damage.");
            Expect(terrain.terrainData.treeInstanceCount == beforeRemovalCount - 1, "Runtime Terrain tree instance array should hide one depleted tree.");
            Expect(!registry.TryApplyDamageForTests(hit.TreeId, 3f, out _), "Repeated damage against the same depleted id should be ignored.");
            Expect(terrain.terrainData.treeInstanceCount == beforeRemovalCount - 1, "Repeated depletion should not remove another tree.");
            Expect(!registry.TryPlayFallingProxy(123456789, Vector3.forward), "Missing tree id should not spawn a proxy.");

            Debug.Log("TerrainTreeChoppingRegistrySelfTest passed.");
        }
        finally
        {
            Object.DestroyImmediate(terrainObject);
            if (destroyPrototype)
            {
                Object.DestroyImmediate(prototype);
            }
        }
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new System.InvalidOperationException(message);
        }
    }
}
