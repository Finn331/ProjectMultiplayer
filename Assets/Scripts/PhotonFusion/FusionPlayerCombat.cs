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
        if (IsDowned() || !IsNetworkReady() || !HasFusionInputAuthority())
        {
            return false;
        }

        RPC_Swing();
        return true;
    }

    public bool RequestSceneTreeHit(Vector3 treePosition, float damage)
    {
        if (IsDowned() || !IsNetworkReady() || !HasFusionInputAuthority() || damage <= 0f)
        {
            return false;
        }

        RPC_SceneTreeHit(treePosition, damage);
        return true;
    }

    public bool RequestTerrainTreeHit(int treeId, Vector3 treePosition, Vector3 chopperPosition, float damage)
    {
        if (IsDowned() || !IsNetworkReady() || !HasFusionInputAuthority() || treeId == 0 || damage <= 0f)
        {
            return false;
        }

        RPC_TerrainTreeHit(treeId, treePosition, chopperPosition, damage);
        return true;
    }

    public bool RequestPlayerDamage(Vector3 targetPosition, float damage)
    {
        if (IsDowned() || !IsNetworkReady() || !HasFusionInputAuthority() || damage <= 0f)
        {
            return false;
        }

        RPC_PlayerDamage(targetPosition, damage);
        return true;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
    private void RPC_Swing(RpcInfo info = default)
    {
        if (Runner != null && info.Source == Runner.LocalPlayer)
        {
            return;
        }

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

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SceneTreeHit(Vector3 treePosition, float damage, RpcInfo info = default)
    {
        if (!TryFindTreeByPosition(treePosition, out TreeChoppable tree))
        {
            return;
        }

        bool wasDepleted = tree.IsDepleted;
        Vector3 treePos = tree.transform.position;
        Vector3 dropBase = tree.DropBasePosition;
        Vector3 dropForward = tree.DropForward;
        bool hasDropPrefab = tree.HasDropPrefab;
        ItemType dropItemType = tree.DropItemType;
        int dropCount = tree.FusionDropCount;
        int amountPerDrop = tree.FusionAmountPerDrop;
        float scatter = tree.DropScatterRadius;

        bool accepted = tree.ApplyFusionReplicatedHit(damage);
        if (accepted && !wasDepleted && tree.IsDepleted && Object != null && Object.HasStateAuthority)
        {
            FusionPlayerInventory fusionInventory = GetComponent<FusionPlayerInventory>();
            if (fusionInventory != null && hasDropPrefab)
            {
                fusionInventory.SpawnTreeDropsFromData(treePos, dropBase, dropForward, dropItemType, dropCount, amountPerDrop, scatter);
            }
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
    private void RPC_TerrainTreeHit(int treeId, Vector3 treePosition, Vector3 chopperPosition, float damage, RpcInfo info = default)
    {
        TerrainTreeChoppingRegistry registry = FindObjectOfType<TerrainTreeChoppingRegistry>();
        if (registry == null)
        {
            return;
        }

        if (!registry.TryApplyDamage(treeId, damage, out bool depleted, out TerrainTreeChoppingRegistry.TreeHit hit))
        {
            return;
        }

        if (!depleted)
        {
            return;
        }

        Vector3 fallDirection = hit.WorldPosition - chopperPosition;
        registry.TryPlayFallingProxy(treeId, fallDirection);

        if (Object != null && Object.HasStateAuthority)
        {
            FusionPlayerInventory fusionInventory = GetComponent<FusionPlayerInventory>();
            if (fusionInventory != null)
            {
                fusionInventory.SpawnTreeDropsFromData(hit.WorldPosition, hit.WorldPosition, fallDirection, ItemType.Wood, 1, 3, 0.75f);
            }

            FusionTerrainTreeDepletionState depletionState = FindObjectOfType<FusionTerrainTreeDepletionState>();
            if (depletionState != null)
            {
                depletionState.AddDepletedTree(treeId);
            }
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
    private void RPC_PlayerDamage(Vector3 targetPosition, float damage, RpcInfo info = default)
    {
        if (!TryFindFusionSurvivalByPosition(targetPosition, out FusionPlayerSurvival targetSurvival))
        {
            return;
        }

        targetSurvival.ApplyDamageForStateAuthority(damage);
    }

    private static bool TryFindTreeByPosition(Vector3 treePosition, out TreeChoppable tree)
    {
        tree = null;
        TreeChoppable[] trees = FindObjectsOfType<TreeChoppable>(true);
        if (trees == null || trees.Length == 0)
        {
            return false;
        }

        float bestSqr = 1f;
        for (int i = 0; i < trees.Length; i++)
        {
            TreeChoppable candidate = trees[i];
            if (candidate == null || candidate.IsDepleted)
            {
                continue;
            }

            float sqr = (candidate.transform.position - treePosition).sqrMagnitude;
            if (sqr > bestSqr)
            {
                continue;
            }

            bestSqr = sqr;
            tree = candidate;
        }

        return tree != null;
    }

    private bool TryFindFusionSurvivalByPosition(Vector3 targetPosition, out FusionPlayerSurvival targetSurvival)
    {
        targetSurvival = null;
        FusionPlayerSurvival[] survivals = FindObjectsOfType<FusionPlayerSurvival>(true);
        if (survivals == null || survivals.Length == 0)
        {
            return false;
        }

        float bestSqr = 4f;
        for (int i = 0; i < survivals.Length; i++)
        {
            FusionPlayerSurvival candidate = survivals[i];
            if (candidate == null || candidate.gameObject == gameObject)
            {
                continue;
            }

            float sqr = (candidate.transform.position - targetPosition).sqrMagnitude;
            if (sqr > bestSqr)
            {
                continue;
            }

            bestSqr = sqr;
            targetSurvival = candidate;
        }

        return targetSurvival != null;
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

    private bool IsDowned()
    {
        FusionPlayerSurvival survival = GetComponent<FusionPlayerSurvival>();
        return survival != null && survival.IsDowned;
    }

    private bool HasFusionInputAuthority()
    {
        return Object != null && Object.HasStateAuthority;
    }
}
