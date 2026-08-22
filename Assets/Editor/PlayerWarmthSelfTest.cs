using UnityEditor;
using UnityEngine;

/// <summary>Self-test Warmth + Night Danger system.</summary>
public static class PlayerWarmthSelfTest
{
    [MenuItem("Project Multiplayer/Run Warmth System Self Test")]
    public static void Run()
    {
        int pass = 0, fail = 0;
        void Check(bool cond, string name)
        {
            if (cond) { pass++; Debug.Log("[WarmthTest] PASS " + name); }
            else { fail++; Debug.LogError("[WarmthTest] FAIL " + name); }
        }

        // 1. Komponen di prefab
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Assets/Prefabs/FusionPlayer.prefab");
        Check(prefab != null, "FusionPlayer.prefab loadable");
        var warmth = prefab != null ? prefab.GetComponent<PlayerWarmthSystem>() : null;
        Check(warmth != null, "PlayerWarmthSystem on prefab");
        var survival = prefab != null ? prefab.GetComponent<PlayerSurvivalSystem>() : null;
        Check(survival != null, "PlayerSurvivalSystem on prefab");

        // 2. DayNightCycle ada di scene Environment (buka dulu kalau perlu)
        var cycle = Object.FindObjectOfType<DayNightCycle>();
        Check(cycle != null, "DayNightCycle in active scene");

        // 3. CampfireCooking type punya IsLitValue accessor
        var campfireType = typeof(CampfireCooking);
        var prop = campfireType.GetProperty("IsLitValue");
        Check(prop != null && prop.PropertyType == typeof(bool), "CampfireCooking.IsLitValue exists");
        Check(campfireType.IsSubclassOf(typeof(Fusion.NetworkBehaviour)), "CampfireCooking is NetworkBehaviour");

        // 4. Buat instance runtime utk test logika drain/regen
        var go = new GameObject("WarmthProbe");
        go.SetActive(false);
        var probe = go.AddComponent<PlayerWarmthSystem>();
        var so = new SerializedObject(probe);

        SetFloat(so, "maxWarmth", 100f);
        SetFloat(so, "startWarmth", 100f);
        SetFloat(so, "nightDrainPerSecond", 10f);   // percepat utk test
        SetFloat(so, "dayRegenPerSecond", 20f);
        SetFloat(so, "campfireRegenPerSecond", 50f);
        SetFloat(so, "freezeDamageGracePeriod", 0f);
        so.ApplyModifiedProperties();

        go.SetActive(true);
        var survProbe = go.AddComponent<PlayerSurvivalSystem>();

        // Baca warmth via reflection setelah 1 frame logika (hindari race timing editor:
        // Update() bisa terpanggil dengan dt besar saat menu execute, drain Night cepat).
        var warmthField = typeof(PlayerWarmthSystem).GetField("currentWarmth",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        float w0 = (float)warmthField.GetValue(probe);
        Check(w0 >= 99f, "start warmth near 100 (got " + w0.ToString("F1") + ")");

        probe.AddWarmth(-50f);
        float w1 = (float)warmthField.GetValue(probe);
        Check(w1 <= w0 - 40f, "AddWarmth(-50) decreases a lot (got " + w1.ToString("F1") + " from " + w0.ToString("F1") + ")");
        probe.RestoreWarmth();
        Check(Mathf.Approximately(probe.CurrentWarmth, 100f), "RestoreWarmth -> 100");

        Object.DestroyImmediate(go);

        Debug.Log(string.Format("[WarmthTest] RESULT: {0} pass / {1} fail", pass, fail));
    }

    private static void SetFloat(SerializedObject so, string name, float value)
    {
        var p = so.FindProperty(name);
        if (p != null) p.floatValue = value;
    }
}
