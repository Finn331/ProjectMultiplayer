using UnityEditor;
using UnityEngine;

/// <summary>Self-test untuk PlayerFootstepAudio (editor only).</summary>
public static class PlayerFootstepAudioSelfTest
{
    [MenuItem("Project Multiplayer/Run Footstep Audio Self Test")]
    public static void Run()
    {
        int pass = 0, fail = 0;
        void Check(bool cond, string name)
        {
            if (cond) { pass++; Debug.Log($"[FootstepTest] PASS {name}"); }
            else { fail++; Debug.LogError($"[FootstepTest] FAIL {name}"); }
        }

        // 1. Script ada di prefab
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Assets/Prefabs/FusionPlayer.prefab");
        Check(prefab != null, "FusionPlayer.prefab loadable");
        var footstep = prefab != null ? prefab.GetComponent<PlayerFootstepAudio>() : null;
        Check(footstep != null, "PlayerFootstepAudio component on prefab");
        var src = prefab != null ? prefab.GetComponent<AudioSource>() : null;
        Check(src != null, "AudioSource component on prefab");

        // 2. Clips ter-import di Resources path
        string[] surfaces = { "wood", "snow", "ice" };
        string[] actions = { "walk", "run", "sprint", "jumpup", "jumpdown" };
        foreach (string s in surfaces)
        {
            foreach (string a in actions)
            {
                AudioClip[] clips = Resources.LoadAll<AudioClip>("Audio/Footsteps/" + s + "/" + a);
                bool ok = clips != null && clips.Length > 0;
                Check(ok, "clips " + s + "/" + a + " (" + (clips != null ? clips.Length : 0) + ")");
            }
        }

        // 3. LoadClipsFromResources mengisi set sesuai surface default (wood)
        var so = new SerializedObject(footstep);
        footstep.LoadClipsFromResources();
        SerializedProperty walkProp = so.FindProperty("walkClips");
        Check(walkProp != null && walkProp.arraySize > 0,
              "auto-load walkClips wood (" + (walkProp != null ? walkProp.arraySize : 0) + ")");

        Debug.Log($"[FootstepTest] RESULT: {pass} pass / {fail} fail");
    }
}
