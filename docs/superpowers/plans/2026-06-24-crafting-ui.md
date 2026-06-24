# Crafting UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a mobile-friendly `Inventory > Crafting` UI with recipe cards, starting with the existing `Bandage` recipe and leaving a clean path for crafting table recipes.

**Architecture:** Keep scene/prefab diffs minimal by reusing the existing `BandageCraftingSystem` component already present on the Fusion player prefab. Add a small reusable recipe model and extend the runtime-generated `PlayerInventoryUI` with an `Items` / `Crafting` tab switch, recipe card rendering, and local-authority-only crafting actions.

**Tech Stack:** Unity C#, uGUI, TextMeshPro, Photon Fusion authority checks, Unity MCP diagnostics/validation.

---

## File Map

- Create: `Assets/Scripts/Player/Survival/CraftingRecipe.cs`
  - Defines `CraftingContext`, `CraftingIngredient`, and `CraftingRecipe` serializable data used by backend and UI.
- Modify: `Assets/Scripts/Player/Survival/BandageCraftingSystem.cs`
  - Preserve the existing component/class name to avoid prefab script migration.
  - Add generic recipe APIs while keeping `CanCraftBandage()` and `TryCraftBandage()` wrappers for existing callers.
- Modify: `Assets/Scripts/Player/Survival/PlayerInventoryUI.cs`
  - Add `Items` / `Crafting` tabs.
  - Render recipe cards in the existing inventory panel.
  - Disable crafting while downed or missing ingredients.
- Verify only, no planned modification: `Assets/Assets/Prefabs/FusionPlayer.prefab`
  - Confirm it still has `BandageCraftingSystem` and no missing script.
- Verify only, no planned modification: `Assets/Scenes/MainMenu.unity`
  - Confirm implementation does not recreate `Inventory UI` or `Inventory Toggle Button` in MainMenu edit mode.

## Task 1: Add Crafting Recipe Data Model

**Files:**
- Create: `Assets/Scripts/Player/Survival/CraftingRecipe.cs`

- [ ] **Step 1: Add recipe model file**

Create `Assets/Scripts/Player/Survival/CraftingRecipe.cs` with:

```csharp
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
```

- [ ] **Step 2: Refresh and validate script**

Use Unity MCP:

```text
unityMCP_refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=true)
unityMCP_validate_script(uri="Assets/Scripts/Player/Survival/CraftingRecipe.cs", level="standard", include_diagnostics=true)
```

Expected: no compiler errors for `CraftingRecipe.cs`.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scripts/Player/Survival/CraftingRecipe.cs"
git commit -m "Add crafting recipe model"
```

## Task 2: Generalize Existing Bandage Crafting Backend

**Files:**
- Modify: `Assets/Scripts/Player/Survival/BandageCraftingSystem.cs`

- [ ] **Step 1: Replace backend with generic recipe-aware version**

Replace the contents of `BandageCraftingSystem.cs` with this implementation. Keep the class name unchanged because the prefab already references it.

```csharp
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
        return recipes;
    }

    public bool CanCraft(CraftingRecipe recipe)
    {
        ResolveReferences();
        if (inventory == null || recipe == null || IsDowned())
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
        if (recipe == null)
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
        if (recipes.Count > 0)
        {
            return;
        }

        recipes.Add(new CraftingRecipe
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
        if (inventory == null || recipe == null || recipe.ingredients.Count == 0)
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
        return networkObject == null || networkObject.HasStateAuthority;
    }

    private bool IsDowned()
    {
        FusionPlayerSurvival survival = GetComponent<FusionPlayerSurvival>();
        return survival != null && survival.IsDowned;
    }
}
```

- [ ] **Step 2: Validate backend script**

Use Unity MCP:

```text
unityMCP_refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=true)
unityMCP_validate_script(uri="Assets/Scripts/Player/Survival/BandageCraftingSystem.cs", level="standard", include_diagnostics=true)
```

Expected: no compiler errors. Existing generic warnings about string concatenation in Unity `Update()` are acceptable only if unrelated and unchanged.

- [ ] **Step 3: Run backend diagnostic**

Use `unityMCP_execute_code` in edit mode with this code:

```csharp
var go = new UnityEngine.GameObject("CraftingBackendDiagnostic");
var inventory = go.AddComponent<PlayerInventory>();
var crafting = go.AddComponent<BandageCraftingSystem>();
inventory.AddItem(ItemType.Fiber, 2);
inventory.AddItem(ItemType.Cloth, 1);
bool canCraft = crafting.CanCraftBandage();
bool crafted = crafting.TryCraftBandage();
int fiber = inventory.GetAmount(ItemType.Fiber);
int cloth = inventory.GetAmount(ItemType.Cloth);
int bandage = inventory.GetAmount(ItemType.Bandage);
UnityEngine.Object.DestroyImmediate(go);
if (!canCraft || !crafted || fiber != 0 || cloth != 0 || bandage != 1)
{
    return "FAIL canCraft=" + canCraft + " crafted=" + crafted + " fiber=" + fiber + " cloth=" + cloth + " bandage=" + bandage;
}
return "PASS bandage recipe consumed ingredients and produced bandage";
```

Expected: `PASS bandage recipe consumed ingredients and produced bandage`.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/Player/Survival/BandageCraftingSystem.cs"
git commit -m "Generalize bandage crafting backend"
```

## Task 3: Add Inventory Crafting Tab UI

**Files:**
- Modify: `Assets/Scripts/Player/Survival/PlayerInventoryUI.cs`

- [ ] **Step 1: Add fields near existing serialized references**

Add these fields after `dropItemButtonText`:

```csharp
[SerializeField] private BandageCraftingSystem craftingSystem;
[SerializeField] private Button itemsTabButton;
[SerializeField] private TextMeshProUGUI itemsTabButtonText;
[SerializeField] private Button craftingTabButton;
[SerializeField] private TextMeshProUGUI craftingTabButtonText;
[SerializeField] private RectTransform craftingListRoot;
```

Add these fields after the existing runtime boolean fields:

```csharp
private readonly System.Collections.Generic.List<GameObject> recipeCardObjects = new System.Collections.Generic.List<GameObject>();
private InventoryView activeView = InventoryView.Items;
private bool createdItemsTabAtRuntime;
private bool createdCraftingTabAtRuntime;

private enum InventoryView
{
    Items,
    Crafting
}
```

- [ ] **Step 2: Resolve crafting system in `Awake()`**

Inside `Awake()`, after resolving `hotbarUI`, add:

```csharp
if (craftingSystem == null)
{
    craftingSystem = GetComponent<BandageCraftingSystem>();
}
```

- [ ] **Step 3: Add tab creation in `EnsureUI()`**

In `EnsureUI()`, after the title block and before `itemsText` creation, add:

```csharp
if (itemsTabButton == null)
{
    itemsTabButton = this.FindExistingButton(panelRoot, "Items Tab Button");
    if (itemsTabButton == null)
    {
        itemsTabButton = this.CreateTabButton("Items Tab Button", panelRoot, new Vector2(-76f, -54f), "Items");
        createdItemsTabAtRuntime = true;
    }
}

if (craftingTabButton == null)
{
    craftingTabButton = this.FindExistingButton(panelRoot, "Crafting Tab Button");
    if (craftingTabButton == null)
    {
        craftingTabButton = this.CreateTabButton("Crafting Tab Button", panelRoot, new Vector2(76f, -54f), "Crafting");
        createdCraftingTabAtRuntime = true;
    }
}

if (itemsTabButton != null)
{
    itemsTabButton.onClick.RemoveListener(this.ShowItemsView);
    itemsTabButton.onClick.AddListener(this.ShowItemsView);
    itemsTabButtonText = itemsTabButton.GetComponentInChildren<TextMeshProUGUI>();
}

if (craftingTabButton != null)
{
    craftingTabButton.onClick.RemoveListener(this.ShowCraftingView);
    craftingTabButton.onClick.AddListener(this.ShowCraftingView);
    craftingTabButtonText = craftingTabButton.GetComponentInChildren<TextMeshProUGUI>();
}
```

Update the `itemsText` rect offsets from:

```csharp
itemsRect.offsetMax = new Vector2(-18f, -56f);
```

to:

```csharp
itemsRect.offsetMax = new Vector2(-18f, -92f);
```

- [ ] **Step 4: Add helper methods before `CreateActionButton()`**

Add:

```csharp
private Button CreateTabButton(string objectName, RectTransform parent, Vector2 anchoredPosition, string label)
{
    GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
    RectTransform rect = buttonObject.GetComponent<RectTransform>();
    rect.SetParent(parent, false);
    rect.anchorMin = new Vector2(0.5f, 1f);
    rect.anchorMax = new Vector2(0.5f, 1f);
    rect.pivot = new Vector2(0.5f, 1f);
    rect.sizeDelta = new Vector2(140f, 34f);
    rect.anchoredPosition = anchoredPosition;

    Image image = buttonObject.GetComponent<Image>();
    image.color = actionButtonColor;

    Button button = buttonObject.GetComponent<Button>();
    TextMeshProUGUI labelText = this.CreateLabel("Label", rect, 18f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, true);
    RectTransform labelRect = labelText.rectTransform;
    labelRect.anchorMin = Vector2.zero;
    labelRect.anchorMax = Vector2.one;
    labelRect.offsetMin = new Vector2(6f, 4f);
    labelRect.offsetMax = new Vector2(-6f, -4f);
    labelText.text = label;
    return button;
}

private void ShowItemsView()
{
    activeView = InventoryView.Items;
    this.Refresh();
}

private void ShowCraftingView()
{
    activeView = InventoryView.Crafting;
    this.Refresh();
}
```

- [ ] **Step 5: Update `Refresh()` to branch by tab**

At the start of `Refresh()`, after title update, add:

```csharp
this.RefreshTabState();
this.ClearRecipeCards();
```

Then before `builder.Clear();`, add:

```csharp
if (activeView == InventoryView.Crafting)
{
    if (itemsText != null)
    {
        itemsText.text = string.Empty;
    }

    this.RefreshCraftingView();
    this.RefreshDropButtonState();
    return;
}
```

- [ ] **Step 6: Add crafting render methods before `OnHotbarSelectionChanged()`**

Add:

```csharp
private void RefreshTabState()
{
    if (itemsTabButtonText != null)
    {
        itemsTabButtonText.text = activeView == InventoryView.Items ? "> Items" : "Items";
    }

    if (craftingTabButtonText != null)
    {
        craftingTabButtonText.text = activeView == InventoryView.Crafting ? "> Crafting" : "Crafting";
    }
}

private void RefreshCraftingView()
{
    if (panelRoot == null)
    {
        return;
    }

    if (craftingSystem == null)
    {
        craftingSystem = GetComponent<BandageCraftingSystem>();
    }

    if (craftingSystem == null)
    {
        this.CreateRecipeInfoText("No crafting available");
        return;
    }

    var recipes = craftingSystem.GetAvailableRecipes(CraftingContext.Simple);
    if (recipes == null || recipes.Count == 0)
    {
        this.CreateRecipeInfoText("No recipes available");
        return;
    }

    float y = -96f;
    for (int i = 0; i < recipes.Count; i++)
    {
        CraftingRecipe recipe = recipes[i];
        if (recipe == null)
        {
            continue;
        }

        this.CreateRecipeCard(recipe, y);
        y -= 86f;
    }
}

private void CreateRecipeInfoText(string text)
{
    TextMeshProUGUI label = this.CreateLabel("Crafting Info", panelRoot, 20f, FontStyles.Normal, itemTextColor, TextAlignmentOptions.Center, true);
    RectTransform rect = label.rectTransform;
    rect.anchorMin = new Vector2(0f, 0f);
    rect.anchorMax = new Vector2(1f, 1f);
    rect.offsetMin = new Vector2(18f, 16f);
    rect.offsetMax = new Vector2(-18f, -92f);
    label.text = text;
    recipeCardObjects.Add(label.gameObject);
}

private void CreateRecipeCard(CraftingRecipe recipe, float y)
{
    GameObject card = new GameObject("Recipe Card - " + recipe.DisplayName, typeof(RectTransform), typeof(Image));
    RectTransform rect = card.GetComponent<RectTransform>();
    rect.SetParent(panelRoot, false);
    rect.anchorMin = new Vector2(0f, 1f);
    rect.anchorMax = new Vector2(1f, 1f);
    rect.pivot = new Vector2(0.5f, 1f);
    rect.offsetMin = new Vector2(16f, 0f);
    rect.offsetMax = new Vector2(-16f, 0f);
    rect.sizeDelta = new Vector2(rect.sizeDelta.x, 76f);
    rect.anchoredPosition = new Vector2(0f, y);

    Image image = card.GetComponent<Image>();
    image.color = new Color(0.05f, 0.08f, 0.1f, 0.82f);

    TextMeshProUGUI title = this.CreateLabel("Title", rect, 20f, FontStyles.Bold, titleColor, TextAlignmentOptions.Left, true);
    title.rectTransform.anchorMin = new Vector2(0f, 1f);
    title.rectTransform.anchorMax = new Vector2(1f, 1f);
    title.rectTransform.offsetMin = new Vector2(12f, -34f);
    title.rectTransform.offsetMax = new Vector2(-112f, -8f);
    title.text = recipe.DisplayName + " x" + recipe.OutputAmount;

    TextMeshProUGUI ingredients = this.CreateLabel("Ingredients", rect, 16f, FontStyles.Normal, itemTextColor, TextAlignmentOptions.Left, false);
    ingredients.rectTransform.anchorMin = new Vector2(0f, 0f);
    ingredients.rectTransform.anchorMax = new Vector2(1f, 0f);
    ingredients.rectTransform.offsetMin = new Vector2(12f, 10f);
    ingredients.rectTransform.offsetMax = new Vector2(-112f, 36f);
    ingredients.text = this.BuildIngredientText(recipe);

    Button craftButton = this.CreateRecipeCraftButton(rect, recipe);
    craftButton.interactable = craftingSystem.CanCraft(recipe);

    recipeCardObjects.Add(card);
}

private Button CreateRecipeCraftButton(RectTransform parent, CraftingRecipe recipe)
{
    GameObject buttonObject = new GameObject("Craft Button", typeof(RectTransform), typeof(Image), typeof(Button));
    RectTransform rect = buttonObject.GetComponent<RectTransform>();
    rect.SetParent(parent, false);
    rect.anchorMin = new Vector2(1f, 0.5f);
    rect.anchorMax = new Vector2(1f, 0.5f);
    rect.pivot = new Vector2(1f, 0.5f);
    rect.sizeDelta = new Vector2(96f, 42f);
    rect.anchoredPosition = new Vector2(-10f, 0f);

    Image image = buttonObject.GetComponent<Image>();
    image.color = actionButtonColor;

    Button button = buttonObject.GetComponent<Button>();
    button.onClick.AddListener(() =>
    {
        if (craftingSystem != null && craftingSystem.TryCraft(recipe))
        {
            this.Refresh();
        }
    });

    TextMeshProUGUI label = this.CreateLabel("Label", rect, 18f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, true);
    label.rectTransform.anchorMin = Vector2.zero;
    label.rectTransform.anchorMax = Vector2.one;
    label.rectTransform.offsetMin = new Vector2(6f, 4f);
    label.rectTransform.offsetMax = new Vector2(-6f, -4f);
    label.text = "Craft";
    return button;
}

private string BuildIngredientText(CraftingRecipe recipe)
{
    builder.Clear();
    for (int i = 0; i < recipe.ingredients.Count; i++)
    {
        CraftingIngredient ingredient = recipe.ingredients[i];
        if (ingredient == null)
        {
            continue;
        }

        if (builder.Length > 0)
        {
            builder.Append("  ");
        }

        int owned = inventory != null ? inventory.GetAmount(ingredient.itemType) : 0;
        builder.Append(ingredient.itemType)
            .Append(' ')
            .Append(owned)
            .Append('/')
            .Append(ingredient.Amount);
    }

    return builder.ToString();
}

private void ClearRecipeCards()
{
    for (int i = 0; i < recipeCardObjects.Count; i++)
    {
        GameObject card = recipeCardObjects[i];
        if (card != null)
        {
            Destroy(card);
        }
    }

    recipeCardObjects.Clear();
}
```

- [ ] **Step 7: Extend runtime cleanup**

In `CleanupRuntimeGeneratedUI()`, before clearing fields, add:

```csharp
this.ClearRecipeCards();

if (createdCraftingTabAtRuntime && craftingTabButton != null)
{
    Destroy(craftingTabButton.gameObject);
}

if (createdItemsTabAtRuntime && itemsTabButton != null)
{
    Destroy(itemsTabButton.gameObject);
}
```

Also set these at the end with the other field resets:

```csharp
createdItemsTabAtRuntime = false;
createdCraftingTabAtRuntime = false;
itemsTabButton = null;
craftingTabButton = null;
itemsTabButtonText = null;
craftingTabButtonText = null;
```

- [ ] **Step 8: Validate UI script**

Use Unity MCP:

```text
unityMCP_refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=true)
unityMCP_validate_script(uri="Assets/Scripts/Player/Survival/PlayerInventoryUI.cs", level="standard", include_diagnostics=true)
```

Expected: no compiler errors.

- [ ] **Step 9: Run UI diagnostic**

Use `unityMCP_execute_code` with:

```csharp
var go = new UnityEngine.GameObject("CraftingUIDiagnostic");
var canvasObject = new UnityEngine.GameObject("CraftingUICanvas", typeof(UnityEngine.Canvas));
var canvas = canvasObject.GetComponent<UnityEngine.Canvas>();
canvas.renderMode = UnityEngine.RenderMode.ScreenSpaceOverlay;
var inventory = go.AddComponent<PlayerInventory>();
var crafting = go.AddComponent<BandageCraftingSystem>();
var ui = go.AddComponent<PlayerInventoryUI>();
inventory.AddItem(ItemType.Fiber, 2);
inventory.AddItem(ItemType.Cloth, 1);
var showCrafting = typeof(PlayerInventoryUI).GetMethod("ShowCraftingView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
showCrafting.Invoke(ui, null);
var cards = UnityEngine.Object.FindObjectsOfType<UnityEngine.RectTransform>(true);
bool foundRecipeCard = false;
for (int i = 0; i < cards.Length; i++)
{
    if (cards[i] != null && cards[i].name.Contains("Recipe Card - Bandage"))
    {
        foundRecipeCard = true;
        break;
    }
}
UnityEngine.Object.DestroyImmediate(go);
UnityEngine.Object.DestroyImmediate(canvasObject);
return foundRecipeCard ? "PASS crafting tab renders Bandage card" : "FAIL missing Bandage recipe card";
```

Expected: `PASS crafting tab renders Bandage card`.

- [ ] **Step 10: Commit**

```bash
git add "Assets/Scripts/Player/Survival/PlayerInventoryUI.cs"
git commit -m "Add inventory crafting tab UI"
```

## Task 4: Verify MainMenu Is Not Polluted by Inventory UI

**Files:**
- Verify: `Assets/Scenes/MainMenu.unity`
- Verify: `Assets/Scenes/Gameplay.unity`

- [ ] **Step 1: Open MainMenu and run diagnostic**

Use Unity MCP:

```text
unityMCP_manage_scene(action="load", path="Assets/Scenes/MainMenu.unity")
unityMCP_execute_code(...)
```

Diagnostic code:

```csharp
string[] blockedNames = { "Inventory UI", "Inventory Toggle Button" };
var objects = UnityEngine.Object.FindObjectsOfType<UnityEngine.GameObject>(true);
System.Text.StringBuilder found = new System.Text.StringBuilder();
for (int i = 0; i < objects.Length; i++)
{
    var obj = objects[i];
    if (obj == null)
    {
        continue;
    }
    for (int j = 0; j < blockedNames.Length; j++)
    {
        if (obj.name == blockedNames[j])
        {
            if (found.Length > 0)
            {
                found.Append(", ");
            }
            found.Append(obj.name);
        }
    }
}
return found.Length == 0 ? "PASS MainMenu has no inventory overlay objects" : "FAIL MainMenu contains " + found.ToString();
```

Expected: `PASS MainMenu has no inventory overlay objects`.

- [ ] **Step 2: Open Gameplay and verify player prefab component path**

Use Unity MCP:

```text
unityMCP_manage_scene(action="load", path="Assets/Scenes/Gameplay.unity")
unityMCP_execute_code(...)
```

Diagnostic code:

```csharp
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Assets/Prefabs/FusionPlayer.prefab");
if (prefab == null)
{
    return "FAIL FusionPlayer prefab not found";
}
var crafting = prefab.GetComponent<BandageCraftingSystem>();
var inventory = prefab.GetComponent<PlayerInventory>();
return crafting != null && inventory != null
    ? "PASS FusionPlayer keeps inventory and crafting components"
    : "FAIL missing component crafting=" + (crafting != null) + " inventory=" + (inventory != null);
```

Expected: `PASS FusionPlayer keeps inventory and crafting components`.

- [ ] **Step 3: Check git status**

Run:

```bash
git status --short
```

Expected: no scene changes from diagnostics. If Unity marks a scene dirty without an intentional edit, do not commit it; ask before saving/reverting.

## Task 5: End-to-End Crafting Verification

**Files:**
- Verify: `Assets/Scripts/Player/Survival/CraftingRecipe.cs`
- Verify: `Assets/Scripts/Player/Survival/BandageCraftingSystem.cs`
- Verify: `Assets/Scripts/Player/Survival/PlayerInventoryUI.cs`

- [ ] **Step 1: Run compile and console check**

Use Unity MCP:

```text
unityMCP_refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=true)
unityMCP_read_console(action="get", types=["error", "warning"], count="20", format="plain", include_stacktrace=false)
```

Expected: no new compiler errors. Existing unrelated warnings may remain, but record them in the final handoff.

- [ ] **Step 2: Run integrated craft diagnostic**

Use `unityMCP_execute_code` with:

```csharp
var go = new UnityEngine.GameObject("CraftingEndToEndDiagnostic");
var inventory = go.AddComponent<PlayerInventory>();
var crafting = go.AddComponent<BandageCraftingSystem>();
bool missingBlocked = !crafting.CanCraftBandage();
inventory.AddItem(ItemType.Fiber, 2);
inventory.AddItem(ItemType.Cloth, 1);
bool canCraft = crafting.CanCraftBandage();
bool crafted = crafting.TryCraftBandage();
bool inventoryUpdated = inventory.GetAmount(ItemType.Bandage) == 1 && inventory.GetAmount(ItemType.Fiber) == 0 && inventory.GetAmount(ItemType.Cloth) == 0;
UnityEngine.Object.DestroyImmediate(go);
return missingBlocked && canCraft && crafted && inventoryUpdated
    ? "PASS simple bandage crafting end-to-end"
    : "FAIL missingBlocked=" + missingBlocked + " canCraft=" + canCraft + " crafted=" + crafted + " inventoryUpdated=" + inventoryUpdated;
```

Expected: `PASS simple bandage crafting end-to-end`.

- [ ] **Step 3: Inspect final diff**

Run:

```bash
git status --short
git diff --stat
```

Expected: no uncommitted changes after prior commits. If Unity generated `.meta` files for `CraftingRecipe.cs`, commit them with Task 1 or a cleanup commit:

```bash
git add "Assets/Scripts/Player/Survival/CraftingRecipe.cs.meta"
git commit -m "Add crafting recipe metadata"
```

- [ ] **Step 4: Manual QA checklist for user**

Ask the user to run multiplayer/manual QA:

```text
1. Pick up Fiber x2 and Cloth x1.
2. Open Inventory.
3. Tap Crafting tab.
4. Confirm Bandage card shows Fiber 2/2 and Cloth 1/1.
5. Tap Craft.
6. Confirm Fiber and Cloth decrease and Bandage increases.
7. Down the local player and confirm Craft button is disabled.
```

## Plan Self-Review

- Spec coverage: plan covers inventory `Crafting` tab, recipe cards, simple Bandage recipe, local authority via existing UI path, downed blocking, inventory-full rollback, and future crafting table hook via recipe context.
- Placeholder scan: no incomplete markers or unspecified test steps remain.
- Type consistency: `CraftingContext`, `CraftingIngredient`, `CraftingRecipe`, `BandageCraftingSystem.GetAvailableRecipes`, `CanCraft`, and `TryCraft` are defined before they are used by `PlayerInventoryUI`.
