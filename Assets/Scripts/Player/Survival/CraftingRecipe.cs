using System;
using System.Collections.Generic;
using UnityEngine;

public enum CraftingContext
{
    Simple,
    CraftingTable
}

[Serializable]
public class CraftingIngredient
{
    public ItemType itemType;
    public int amount = 1;

    public int Amount => Mathf.Max(1, amount);
}

[Serializable]
public class CraftingRecipe
{
    public string recipeId = "bandage";
    public string displayName = "Bandage";
    public ItemType outputItemType = ItemType.Bandage;
    public int outputAmount = 1;
    public CraftingContext context = CraftingContext.Simple;
    public List<CraftingIngredient> ingredients = new List<CraftingIngredient>();

    public int OutputAmount => Mathf.Max(1, outputAmount);

    public string DisplayName => string.IsNullOrWhiteSpace(displayName)
        ? outputItemType.ToString()
        : displayName;
}
