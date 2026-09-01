using Fusion;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// SAFE, reversible tool: clones the existing, fully-wired FusionPlayer.prefab (preserving ALL
/// Inventory/Survival/persistence/Fusion serialized references) and swaps ONLY the visual + control
/// layer to the KINEMATION/SAS FPSGenericPlayer rig (first-person SAS controller + camera + weapon).
///
/// FusionPlayer.prefab is NOT modified. Output: Assets/Assets/Prefabs/SasPlayerFusion.prefab.
/// Invoke: Tools/ProjectMultiplayer/Swap FusionPlayer to SAS Rig
/// </summary>
public static class SasRigSwapBuilder
{
    private const string FusionPlayerPath = "Assets/Assets/Prefabs/FusionPlayer.prefab";
    private const string SasPrefabPath = "Assets/KINEMATION/scriptable-animation-system-main/Assets/Demo/Prefabs/FPSGenericPlayer.prefab";
    private const string OutputPrefabPath = "Assets/Assets/Prefabs/SasPlayerFusion.prefab";

    [MenuItem("Tools/ProjectMultiplayer/Swap FusionPlayer to SAS Rig")]
    public static void Build()
    {
        GameObject fusion = AssetDatabase.LoadAssetAtPath<GameObject>(FusionPlayerPath);
        GameObject sas = AssetDatabase.LoadAssetAtPath<GameObject>(SasPrefabPath);
        if (fusion == null) { Debug.LogError("[SasRigSwap] fusion not found"); return; }
        if (sas == null) { Debug.LogError("[SasRigSwap] sas not found"); return; }

        // --- Instantiate FPSGenericPlayer as a LIVE instance (children then become normal scene objects) ---
        GameObject sasInstance = (GameObject)PrefabUtility.InstantiatePrefab(sas);
        sasInstance.name = "FPSGenericPlayer_Instance";
        // Detach from prefab so we can reparent children safely (breaks prefab link, that's fine — source asset untouched).
        GameObject sasLive = UnityEngine.Object.Instantiate(sasInstance);
        UnityEngine.Object.DestroyImmediate(sasInstance);

        // --- Clone FusionPlayer (keeps all networked gameplay serialized refs) ---
        GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(fusion);
        root.name = "SasPlayerFusion";
        // Detach from source prefab so grafts are legal & not double-linked.
        GameObject rootLive = UnityEngine.Object.Instantiate(root);
        UnityEngine.Object.DestroyImmediate(root);

        // --- Remove mixamo visual/control layer from the clone ---
        RemoveChild(rootLive, "Player Prototype");
        RemoveChild(rootLive, "HeadAimRig");
        RemoveChild(rootLive, "Camera Holder");

        // Two-pass removal: first destroy the RequireComponent dependents, then the base component,
        // so Unity doesn't refuse ("Can't remove FusionPlayerMovement because FusionFPSController depends on it").
        RemoveComponentsByName(rootLive, "FusionFPSController");
        RemoveComponentsByName(rootLive,
            "FPSControllerMobile",            // mobile third-person input -> replaced by FusionFpsSasBridge + SAS FPSController
            "PlayerProceduralAnimation",
            "PlayerAnimatorDriver",
            "RigBuilder",
            "HeadLookRigAutoSetup",
            "RigBuilderRuntimeBootstrap",
            "LowHealthInjuredAnimationController",
            "PlayerAxeCombat",                 // axe -> replaced by SAS FPSItem gun
            "HotbarHeldItemPresenter",
            "FusionAnimatorSync",             // mixes humanoid Speed params; SAS FPSAnimator controls Animator instead
            "FusionPlayerMovement",           // standalone base -> superseded by FusionFpsSasBridge (which extends it)
            "FPSMovement"                     // remove old SAS movement (will add fresh from FPSGenericPlayer)
        );

        // --- Graft the SAS rig children + root SAS components from the LIVE SAS instance ---
        // Grab children from the LIVE SAS instance, then destroy the instance.
        List<GameObject> sasChildren = new List<GameObject>();
        for (int i = 0; i < sasLive.transform.childCount; i++)
        {
            Transform child = sasLive.transform.GetChild(i);
            sasChildren.Add(child.gameObject);
        }
        foreach (GameObject child in sasChildren)
        {
            if (child == null) continue;
            string name = child.name;
            // Camera is nested inside Skeleton->...->Head->Camera, so moving Skeleton carries it.
            child.transform.SetParent(rootLive.transform, true);
            child.name = name;
        }

        // Move the SAS root components (FPSMovement/FPSController/FPSAnimator/FPSPlayablesController/
        // FPSBoneController/UserInputController/KRigComponent/Recoil*) FROM the SAS instance onto the clone,
        // copying their full serialized configuration (settings/weaponPrefabs/camera refs) from FPSGenericPlayer.
        CopyComponent<Demo.Scripts.Runtime.Character.FPSMovement>(sasLive, rootLive);
        CopyComponent<KINEMATION.Shared.KAnimationCore.Runtime.Input.UserInputController>(sasLive, rootLive);
        CopyComponent<KINEMATION.FPSAnimationFramework.Runtime.Core.FPSAnimator>(sasLive, rootLive);
        CopyComponent<Demo.Scripts.Runtime.Character.FPSController>(sasLive, rootLive);
        CopyComponent<KINEMATION.FPSAnimationFramework.Runtime.Playables.FPSPlayablesController>(sasLive, rootLive);
        CopyComponent<KINEMATION.FPSAnimationFramework.Runtime.Core.FPSBoneController>(sasLive, rootLive);
        CopyComponentByName(sasLive, rootLive, "RecoilAnimation");
        CopyComponentByName(sasLive, rootLive, "RecoilPattern");
        UnityEngine.Object.DestroyImmediate(sasLive);

        // --- Ensure SAS chains exist (safety net; CopyComponent above is the authoritative path) ---
        EnsureComponent<Demo.Scripts.Runtime.Character.FPSMovement>(rootLive);
        EnsureComponent<KINEMATION.Shared.KAnimationCore.Runtime.Input.UserInputController>(rootLive);
        EnsureComponent<KINEMATION.FPSAnimationFramework.Runtime.Core.FPSAnimator>(rootLive);
        EnsureComponent<Demo.Scripts.Runtime.Character.FPSController>(rootLive);
        EnsureComponent<KINEMATION.FPSAnimationFramework.Runtime.Playables.FPSPlayablesController>(rootLive);
        EnsureComponent<KINEMATION.FPSAnimationFramework.Runtime.Core.FPSBoneController>(rootLive);
        EnsureComponent<KINEMATION.Shared.KAnimationCore.Runtime.Rig.KRigComponent>(rootLive);
        EnsureComponent<KINEMATION.FPSAnimationFramework.Runtime.Camera.FPSCameraController>(rootLive);
        EnsureComponentByName(rootLive, "RecoilAnimation");
        EnsureComponentByName(rootLive, "RecoilPattern");

        // --- Fusion mobile bridge (replaces FPSControllerMobile) ---
        EnsureComponent<FusionFpsSasBridge>(rootLive);
        // Remove any standalone FusionPlayerMovement base component (not the bridge) to avoid
        // double FixedUpdateNetwork. Bridge extends FusionPlayerMovement, satisfying all
        // GetComponent<FusionPlayerMovement>() callers (FootstepAudio, SurfaceEffects, Death, Downed, spawner).
        var allMovements = rootLive.GetComponents<FusionPlayerMovement>();
        for (int i = 0; i < allMovements.Length; i++)
        {
            if (allMovements[i] != null && allMovements[i].GetType() == typeof(FusionPlayerMovement))
            {
                Object.DestroyImmediate(allMovements[i]);
            }
        }

        // --- Save as NEW prefab (does not touch FusionPlayer.prefab) ---
        if (rootLive.GetComponent<CharacterController>() == null)
        {
            rootLive.AddComponent<CharacterController>();
        }
        AssetDatabase.DeleteAsset(OutputPrefabPath);
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(rootLive, OutputPrefabPath);
        Object.DestroyImmediate(rootLive);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        if (saved != null)
        {
            Debug.Log("[SasRigSwap] SAVED: " + OutputPrefabPath);
            Selection.activeObject = saved;
            EditorGUIUtility.PingObject(saved);
        }
        else
        {
            Debug.LogError("[SasRigSwap] Failed to save prefab");
        }
    }

    private static void RemoveChild(GameObject root, string childName)
    {
        Transform t = root.transform.Find(childName);
        if (t != null) Object.DestroyImmediate(t.gameObject);
    }

    private static void RemoveComponentsByName(GameObject root, params string[] names)
    {
        HashSet<string> set = new HashSet<string>(names);
        Component[] comps = root.GetComponents<Component>();
        for (int i = 0; i < comps.Length; i++)
        {
            Component c = comps[i];
            if (c != null && set.Contains(c.GetType().Name))
            {
                Object.DestroyImmediate(c);
            }
        }
    }

    private static void EnsureComponent<T>(GameObject go) where T : Component
    {
        if (go.GetComponent<T>() == null) go.AddComponent<T>();
    }

    /// <summary>Copy a component (with full serialized values) from a source GameObject to a target GameObject.</summary>
    private static void CopyComponent<T>(GameObject source, GameObject target) where T : Component
    {
        T src = source.GetComponent<T>();
        if (src == null) { Debug.LogWarning("[SasRigSwap] source missing " + typeof(T).Name); return; }
        T dst = target.GetComponent<T>();
        if (dst == null) dst = target.AddComponent<T>();
        UnityEditor.EditorUtility.CopySerialized(src, dst);
    }

    private static void CopyComponentByName(GameObject source, GameObject target, string typeName)
    {
        System.Type t = FindTypeInsensitive(typeName);
        if (t == null) { Debug.LogWarning("[SasRigSwap] type not found: " + typeName); return; }
        Component src = source.GetComponent(t);
        if (src == null) { Debug.LogWarning("[SasRigSwap] source missing " + typeName); return; }
        Component dst = target.GetComponent(t);
        if (dst == null) dst = target.AddComponent(t);
        UnityEditor.EditorUtility.CopySerialized(src, dst);
    }

    private static void EnsureComponentByName(GameObject go, string typeName)
    {
        System.Type t = FindTypeInsensitive(typeName);
        if (t == null) { Debug.LogWarning("[SasRigSwap] type not found: " + typeName); return; }
        if (go.GetComponent(t) == null) go.AddComponent(t);
    }

    private static System.Type FindTypeInsensitive(string name)
    {
        System.Type best = null;
        foreach (System.Reflection.Assembly asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            System.Type[] types = null;
            try { types = asm.GetTypes(); } catch { }
            if (types == null) continue;
            for (int i = 0; i < types.Length; i++)
            {
                System.Type t = types[i];
                if (t != null && t.Name == name)
                {
                    best = t;
                    if (t.Namespace != null && t.Namespace.StartsWith("KINEMATION")) return t;
                }
            }
        }
        return best;
    }
}
