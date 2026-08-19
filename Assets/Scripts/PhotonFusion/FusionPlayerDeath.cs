using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class FusionPlayerDeath : NetworkBehaviour
{
    [Header("Respawn")]
    [SerializeField] private float respawnDelaySeconds = 20f;

    [Header("References")]
    [SerializeField] private FusionPlayerSurvival survival;
    [SerializeField] private FusionPlayerMovement movement; // Reserved: may disable controls while downed -> respawning if needed.
    [SerializeField] private FusionPlayerInventory inventory;
    [SerializeField] private FusionPlayerSpawner spawner;

    private static bool warnedDropped;

    private bool lastDowned;
    private float respawnTimer;
    private bool respawnTimerArmed;

    [SerializeField] private float revivePauseCheckInterval = 0.2f;
    private float nextReviveCheckTime;

    public bool IsRespawnTimerArmedForTest() => respawnTimerArmed;
    public bool CanRespawnNowForTest() => lastDowned;

    public void SetDownedForTest(bool downed) { lastDowned = downed; respawnTimerArmed = downed; }
    public void SetReviveInProgressForTest(bool inProgress) { if (!inProgress) return; respawnTimerArmed = true; }

    public override void Spawned()
    {
        ResolveReferences();
        lastDowned = IsSurvivalDowned();
    }

    private void Update()
    {
        if (Object == null || !Object.HasStateAuthority)
        {
            return;
        }

        bool downed = IsSurvivalDowned();
        if (downed != lastDowned)
        {
            lastDowned = downed;
            if (downed)
            {
                OnDownedStarted();
            }
            else
            {
                respawnTimerArmed = false;
                respawnTimer = 0f;
            }
        }

        if (!respawnTimerArmed)
        {
            return;
        }

        if (Time.unscaledTime < nextReviveCheckTime)
        {
            return;
        }

        nextReviveCheckTime = Time.unscaledTime + revivePauseCheckInterval;

        if (IsReviveInProgress())
        {
            return;
        }

        respawnTimer += Time.unscaledDeltaTime;
        if (respawnTimer >= Mathf.Max(0.5f, respawnDelaySeconds))
        {
            Respawn();
        }
    }

    public void RequestRespawnNow()
    {
        if (Object == null || !Object.HasStateAuthority || survival == null || !survival.IsDowned)
        {
            return;
        }

        Respawn();
    }

    private void OnDownedStarted()
    {
        respawnTimer = 0f;
        respawnTimerArmed = true;

        EmitKillFeedEvent(isKill: false);
        TryDropInventory();
    }

    private void Respawn()
    {
        respawnTimerArmed = false;
        respawnTimer = 0f;

        EmitKillFeedEvent(isKill: true);
        ResetSurvival();
        TeleportToRespawnPoint();
        ClearLastDamager();
    }

    private bool IsSurvivalDowned()
    {
        return survival != null && survival.IsDowned;
    }

    private bool IsReviveInProgress()
    {
        return survival != null && survival.IsRevivePending;
    }

    private void ResetSurvival()
    {
        if (survival != null)
        {
            survival.ResetForRespawn();
        }
    }

    private void TeleportToRespawnPoint()
    {
        if (spawner != null)
        {
            spawner.TeleportPlayerToSpawnPoint(Object.InputAuthority, transform);
        }
    }

    private void ClearLastDamager()
    {
        if (survival != null)
        {
            survival.ClearLastDamager();
        }
    }

    private void EmitKillFeedEvent(bool isKill)
    {
        if (Object == null || Runner == null)
        {
            return;
        }

        string victimName = survival != null && survival.DisplayName.Length > 0
            ? survival.DisplayName.ToString()
            : "Player";

        string killerName = "";
        if (survival != null && survival.LastDamagerRef.IsNone == false)
        {
            if (Runner.TryGetPlayerObject(survival.LastDamagerRef, out NetworkObject killerObject)
                && killerObject != null)
            {
                FusionPlayerSurvival killerSurvival = killerObject.GetComponent<FusionPlayerSurvival>();
                if (killerSurvival != null && killerSurvival.DisplayName.Length > 0)
                {
                    killerName = killerSurvival.DisplayName.ToString();
                }
            }
        }

        RPC_KillFeedMessage(victimName, killerName, isKill);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_KillFeedMessage(string victimName, string killerName, bool isKill, RpcInfo info = default)
    {
        KillFeedHUD hud = KillFeedHUD.Instance;
        if (hud != null)
        {
            hud.EnqueueMessage(killerName, victimName, isKill);
        }
    }

    private void TryDropInventory()
    {
        if (inventory != null && !warnedDropped)
        {
            warnedDropped = true;
            Debug.LogWarning("[FusionPlayerDeath] Inventory drop not implemented yet (Task 4).");
        }
    }

    private void ResolveReferences()
    {
        if (survival == null)
        {
            survival = GetComponent<FusionPlayerSurvival>();
        }

        if (movement == null)
        {
            movement = GetComponent<FusionPlayerMovement>();
        }

        if (inventory == null)
        {
            inventory = GetComponent<FusionPlayerInventory>();
        }

        if (spawner == null)
        {
            spawner = FindObjectOfType<FusionPlayerSpawner>();
        }
    }
}