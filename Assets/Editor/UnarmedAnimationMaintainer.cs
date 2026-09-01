#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Regenerates the unarmed animation setup after a fresh clone or a vendor (KINEMATION)
/// re-import. Run once via menu: Tools > Fix Unarmed Animation (vendor re-import).
///
/// Why this exists: the unarmed override controller lives inside the vendor folder
/// (Assets/KINEMATION/.../Fists/FPSAnimator_Unarmed_Generic.overrideController) which is
/// git-ignored. SasPlayerFusion2 references it by GUID, so after re-importing the vendor
/// package that reference breaks. This script rebuilds the override controller from the
/// vendor's base controller, re-applies the full unarmed mapping (incl. the synthesized
/// unarmed jump clips under Assets/Animations/UnarmedJump, which ARE versioned), and
/// re-links the prefab. Idempotent — safe to run repeatedly.
///
/// Original fixes (2026-09-01/02, all runtime-verified):
/// - weapon pose on scene entry  -> prefab default controller = unarmed override
/// - jump showed a rifle grip    -> base C_Jump* clips are weapon-authored; synthesized
///   unarmed jump clips (body curves from base + constant arm curves from Unarmed_Idle)
/// - empty weaponPrefabs         -> only C_Rifle_* clips need overriding, never bare motion
/// </summary>
public static class UnarmedAnimationMaintainer
{
    const string VendorOverridePath =
        "Assets/KINEMATION/scriptable-animation-system-main/Assets/Demo/Prefabs/Fists/FPSAnimator_Unarmed_Generic.overrideController";
    const string PrefabPath = "Assets/Assets/Prefabs/SasPlayerFusion2.prefab";
    const string UnarmedSetFolder = "Assets/KINEMATION/scriptable-animation-system-main/Assets/Demo/Animations/Locomotion/Generic/Unarmed";
    const string JumpBaseFolder = "Assets/KINEMATION/scriptable-animation-system-main/Assets/Demo/Animations/Locomotion/Generic/InAir";
    const string JumpOutFolder = "Assets/Animations/UnarmedJump";

    // C_Rifle_* -> unarmed counterpart (only clips whose base carries a weapon pose)
    static readonly (string baseClip, string unarmedClip)[] RifleMap =
    {
        ("C_Rifle_Idle", "Unarmed_Idle"),
        ("C_Rifle_Sprint_Fwd", "C_Unarmed_Sprint_Fwd"),
        ("C_Rifle_Run_Fwd", "Unarmed_Jog_Forward"),
        ("C_Rifle_Run_Fwd_Left", "Unarmed_Jog_Forward_-45"),
        ("C_Rifle_Run_Fwd_Right", "Unarmed_Jog_Forward_45"),
        ("C_Rifle_Run_Bwd", "Unarmed_Jog_Bwd"),
        ("C_Rifle_Run_Bwd_Left", "Unarmed_Jog_Bwd_-45"),
        ("C_Rifle_Run_Bwd_Right", "Unarmed_Jog_Bwd_45"),
        ("C_Rifle_Strafe_Left", "Unarmed_Jog_Left"),
        ("C_Rifle_Strafe_Right", "Unarmed_Jog_Right"),
    };

    [MenuItem("Tools/Fix Unarmed Animation (vendor re-import)")]
    public static void Run()
    {
        var report = new System.Text.StringBuilder();

        var idle = FindClip("Unarmed_Idle", UnarmedSetFolder);
        if (idle == null) { Debug.LogError("[UnarmedFix] Unarmed_Idle.anim not found — is KINEMATION imported?"); return; }

        // 1) Ensure the unarmed override controller exists (vendor re-import wipes it).
        var oc = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(VendorOverridePath);
        if (oc == null)
        {
            var baseCtl = FindController("FPSAnimator_Generic");
            if (baseCtl == null) { Debug.LogError("[UnarmedFix] base FPSAnimator_Generic controller not found."); return; }
            oc = new AnimatorOverrideController { runtimeAnimatorController = baseCtl };
            AssetDatabase.CreateAsset(oc, VendorOverridePath);
            report.AppendLine("created override controller: " + VendorOverridePath);
        }

        // 2) Ensure the synthesized unarmed jump clips exist (they are versioned, so on a
        //    fresh clone they are already present; regenerate only when missing).
        var jumpOverrides = new (string baseClip, string overrideClip)[3];
        string[] jumpNames = { "C_JumpStart", "C_JumpLoop", "C_JumpEnd" };
        for (int i = 0; i < jumpNames.Length; i++)
        {
            var generated = AssetDatabase.LoadAssetAtPath<AnimationClip>(JumpOutFolder + "/" + jumpNames[i] + "_Unarmed.anim");
            if (generated == null)
            {
                var src = AssetDatabase.LoadAssetAtPath<AnimationClip>(JumpBaseFolder + "/" + jumpNames[i] + ".anim");
                if (src == null) { Debug.LogError("[UnarmedFix] missing base clip " + jumpNames[i]); return; }
                generated = SynthesizeUnarmedJump(src, idle, JumpOutFolder + "/" + jumpNames[i] + "_Unarmed.anim");
                report.AppendLine("synthesized " + jumpNames[i] + "_Unarmed");
            }
            jumpOverrides[i] = (jumpNames[i], generated.name);
        }

        // 3) Build the full override list: 10 C_Rifle_* mappings + 3 jump mappings.
        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        int applied = 0;
        foreach (var (baseName, unarmedName) in RifleMap)
        {
            var baseClip = FindClip(baseName, null);
            var unarmed = FindClip(unarmedName, UnarmedSetFolder);
            if (baseClip == null || unarmed == null)
            {
                Debug.LogWarning("[UnarmedFix] skip (not found): " + baseName + " -> " + unarmedName);
                continue;
            }
            pairs.Add(new KeyValuePair<AnimationClip, AnimationClip>(baseClip, unarmed));
            applied++;
        }
        for (int i = 0; i < jumpOverrides.Length; i++)
        {
            var baseClip = FindClip(jumpOverrides[i].baseClip, JumpBaseFolder);
            var overClip = FindClip(jumpOverrides[i].overrideClip, JumpOutFolder);
            if (baseClip == null || overClip == null) continue;
            pairs.Add(new KeyValuePair<AnimationClip, AnimationClip>(baseClip, overClip));
            applied++;
        }
        oc.ApplyOverrides(pairs);
        EditorUtility.SetDirty(oc);
        report.AppendLine("override pairs applied: " + applied);

        // 4) Re-link the prefab's default controller (fixes the weapon-pose flash on scene
        //    entry after a vendor re-import resets GUIDs).
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) { Debug.LogError("[UnarmedFix] prefab not found: " + PrefabPath); return; }
        var animator = prefab.GetComponent<Animator>();
        if (animator != null && animator.runtimeAnimatorController != oc)
        {
            animator.runtimeAnimatorController = oc;
            EditorUtility.SetDirty(prefab);
            report.AppendLine("prefab controller re-linked");
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[UnarmedFix] done:\n" + report);
    }

    static AnimationClip FindClip(string name, string folder)
    {
        var guids = folder != null
            ? AssetDatabase.FindAssets(name + " t:AnimationClip", new[] { folder })
            : AssetDatabase.FindAssets(name + " t:AnimationClip");
        for (int i = 0; i < guids.Length; i++)
        {
            var path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip != null && clip.name == name) return clip;
        }
        return null;
    }

    static RuntimeAnimatorController FindController(string name)
    {
        var guids = AssetDatabase.FindAssets(name + " t:RuntimeAnimatorController");
        for (int i = 0; i < guids.Length; i++)
        {
            var path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (path.EndsWith(".overrideController")) continue; // want the plain controller
            var ctl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path);
            if (ctl != null && ctl.name == name) return ctl;
        }
        return null;
    }

    // Body (non-arm) curves from the weapon-authored jump clip + constant arm/hand pose from
    // Unarmed_Idle. Arm paths: Shoulder/UpperArm/LowerArm/Hand incl. all finger bones.
    static AnimationClip SynthesizeUnarmedJump(AnimationClip src, AnimationClip idle, string outPath)
    {
        var clip = new AnimationClip { frameRate = src.frameRate, wrapMode = src.wrapMode };
        AnimationUtility.SetAnimationClipSettings(clip, AnimationUtility.GetAnimationClipSettings(src));
        bool IsArm(string p) =>
            p.Contains("Shoulder") || p.Contains("UpperArm") || p.Contains("LowerArm") || p.Contains("Hand");

        var srcBindings = AnimationUtility.GetCurveBindings(src);
        foreach (var b in srcBindings)
        {
            if (IsArm(b.path)) continue;
            AnimationUtility.SetEditorCurve(clip, b, AnimationUtility.GetEditorCurve(src, b));
        }
        var idleBindings = AnimationUtility.GetCurveBindings(idle);
        foreach (var b in idleBindings)
        {
            if (!IsArm(b.path)) continue;
            AnimationUtility.SetEditorCurve(clip, b, AnimationUtility.GetEditorCurve(idle, b));
        }
        AssetDatabase.CreateAsset(clip, outPath); // overwrites if the path already exists
        return clip;
    }
}
#endif
