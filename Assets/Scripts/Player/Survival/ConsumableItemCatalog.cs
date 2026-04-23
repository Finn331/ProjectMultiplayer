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
            case ItemType.HungerConsumable:
                effect = new ConsumableItemEffect(0f, DefaultHungerBoostAmount, 0f);
                return true;
            case ItemType.ThirstConsumable:
                effect = new ConsumableItemEffect(0f, 0f, DefaultThirstBoostAmount);
                return true;
            default:
                effect = default;
                return false;
        }
    }

    public static bool TryApply(PlayerSurvivalSystem survivalSystem, ItemType itemType)
    {
        if (survivalSystem == null || !TryGetEffect(itemType, out ConsumableItemEffect effect))
        {
            return false;
        }

        if (effect.HealthAmount > 0f)
        {
            survivalSystem.Heal(effect.HealthAmount);
        }

        if (effect.HungerAmount > 0f)
        {
            survivalSystem.ConsumeFood(effect.HungerAmount);
        }

        if (effect.ThirstAmount > 0f)
        {
            survivalSystem.Drink(effect.ThirstAmount);
        }

        return true;
    }
}
