using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class FusionPlayerSurvivalSelfTest
{
    [MenuItem("Project Multiplayer/Run Fusion Player Survival Self Test")]
    public static void Run()
    {
        StringBuilder results = new StringBuilder();
        bool ok = true;

        System.Type type = typeof(FusionPlayerSurvival);

        bool hasLastDamager = type.GetProperty("LastDamagerRef", BindingFlags.Public | BindingFlags.Instance) != null;
        results.AppendLine("lastDamagerMember=" + hasLastDamager);
        ok &= hasLastDamager;

        bool hasDisplayName = type.GetProperty("DisplayName", BindingFlags.Public | BindingFlags.Instance) != null;
        results.AppendLine("displayNameMember=" + hasDisplayName);
        ok &= hasDisplayName;

        var survival = new GameObject("FusionPlayerSurvivalSelfTest_Survival").AddComponent<FusionPlayerSurvival>();

        // In edit mode (no runner) Fusion rejects reads of networked properties before
        // Spawned(). Both members must be Fusion-backed (no eager Awake/field
        // initializer), so a pre-spawn access must throw.
        bool lastDamagerGuarded = IsNetworkedPropertyGuarded(() => { _ = survival.LastDamagerRef; });
        results.AppendLine("lastDamagerGuarded=" + lastDamagerGuarded);
        ok &= lastDamagerGuarded;

        bool displayNameGuarded = IsNetworkedPropertyGuarded(() => { _ = survival.DisplayName; });
        results.AppendLine("displayNameGuarded=" + displayNameGuarded);
        ok &= displayNameGuarded;

        UnityEngine.Object.DestroyImmediate(survival.gameObject);

        if (!ok)
        {
            throw new System.Exception("FusionPlayerSurvivalSelfTest FAILED\n" + results);
        }

        Debug.Log("FusionPlayerSurvivalSelfTest passed.");
    }

    private static bool IsNetworkedPropertyGuarded(System.Action access)
    {
        try
        {
            access();
            return false;
        }
        catch (System.InvalidOperationException)
        {
            return true;
        }
    }
}