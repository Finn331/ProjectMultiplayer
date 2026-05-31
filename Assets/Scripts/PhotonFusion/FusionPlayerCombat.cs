using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class FusionPlayerCombat : NetworkBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private string swingTrigger = "HeavyWeaponSwing";
    [SerializeField] private string fallbackSwingTrigger = "Swing";

    public override void Spawned()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        ResolveReferences();
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
