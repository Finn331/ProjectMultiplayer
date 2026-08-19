using UnityEditor;
using UnityEngine;

public static class FusionTerrainTreeDepletionStateSelfTest
{
    [MenuItem("Project Multiplayer/Run Fusion Terrain Tree Depletion State Self Test")]
    public static void Run()
    {
        DepletionIdBuffer state = ScriptableObject.CreateInstance<DepletionIdBuffer>();

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

        ScriptableObject.DestroyImmediate(state);
        Debug.Log("FusionTerrainTreeDepletionStateSelfTest passed.");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new System.InvalidOperationException(message);
        }
    }
}
