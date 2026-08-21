using UnityEditor;
using UnityEngine;

public static class FusionTerrainTreeDepletionStateSelfTest
{
    [MenuItem("Project Multiplayer/Run Fusion Terrain Tree Depletion State Self Test")]
    public static void Run()
    {
        DepletionIdBuffer state = new DepletionIdBuffer();

        Expect(state.Count == 0, "Buffer should start empty.");
        Expect(state.Add(5), "First add should return true.");
        Expect(!state.Add(5), "Duplicate add should return false.");
        Expect(state.Add(7), "Second distinct add should return true.");
        Expect(state.Count == 2, "Buffer should contain two distinct ids.");
        Expect(state.Contains(5) && state.Contains(7), "Buffer should contain the added ids.");

        int[] snapshot = state.ToArray();
        Expect(snapshot.Length == 2 && snapshot[0] == 5 && snapshot[1] == 7,
            "Buffer should preserve insertion order.");

        state.Load(new[] { 5, 7, 5, 0, 9 });
        Expect(state.Count == 3, "Load should replace the set and de-duplicate.");
        Expect(state.Contains(9), "Load should include the new id.");

        int[] loaded = state.ToArray();
        Expect(loaded.Length == 3 && loaded[0] == 5 && loaded[1] == 7 && loaded[2] == 9,
            "Load should keep the exact set [5, 7, 9] and filter the 0 sentinel.");

        RunLateRegistryResolutionTest();

        Debug.Log("FusionTerrainTreeDepletionStateSelfTest passed.");
    }

    private static void RunLateRegistryResolutionTest()
    {
        GameObject host = new GameObject("FusionTerrainTreeDepletionStateSelfTest_LateResolve");
        try
        {
            FusionTerrainTreeDepletionState depletionState = host.AddComponent<FusionTerrainTreeDepletionState>();

            // Simulate Spawned() before the forest scene finished loading: no cached registry yet.
            System.Reflection.FieldInfo registryField = typeof(FusionTerrainTreeDepletionState)
                .GetField("registry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            registryField.SetValue(depletionState, null);

            bool registryExistsInOpenScene = Object.FindObjectOfType<TerrainTreeChoppingRegistry>() != null;
            bool firstAttempt = depletionState.TryResolveRegistryForTests();
            if (registryExistsInOpenScene)
            {
                Expect(firstAttempt,
                    "Resolution should find a TerrainTreeChoppingRegistry that already exists in an open scene.");
            }
            else
            {
                Expect(!firstAttempt,
                    "Resolution should fail while no TerrainTreeChoppingRegistry exists in the scene.");
            }

            TerrainTreeChoppingRegistry fallbackRegistry = host.AddComponent<TerrainTreeChoppingRegistry>();
            fallbackRegistry.RebuildForTests(new Terrain[0]);
            registryField.SetValue(depletionState, null);

            if (!registryExistsInOpenScene)
            {
                Expect(depletionState.TryResolveRegistryForTests(),
                    "Retry resolution should succeed once the registry component exists (late scene load).");
            }

            Expect(depletionState.TryResolveRegistryForTests(),
                "Resolved state should stay sticky across repeated calls.");
        }
        finally
        {
            Object.DestroyImmediate(host);
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
