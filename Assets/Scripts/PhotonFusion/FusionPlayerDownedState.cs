using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class FusionPlayerDownedState : NetworkBehaviour
{
    [SerializeField] private FusionPlayerSurvival survival;
    [SerializeField] private FusionPlayerMovement movement;
    [SerializeField] private PlayerInteractionSystem interaction;
    [SerializeField] private HotbarConsumeUI consumeUI;
    [SerializeField] private PlayerAxeCombat axeCombat;

    private bool lastAppliedDowned;
    private bool hasApplied;

    public override void Spawned()
    {
        ResolveReferences();
        ApplyDownedState(IsDowned());
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        bool downed = IsDowned();
        if (!hasApplied || downed != lastAppliedDowned)
        {
            ApplyDownedState(downed);
        }
    }

    private bool IsDowned()
    {
        return survival != null && survival.IsDowned;
    }

    private void ApplyDownedState(bool downed)
    {
        hasApplied = true;
        lastAppliedDowned = downed;

        if (movement != null)
        {
            movement.ControlsBlocked = downed;
        }

        if (Object != null && Object.HasStateAuthority)
        {
            if (interaction != null)
            {
                interaction.enabled = !downed;
            }

            if (consumeUI != null)
            {
                consumeUI.enabled = !downed;
            }

            if (axeCombat != null)
            {
                axeCombat.enabled = !downed;
            }
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

        if (interaction == null)
        {
            interaction = GetComponent<PlayerInteractionSystem>();
        }

        if (consumeUI == null)
        {
            consumeUI = GetComponent<HotbarConsumeUI>();
        }

        if (axeCombat == null)
        {
            axeCombat = GetComponent<PlayerAxeCombat>();
        }
    }
}
