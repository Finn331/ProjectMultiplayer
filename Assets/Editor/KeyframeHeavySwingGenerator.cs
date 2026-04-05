using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class KeyframeHeavySwingGenerator
{
    private const string ClipPath = "Assets/Animations/Player/HeavyWeaponSwing_Keyframed.anim";
    private const string ControllerPath = "Assets/Animations/Player/PlayerLocomotion_KI_Male_Directional.controller";
    private const string StandingAttackClipPath = "Assets/Assets/Model/Player/Prototype/Standing Melee Attack Downward.anim";
    private const string StateName = "Heavy Weapon Swing";
    private const string StandingStateName = "Standing Melee Attack Downward";
    private const string TriggerName = "HeavyWeaponSwing";

    [MenuItem("Tools/Animation/Generate Heavy Weapon Swing Keyframed")]
    public static void Generate()
    {
        AnimationClip clip = BuildClip();
        UpsertClipAsset(clip, ClipPath);
        AssignClipToController(ClipPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Keyframe heavy swing generated and assigned.");
    }

    [MenuItem("Tools/Animation/Use Standing Melee Attack Downward")]
    public static void UseStandingMeleeAttackDownward()
    {
        ReplaceHeavyStateWithStanding();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Attack state now uses Standing Melee Attack Downward and Heavy Weapon Swing state removed.");
    }

    private static void ReplaceHeavyStateWithStanding()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        AnimationClip standingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(StandingAttackClipPath);
        if (controller == null || standingClip == null)
        {
            Debug.LogError("Controller or Standing Melee Attack Downward clip not found.");
            return;
        }

        AnimatorControllerLayer baseLayer = controller.layers[0];
        AnimatorStateMachine sm = baseLayer.stateMachine;
        EnsureTrigger(controller, TriggerName);

        AnimatorState heavyState = FindState(sm, StateName);
        AnimatorState standingState = FindState(sm, StandingStateName);

        Vector3 targetPosition = new Vector3(620f, 260f, 0f);
        if (heavyState != null)
        {
            ChildAnimatorState[] children = sm.states;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].state == heavyState)
                {
                    targetPosition = children[i].position;
                    break;
                }
            }
        }

        if (standingState == null)
        {
            standingState = sm.AddState(StandingStateName, targetPosition);
        }

        standingState.motion = standingClip;
        standingState.speed = 1f;

        EnsureAnyStateTransition(sm, standingState, TriggerName);
        EnsureExitTransitionToIdle(sm, standingState);
        RemoveAnyStateTransitionsTo(sm, heavyState);

        if (heavyState != null)
        {
            sm.RemoveState(heavyState);
        }

        EditorUtility.SetDirty(controller);
    }

    private static AnimationClip BuildClip()
    {
        AnimationClip clip = new AnimationClip
        {
            frameRate = 30f,
            name = "HeavyWeaponSwing_Keyframed"
        };

        // Timeline in seconds: settle -> windup -> impact -> follow-through -> recover
        Keyframe[] k0 = MakeKeys(
            (0f, 0f),
            (0.18f, -12f),
            (0.32f, 8f),
            (0.48f, 4f),
            (0.78f, 0f));

        string hips = "Player Prototype/mixamorig10:Hips";
        string spine2 = "Player Prototype/mixamorig10:Hips/mixamorig10:Spine/mixamorig10:Spine1/mixamorig10:Spine2";
        string rightShoulder = spine2 + "/mixamorig10:RightShoulder";
        string rightArm = rightShoulder + "/mixamorig10:RightArm";
        string rightForeArm = rightArm + "/mixamorig10:RightForeArm";
        string rightHand = rightForeArm + "/mixamorig10:RightHand";
        string leftArm = spine2 + "/mixamorig10:LeftShoulder/mixamorig10:LeftArm";
        string axe = rightHand + "/axe";

        SetEuler(clip, hips, 'y', k0);
        SetEuler(clip, spine2, 'x', MakeKeys((0f, 0f), (0.18f, -10f), (0.32f, 18f), (0.48f, 10f), (0.78f, 0f)));
        SetEuler(clip, spine2, 'y', MakeKeys((0f, 0f), (0.18f, -18f), (0.32f, 10f), (0.48f, 4f), (0.78f, 0f)));

        SetEuler(clip, rightShoulder, 'z', MakeKeys((0f, 0f), (0.18f, 12f), (0.32f, -8f), (0.48f, -4f), (0.78f, 0f)));
        SetEuler(clip, rightArm, 'x', MakeKeys((0f, 0f), (0.18f, -35f), (0.32f, 45f), (0.48f, 15f), (0.78f, 0f)));
        SetEuler(clip, rightArm, 'y', MakeKeys((0f, 0f), (0.18f, 18f), (0.32f, -12f), (0.48f, -6f), (0.78f, 0f)));

        SetEuler(clip, rightForeArm, 'x', MakeKeys((0f, 0f), (0.18f, -25f), (0.32f, 55f), (0.48f, 20f), (0.78f, 0f)));
        SetEuler(clip, rightForeArm, 'y', MakeKeys((0f, 0f), (0.18f, 8f), (0.32f, -14f), (0.48f, -6f), (0.78f, 0f)));

        SetEuler(clip, rightHand, 'x', MakeKeys((0f, 0f), (0.18f, -8f), (0.32f, 22f), (0.48f, 8f), (0.78f, 0f)));
        SetEuler(clip, rightHand, 'y', MakeKeys((0f, 0f), (0.18f, 10f), (0.32f, -12f), (0.48f, -4f), (0.78f, 0f)));

        SetEuler(clip, leftArm, 'x', MakeKeys((0f, 0f), (0.18f, 12f), (0.32f, -18f), (0.48f, -6f), (0.78f, 0f)));
        SetEuler(clip, leftArm, 'y', MakeKeys((0f, 0f), (0.18f, -8f), (0.32f, 12f), (0.48f, 4f), (0.78f, 0f)));

        SetEuler(clip, axe, 'x', MakeKeys((0f, 0f), (0.18f, -30f), (0.32f, 80f), (0.48f, 25f), (0.78f, 0f)));
        SetEuler(clip, axe, 'y', MakeKeys((0f, 0f), (0.18f, 15f), (0.32f, -20f), (0.48f, -8f), (0.78f, 0f)));
        SetEuler(clip, axe, 'z', MakeKeys((0f, 0f), (0.18f, 8f), (0.32f, -10f), (0.48f, -4f), (0.78f, 0f)));

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        return clip;
    }

    private static void AssignClipToController(string clipPath)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (controller == null || clip == null)
        {
            Debug.LogError("Controller or clip not found for assignment.");
            return;
        }

        AnimatorControllerLayer baseLayer = controller.layers[0];
        AnimatorStateMachine sm = baseLayer.stateMachine;

        EnsureTrigger(controller, TriggerName);

        AnimatorState swingState = FindState(sm, StateName);
        if (swingState == null)
        {
            swingState = sm.AddState(StateName, new Vector3(600f, 250f, 0f));
        }

        swingState.motion = clip;
        swingState.speed = 1f;

        EnsureAnyStateTransition(sm, swingState, TriggerName);
        EnsureExitTransitionToIdle(sm, swingState);

        EditorUtility.SetDirty(controller);
    }

    private static void EnsureTrigger(AnimatorController controller, string triggerName)
    {
        foreach (AnimatorControllerParameter p in controller.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == triggerName)
            {
                return;
            }
        }

        controller.AddParameter(triggerName, AnimatorControllerParameterType.Trigger);
    }

    private static AnimatorState FindState(AnimatorStateMachine sm, string name)
    {
        ChildAnimatorState[] states = sm.states;
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i].state != null && states[i].state.name == name)
            {
                return states[i].state;
            }
        }

        return null;
    }

    private static void EnsureAnyStateTransition(AnimatorStateMachine sm, AnimatorState destination, string triggerName)
    {
        AnimatorStateTransition[] transitions = sm.anyStateTransitions;
        for (int i = 0; i < transitions.Length; i++)
        {
            AnimatorStateTransition t = transitions[i];
            if (t == null || t.destinationState != destination)
            {
                continue;
            }

            bool hasTrigger = false;
            foreach (AnimatorCondition c in t.conditions)
            {
                if (c.parameter == triggerName && c.mode == AnimatorConditionMode.If)
                {
                    hasTrigger = true;
                    break;
                }
            }

            if (hasTrigger)
            {
                t.hasExitTime = false;
                t.duration = 0.06f;
                return;
            }
        }

        AnimatorStateTransition created = sm.AddAnyStateTransition(destination);
        created.hasExitTime = false;
        created.duration = 0.06f;
        created.AddCondition(AnimatorConditionMode.If, 0f, triggerName);
    }

    private static void RemoveAnyStateTransitionsTo(AnimatorStateMachine sm, AnimatorState destination)
    {
        if (sm == null || destination == null)
        {
            return;
        }

        AnimatorStateTransition[] transitions = sm.anyStateTransitions;
        for (int i = transitions.Length - 1; i >= 0; i--)
        {
            AnimatorStateTransition transition = transitions[i];
            if (transition != null && transition.destinationState == destination)
            {
                sm.RemoveAnyStateTransition(transition);
            }
        }
    }

    private static void EnsureExitTransitionToIdle(AnimatorStateMachine sm, AnimatorState swingState)
    {
        AnimatorState idle = FindState(sm, "Idle");
        if (idle == null)
        {
            return;
        }

        AnimatorStateTransition[] transitions = swingState.transitions;
        for (int i = 0; i < transitions.Length; i++)
        {
            AnimatorStateTransition t = transitions[i];
            if (t != null && t.destinationState == idle)
            {
                t.hasExitTime = true;
                t.exitTime = 0.92f;
                t.duration = 0.08f;
                return;
            }
        }

        AnimatorStateTransition created = swingState.AddTransition(idle);
        created.hasExitTime = true;
        created.exitTime = 0.92f;
        created.duration = 0.08f;
    }

    private static void UpsertClipAsset(AnimationClip clip, string path)
    {
        AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(clip, path);
            return;
        }

        EditorUtility.CopySerialized(clip, existing);
        EditorUtility.SetDirty(existing);
    }

    private static Keyframe[] MakeKeys(params (float time, float value)[] values)
    {
        List<Keyframe> keys = new List<Keyframe>(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            Keyframe k = new Keyframe(values[i].time, values[i].value);
            keys.Add(k);
        }

        return keys.ToArray();
    }

    private static void SetEuler(AnimationClip clip, string path, char axis, Keyframe[] keys)
    {
        string property = axis switch
        {
            'x' => "localEulerAnglesRaw.x",
            'y' => "localEulerAnglesRaw.y",
            _ => "localEulerAnglesRaw.z"
        };

        EditorCurveBinding binding = new EditorCurveBinding
        {
            type = typeof(Transform),
            path = path,
            propertyName = property
        };

        AnimationCurve curve = new AnimationCurve(keys);
        for (int i = 0; i < curve.keys.Length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
        }

        AnimationUtility.SetEditorCurve(clip, binding, curve);
    }
}
