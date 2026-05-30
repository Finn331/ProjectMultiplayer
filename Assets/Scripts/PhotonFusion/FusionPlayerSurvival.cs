using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class FusionPlayerSurvival : NetworkBehaviour
{
    [SerializeField] private PlayerSurvivalSystem survivalSystem;
    [SerializeField] private float syncIntervalSeconds = 0.2f;
    [SerializeField] private float minDeltaToSync = 0.1f;

    [Networked] public float Health { get; private set; }
    [Networked] public float Hunger { get; private set; }
    [Networked] public float Thirst { get; private set; }
    [Networked] public NetworkBool Injured { get; private set; }
    [Networked] public NetworkBool IsInitialized { get; private set; }

    private float pendingHealth;
    private float pendingHunger;
    private float pendingThirst;
    private float lastSentHealth;
    private float lastSentHunger;
    private float lastSentThirst;
    private float nextSyncTime;
    private bool hasPendingSnapshot;
    private bool hasLastSentSnapshot;
    private float lastAppliedHealth = float.NaN;
    private float lastAppliedHunger = float.NaN;
    private float lastAppliedThirst = float.NaN;

    public override void Spawned()
    {
        ResolveReferences();

        if (HasFusionStateAuthority())
        {
            if (survivalSystem != null)
            {
                survivalSystem.StatsChanged += OnStateAuthoritySurvivalChanged;
                QueueSnapshot(survivalSystem.CurrentHealth, survivalSystem.CurrentHunger, survivalSystem.CurrentThirst);
            }
            else
            {
                QueueSnapshot(100f, 100f, 100f);
            }

            TryFlushSnapshot(true);
            return;
        }

        if (survivalSystem != null)
        {
            survivalSystem.SetLocalSimulationEnabled(false);
            if (IsInitialized)
            {
                ApplySnapshotToLocalSystem();
            }
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (survivalSystem != null)
        {
            survivalSystem.StatsChanged -= OnStateAuthoritySurvivalChanged;

            if (!HasFusionStateAuthority())
            {
                survivalSystem.SetLocalSimulationEnabled(true);
            }
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (HasFusionStateAuthority())
        {
            TryFlushSnapshot(false);
        }
    }

    private void Update()
    {
        if (!HasFusionStateAuthority() && IsInitialized && HasNetworkSnapshotChanged())
        {
            ApplySnapshotToLocalSystem();
        }
    }

    public void ApplyDamageForStateAuthority(float damage)
    {
        if (!HasFusionStateAuthority() || survivalSystem == null || damage <= 0f)
        {
            return;
        }

        survivalSystem.ApplyDamage(damage);
    }

    private void OnStateAuthoritySurvivalChanged(float health, float hunger, float thirst)
    {
        QueueSnapshot(health, hunger, thirst);
    }

    private void QueueSnapshot(float health, float hunger, float thirst)
    {
        pendingHealth = health;
        pendingHunger = hunger;
        pendingThirst = thirst;
        hasPendingSnapshot = true;
    }

    private void TryFlushSnapshot(bool force)
    {
        if (!HasFusionStateAuthority() || !hasPendingSnapshot)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (!force && now < nextSyncTime)
        {
            return;
        }

        if (!force && hasLastSentSnapshot)
        {
            bool isSmallChange =
                Mathf.Abs(pendingHealth - lastSentHealth) < minDeltaToSync &&
                Mathf.Abs(pendingHunger - lastSentHunger) < minDeltaToSync &&
                Mathf.Abs(pendingThirst - lastSentThirst) < minDeltaToSync;

            if (isSmallChange)
            {
                return;
            }
        }

        Health = pendingHealth;
        Hunger = pendingHunger;
        Thirst = pendingThirst;
        Injured = Health <= 35f;
        IsInitialized = true;

        lastSentHealth = pendingHealth;
        lastSentHunger = pendingHunger;
        lastSentThirst = pendingThirst;
        hasLastSentSnapshot = true;
        hasPendingSnapshot = false;
        nextSyncTime = now + Mathf.Max(0.05f, syncIntervalSeconds);
    }

    private void ApplySnapshotToLocalSystem()
    {
        if (survivalSystem == null)
        {
            return;
        }

        survivalSystem.ApplyNetworkSnapshot(Health, Hunger, Thirst);
        lastAppliedHealth = Health;
        lastAppliedHunger = Hunger;
        lastAppliedThirst = Thirst;
    }

    private bool HasNetworkSnapshotChanged()
    {
        return !Mathf.Approximately(lastAppliedHealth, Health) ||
            !Mathf.Approximately(lastAppliedHunger, Hunger) ||
            !Mathf.Approximately(lastAppliedThirst, Thirst);
    }

    private void ResolveReferences()
    {
        if (survivalSystem == null)
        {
            survivalSystem = GetComponent<PlayerSurvivalSystem>();
        }
    }

    private bool HasFusionStateAuthority()
    {
        return Object != null && Object.HasStateAuthority;
    }
}
