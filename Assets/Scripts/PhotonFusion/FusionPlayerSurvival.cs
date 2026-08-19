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
    [Networked] public NetworkBool IsDowned { get; private set; }
    [Networked] public NetworkBool IsRevivePending { get; set; }
    [Networked] public NetworkBool IsInitialized { get; private set; }
    [Networked] public PlayerRef LastDamagerRef { get; set; }
    [Networked] public NetworkString<_16> DisplayName { get; private set; }

    private float pendingHealth;
    private float pendingHunger;
    private float pendingThirst;
    private float lastSentHealth;
    private float lastSentHunger;
    private float lastSentThirst;
    private float nextSyncTime;
    private bool hasPendingSnapshot;
    private bool hasLastSentSnapshot;
    private bool subscribedToDeath;
    private float lastAppliedHealth = float.NaN;
    private float lastAppliedHunger = float.NaN;
    private float lastAppliedThirst = float.NaN;

    public override void Spawned()
    {
        if (HasFusionStateAuthority())
        {
            string name = PhotonFusionSessionState.HasSession
                ? PhotonFusionSessionState.Active.PlayerName
                : "Player";
            if (string.IsNullOrEmpty(name))
            {
                name = "Player";
            }
            DisplayName = name.Length > 16 ? name.Substring(0, 16) : name;
        }

        ResolveReferences();
        SubscribeDeathEvent();

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

        UnsubscribeDeathEvent();
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

    public void ApplyDamageForStateAuthority(float damage, PlayerRef attacker)
    {
        if (!HasFusionStateAuthority() || survivalSystem == null || damage <= 0f)
        {
            return;
        }

        if (attacker.IsNone == false && attacker != Object.InputAuthority)
        {
            LastDamagerRef = attacker;
        }

        survivalSystem.ApplyDamage(damage);
    }

    public bool RequestReviveFrom(Vector3 reviverPosition, float reviveRange, float reviveHealthPercent)
    {
        return RequestReviveFrom(reviverPosition, reviveRange, reviveHealthPercent, 0);
    }

    public bool RequestReviveFrom(Vector3 reviverPosition, float reviveRange, float reviveHealthPercent, int requestId)
    {
        if (Runner == null || Object == null || !Object.IsValid)
        {
            return false;
        }

        RPC_RequestRevive(reviverPosition, reviveRange, reviveHealthPercent, requestId);
        return true;
    }

    public void NotifyReviveInProgress(bool inProgress)
    {
        if (Runner == null || Object == null || !Object.IsValid)
        {
            return;
        }

        RPC_NotifyReviveInProgress(inProgress);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_NotifyReviveInProgress(bool inProgress, RpcInfo info = default)
    {
        if (inProgress == IsRevivePending)
        {
            return;
        }

        IsRevivePending = inProgress;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestRevive(Vector3 reviverPosition, float reviveRange, float reviveHealthPercent, int requestId, RpcInfo info = default)
    {
        ResolveReferences();
        if (survivalSystem == null || !IsDowned)
        {
            RejectRevive(info.Source, requestId);
            return;
        }

        if (Runner == null || !Runner.TryGetPlayerObject(info.Source, out NetworkObject reviverObject))
        {
            RejectRevive(info.Source, requestId);
            return;
        }

        FusionPlayerSurvival reviverSurvival = reviverObject.GetComponent<FusionPlayerSurvival>();
        if (reviverSurvival == null || reviverSurvival.IsDowned)
        {
            RejectRevive(info.Source, requestId);
            return;
        }

        // Reviver inventory is validated by the reviver's state-authority interactor because inventory is local-owner state in this MVP.
        float allowedRange = Mathf.Clamp(reviveRange, 0.5f, 4f) + 0.5f;
        if ((transform.position - reviverPosition).sqrMagnitude > allowedRange * allowedRange)
        {
            RejectRevive(info.Source, requestId);
            return;
        }

        survivalSystem.Revive(reviveHealthPercent);
        IsDowned = false;
        QueueSnapshot(survivalSystem.CurrentHealth, survivalSystem.CurrentHunger, survivalSystem.CurrentThirst);
        TryFlushSnapshot(true);
        ResolveRevive(info.Source, requestId, true);
    }

    private void RejectRevive(PlayerRef reviver, int requestId)
    {
        ResolveRevive(reviver, requestId, false);
    }

    private void ResolveRevive(PlayerRef reviver, int requestId, bool accepted)
    {
        if (reviver.IsNone)
        {
            return;
        }

        RPC_ReviveRequestResolved(reviver, requestId, accepted);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ReviveRequestResolved(PlayerRef reviver, int requestId, bool accepted, RpcInfo info = default)
    {
        if (Runner == null || Runner.LocalPlayer != reviver)
        {
            return;
        }

        if (!Runner.TryGetPlayerObject(reviver, out NetworkObject localPlayerObject) || localPlayerObject == null)
        {
            return;
        }

        FusionPlayerReviveInteractor reviveInteractor = localPlayerObject.GetComponent<FusionPlayerReviveInteractor>();
        if (reviveInteractor != null)
        {
            reviveInteractor.HandleReviveResolved(this, requestId, accepted);
        }
    }

    private void OnStateAuthoritySurvivalChanged(float health, float hunger, float thirst)
    {
        QueueSnapshot(health, hunger, thirst);
    }

    private void SubscribeDeathEvent()
    {
        if (subscribedToDeath || survivalSystem == null)
        {
            return;
        }

        survivalSystem.Died += OnStateAuthoritySurvivalDied;
        subscribedToDeath = true;
    }

    private void UnsubscribeDeathEvent()
    {
        if (!subscribedToDeath || survivalSystem == null)
        {
            subscribedToDeath = false;
            return;
        }

        survivalSystem.Died -= OnStateAuthoritySurvivalDied;
        subscribedToDeath = false;
    }

    private void OnStateAuthoritySurvivalDied()
    {
        if (!HasFusionStateAuthority())
        {
            return;
        }

        IsDowned = true;
        QueueSnapshot(0f, survivalSystem.CurrentHunger, survivalSystem.CurrentThirst);
        TryFlushSnapshot(true);
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
        if (Health <= 0f)
        {
            IsDowned = true;
        }

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

    public void ResetForRespawn()
    {
        if (!HasFusionStateAuthority() || survivalSystem == null)
        {
            return;
        }

        survivalSystem.Revive(1f);
        survivalSystem.RestoreAllNeeds();
        IsDowned = false;
        IsRevivePending = false;
        LastDamagerRef = default;
        QueueSnapshot(survivalSystem.CurrentHealth, survivalSystem.CurrentHunger, survivalSystem.CurrentThirst);
        TryFlushSnapshot(true);
    }

    public void ClearLastDamager()
    {
        LastDamagerRef = default;
    }
}
