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

    [Header("Optional Drops")]
    [SerializeField] private NetworkPrefabRef dropPrefab;
    [SerializeField] private int dropAmount = 1;
    [SerializeField] private Transform dropPoint;
    [SerializeField] private float dropImpulse = 1.8f;
    [SerializeField] private bool spawnAsSingleStack = false;
    [SerializeField] private float dropScatterRadius = 0.25f;

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
        SpawnDrop();

        if (despawnWhenDepleted && Runner != null && Object != null && Object.IsValid)
        {
            Runner.Despawn(Object);
        }
    }

    private void SpawnDrop()
    {
        if (!dropPrefab.IsValid || Runner == null || !Runner.IsRunning)
        {
            return;
        }

        int totalAmount = Mathf.Max(1, dropAmount);
        int spawnCount = spawnAsSingleStack ? 1 : totalAmount;
        int amountPerDrop = spawnAsSingleStack ? totalAmount : 1;
        Vector3 basePosition = dropPoint != null
            ? dropPoint.position
            : transform.position + (Vector3.up * 0.5f);

        for (int i = 0; i < spawnCount; i++)
        {
            Vector2 randomOffset2D = Random.insideUnitCircle * Mathf.Max(0f, dropScatterRadius);
            Vector3 spawnPosition = basePosition + new Vector3(randomOffset2D.x, 0f, randomOffset2D.y);
            
            // Player yang menebang akan mendapat State Authority sementara atas drop ini (di Shared Mode)
            NetworkObject droppedObj = Runner.Spawn(dropPrefab, spawnPosition, Quaternion.identity, Runner.LocalPlayer);

            FusionPickableItem pickable = droppedObj.GetComponent<FusionPickableItem>();
            if (pickable != null)
            {
                pickable.Amount = Mathf.Max(1, amountPerDrop);
            }

            Rigidbody rb = droppedObj.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Vector3 randomPush = transform.forward + Vector3.up + new Vector3(randomOffset2D.x, 0f, randomOffset2D.y);
                rb.AddForce(randomPush.normalized * dropImpulse, ForceMode.VelocityChange);
            }
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
