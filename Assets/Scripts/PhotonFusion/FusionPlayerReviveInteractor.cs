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
            ResetReviveUI();
            return;
        }

        FusionPlayerSurvival nextTarget = FindBestDownedTarget();
        if (nextTarget != currentTarget)
        {
            reviveProgressSeconds = 0f;
        }

        currentTarget = nextTarget;
        if (currentTarget == null)
        {
            ResetReviveUI();
            return;
        }

        bool hasBandage = inventory != null && inventory.HasItem(ItemType.Bandage, 1);
        GameplayReviveHUD hud = GameplayReviveHUD.Instance;
        if (hud != null)
        {
            hud.ShowPrompt(hasBandage ? "Hold Interact to Revive (Bandage x1)" : "Need Bandage to Revive", hasBandage);
        }

        bool holding = hasBandage && (Input.GetKey(keyboardReviveKey) || (hud != null && hud.IsMobileReviveHeld));
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

        if (!inventory.RemoveItem(ItemType.Bandage, 1))
        {
            ResetReviveUI();
            return;
        }

        pendingReviveTarget = currentTarget;
        hasPendingBandageConsume = true;
        bool requested = currentTarget.RequestReviveFrom(transform.position, reviveRange, reviveHealthPercent);
        if (!requested)
        {
            RefundPendingBandage(currentTarget);
        }

        ResetReviveUI();
    }

    public void HandleReviveRejected(FusionPlayerSurvival rejectedTarget)
    {
        if (!hasPendingBandageConsume || pendingReviveTarget != rejectedTarget)
        {
            return;
        }

        RefundPendingBandage(rejectedTarget);
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
        if (!hasPendingBandageConsume || pendingReviveTarget == null || pendingReviveTarget.IsDowned)
        {
            return;
        }

        hasPendingBandageConsume = false;
        pendingReviveTarget = null;
    }

    private void RefundPendingBandage(FusionPlayerSurvival target)
    {
        if (!hasPendingBandageConsume || pendingReviveTarget != target)
        {
            return;
        }

        if (inventory != null)
        {
            inventory.AddItem(ItemType.Bandage, 1);
        }

        hasPendingBandageConsume = false;
        pendingReviveTarget = null;
    }

    private void ResetReviveUI()
    {
        reviveProgressSeconds = 0f;
        currentTarget = null;
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
