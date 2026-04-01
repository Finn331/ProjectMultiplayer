using System.Collections.Generic;
using UnityEngine;

public class LowHealthInjuredAnimationController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerSurvivalSystem survivalSystem;

    [Header("Threshold")]
    [SerializeField, Range(0.01f, 1f)] private float injuredHealthThresholdNormalized = 0.35f;
    [SerializeField, Range(0.05f, 1f)] private float injuredMovementSpeedMultiplier = 0.2f;

    [Header("Debug Trigger (Inspector)")]
    [SerializeField] private bool overrideInjuredFromInspector;
    [SerializeField] private bool forcedInjuredState;

    [Header("Injured Clips (MoCapCentral)")]
    [SerializeField] private AnimationClip injuredIdleClip;
    [SerializeField] private AnimationClip injuredWalkClip;
    [SerializeField] private AnimationClip injuredWalkLeftClip;
    [SerializeField] private AnimationClip injuredWalkRightClip;
    [SerializeField] private AnimationClip injuredWalkBackwardClip;
    [SerializeField] private bool useOriginalBackwardWhenMissingInjuredBackward = true;
    [SerializeField] private AnimationClip injuredRunClip;
    [SerializeField] private AnimationClip injuredTurnLeftClip;
    [SerializeField] private AnimationClip injuredTurnRightClip;
    [SerializeField] private AnimationClip injuredTurnClip;

    [Header("Optional Replacements")]
    [SerializeField] private bool replaceJumpAndFall;
    [SerializeField] private AnimationClip injuredAirborneLoopClip;

    private RuntimeAnimatorController healthyController;
    private AnimatorOverrideController injuredOverrideController;

    public bool IsInjuredActive => injuredStateApplied;
    public float InjuredHealthThresholdNormalized => injuredHealthThresholdNormalized;
    public float InjuredMovementSpeedMultiplier => Mathf.Clamp01(injuredMovementSpeedMultiplier);
    private bool injuredStateApplied;

    private void Awake()
    {
#if UNITY_EDITOR
        this.TryAutoAssignInjuredClipsIfNeeded();
#endif

        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (survivalSystem == null)
        {
            survivalSystem = GetComponent<PlayerSurvivalSystem>();
        }

        if (animator != null)
        {
            healthyController = animator.runtimeAnimatorController;
        }

        this.EnsureDefaultRunClipFallback();
        this.BuildInjuredOverrideControllerIfPossible();
    }

    private void OnEnable()
    {
        if (survivalSystem != null)
        {
            survivalSystem.StatsChanged += this.OnSurvivalStatsChanged;
            this.RefreshInjuredState();
        }
        else
        {
            this.ApplyState(false);
        }
    }

    private void OnDisable()
    {
        if (survivalSystem != null)
        {
            survivalSystem.StatsChanged -= this.OnSurvivalStatsChanged;
        }

        if (animator != null && healthyController != null)
        {
            animator.runtimeAnimatorController = healthyController;
        }

        injuredStateApplied = false;
    }

    private void OnSurvivalStatsChanged(float health, float hunger, float thirst)
    {
        this.RefreshInjuredState();
    }

    private void Update()
    {
        // Allow live tweaking in Inspector during play mode.
        if (Application.isPlaying)
        {
            this.RefreshInjuredState();
        }
    }

    private void UpdateStateFromHealth(float healthNormalized)
    {
        bool shouldUseInjured =
            animator != null &&
            injuredOverrideController != null &&
            healthNormalized <= injuredHealthThresholdNormalized;

        this.ApplyState(shouldUseInjured);
    }

    private void RefreshInjuredState()
    {
        if (overrideInjuredFromInspector)
        {
            this.ApplyState(forcedInjuredState);
            return;
        }

        float normalized = survivalSystem != null ? survivalSystem.HealthNormalized : 1f;
        this.UpdateStateFromHealth(normalized);
    }

    private void ApplyState(bool useInjured)
    {
        if (animator == null || healthyController == null)
        {
            return;
        }

        if (useInjured == injuredStateApplied)
        {
            return;
        }

        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
        int currentStateHash = currentState.fullPathHash;
        float normalizedTime = currentState.normalizedTime % 1f;

        animator.runtimeAnimatorController = useInjured
            ? injuredOverrideController
            : healthyController;

        if (animator.isActiveAndEnabled)
        {
            animator.Update(0f);

            if (currentStateHash != 0)
            {
                animator.CrossFade(currentStateHash, 0.08f, 0, normalizedTime);
            }
        }

        injuredStateApplied = useInjured;
    }

    private void BuildInjuredOverrideControllerIfPossible()
    {
        if (healthyController == null || injuredIdleClip == null || injuredWalkClip == null)
        {
            return;
        }

        injuredOverrideController = new AnimatorOverrideController(healthyController);

        List<KeyValuePair<AnimationClip, AnimationClip>> clipOverrides =
            new List<KeyValuePair<AnimationClip, AnimationClip>>(injuredOverrideController.overridesCount);
        injuredOverrideController.GetOverrides(clipOverrides);

        for (int i = 0; i < clipOverrides.Count; i++)
        {
            AnimationClip originalClip = clipOverrides[i].Key;
            AnimationClip replacement = this.GetReplacementClipForOriginal(originalClip);

            if (replacement != null)
            {
                clipOverrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(originalClip, replacement);
            }
        }

        injuredOverrideController.ApplyOverrides(clipOverrides);
    }

    private AnimationClip GetReplacementClipForOriginal(AnimationClip originalClip)
    {
        if (originalClip == null)
        {
            return null;
        }

        string clipName = originalClip.name.ToLowerInvariant();
        bool isLocomotion =
            clipName.Contains("walk") ||
            clipName.Contains("run") ||
            clipName.Contains("sprint") ||
            clipName.Contains("loco");

        if (clipName.Contains("idle"))
        {
            return injuredIdleClip;
        }

        if (isLocomotion)
        {
            if (clipName.Contains("left"))
            {
                return injuredWalkLeftClip;
            }

            if (clipName.Contains("right"))
            {
                return injuredWalkRightClip;
            }

            if (clipName.Contains("back"))
            {
                if (injuredWalkBackwardClip != null)
                {
                    return injuredWalkBackwardClip;
                }

                if (useOriginalBackwardWhenMissingInjuredBackward)
                {
                    return null;
                }

                return injuredWalkClip;
            }

            return injuredWalkClip;
        }

        if (clipName.Contains("turn"))
        {
            if (clipName.Contains("left"))
            {
                return injuredTurnLeftClip;
            }

            if (clipName.Contains("right"))
            {
                return injuredTurnRightClip;
            }

            return injuredTurnClip;
        }

        if (replaceJumpAndFall &&
            (clipName.Contains("jump") || clipName.Contains("fall") || clipName.Contains("land")))
        {
            return injuredAirborneLoopClip != null ? injuredAirborneLoopClip : injuredIdleClip;
        }

        return null;
    }

    private void EnsureDefaultRunClipFallback()
    {
        if (injuredTurnLeftClip == null)
        {
            injuredTurnLeftClip = injuredTurnClip;
        }

        if (injuredTurnRightClip == null)
        {
            injuredTurnRightClip = injuredTurnClip;
        }

        if (injuredWalkLeftClip == null)
        {
            injuredWalkLeftClip = injuredTurnLeftClip != null ? injuredTurnLeftClip : injuredWalkClip;
        }

        if (injuredWalkRightClip == null)
        {
            injuredWalkRightClip = injuredTurnRightClip != null ? injuredTurnRightClip : injuredWalkClip;
        }

        if (injuredRunClip == null)
        {
            injuredRunClip = injuredWalkClip;
        }

        if (injuredTurnClip == null)
        {
            injuredTurnClip = injuredTurnLeftClip != null ? injuredTurnLeftClip : injuredWalkClip;
        }
    }

    [ContextMenu("Debug/Force Injured ON")]
    private void DebugForceInjuredOn()
    {
        overrideInjuredFromInspector = true;
        forcedInjuredState = true;
        this.RefreshInjuredState();
    }

    [ContextMenu("Debug/Force Injured OFF")]
    private void DebugForceInjuredOff()
    {
        overrideInjuredFromInspector = true;
        forcedInjuredState = false;
        this.RefreshInjuredState();
    }

    [ContextMenu("Debug/Use Health Threshold")]
    private void DebugUseHealthThreshold()
    {
        overrideInjuredFromInspector = false;
        forcedInjuredState = false;
        this.RefreshInjuredState();
    }

#if UNITY_EDITOR
    private const string InjuredRootPath = "Assets/MoCapCentral/MC_Sample/Animations/Injured/";

    private void OnValidate()
    {
        this.TryAutoAssignInjuredClipsIfNeeded();
    }

    [ContextMenu("Auto Assign Injured Clips From MoCapCentral")]
    private void AutoAssignInjuredClipsFromMoCapCentral()
    {
        injuredIdleClip = this.LoadFirstClipFromFbx(InjuredRootPath + "MCU_am_InjuredBelly_Idle_01.FBX");
        injuredWalkClip = this.LoadFirstClipFromFbx(InjuredRootPath + "MCU_am_InjuredBelly_Loco_Walk_Fwd_NoRM.FBX");
        injuredWalkBackwardClip = null;
        injuredWalkLeftClip = this.LoadFirstClipFromFbx(InjuredRootPath + "MCU_am_InjuredBelly_Idle_TurnL90.FBX");
        injuredWalkRightClip = this.LoadFirstClipFromFbx(InjuredRootPath + "MCU_am_InjuredBelly_Idle_TurnR90.FBX");
        injuredRunClip = this.LoadFirstClipFromFbx(InjuredRootPath + "MCU_am_InjuredBelly_Loco_Walk_Fwd_NoRM.FBX");
        injuredTurnLeftClip = this.LoadFirstClipFromFbx(InjuredRootPath + "MCU_am_InjuredBelly_Idle_TurnL90.FBX");
        injuredTurnRightClip = this.LoadFirstClipFromFbx(InjuredRootPath + "MCU_am_InjuredBelly_Idle_TurnR90.FBX");
        injuredTurnClip = injuredTurnLeftClip;
        injuredAirborneLoopClip = this.LoadFirstClipFromFbx(InjuredRootPath + "MCU_am_InjuredBelly_DropToFloor_Loop.FBX");

        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (survivalSystem == null)
        {
            survivalSystem = GetComponent<PlayerSurvivalSystem>();
        }

        UnityEditor.EditorUtility.SetDirty(this);
    }

    private void TryAutoAssignInjuredClipsIfNeeded()
    {
        bool hasCoreClips =
            injuredIdleClip != null &&
            injuredWalkClip != null &&
            injuredWalkLeftClip != null &&
            injuredWalkRightClip != null &&
            injuredTurnLeftClip != null &&
            injuredTurnRightClip != null;

        if (hasCoreClips)
        {
            return;
        }

        this.AutoAssignInjuredClipsFromMoCapCentral();
    }

    private AnimationClip LoadFirstClipFromFbx(string assetPath)
    {
        if (!UnityEditor.AssetDatabase.LoadMainAssetAtPath(assetPath))
        {
            return null;
        }

        Object[] assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(assetPath);
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is AnimationClip clip && !clip.name.StartsWith("__preview__"))
            {
                return clip;
            }
        }

        return null;
    }
#endif
}
