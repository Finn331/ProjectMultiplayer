using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class FusionPlayerDownedState : NetworkBehaviour
{
    [SerializeField] private FusionPlayerSurvival survival;
    [SerializeField] private FusionPlayerMovement movement;
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
        if (!hasApplied || downed != lastAppliedDowned || downed)
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

        if (axeCombat != null)
        {
            axeCombat.ControlsBlocked = downed;
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

        if (axeCombat == null)
        {
            axeCombat = GetComponent<PlayerAxeCombat>();
        }
    }
}
