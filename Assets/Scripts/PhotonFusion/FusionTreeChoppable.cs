using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class FusionTreeChoppable : NetworkBehaviour
{
    [SerializeField] private int startHealth = 3;
    [SerializeField] private bool despawnWhenDepleted = true;
    [SerializeField] private int maxDamagePerChop = 1;
    [SerializeField] private float maxChopDistance = 3.5f;
    [SerializeField] private GameObject visualRoot;
    [SerializeField] private Collider[] collidersToDisable;

    [Networked] public int Health { get; private set; }
    [Networked] public NetworkBool IsDepleted { get; private set; }

    public override void Spawned()
    {
        ResolveReferences();

        if (HasFusionStateAuthority() && Health <= 0 && !IsDepleted)
        {
            Health = Mathf.Max(1, startHealth);
        }

        ApplyDepletionVisuals();
    }

    public override void Render()
    {
        ApplyDepletionVisuals();
    }

    private void Awake()
    {
        ResolveReferences();
    }

    public bool RequestChop(NetworkObject playerObject, int damage = 1)
    {
        if (Runner == null || Object == null || !Object.IsValid || playerObject == null || !playerObject.IsValid || IsDepleted)
        {
            return false;
        }

        RPC_Chop(playerObject, Mathf.Max(1, damage), playerObject.transform.position);
        return true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Chop(NetworkObject playerObject, int damage, Vector3 attackerPosition, RpcInfo info = default)
    {
        if (!HasFusionStateAuthority() || IsDepleted || !IsAuthorizedRequester(playerObject, attackerPosition, info))
        {
            return;
        }

        int clampedDamage = Mathf.Clamp(damage, 1, Mathf.Max(1, maxDamagePerChop));
        int nextHealth = Mathf.Max(0, Health - clampedDamage);
        Health = nextHealth;

        if (nextHealth > 0)
        {
            return;
        }

        IsDepleted = true;
        ApplyDepletionVisuals();

        if (despawnWhenDepleted && Runner != null && Object != null && Object.IsValid)
        {
            Runner.Despawn(Object);
        }
    }

    private void ResolveReferences()
    {
        if (collidersToDisable == null || collidersToDisable.Length == 0)
        {
            collidersToDisable = GetComponentsInChildren<Collider>(true);
        }
    }

    private void ApplyDepletionVisuals()
    {
        bool depleted = IsDepleted;
        if (visualRoot != null && visualRoot.activeSelf == depleted)
        {
            visualRoot.SetActive(!depleted);
        }

        if (collidersToDisable == null)
        {
            return;
        }

        for (int i = 0; i < collidersToDisable.Length; i++)
        {
            Collider targetCollider = collidersToDisable[i];
            if (targetCollider != null && targetCollider.enabled == depleted)
            {
                targetCollider.enabled = !depleted;
            }
        }
    }

    private bool HasFusionStateAuthority()
    {
        return Object != null && Object.HasStateAuthority;
    }

    private bool IsAuthorizedRequester(NetworkObject playerObject, Vector3 attackerPosition, RpcInfo info)
    {
        if (playerObject == null || !playerObject.IsValid)
        {
            return false;
        }

        if (playerObject.InputAuthority != info.Source)
        {
            return false;
        }

        float maxDistance = Mathf.Max(0.5f, maxChopDistance);
        if ((playerObject.transform.position - transform.position).sqrMagnitude > maxDistance * maxDistance)
        {
            return false;
        }

        return (attackerPosition - transform.position).sqrMagnitude <= maxDistance * maxDistance;
    }
}
