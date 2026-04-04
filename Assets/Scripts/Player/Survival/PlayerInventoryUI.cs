using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class PlayerInventoryUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private NetworkInventoryBridge networkInventoryBridge;
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

    [Header("Behavior")]
    [SerializeField] private bool autoCreateUI = true;
    [SerializeField] private bool visibleOnStart = false;
    [SerializeField] private bool autoCreateToggleButton = true;
    [SerializeField] private bool autoCreateActionButtons = true;
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

        if (!this.HasLocalInventoryAuthority())
        {
            enabled = false;
            return;
        }

        this.EnsureUI();
    }

    private void OnEnable()
    {
        if (Application.isPlaying && !this.HasLocalInventoryAuthority())
        {
            enabled = false;
            return;
        }

        if (!Application.isPlaying)
        {
            this.EnsureUI();
            this.Refresh();
        }

        this.EnsureUI();

        if (inventory != null)
        {
            inventory.InventoryChanged += this.Refresh;
        }

        this.Refresh();
    }

    private void OnDisable()
    {
        if (inventory != null)
        {
            inventory.InventoryChanged -= this.Refresh;
        }

        if (Application.isPlaying)
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
    }

    private void Update()
    {
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
        if (inventory == null || inventory.Entries.Count == 0)
        {
            selectedIndex = 0;
            this.Refresh();
            return;
        }

        selectedIndex = (selectedIndex + 1) % inventory.Entries.Count;
        this.Refresh();
    }

    public void DropSelectedItem()
    {
        if (inventory == null || inventory.Entries.Count == 0)
        {
            return;
        }

        int clampedIndex = Mathf.Clamp(selectedIndex, 0, inventory.Entries.Count - 1);
        PlayerInventory.InventoryEntry selected = inventory.Entries[clampedIndex];

        if (networkInventoryBridge != null && networkInventoryBridge.UseNetworkedInventory)
        {
            networkInventoryBridge.TryRequestDrop(selected.itemType, 1);
            return;
        }

        inventory.DropItem(selected.itemType, 1);
    }

    private void EnsureUI()
    {
        if (initialized)
        {
            return;
        }

        if (targetCanvas == null)
        {
            targetCanvas = FindObjectOfType<Canvas>();
        }

        if (targetCanvas == null && autoCreateUI)
        {
            targetCanvas = this.CreateFallbackCanvas();
        }

        if (targetCanvas == null || !autoCreateUI)
        {
            return;
        }

        if (panelRoot == null)
        {
            panelRoot = this.FindExistingPanel(targetCanvas.transform as RectTransform);
            if (panelRoot == null)
            {
                panelRoot = this.CreatePanel(targetCanvas.transform as RectTransform);
                createdPanelAtRuntime = true;
            }
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
            itemsRect.offsetMax = new Vector2(-18f, -56f);
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

        Transform existing = parent.Find("Inventory UI");
        return existing != null ? existing as RectTransform : null;
    }

    private Button FindExistingButton(RectTransform parent, string objectName)
    {
        if (parent == null)
        {
            return null;
        }

        Transform existing = parent.Find(objectName);
        return existing != null ? existing.GetComponent<Button>() : null;
    }

    private void Refresh()
    {
        if (!initialized || itemsText == null || inventory == null)
        {
            return;
        }

        if (titleText != null)
        {
            titleText.text = "Inventory (" + inventory.CurrentTotalItems + "/" + inventory.MaxTotalItems + ")";
        }

        builder.Clear();

        int entryCount = inventory.Entries.Count;
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
                PlayerInventory.InventoryEntry entry = inventory.Entries[i];
                builder.Append(i == selectedIndex ? "> " : "  ")
                    .Append(entry.itemType)
                    .Append(" x")
                    .Append(entry.amount);

                if (i < entryCount - 1)
                {
                    builder.Append('\n');
                }
            }
        }

        itemsText.text = builder.ToString();
    }

    private bool HasLocalInventoryAuthority()
    {
        NetworkObject networkObject = GetComponent<NetworkObject>();
        if (networkObject != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (!networkObject.IsSpawned)
            {
                return false;
            }

            return networkObject.IsOwner;
        }

        if (networkInventoryBridge == null || !networkInventoryBridge.UseNetworkedInventory)
        {
            return true;
        }

        return networkInventoryBridge.HasInputAuthority;
    }

    private void CleanupRuntimeGeneratedUI()
    {
        if (createdDropButtonAtRuntime && dropItemButton != null)
        {
            Destroy(dropItemButton.gameObject);
        }

        if (createdNextButtonAtRuntime && nextItemButton != null)
        {
            Destroy(nextItemButton.gameObject);
        }

        if (createdToggleButtonAtRuntime && toggleButton != null)
        {
            Destroy(toggleButton.gameObject);
        }

        if (createdPanelAtRuntime && panelRoot != null)
        {
            Destroy(panelRoot.gameObject);
        }

        createdPanelAtRuntime = false;
        createdToggleButtonAtRuntime = false;
        createdNextButtonAtRuntime = false;
        createdDropButtonAtRuntime = false;
        initialized = false;
        panelRoot = null;
        itemsText = null;
        titleText = null;
        toggleButton = null;
        nextItemButton = null;
        dropItemButton = null;
    }
}
