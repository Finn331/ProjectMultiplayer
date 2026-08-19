using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class FusionPlayerReviveInteractor : NetworkBehaviour
{
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private FusionPlayerSurvival selfSurvival;
    [SerializeField] private float reviveRange = 2.2f;
    [SerializeField] private float reviveDurationSeconds = 5f;
    [SerializeField, Range(0.01f, 1f)] private float reviveHealthPercent = 0.25f;
    [SerializeField] private KeyCode keyboardReviveKey = KeyCode.E;

    private FusionPlayerSurvival currentTarget;
    private FusionPlayerSurvival pendingReviveTarget;
    private float reviveProgressSeconds;
    private bool hasPendingBandageConsume;
    private int nextReviveRequestId;
    private int pendingReviveRequestId;
    private bool wasHolding;

    public override void Spawned()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        if (!HasLocalAuthority())
        {
            return;
        }

        ResolveReferences();
        ResolvePendingReviveResult();

        if (selfSurvival != null && selfSurvival.IsDowned)
        {
            ResetReviveState();
            return;
        }

        FusionPlayerSurvival previousTarget = currentTarget;
        FusionPlayerSurvival nextTarget = FindBestDownedTarget();
        if (nextTarget != currentTarget)
        {
            reviveProgressSeconds = 0f;
        }

        currentTarget = nextTarget;
        if (currentTarget == null)
        {
            if (wasHolding && previousTarget != null &&
                (!hasPendingBandageConsume || pendingReviveTarget != previousTarget))
            {
                previousTarget.NotifyReviveInProgress(false);
            }

            wasHolding = false;
            ResetReviveUI();
            return;
        }

        bool hasBandage = inventory != null && inventory.HasItem(ItemType.Bandage, 1);
        GameplayReviveHUD hud = GameplayReviveHUD.Instance;
        if (hud != null)
        {
            hud.ShowPrompt(hasBandage ? "Hold Interact to Revive (Bandage x1)" : "Need Bandage to Revive", true);
        }

        bool holding = hasBandage && (Input.GetKey(keyboardReviveKey) || (hud != null && hud.IsMobileReviveHeld));
        if (holding && !wasHolding)
        {
            currentTarget.NotifyReviveInProgress(true);
        }
        else if (!holding && wasHolding)
        {
            currentTarget.NotifyReviveInProgress(false);
        }
        else if (holding && currentTarget != previousTarget)
        {
            if (previousTarget != null)
            {
                previousTarget.NotifyReviveInProgress(false);
            }

            currentTarget.NotifyReviveInProgress(true);
        }

        wasHolding = holding;

        if (!holding)
        {
            reviveProgressSeconds = 0f;
            if (hud != null)
            {
                hud.SetProgress(0f);
            }

            return;
        }

        reviveProgressSeconds += Time.deltaTime;
        if (hud != null)
        {
            hud.SetProgress(reviveProgressSeconds / Mathf.Max(0.1f, reviveDurationSeconds));
        }

        if (reviveProgressSeconds >= reviveDurationSeconds)
        {
            CompleteRevive();
        }
    }

    private void CompleteRevive()
    {
        if (currentTarget == null || inventory == null || selfSurvival == null || selfSurvival.IsDowned)
        {
            ResetReviveUI();
            return;
        }

        if ((currentTarget.transform.position - transform.position).sqrMagnitude > reviveRange * reviveRange)
        {
            ResetReviveUI();
            return;
        }

        if (hasPendingBandageConsume)
        {
            if (PickupUIManager.instance != null)
            {
                PickupUIManager.instance.ShowInfo("Revive request pending");
            }

            return;
        }

        // In Fusion Shared Mode, the reviver owns inventory locally; target authority validates target/range and refunds on rejection.
        if (!inventory.RemoveItem(ItemType.Bandage, 1))
        {
            ResetReviveUI();
            return;
        }

        pendingReviveTarget = currentTarget;
        hasPendingBandageConsume = true;
        pendingReviveRequestId = ++nextReviveRequestId;
        bool requested = currentTarget.RequestReviveFrom(transform.position, reviveRange, reviveHealthPercent, pendingReviveRequestId);
        if (!requested)
        {
            RefundPendingBandage(currentTarget);
        }

        ResetReviveUI();
    }

    public bool HasPendingBandageConsumeFor(FusionPlayerSurvival target)
    {
        return hasPendingBandageConsume && pendingReviveTarget == target;
    }

    public bool IsRevivingTarget(FusionPlayerSurvival target)
    {
        return hasPendingBandageConsume && pendingReviveTarget == target;
    }

    public void HandleReviveRejected(FusionPlayerSurvival rejectedTarget)
    {
        HandleReviveResolved(rejectedTarget, pendingReviveRequestId, false);
    }

    public void HandleReviveResolved(FusionPlayerSurvival resolvedTarget, int requestId, bool accepted)
    {
        if (!hasPendingBandageConsume || pendingReviveTarget != resolvedTarget || pendingReviveRequestId != requestId)
        {
            return;
        }

        if (!accepted)
        {
            RefundPendingBandage(resolvedTarget);
            return;
        }

        if (resolvedTarget != null)
        {
            resolvedTarget.NotifyReviveInProgress(false);
        }

        hasPendingBandageConsume = false;
        pendingReviveTarget = null;
        pendingReviveRequestId = 0;
    }

    private FusionPlayerSurvival FindBestDownedTarget()
    {
        FusionPlayerSurvival[] survivals = FindObjectsOfType<FusionPlayerSurvival>(true);
        if (survivals == null || survivals.Length == 0)
        {
            return null;
        }

        FusionPlayerSurvival best = null;
        float bestSqr = reviveRange * reviveRange;
        for (int i = 0; i < survivals.Length; i++)
        {
            FusionPlayerSurvival candidate = survivals[i];
            if (candidate == null || candidate == selfSurvival || !candidate.IsDowned)
            {
                continue;
            }

            float sqr = (candidate.transform.position - transform.position).sqrMagnitude;
            if (sqr <= bestSqr)
            {
                bestSqr = sqr;
                best = candidate;
            }
        }

        return best;
    }

    private void ResolvePendingReviveResult()
    {
        if (!hasPendingBandageConsume)
        {
            return;
        }

        if (pendingReviveTarget == null)
        {
            RefundPendingBandage(null);
        }
    }

    private void RefundPendingBandage(FusionPlayerSurvival target)
    {
        if (!hasPendingBandageConsume)
        {
            return;
        }

        if ((target != null && pendingReviveTarget != target) || (target == null && pendingReviveTarget != null))
        {
            return;
        }

        if (inventory != null)
        {
            int added = inventory.AddItem(ItemType.Bandage, 1);
            if (added == 0 && PickupUIManager.instance != null)
            {
                PickupUIManager.instance.ShowInfo("Bandage refund failed: inventory full");
            }
        }

        if (pendingReviveTarget != null)
        {
            pendingReviveTarget.NotifyReviveInProgress(false);
        }

        hasPendingBandageConsume = false;
        pendingReviveTarget = null;
        pendingReviveRequestId = 0;
    }

    private void ResetReviveState()
    {
        if (currentTarget != null && !hasPendingBandageConsume)
        {
            currentTarget.NotifyReviveInProgress(false);
        }

        reviveProgressSeconds = 0f;
        currentTarget = null;
        wasHolding = false;
    }

    private void OnDisable()
    {
        if (currentTarget != null)
        {
            currentTarget.NotifyReviveInProgress(false);
        }

        if (hasPendingBandageConsume && pendingReviveTarget != null)
        {
            pendingReviveTarget.NotifyReviveInProgress(false);
        }
    }

    private void ResetReviveUI()
    {
        ResetReviveState();
        GameplayReviveHUD hud = GameplayReviveHUD.Instance;
        if (hud != null)
        {
            hud.Clear();
        }
    }

    private void ResolveReferences()
    {
        if (inventory == null)
        {
            inventory = GetComponent<PlayerInventory>();
        }

        if (selfSurvival == null)
        {
            selfSurvival = GetComponent<FusionPlayerSurvival>();
        }
    }

    private bool HasLocalAuthority()
    {
        return Object != null && Object.HasStateAuthority;
    }
}
