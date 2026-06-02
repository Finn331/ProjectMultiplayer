using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class FusionPlayerCombat : NetworkBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerAxeCombat axeCombat;
    [SerializeField] private string swingTrigger = "HeavyWeaponSwing";
    [SerializeField] private string fallbackSwingTrigger = "Swing";

    [Networked] private NetworkBool AxeEquipped { get; set; }

    private bool lastAppliedAxeEquipped;
    private bool hasAppliedAxeState;

    public override void Spawned()
    {
        ResolveReferences();
        ApplyAxeEquippedState(AxeEquipped);
    }

    public override void Render()
    {
        ApplyAxeEquippedState(AxeEquipped);
    }

    private void Awake()
    {
        ResolveReferences();
    }

    public void SetAxeEquippedForFusion(bool equipped)
    {
        ResolveReferences();
        ApplyAxeEquippedState(equipped);

        if (!IsNetworkReady() || !HasFusionInputAuthority())
        {
            return;
        }

        if (AxeEquipped != equipped)
        {
            AxeEquipped = equipped;
        }
    }

    public bool RequestSwing()
    {
        if (!IsNetworkReady() || !HasFusionInputAuthority())
        {
            return false;
        }

        RPC_Swing();
        return true;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
    private void RPC_Swing(RpcInfo info = default)
    {
        ResolveReferences();
        PlaySwingAnimation();
    }

    private void PlaySwingAnimation()
    {
        if (animator == null)
        {
            return;
        }

        if (TrySetTrigger(swingTrigger))
        {
            return;
        }

        TrySetTrigger(fallbackSwingTrigger);
    }

    private bool TrySetTrigger(string triggerName)
    {
        if (animator == null || string.IsNullOrWhiteSpace(triggerName))
        {
            return false;
        }

        int triggerHash = Animator.StringToHash(triggerName);
        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.nameHash == triggerHash)
            {
                animator.SetTrigger(triggerHash);
                return true;
            }
        }

        return false;
    }

    private void ResolveReferences()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (axeCombat == null)
        {
            axeCombat = GetComponent<PlayerAxeCombat>();
        }
    }

    private void ApplyAxeEquippedState(bool equipped)
    {
        ResolveReferences();
        if (axeCombat == null || (hasAppliedAxeState && lastAppliedAxeEquipped == equipped))
        {
            return;
        }

        hasAppliedAxeState = true;
        lastAppliedAxeEquipped = equipped;
        axeCombat.SetAxeEquippedFromNetwork(equipped);
    }

    private bool IsNetworkReady()
    {
        return Runner != null && Object != null && Object.IsValid;
    }

    private bool HasFusionInputAuthority()
    {
        return Object != null && Object.HasStateAuthority;
    }
}
