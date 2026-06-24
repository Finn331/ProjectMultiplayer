using System.Collections.Generic;
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class BandageCraftingSystem : MonoBehaviour
{
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private int fiberCost = 2;
    [SerializeField] private int clothCost = 1;
    [SerializeField] private int bandageOutput = 1;
    [SerializeField] private bool allowKeyboardCraft = true;
    [SerializeField] private KeyCode keyboardCraftKey = KeyCode.C;
    [SerializeField] private List<CraftingRecipe> recipes = new List<CraftingRecipe>();

    private readonly List<CraftingRecipe> availableRecipes = new List<CraftingRecipe>();
    private FusionPlayerSurvival survival;

    public int FiberCost => Mathf.Max(1, fiberCost);
    public int ClothCost => Mathf.Max(1, clothCost);
    public int BandageOutput => Mathf.Max(1, bandageOutput);

    private void Awake()
    {
        ResolveReferences();
        EnsureDefaultRecipes();
    }

    private void OnValidate()
    {
        fiberCost = Mathf.Max(1, fiberCost);
        clothCost = Mathf.Max(1, clothCost);
        bandageOutput = Mathf.Max(1, bandageOutput);
    }

    private void Update()
    {
        if (!allowKeyboardCraft || !HasLocalCraftAuthority() || !Input.GetKeyDown(keyboardCraftKey))
        {
            return;
        }

        TryCraftBandage();
    }

    public IReadOnlyList<CraftingRecipe> GetAvailableRecipes(CraftingContext context)
    {
        EnsureDefaultRecipes();

        availableRecipes.Clear();
        for (int i = 0; i < recipes.Count; i++)
        {
            CraftingRecipe recipe = recipes[i];
            if (recipe != null && recipe.context == context)
            {
                availableRecipes.Add(recipe);
            }
        }

        return availableRecipes;
    }

    public bool CanCraft(CraftingRecipe recipe)
    {
        ResolveReferences();
        if (inventory == null || recipe == null || recipe.ingredients == null || IsDowned())
        {
            return false;
        }

        for (int i = 0; i < recipe.ingredients.Count; i++)
        {
            CraftingIngredient ingredient = recipe.ingredients[i];
            if (ingredient == null || !inventory.HasItem(ingredient.itemType, ingredient.Amount))
            {
                return false;
            }
        }

        return true;
    }

    public bool TryCraft(CraftingRecipe recipe)
    {
        ResolveReferences();
        if (inventory == null || recipe == null || recipe.ingredients == null)
        {
            return false;
        }

        if (IsDowned())
        {
            ShowInfo("Cannot craft while downed");
            return false;
        }

        if (!CanCraft(recipe))
        {
            ShowInfo(BuildMissingIngredientMessage(recipe));
            return false;
        }

        int ingredientTotal = GetIngredientTotal(recipe);
        if (inventory.RemainingCapacity + ingredientTotal < recipe.OutputAmount)
        {
            ShowInfo("Inventory Full");
            return false;
        }

        List<CraftingIngredient> removed = new List<CraftingIngredient>();
        for (int i = 0; i < recipe.ingredients.Count; i++)
        {
            CraftingIngredient ingredient = recipe.ingredients[i];
            if (!inventory.RemoveItem(ingredient.itemType, ingredient.Amount))
            {
                RollbackIngredients(removed);
                ShowInfo(BuildMissingIngredientMessage(recipe));
                return false;
            }

            removed.Add(ingredient);
        }

        int added = inventory.AddItem(recipe.outputItemType, recipe.OutputAmount);
        if (added < recipe.OutputAmount)
        {
            if (added > 0)
            {
                inventory.RemoveItem(recipe.outputItemType, added);
            }

            RollbackIngredients(removed);
            ShowInfo("Inventory Full");
            return false;
        }

        ShowInfo("Crafted " + recipe.DisplayName);
        return true;
    }

    public bool CanCraftBandage()
    {
        return CanCraft(GetBandageRecipe());
    }

    public bool TryCraftBandage()
    {
        return TryCraft(GetBandageRecipe());
    }

    public CraftingRecipe GetBandageRecipe()
    {
        EnsureDefaultRecipes();
        for (int i = 0; i < recipes.Count; i++)
        {
            CraftingRecipe recipe = recipes[i];
            if (recipe != null && recipe.outputItemType == ItemType.Bandage)
            {
                return recipe;
            }
        }

        return null;
    }

    private void EnsureDefaultRecipes()
    {
        if (recipes == null)
        {
            recipes = new List<CraftingRecipe>();
        }

        AddDefaultRecipeIfMissing(new CraftingRecipe
        {
            recipeId = "bandage",
            displayName = "Bandage",
            outputItemType = ItemType.Bandage,
            outputAmount = BandageOutput,
            context = CraftingContext.Simple,
            ingredients = new List<CraftingIngredient>
            {
                new CraftingIngredient { itemType = ItemType.Fiber, amount = FiberCost },
                new CraftingIngredient { itemType = ItemType.Cloth, amount = ClothCost }
            }
        });

        AddDefaultRecipeIfMissing(new CraftingRecipe
        {
            recipeId = "crafting_table",
            displayName = "Crafting Table",
            outputItemType = ItemType.CraftingTable,
            outputAmount = 1,
            context = CraftingContext.Simple,
            ingredients = new List<CraftingIngredient>
            {
                new CraftingIngredient { itemType = ItemType.Wood, amount = 8 },
                new CraftingIngredient { itemType = ItemType.Stone, amount = 2 },
                new CraftingIngredient { itemType = ItemType.Fiber, amount = 2 }
            }
        });

        AddDefaultRecipeIfMissing(new CraftingRecipe
        {
            recipeId = "axe",
            displayName = "Axe",
            outputItemType = ItemType.Axe,
            outputAmount = 1,
            context = CraftingContext.CraftingTable,
            ingredients = new List<CraftingIngredient>
            {
                new CraftingIngredient { itemType = ItemType.Wood, amount = 3 },
                new CraftingIngredient { itemType = ItemType.Stone, amount = 2 }
            }
        });

        AddDefaultRecipeIfMissing(new CraftingRecipe
        {
            recipeId = "campfire",
            displayName = "Campfire",
            outputItemType = ItemType.Campfire,
            outputAmount = 1,
            context = CraftingContext.CraftingTable,
            ingredients = new List<CraftingIngredient>
            {
                new CraftingIngredient { itemType = ItemType.Wood, amount = 4 },
                new CraftingIngredient { itemType = ItemType.Stone, amount = 4 }
            }
        });
    }

    private void AddDefaultRecipeIfMissing(CraftingRecipe defaultRecipe)
    {
        if (defaultRecipe == null || string.IsNullOrWhiteSpace(defaultRecipe.recipeId))
        {
            return;
        }

        for (int i = 0; i < recipes.Count; i++)
        {
            CraftingRecipe recipe = recipes[i];
            if (recipe != null && recipe.recipeId == defaultRecipe.recipeId)
            {
                return;
            }
        }

        recipes.Add(defaultRecipe);
    }

    private int GetIngredientTotal(CraftingRecipe recipe)
    {
        int total = 0;
        if (recipe == null || recipe.ingredients == null)
        {
            return total;
        }

        for (int i = 0; i < recipe.ingredients.Count; i++)
        {
            CraftingIngredient ingredient = recipe.ingredients[i];
            if (ingredient != null)
            {
                total += ingredient.Amount;
            }
        }

        return total;
    }

    private void RollbackIngredients(List<CraftingIngredient> removed)
    {
        for (int i = 0; i < removed.Count; i++)
        {
            CraftingIngredient ingredient = removed[i];
            if (ingredient != null)
            {
                inventory.AddItem(ingredient.itemType, ingredient.Amount);
            }
        }
    }

    private string BuildMissingIngredientMessage(CraftingRecipe recipe)
    {
        if (inventory == null || recipe == null || recipe.ingredients == null || recipe.ingredients.Count == 0)
        {
            return "Missing Ingredients";
        }

        for (int i = 0; i < recipe.ingredients.Count; i++)
        {
            CraftingIngredient ingredient = recipe.ingredients[i];
            if (ingredient == null)
            {
                continue;
            }

            int owned = inventory.GetAmount(ingredient.itemType);
            if (owned < ingredient.Amount)
            {
                return "Need " + ingredient.Amount + " " + ingredient.itemType;
            }
        }

        return "Missing Ingredients";
    }

    private void ResolveReferences()
    {
        if (inventory == null)
        {
            inventory = GetComponent<PlayerInventory>();
        }

        if (survival == null)
        {
            survival = GetComponent<FusionPlayerSurvival>();
        }
    }

    private static void ShowInfo(string message)
    {
        if (PickupUIManager.instance != null)
        {
            PickupUIManager.instance.ShowInfo(message);
        }
    }

    private bool HasLocalCraftAuthority()
    {
        NetworkObject networkObject = GetComponent<NetworkObject>();
        return networkObject == null || networkObject.HasStateAuthority || networkObject.HasInputAuthority;
    }

    private bool IsDowned()
    {
        ResolveReferences();
        return survival != null && survival.IsDowned;
    }
}
