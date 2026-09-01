using Fusion;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// OPTION A — build the SAS networked player from FPSGenericPlayer PURELY (source rig + Animator +
/// runtimeAnimatorController kept exactly as-is), then LAYER Fusion networking + Inventory + Survival +
/// persistence + footstep systems on top. No mixamo graft => no Animator/playable-graph conflict.
///
/// FusionPlayer.prefab is NOT modified. Output: Assets/Assets/Prefabs/SasPlayerFusion2.prefab.
/// Invoke: Tools/ProjectMultiplayer/Build SAS Player (Option A)
/// </summary>
public static class SasPlayerBuilderA
{
    private const string SourcePrefabPath = "Assets/KINEMATION/scriptable-animation-system-main/Assets/Demo/Prefabs/FPSGenericPlayer.prefab";
    private const string OutputPrefabPath = "Assets/Assets/Prefabs/SasPlayerFusion2.prefab";

    [MenuItem("Tools/ProjectMultiplayer/Build SAS Player (Option A)")]
    public static void Build()
    {
        GameObject src = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
        if (src == null) { Debug.LogError("[SasPlayerA] source not found"); return; }

        GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(src);
        root.name = "SasPlayerFusion2";
        GameObject live = Object.Instantiate(root);
        Object.DestroyImmediate(root);

        // --- 1. Remove desktop Input System PlayerInput (replaced by FusionFpsSasBridge mobile input) ---
        var playerInput = live.GetComponent<UnityEngine.InputSystem.PlayerInput>();
        if (playerInput != null) Object.DestroyImmediate(playerInput);

        // --- 2. Ensure network CharacterController (FPSGenericPlayer already has one) ---
        if (live.GetComponent<CharacterController>() == null) live.AddComponent<CharacterController>();

        // --- 3. Fusion networking layer ---
        if (live.GetComponent<NetworkObject>() == null) live.AddComponent<NetworkObject>();
        if (live.GetComponent<NetworkTransform>() == null) live.AddComponent<NetworkTransform>();
        if (live.GetComponent<FusionPlayerOwnerSetup>() == null) live.AddComponent<FusionPlayerOwnerSetup>();
        if (live.GetComponent<NetworkObjectPrefabData>() == null) live.AddComponent<NetworkObjectPrefabData>();

        // --- 4. Single SAS movement/look driver (extends FusionPlayerMovement) ---
        if (live.GetComponent<FusionFpsSasBridge>() == null) live.AddComponent<FusionFpsSasBridge>();

        // --- 5. Inventory + Survival + persistence (all GetComponent-resolve, data-driven) ---
        if (live.GetComponent<PlayerInventory>() == null) live.AddComponent<PlayerInventory>();
        if (live.GetComponent<PlayerSurvivalSystem>() == null) live.AddComponent<PlayerSurvivalSystem>();
        if (live.GetComponent<FusionPlayerInventory>() == null) live.AddComponent<FusionPlayerInventory>();
        if (live.GetComponent<FusionPlayerSurvival>() == null) live.AddComponent<FusionPlayerSurvival>();
        if (live.GetComponent<FusionPlayerPersistenceBridge>() == null) live.AddComponent<FusionPlayerPersistenceBridge>();
        if (live.GetComponent<PlayerStatsPersistence>() == null) live.AddComponent<PlayerStatsPersistence>();

        // --- 6. Footstep + surface (require FusionPlayerMovement => satisfied by bridge) ---
        if (live.GetComponent<PlayerFootstepAudio>() == null) live.AddComponent<PlayerFootstepAudio>();
        if (live.GetComponent<PlayerSurfaceDetector>() == null) live.AddComponent<PlayerSurfaceDetector>();
        if (live.GetComponent<PlayerSurfaceEffects>() == null) live.AddComponent<PlayerSurfaceEffects>();

        // Make the SAS FPSMovement get ticked externally by the Fusion bridge.
        var sas = live.GetComponent<Demo.Scripts.Runtime.Character.FPSMovement>();
        if (sas != null) { /* SetExternalTick handled in bridge.Spawned() */ }

        // --- 7. Save as NEW prefab ---
        AssetDatabase.DeleteAsset(OutputPrefabPath);
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(live, OutputPrefabPath);
        Object.DestroyImmediate(live);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        if (saved != null)
        {
            Debug.Log("[SasPlayerA] SAVED: " + OutputPrefabPath);
            Selection.activeObject = saved;
            EditorGUIUtility.PingObject(saved);
        }
        else
        {
            Debug.LogError("[SasPlayerA] Failed to save prefab");
        }
    }
}
