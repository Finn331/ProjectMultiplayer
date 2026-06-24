using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public class PlayerInventoryUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private NetworkInventoryBridge networkInventoryBridge;
    [SerializeField] private MobileHotbarUI hotbarUI;
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private RectTransform panelRoot;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI itemsText;
    [SerializeField] private Button toggleButton;
    [SerializeField] private TextMeshProUGUI toggleButtonText;
    [SerializeField] private Button nextItemButton;
    [SerializeField] private TextMeshProUGUI nextItemButtonText;
    [SerializeField] private Button dropItemButton;
    [SerializeField] private TextMeshProUGUI dropItemButtonText;
    [SerializeField] private BandageCraftingSystem craftingSystem;
    [SerializeField] private Button itemsTabButton;
    [SerializeField] private TextMeshProUGUI itemsTabButtonText;
    [SerializeField] private Button craftingTabButton;
    [SerializeField] private TextMeshProUGUI craftingTabButtonText;
    [SerializeField] private RectTransform craftingListRoot;

    [Header("Behavior")]
    [SerializeField] private bool autoCreateUI = true;
    [SerializeField] private bool visibleOnStart = false;
    [SerializeField] private bool autoCreateToggleButton = true;
    [SerializeField] private bool autoCreateActionButtons = true;
    [SerializeField] private bool showEditorPreview = true;
    [SerializeField] private bool allowKeyboardToggle = false;
    [SerializeField] private bool allowKeyboardInventoryActions = true;
    [SerializeField] private KeyCode toggleKey = KeyCode.I;
    [SerializeField] private KeyCode nextItemKey = KeyCode.Tab;
    [SerializeField] private KeyCode dropItemKey = KeyCode.G;

    [Header("Layout")]
    [SerializeField] private Vector2 panelSize = new Vector2(360f, 280f);
    [SerializeField] private Vector2 anchoredOffset = new Vector2(-24f, -130f);
    [SerializeField] private Vector2 toggleButtonSize = new Vector2(180f, 56f);
    [SerializeField] private Vector2 toggleButtonOffset = new Vector2(-24f, -24f);
    [SerializeField] private Vector2 actionButtonSize = new Vector2(152f, 44f);
    [SerializeField] private Vector2 nextButtonOffset = new Vector2(82f, 20f);
    [SerializeField] private Vector2 dropButtonOffset = new Vector2(-82f, 20f);

    [Header("Style")]
    [SerializeField] private Color panelColor = new Color(0f, 0f, 0f, 0.58f);
    [SerializeField] private Color titleColor = new Color(1f, 0.85f, 0.4f, 1f);
    [SerializeField] private Color itemTextColor = Color.white;
    [SerializeField] private Color toggleButtonColor = new Color(0.13f, 0.22f, 0.35f, 0.95f);
    [SerializeField] private Color actionButtonColor = new Color(0.18f, 0.25f, 0.18f, 0.95f);

    private readonly StringBuilder builder = new StringBuilder(256);
    private bool initialized;
    private int selectedIndex;
    private bool createdPanelAtRuntime;
    private bool createdToggleButtonAtRuntime;
    private bool createdNextButtonAtRuntime;
    private bool createdDropButtonAtRuntime;
    private readonly System.Collections.Generic.List<GameObject> recipeCardObjects = new System.Collections.Generic.List<GameObject>();
    private InventoryView activeView = InventoryView.Items;
    private bool createdItemsTabAtRuntime;
    private bool createdCraftingTabAtRuntime;

    private enum InventoryView
    {
        Items,
        Crafting
    }
#if UNITY_EDITOR
    private bool editorEnsureQueued;
#endif

    private void Awake()
    {
        if (inventory == null)
        {
            inventory = GetComponent<PlayerInventory>();
        }

        if (networkInventoryBridge == null)
        {
            networkInventoryBridge = GetComponent<NetworkInventoryBridge>();
        }

        if (hotbarUI == null)
        {
            hotbarUI = GetComponent<MobileHotbarUI>();
        }

        if (craftingSystem == null)
        {
            craftingSystem = GetComponent<BandageCraftingSystem>();
        }

        if (!this.HasLocalInventoryAuthority())
        {
            enabled = false;
            return;
        }

        if (Application.isPlaying || this.CanRenderEditorPreview())
        {
            this.EnsureUI();
        }
    }

    private void OnEnable()
    {
        if (!Application.isPlaying && !this.CanRenderEditorPreview())
        {
            return;
        }

        if (Application.isPlaying && !this.HasLocalInventoryAuthority())
        {
            enabled = false;
            return;
        }

        this.EnsureUI();

        if (Application.isPlaying && inventory != null)
        {
            inventory.InventoryChanged += this.Refresh;
        }

        if (Application.isPlaying && hotbarUI != null)
        {
            hotbarUI.SelectedSlotChanged += this.OnHotbarSelectionChanged;
        }

        this.Refresh();
    }

    private void OnValidate()
    {
        if (!this.CanRenderEditorPreview())
        {
            if (this.HasGeneratedUIObjects())
            {
                this.CleanupRuntimeGeneratedUI();
            }

            return;
        }

        initialized = false;
#if UNITY_EDITOR
        if (editorEnsureQueued)
        {
            return;
        }

        editorEnsureQueued = true;
        UnityEditor.EditorApplication.delayCall += this.EnsureEditorPreviewDelayed;
#endif
    }

#if UNITY_EDITOR
    private void EnsureEditorPreviewDelayed()
    {
        editorEnsureQueued = false;

        if (this == null || !this.CanRenderEditorPreview())
        {
            return;
        }

        this.EnsureUI();
        this.Refresh();
    }
#endif

    private void OnDisable()
    {
        if (inventory != null)
        {
            inventory.InventoryChanged -= this.Refresh;
        }

        if (hotbarUI != null)
        {
            hotbarUI.SelectedSlotChanged -= this.OnHotbarSelectionChanged;
        }

        if (this.HasGeneratedUIObjects())
        {
            this.CleanupRuntimeGeneratedUI();
        }
    }

    private void OnDestroy()
    {
        if (toggleButton != null)
        {
            toggleButton.onClick.RemoveListener(this.Toggle);
        }

        if (nextItemButton != null)
        {
            nextItemButton.onClick.RemoveListener(this.SelectNextItem);
        }

        if (dropItemButton != null)
        {
            dropItemButton.onClick.RemoveListener(this.DropSelectedItem);
        }

        if (itemsTabButton != null)
        {
            itemsTabButton.onClick.RemoveListener(this.ShowItemsView);
        }

        if (craftingTabButton != null)
        {
            craftingTabButton.onClick.RemoveListener(this.ShowCraftingView);
        }
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (allowKeyboardToggle && Input.GetKeyDown(toggleKey))
        {
            this.Toggle();
        }

        if (allowKeyboardInventoryActions && initialized)
        {
            if (Input.GetKeyDown(nextItemKey))
            {
                this.SelectNextItem();
            }

            if (Input.GetKeyDown(dropItemKey))
            {
                this.DropSelectedItem();
            }
        }
    }

    public void Toggle()
    {
        if (panelRoot == null)
        {
            return;
        }

        panelRoot.gameObject.SetActive(!panelRoot.gameObject.activeSelf);
    }

    public void SetVisible(bool visible)
    {
        if (panelRoot == null)
        {
            return;
        }

        panelRoot.gameObject.SetActive(visible);
    }

    public void SelectNextItem()
    {
        int visibleCount = this.GetVisibleInventorySlotCount();
        if (inventory == null || visibleCount == 0)
        {
            selectedIndex = 0;
            this.Refresh();
            return;
        }

        selectedIndex = (selectedIndex + 1) % visibleCount;
        this.Refresh();
    }

    public int GetSelectedInventorySlotIndex()
    {
        return this.GetVisibleInventorySlotIndexAt(selectedIndex);
    }

    public ItemType? GetSelectedInventoryItemType()
    {
        int slotIndex = this.GetSelectedInventorySlotIndex();
        return slotIndex >= 0 && inventory != null ? inventory.GetSlotItemType(slotIndex) : (ItemType?)null;
    }

    public void DropSelectedItem()
    {
        if (this.CanDropSelectedHotbarItem())
        {
            hotbarUI.DropSelectedItem();
            return;
        }

        if (PickupUIManager.instance != null)
        {
            PickupUIManager.instance.ShowInfo("Drag item ke hotbar bawah lalu pilih dulu untuk drop.");
        }
    }

    private void EnsureUI()
    {
        this.ResolveLocalReferences();

        if (initialized)
        {
            return;
        }

        if (targetCanvas == null)
        {
            targetCanvas = this.FindTargetCanvas();
        }

        if (targetCanvas == null && autoCreateUI)
        {
            targetCanvas = this.CreateFallbackCanvas();
        }

        if (targetCanvas == null)
        {
            return;
        }

        if (panelRoot == null)
        {
            panelRoot = this.FindExistingPanel(targetCanvas.transform as RectTransform);
            if (panelRoot == null && autoCreateUI)
            {
                panelRoot = this.CreatePanel(targetCanvas.transform as RectTransform);
                createdPanelAtRuntime = true;
            }
        }

        if (panelRoot == null)
        {
            return;
        }

        if (titleText == null)
        {
            Transform existingTitle = panelRoot.Find("Title");
            titleText = existingTitle != null ? existingTitle.GetComponent<TextMeshProUGUI>() : null;
            if (titleText == null)
            {
                titleText = this.CreateLabel("Title", panelRoot, 26f, FontStyles.Bold, titleColor, TextAlignmentOptions.Center, true);
            }

            RectTransform titleRect = titleText.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -12f);
            titleRect.sizeDelta = new Vector2(-30f, 40f);
            titleText.text = "Inventory";
        }

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

        if (itemsText == null)
        {
            Transform existingItems = panelRoot.Find("Items");
            itemsText = existingItems != null ? existingItems.GetComponent<TextMeshProUGUI>() : null;
            if (itemsText == null)
            {
                itemsText = this.CreateLabel("Items", panelRoot, 22f, FontStyles.Normal, itemTextColor, TextAlignmentOptions.TopLeft, false);
            }

            RectTransform itemsRect = itemsText.rectTransform;
            itemsRect.anchorMin = new Vector2(0f, 0f);
            itemsRect.anchorMax = new Vector2(1f, 1f);
            itemsRect.offsetMin = new Vector2(18f, 16f);
            itemsRect.offsetMax = new Vector2(-18f, -92f);
        }

        if (toggleButton == null && autoCreateToggleButton)
        {
            toggleButton = this.FindExistingButton(targetCanvas.transform as RectTransform, "Inventory Toggle Button");
            if (toggleButton == null)
            {
                toggleButton = this.CreateToggleButton(targetCanvas.transform as RectTransform);
                createdToggleButtonAtRuntime = true;
            }
        }

        if (toggleButton != null)
        {
            toggleButton.onClick.RemoveListener(this.Toggle);
            toggleButton.onClick.AddListener(this.Toggle);
        }

        if (autoCreateActionButtons && panelRoot != null)
        {
            if (nextItemButton == null)
            {
                nextItemButton = this.FindExistingButton(panelRoot, "Next Item Button");
                if (nextItemButton == null)
                {
                    nextItemButton = this.CreateActionButton("Next Item Button", panelRoot, nextButtonOffset, "Next");
                    createdNextButtonAtRuntime = true;
                }
            }

            if (dropItemButton == null)
            {
                dropItemButton = this.FindExistingButton(panelRoot, "Drop Item Button");
                if (dropItemButton == null)
                {
                    dropItemButton = this.CreateActionButton("Drop Item Button", panelRoot, dropButtonOffset, "Drop 1");
                    createdDropButtonAtRuntime = true;
                }
            }
        }

        if (nextItemButton != null)
        {
            nextItemButton.onClick.RemoveListener(this.SelectNextItem);
            nextItemButton.onClick.AddListener(this.SelectNextItem);
            nextItemButtonText = nextItemButton.GetComponentInChildren<TextMeshProUGUI>();
        }

        if (dropItemButton != null)
        {
            dropItemButton.onClick.RemoveListener(this.DropSelectedItem);
            dropItemButton.onClick.AddListener(this.DropSelectedItem);
            dropItemButtonText = dropItemButton.GetComponentInChildren<TextMeshProUGUI>();
        }

        panelRoot.gameObject.SetActive(visibleOnStart);
        initialized = true;
    }

    private RectTransform CreatePanel(RectTransform parent)
    {
        GameObject panel = new GameObject("Inventory UI", typeof(RectTransform), typeof(Image));
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = panelSize;
        rect.anchoredPosition = anchoredOffset;

        Image image = panel.GetComponent<Image>();
        image.color = panelColor;

        return rect;
    }

    private Canvas CreateFallbackCanvas()
    {
        GameObject canvasObject = new GameObject(
            "Inventory Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

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
        this.EnsureUI();
        this.Refresh();
    }

    private void ShowCraftingView()
    {
        activeView = InventoryView.Crafting;
        this.EnsureUI();
        this.Refresh();
    }

    private Button CreateToggleButton(RectTransform parent)
    {
        GameObject buttonObject = new GameObject("Inventory Toggle Button", typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = toggleButtonSize;
        rect.anchoredPosition = toggleButtonOffset;

        Image image = buttonObject.GetComponent<Image>();
        image.color = toggleButtonColor;

        Button button = buttonObject.GetComponent<Button>();

        if (toggleButtonText == null)
        {
            toggleButtonText = this.CreateLabel("Label", rect, 22f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, true);
            RectTransform labelRect = toggleButtonText.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 6f);
            labelRect.offsetMax = new Vector2(-8f, -6f);
            toggleButtonText.text = "Inventory";
        }

        return button;
    }

    private Button CreateActionButton(string objectName, RectTransform parent, Vector2 anchoredPosition, string label)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = actionButtonSize;
        rect.anchoredPosition = anchoredPosition;

        Image image = buttonObject.GetComponent<Image>();
        image.color = actionButtonColor;

        Button button = buttonObject.GetComponent<Button>();
        TextMeshProUGUI labelText = this.CreateLabel("Label", rect, 20f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, true);
        RectTransform labelRect = labelText.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(8f, 6f);
        labelRect.offsetMax = new Vector2(-8f, -6f);
        labelText.text = label;
        return button;
    }

    private TextMeshProUGUI CreateLabel(
        string objectName,
        RectTransform parent,
        float fontSize,
        FontStyles style,
        Color color,
        TextAlignmentOptions alignment,
        bool autoSizing)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.enableAutoSizing = autoSizing;
        text.enableWordWrapping = false;
        return text;
    }

    private RectTransform FindExistingPanel(RectTransform parent)
    {
        if (parent == null)
        {
            return null;
        }

        Transform existing = this.FindDeepChildByName(parent, "Inventory UI");
        return existing != null ? existing as RectTransform : null;
    }

    private Button FindExistingButton(RectTransform parent, string objectName)
    {
        if (parent == null)
        {
            return null;
        }

        Transform existing = this.FindDeepChildByName(parent, objectName);
        return existing != null ? existing.GetComponent<Button>() : null;
    }

    private Canvas FindTargetCanvas()
    {
        Canvas[] canvases = FindObjectsOfType<Canvas>(true);
        if (canvases == null || canvases.Length == 0)
        {
            return null;
        }

        Canvas fallback = null;
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null)
            {
                continue;
            }

            if (fallback == null)
            {
                fallback = canvas;
            }

            if (canvas.GetComponent<PlayerSurvivalUI>() != null || canvas.GetComponent<PickupUIManager>() != null)
            {
                return canvas;
            }
        }

        return fallback;
    }

    private Transform FindDeepChildByName(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
        {
            return null;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == childName)
            {
                return child;
            }

            Transform nested = this.FindDeepChildByName(child, childName);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private void Refresh()
    {
        if (!initialized || itemsText == null || inventory == null)
        {
            return;
        }

        if (titleText != null)
        {
            titleText.text = "Inventory (" + inventory.UsedSlotCount + "/" + inventory.TotalSlotCount + ")";
        }

        this.RefreshTabState();
        this.ClearRecipeCards();

        if (activeView == InventoryView.Crafting)
        {
            itemsText.text = string.Empty;
            this.RefreshCraftingView();
            this.RefreshDropButtonState();
            return;
        }

        builder.Clear();

        int entryCount = this.GetVisibleInventorySlotCount();
        if (entryCount == 0)
        {
            selectedIndex = 0;
            builder.Append("- Empty");
        }
        else
        {
            selectedIndex = Mathf.Clamp(selectedIndex, 0, entryCount - 1);
            for (int i = 0; i < entryCount; i++)
            {
                int slotIndex = this.GetVisibleInventorySlotIndexAt(i);
                if (slotIndex < 0)
                {
                    continue;
                }

                ItemType? itemType = inventory.GetSlotItemType(slotIndex);
                int amount = inventory.GetSlotAmount(slotIndex);
                if (itemType == null || amount <= 0)
                {
                    continue;
                }

                builder.Append(i == selectedIndex ? "> " : "  ")
                    .Append(itemType.Value)
                    .Append(" x")
                    .Append(amount);

                if (i < entryCount - 1)
                {
                    builder.Append('\n');
                }
            }
        }

        itemsText.text = builder.ToString();
        this.RefreshDropButtonState();
    }

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
        if (recipe == null || recipe.ingredients == null)
        {
            return string.Empty;
        }

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
            if (card == null)
            {
                continue;
            }

            this.DestroyGeneratedObject(card);
        }

        recipeCardObjects.Clear();
    }

    private void OnHotbarSelectionChanged(int slotIndex, ItemType? itemType)
    {
        this.Refresh();
    }

    private bool CanDropSelectedHotbarItem()
    {
        return hotbarUI != null && hotbarUI.SelectedSlotIndex >= 0 && hotbarUI.SelectedItem != null;
    }

    private void RefreshDropButtonState()
    {
        if (dropItemButton == null)
        {
            return;
        }

        bool canDrop = this.CanDropSelectedHotbarItem();
        dropItemButton.interactable = canDrop;

        if (dropItemButtonText != null)
        {
            dropItemButtonText.text = canDrop ? "Drop 1" : "Pilih Hotbar";
        }
    }

    private int GetVisibleInventorySlotCount()
    {
        if (inventory == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < inventory.InventorySlotCount; i++)
        {
            if (!inventory.IsSlotEmpty(i))
            {
                count++;
            }
        }

        return count;
    }

    private int GetVisibleInventorySlotIndexAt(int visibleIndex)
    {
        if (inventory == null || visibleIndex < 0)
        {
            return -1;
        }

        int currentVisibleIndex = 0;
        for (int i = 0; i < inventory.InventorySlotCount; i++)
        {
            if (inventory.IsSlotEmpty(i))
            {
                continue;
            }

            if (currentVisibleIndex == visibleIndex)
            {
                return i;
            }

            currentVisibleIndex++;
        }

        return -1;
    }

    private bool HasLocalInventoryAuthority()
    {
        var fusionObject = GetComponent<Fusion.NetworkObject>();
        if (fusionObject != null && fusionObject.IsValid)
        {
            return fusionObject.HasStateAuthority;
        }

        NetworkObject networkObject = GetComponent<NetworkObject>();
        if (networkObject != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (!networkObject.IsSpawned)
            {
                return false;
            }

            return networkObject.IsOwner;
        }
        else if (networkInventoryBridge != null && networkInventoryBridge.UseNetworkedInventory)
        {
            return networkInventoryBridge.HasInputAuthority;
        }

        return true;
    }

    private void ResolveLocalReferences()
    {
        if (inventory == null)
        {
            inventory = GetComponent<PlayerInventory>();
        }

        if (networkInventoryBridge == null)
        {
            networkInventoryBridge = GetComponent<NetworkInventoryBridge>();
        }

        if (hotbarUI == null)
        {
            hotbarUI = GetComponent<MobileHotbarUI>();
        }

        if (craftingSystem == null)
        {
            craftingSystem = GetComponent<BandageCraftingSystem>();
        }
    }

    private bool CanRenderEditorPreview()
    {
        if (Application.isPlaying || !showEditorPreview)
        {
            return false;
        }

        if (!gameObject.scene.IsValid())
        {
            return false;
        }

        return !string.IsNullOrEmpty(gameObject.scene.path);
    }

    private bool HasGeneratedUIObjects()
    {
        return recipeCardObjects.Count > 0
            || createdCraftingTabAtRuntime
            || createdItemsTabAtRuntime
            || createdDropButtonAtRuntime
            || createdNextButtonAtRuntime
            || createdToggleButtonAtRuntime
            || createdPanelAtRuntime;
    }

    private void DestroyGeneratedObject(GameObject obj)
    {
        if (obj == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(obj);
        }
        else
        {
            DestroyImmediate(obj);
        }
    }

    private void CleanupRuntimeGeneratedUI()
    {
        this.ClearRecipeCards();

        if (createdCraftingTabAtRuntime && craftingTabButton != null)
        {
            this.DestroyGeneratedObject(craftingTabButton.gameObject);
        }

        if (createdItemsTabAtRuntime && itemsTabButton != null)
        {
            this.DestroyGeneratedObject(itemsTabButton.gameObject);
        }

        if (createdDropButtonAtRuntime && dropItemButton != null)
        {
            this.DestroyGeneratedObject(dropItemButton.gameObject);
        }

        if (createdNextButtonAtRuntime && nextItemButton != null)
        {
            this.DestroyGeneratedObject(nextItemButton.gameObject);
        }

        if (createdToggleButtonAtRuntime && toggleButton != null)
        {
            this.DestroyGeneratedObject(toggleButton.gameObject);
        }

        if (createdPanelAtRuntime && panelRoot != null)
        {
            this.DestroyGeneratedObject(panelRoot.gameObject);
        }

        createdPanelAtRuntime = false;
        createdToggleButtonAtRuntime = false;
        createdNextButtonAtRuntime = false;
        createdDropButtonAtRuntime = false;
        createdItemsTabAtRuntime = false;
        createdCraftingTabAtRuntime = false;
        initialized = false;
        panelRoot = null;
        itemsText = null;
        titleText = null;
        toggleButton = null;
        nextItemButton = null;
        dropItemButton = null;
        itemsTabButton = null;
        craftingTabButton = null;
        itemsTabButtonText = null;
        craftingTabButtonText = null;
        craftingListRoot = null;
    }
}
