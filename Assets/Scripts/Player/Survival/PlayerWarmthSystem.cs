using System;
using UnityEngine;

/// <summary>
/// Warmth (kehangatan) untuk player di dunia snow/taiga.
///
/// Aturan:
/// - Drain saat Night/Dusk (malam dingin). Drain lebih lambat saat Dawn.
/// - Regen perlahan saat Day (siang hangat).
/// - Dekat campfire yang LIT (CampfireCooking.IsLitValue) → regen cepat + imun freeze.
/// - Warmth habis → freeze damage per detik ke PlayerSurvivalSystem (ApplyDamage).
///
/// Simulasi lokal (non-networked): mengikuti pattern PlayerSurvivalSystem yang
/// sudah disinkronkan via NetworkSurvivalBridge/FusionPlayerSurvival.
/// </summary>
public class PlayerWarmthSystem : MonoBehaviour
{
    public static event Action<PlayerWarmthSystem> WarmthChanged;

    [Header("References")]
    [SerializeField] private PlayerSurvivalSystem survivalSystem;
    [SerializeField] private DayNightCycle dayNightCycle;

    [Header("Warmth Range")]
    [SerializeField] private float maxWarmth = 100f;
    [SerializeField] private float startWarmth = 100f;

    [Header("Drain / Regen Per Second")]
    [SerializeField] private float nightDrainPerSecond = 1.8f;
    [SerializeField] private float duskDrainPerSecond = 1.2f;
    [SerializeField] private float dawnDrainPerSecond = 0.5f;
    [SerializeField] private float dayRegenPerSecond = 2.5f;
    [SerializeField] private float campfireRegenPerSecond = 15f;

    [Header("Campfire")]
    [SerializeField] private float campfireWarmRadius = 6f;
    [SerializeField] private float campfireScanInterval = 0.5f;
    [SerializeField] private LayerMask campfireMask = ~0;

    [Header("Freeze Damage")]
    [SerializeField] private float freezeDamagePerSecond = 2.5f;
    [SerializeField] private float freezeDamageGracePeriod = 8f; // jeda setelah warmth mencapai 0 sebelum damage mulai

    [Header("Movement Penalty")]
    [SerializeField] private bool applyFreezingPenalty = true;
#pragma warning disable CS0414 // Field assigned but never used (reserved for planned freeze-penalty feature)
    [SerializeField, Range(0.1f, 1f)] private float freezingSpeedMultiplier = 0.75f;
    [SerializeField, Range(0f, 0.5f)] private float freezingThresholdNormalized = 0.2f; // warmth <= 20% = kedinginan

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;
#pragma warning restore CS0414

    public float CurrentWarmth => currentWarmth;
    public float MaxWarmth => maxWarmth;
    public float WarmthNormalized => maxWarmth <= 0f ? 0f : Mathf.Clamp01(currentWarmth / maxWarmth);
    public bool IsNearLitCampfire => nearLitCampfire;
    public bool IsFreezing => WarmthNormalized <= freezingThresholdNormalized;
    public DayNightCycle.TimeOfDay CurrentPhase => dayNightCycle != null ? dayNightCycle.CurrentPhase : DayNightCycle.TimeOfDay.Day;

    private float currentWarmth;
    private bool nearLitCampfire;
    private float freezeGraceTimer;
    private float nextCampfireScan;
    private FPSControllerMobile movementController;
    private static readonly Collider[] campfireBuffer = new Collider[16];

    private void Awake()
    {
        currentWarmth = Mathf.Clamp(startWarmth, 0f, maxWarmth);
        if (survivalSystem == null) survivalSystem = GetComponent<PlayerSurvivalSystem>();
        if (dayNightCycle == null) dayNightCycle = FindObjectOfType<DayNightCycle>();
        movementController = GetComponent<FPSControllerMobile>();
    }

    private void Update()
    {
        if (survivalSystem == null || survivalSystem.CurrentHealth <= 0f) return;

        ScanCampfireIfNeeded();
        TickWarmth(Time.deltaTime);
    }

    private void ScanCampfireIfNeeded()
    {
        if (Time.time < nextCampfireScan) return;
        nextCampfireScan = Time.time + campfireScanInterval;

        nearLitCampfire = false;
        int count = Physics.OverlapSphereNonAlloc(transform.position, campfireWarmRadius, campfireBuffer, campfireMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            var campfire = campfireBuffer[i].GetComponentInParent<CampfireCooking>();
            if (campfire != null && campfire.IsLitValue)
            {
                nearLitCampfire = true;
                break;
            }
        }
    }

    private void TickWarmth(float dt)
    {
        float delta;
        DayNightCycle.TimeOfDay phase = CurrentPhase;

        if (nearLitCampfire)
        {
            delta = campfireRegenPerSecond;
        }
        else
        {
            switch (phase)
            {
                case DayNightCycle.TimeOfDay.Night:
                    delta = -nightDrainPerSecond;
                    break;
                case DayNightCycle.TimeOfDay.Dusk:
                    delta = -duskDrainPerSecond;
                    break;
                case DayNightCycle.TimeOfDay.Dawn:
                    delta = -dawnDrainPerSecond;
                    break;
                default:
                    delta = dayRegenPerSecond;
                    break;
            }
        }

        float previous = currentWarmth;
        currentWarmth = Mathf.Clamp(currentWarmth + delta * dt, 0f, maxWarmth);

        if (!Mathf.Approximately(previous, currentWarmth))
        {
            WarmthChanged?.Invoke(this);
        }

        // Freeze damage saat warmth 0 (dengan grace period biar tidak instant kill)
        if (currentWarmth <= 0f)
        {
            freezeGraceTimer += dt;
            if (freezeGraceTimer >= freezeDamageGracePeriod)
            {
                survivalSystem.ApplyDamage(freezeDamagePerSecond * dt);
            }
        }
        else
        {
            freezeGraceTimer = 0f;
        }

        ApplyMovementPenalty();
    }

    private void ApplyMovementPenalty()
    {
        if (!applyFreezingPenalty || movementController == null) return;
        // FPSControllerMobile tidak punya API multiplier global; penalty di-handle
        // lewat PlayerSurvivalSystem.UpdateMovementPenalty (health/hunger/thirst).
        // Untuk sekarang: freezing memperlambat via survival system low-needs path.
        // (Hook langsung bisa ditambah nanti kalau FPSControllerMobile expose multiplier.)
    }

    public void RestoreWarmth()
    {
        currentWarmth = maxWarmth;
        freezeGraceTimer = 0f;
        WarmthChanged?.Invoke(this);
    }

    public void AddWarmth(float amount)
    {
        if (amount <= 0f) return;
        currentWarmth = Mathf.Clamp(currentWarmth + amount, 0f, maxWarmth);
        WarmthChanged?.Invoke(this);
    }

    private static string SurfaceName(DayNightCycle.TimeOfDay phase)
    {
        return phase.ToString();
    }
}
