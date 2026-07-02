using UnityEngine;

public readonly struct ConsumableItemEffect
{
    public ConsumableItemEffect(float healthAmount, float hungerAmount, float thirstAmount)
    {
        HealthAmount = healthAmount;
        HungerAmount = hungerAmount;
        ThirstAmount = thirstAmount;
    }

    public float HealthAmount { get; }
    public float HungerAmount { get; }
    public float ThirstAmount { get; }
}

public static class ConsumableItemCatalog
{
    public const float DefaultHealthBoostAmount = 25f;
    public const float DefaultHungerBoostAmount = 25f;
    public const float DefaultThirstBoostAmount = 25f;
    public const float LegacyFoodBoostAmount = 20f;

    public static bool TryGetEffect(ItemType itemType, out ConsumableItemEffect effect)
    {
        switch (itemType)
        {
            case ItemType.Food:
                effect = new ConsumableItemEffect(0f, LegacyFoodBoostAmount, 0f);
                return true;
            case ItemType.HealthConsumable:
                effect = new ConsumableItemEffect(DefaultHealthBoostAmount, 0f, 0f);
                return true;
            case ItemType.Bandage:
                effect = new ConsumableItemEffect(DefaultHealthBoostAmount, 0f, 0f);
                return true;
            case ItemType.HungerConsumable:
                effect = new ConsumableItemEffect(0f, DefaultHungerBoostAmount, 0f);
                return true;
            case ItemType.ThirstConsumable:
                effect = new ConsumableItemEffect(0f, 0f, DefaultThirstBoostAmount);
                return true;
            case ItemType.CookedChicken:
                effect = new ConsumableItemEffect(0f, 40f, 0f);
                return true;
            case ItemType.CookedFish:
                effect = new ConsumableItemEffect(0f, 30f, 0f);
                return true;
            default:
                effect = default;
                return false;
        }
    }

    public static bool TryApply(PlayerSurvivalSystem survivalSystem, ItemType itemType)
    {
        return TryApply(survivalSystem, itemType, 1f);
    }

    public static bool TryApply(PlayerSurvivalSystem survivalSystem, ItemType itemType, float multiplier)
    {
        if (survivalSystem == null || !TryGetEffect(itemType, out ConsumableItemEffect effect))
        {
            return false;
        }

        float clampedMultiplier = Mathf.Clamp(multiplier, 0.1f, 5f);
        float healthAmount = effect.HealthAmount * clampedMultiplier;
        float hungerAmount = effect.HungerAmount * clampedMultiplier;
        float thirstAmount = effect.ThirstAmount * clampedMultiplier;

        if (healthAmount > 0f)
        {
            survivalSystem.Heal(healthAmount);
        }

        if (hungerAmount > 0f)
        {
            survivalSystem.ConsumeFood(hungerAmount);
        }

        if (thirstAmount > 0f)
        {
            survivalSystem.Drink(thirstAmount);
        }

        return true;
    }
}
