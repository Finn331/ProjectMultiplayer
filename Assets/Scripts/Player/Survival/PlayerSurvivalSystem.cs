using System;
using UnityEngine;

public class PlayerSurvivalSystem : MonoBehaviour
{
    [Header("Maximum Values")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float maxHunger = 100f;
    [SerializeField] private float maxThirst = 100f;

    [Header("Starting Values")]
    [SerializeField] private float startHealth = 100f;
    [SerializeField] private float startHunger = 100f;
    [SerializeField] private float startThirst = 100f;

    [Header("Need Drain Per Second")]
    [SerializeField] private float hungerDrainPerSecond = 0.35f;
    [SerializeField] private float thirstDrainPerSecond = 0.6f;

    [Header("Health Impact")]
    [SerializeField] private float healthLossWhenStarvingPerSecond = 3f;
    [SerializeField] private float healthLossWhenDehydratedPerSecond = 5f;
    [SerializeField] private bool enablePassiveRegen = true;
    [SerializeField] private float passiveRegenPerSecond = 1.5f;
    [SerializeField] private float minimumHungerForRegen = 40f;
    [SerializeField] private float minimumThirstForRegen = 40f;

    [Header("Movement Penalty")]
    [SerializeField] private bool applyLowNeedsMovementPenalty = true;
    [SerializeField] private float lowNeedsThreshold = 20f;
    [SerializeField] private float movementSpeedMultiplierWhenLow = 0.7f;
    [SerializeField] private bool applyLowHealthMovementPenalty = false;
    [SerializeField, Range(0.01f, 1f)] private float lowHealthThresholdNormalized = 0.35f;
    [SerializeField, Range(0.1f, 1f)] private float movementSpeedMultiplierWhenLowHealth = 1f;
    [SerializeField] private bool applyInjuredStateMovementPenalty = true;
    [SerializeField] private FPSControllerMobile movementController;
    [SerializeField] private LowHealthInjuredAnimationController injuredAnimationController;

    [Header("Debug")]
    [SerializeField] private bool godMode;

    public event Action<float, float, float> StatsChanged;
    public event Action Died;

    public float CurrentHealth => currentHealth;
    public float CurrentHunger => currentHunger;
    public float CurrentThirst => currentThirst;

    public float HealthNormalized => maxHealth <= 0f ? 0f : currentHealth / maxHealth;
    public float HungerNormalized => maxHunger <= 0f ? 0f : currentHunger / maxHunger;
    public float ThirstNormalized => maxThirst <= 0f ? 0f : currentThirst / maxThirst;

    public bool IsDead => isDead;

    private float currentHealth;
    private float currentHunger;
    private float currentThirst;

    private bool isDead;
    private float baseMoveSpeed;

    private void Awake()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        maxHunger = Mathf.Max(1f, maxHunger);
        maxThirst = Mathf.Max(1f, maxThirst);

        currentHealth = Mathf.Clamp(startHealth, 0f, maxHealth);
        currentHunger = Mathf.Clamp(startHunger, 0f, maxHunger);
        currentThirst = Mathf.Clamp(startThirst, 0f, maxThirst);

        if (movementController == null)
        {
            movementController = GetComponent<FPSControllerMobile>();
        }

        if (injuredAnimationController == null)
        {
            injuredAnimationController = GetComponent<LowHealthInjuredAnimationController>();
        }

        if (movementController != null)
        {
            baseMoveSpeed = movementController.moveSpeed;
        }

        if (currentHealth <= 0f)
        {
            this.SetDeathState(true);
        }

        this.RaiseStatsChanged(currentHealth, currentHunger, currentThirst);
    }

    private void Update()
    {
        if (isDead)
        {
            return;
        }

        float deltaTime = Time.deltaTime;

        if (!godMode)
        {
            this.ApplyNeedsDrain(deltaTime);
            this.ApplyHealthConsequences(deltaTime);
        }

        this.UpdateMovementPenalty(currentHealth, currentHunger, currentThirst);
    }

    public void ApplyDamage(float amount)
    {
        if (amount <= 0f || isDead || godMode)
        {
            return;
        }

        this.ChangeHealth(-amount);
    }

    public void Heal(float amount)
    {
        if (amount <= 0f || isDead)
        {
            return;
        }

        this.ChangeHealth(amount);
    }

    public void ConsumeFood(float amount)
    {
        if (amount <= 0f || isDead)
        {
            return;
        }

        this.ChangeHunger(amount);
    }

    public void Drink(float amount)
    {
        if (amount <= 0f || isDead)
        {
            return;
        }

        this.ChangeThirst(amount);
    }

    public void RestoreAllNeeds()
    {
        if (isDead)
        {
            return;
        }

        currentHunger = maxHunger;
        currentThirst = maxThirst;

        this.RaiseStatsChanged(currentHealth, currentHunger, currentThirst);
    }

    public void SetGodMode(bool active)
    {
        godMode = active;
    }

    public void Revive(float healthPercent)
    {
        if (!isDead)
        {
            return;
        }

        float clampedPercent = Mathf.Clamp01(healthPercent);
        currentHealth = Mathf.Max(1f, maxHealth * clampedPercent);
        isDead = false;

        if (movementController != null)
        {
            movementController.enabled = true;
        }

        this.RaiseStatsChanged(currentHealth, currentHunger, currentThirst);
    }

    private void ApplyNeedsDrain(float deltaTime)
    {
        this.ChangeHunger(-hungerDrainPerSecond * deltaTime);
        this.ChangeThirst(-thirstDrainPerSecond * deltaTime);
    }

    private void ApplyHealthConsequences(float deltaTime)
    {
        float healthDelta = 0f;

        if (currentHunger <= 0f)
        {
            healthDelta -= healthLossWhenStarvingPerSecond * deltaTime;
        }

        if (currentThirst <= 0f)
        {
            healthDelta -= healthLossWhenDehydratedPerSecond * deltaTime;
        }

        bool canRegenerate =
            enablePassiveRegen &&
            currentHealth < maxHealth &&
            currentHunger >= minimumHungerForRegen &&
            currentThirst >= minimumThirstForRegen;

        if (healthDelta >= 0f && canRegenerate)
        {
            healthDelta += passiveRegenPerSecond * deltaTime;
        }

        if (Mathf.Abs(healthDelta) > Mathf.Epsilon)
        {
            this.ChangeHealth(healthDelta);
        }
    }

    private void UpdateMovementPenalty(float healthValue, float hungerValue, float thirstValue)
    {
        if (movementController == null || baseMoveSpeed <= 0f)
        {
            return;
        }

        if (!applyLowNeedsMovementPenalty && !applyLowHealthMovementPenalty)
        {
            movementController.moveSpeed = baseMoveSpeed;
            return;
        }

        float finalMultiplier = 1f;

        if (applyLowNeedsMovementPenalty)
        {
            bool lowNeeds = hungerValue <= lowNeedsThreshold || thirstValue <= lowNeedsThreshold;
            if (lowNeeds)
            {
                finalMultiplier = Mathf.Min(finalMultiplier, Mathf.Clamp01(movementSpeedMultiplierWhenLow));
            }
        }

        if (applyLowHealthMovementPenalty)
        {
            float healthNormalized = maxHealth <= 0f ? 0f : healthValue / maxHealth;
            if (healthNormalized <= lowHealthThresholdNormalized)
            {
                finalMultiplier = Mathf.Min(
                    finalMultiplier,
                    Mathf.Clamp01(movementSpeedMultiplierWhenLowHealth));
            }
        }

        if (applyInjuredStateMovementPenalty && injuredAnimationController != null && injuredAnimationController.IsInjuredActive)
        {
            finalMultiplier = Mathf.Min(
                finalMultiplier,
                injuredAnimationController.InjuredMovementSpeedMultiplier);
        }

        movementController.moveSpeed = baseMoveSpeed * finalMultiplier;
    }

    private void ChangeHealth(float delta)
    {
        float nextValue = Mathf.Clamp(currentHealth + delta, 0f, maxHealth);

        if (Mathf.Approximately(nextValue, currentHealth))
        {
            return;
        }

        currentHealth = nextValue;

        if (currentHealth <= 0f)
        {
            this.SetDeathState(true);
        }

        this.RaiseStatsChanged(currentHealth, currentHunger, currentThirst);
    }

    private void ChangeHunger(float delta)
    {
        float nextValue = Mathf.Clamp(currentHunger + delta, 0f, maxHunger);

        if (Mathf.Approximately(nextValue, currentHunger))
        {
            return;
        }

        currentHunger = nextValue;
        this.RaiseStatsChanged(currentHealth, currentHunger, currentThirst);
    }

    private void ChangeThirst(float delta)
    {
        float nextValue = Mathf.Clamp(currentThirst + delta, 0f, maxThirst);

        if (Mathf.Approximately(nextValue, currentThirst))
        {
            return;
        }

        currentThirst = nextValue;
        this.RaiseStatsChanged(currentHealth, currentHunger, currentThirst);
    }

    private void SetDeathState(bool disableMovementController)
    {
        if (isDead)
        {
            return;
        }

        isDead = true;

        if (disableMovementController && movementController != null)
        {
            movementController.enabled = false;
        }

        Died?.Invoke();
    }

    private void RaiseStatsChanged(float healthValue, float hungerValue, float thirstValue)
    {
        StatsChanged?.Invoke(healthValue, hungerValue, thirstValue);
    }
}
