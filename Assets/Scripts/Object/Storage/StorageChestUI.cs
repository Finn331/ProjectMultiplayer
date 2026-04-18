using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StorageChestUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private PlayerInventoryUI playerInventoryUI;
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private RectTransform panelRoot;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI chestItemsText;
    [SerializeField] private Button previousSlotButton;
    [SerializeField] private Button nextSlotButton;
    [SerializeField] private Button storeButton;
    [SerializeField] private Button takeButton;
    [SerializeField] private Button closeButton;

    [Header("Behavior")]
    [SerializeField] private float autoCloseDistancePadding = 0.35f;

    private readonly StringBuilder builder = new StringBuilder(256);
    private StorageChest activeChest;
    private int selectedChestSlotIndex;
    private bool initialized;

    private void Awake()
    {
        if (playerInventory == null)
        {
            playerInventory = GetComponent<PlayerInventory>();
        }

        if (playerInventoryUI == null)
        {
            playerInventoryUI = GetComponent<PlayerInventoryUI>();
        }
    }

    private void OnEnable()
    {
        this.EnsureUI();
        this.SetVisible(false);
    }

    private void OnDisable()
    {
        this.UnbindChest();
    }

    private void Update()
    {
        if (!this.ShouldKeepChestOpen())
        {
            this.CloseChest();
        }
    }

    public void OpenChest(StorageChest chest)
    {
        if (chest == null)
        {
            return;
        }

        this.EnsureUI();
        if (activeChest != chest)
        {
            this.UnbindChest();
            activeChest = chest;
            activeChest.ChestChanged += this.Refresh;
        }

        selectedChestSlotIndex = Mathf.Clamp(selectedChestSlotIndex, 0, chest.SlotCount - 1);
        this.SetVisible(true);
        this.Refresh();
    }

    public void CloseChest()
    {
        this.UnbindChest();
        this.SetVisible(false);
    }

    public void SelectNextChestSlot()
    {
        if (activeChest == null)
        {
            return;
        }

        selectedChestSlotIndex = (selectedChestSlotIndex + 1) % activeChest.SlotCount;
        this.Refresh();
    }

    public void SelectPreviousChestSlot()
    {
        if (activeChest == null)
        {
            return;
        }

        selectedChestSlotIndex = (selectedChestSlotIndex - 1 + activeChest.SlotCount) % activeChest.SlotCount;
        this.Refresh();
    }

    public void StoreSelectedInventoryItem()
    {
        if (activeChest == null || playerInventoryUI == null || playerInventory == null)
        {
            return;
        }

        int selectedPlayerSlot = playerInventoryUI.GetSelectedInventorySlotIndex();
        if (selectedPlayerSlot < 0)
        {
            return;
        }

        activeChest.TryRequestStore(playerInventory, selectedPlayerSlot, selectedChestSlotIndex);
    }

    public void TakeSelectedChestItem()
    {
        if (activeChest == null || playerInventory == null)
        {
            return;
        }

        int preferredPlayerSlot = playerInventoryUI != null ? playerInventoryUI.GetSelectedInventorySlotIndex() : -1;
        activeChest.TryRequestTake(playerInventory, selectedChestSlotIndex, preferredPlayerSlot);
    }

    private void Refresh()
    {
        if (!initialized || titleText == null || chestItemsText == null)
        {
            return;
        }

        if (activeChest == null)
        {
            titleText.text = "Storage Chest";
            chestItemsText.text = "- Empty";
            return;
        }

        titleText.text = activeChest.ChestName + " (" + activeChest.UsedSlotCount + "/" + activeChest.SlotCount + ")";
        builder.Clear();
        for (int i = 0; i < activeChest.SlotCount; i++)
        {
            ItemType? itemType = activeChest.GetSlotItemType(i);
            int amount = activeChest.GetSlotAmount(i);
            builder.Append(i == selectedChestSlotIndex ? "> " : "  ");
            builder.Append("[").Append(i + 1).Append("] ");
            if (itemType == null || amount <= 0)
            {
                builder.Append("Empty");
            }
            else
            {
                builder.Append(itemType.Value).Append(" x").Append(amount);
            }

            if (i < activeChest.SlotCount - 1)
            {
                builder.Append('\n');
            }
        }

        chestItemsText.text = builder.ToString();
    }

    private void EnsureUI()
    {
        if (initialized)
        {
            return;
        }

        if (targetCanvas == null)
        {
            targetCanvas = FindObjectOfType<Canvas>(true);
        }

        if (targetCanvas == null)
        {
            return;
        }

        if (panelRoot == null)
        {
            GameObject panelObject = new GameObject("Storage Chest UI", typeof(RectTransform), typeof(Image));
            panelRoot = panelObject.GetComponent<RectTransform>();
            panelRoot.SetParent(targetCanvas.transform, false);
            panelRoot.anchorMin = new Vector2(1f, 0.5f);
            panelRoot.anchorMax = new Vector2(1f, 0.5f);
            panelRoot.pivot = new Vector2(1f, 0.5f);
            panelRoot.sizeDelta = new Vector2(360f, 420f);
            panelRoot.anchoredPosition = new Vector2(-400f, 0f);
            panelObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);
        }

        titleText = this.CreateLabel("Title", panelRoot, 26f, FontStyles.Bold, new Color(1f, 0.85f, 0.4f, 1f), TextAlignmentOptions.Center);
        RectTransform titleRect = titleText.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -12f);
        titleRect.sizeDelta = new Vector2(-24f, 36f);

        chestItemsText = this.CreateLabel("ChestItems", panelRoot, 22f, FontStyles.Normal, Color.white, TextAlignmentOptions.TopLeft);
        RectTransform itemsRect = chestItemsText.rectTransform;
        itemsRect.anchorMin = new Vector2(0f, 0f);
        itemsRect.anchorMax = new Vector2(1f, 1f);
        itemsRect.offsetMin = new Vector2(16f, 80f);
        itemsRect.offsetMax = new Vector2(-16f, -56f);

        previousSlotButton = this.CreateButton("Prev Chest Slot", panelRoot, new Vector2(-110f, 20f), "Prev", this.SelectPreviousChestSlot);
        nextSlotButton = this.CreateButton("Next Chest Slot", panelRoot, new Vector2(110f, 20f), "Next", this.SelectNextChestSlot);
        storeButton = this.CreateButton("Store Button", panelRoot, new Vector2(-110f, -32f), "Store", this.StoreSelectedInventoryItem);
        takeButton = this.CreateButton("Take Button", panelRoot, new Vector2(0f, -32f), "Take", this.TakeSelectedChestItem);
        closeButton = this.CreateButton("Close Chest Button", panelRoot, new Vector2(110f, -32f), "Close", this.CloseChest);

        initialized = true;
    }

    private Button CreateButton(string name, RectTransform parent, Vector2 anchoredPosition, string label, UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(96f, 40f);
        rect.anchoredPosition = anchoredPosition;
        buttonObject.GetComponent<Image>().color = new Color(0.18f, 0.25f, 0.18f, 0.95f);
        Button button = buttonObject.GetComponent<Button>();
        button.onClick.AddListener(action);

        TextMeshProUGUI labelText = this.CreateLabel("Label", rect, 18f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        RectTransform labelRect = labelText.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(6f, 4f);
        labelRect.offsetMax = new Vector2(-6f, -4f);
        labelText.text = label;
        return button;
    }

    private TextMeshProUGUI CreateLabel(string objectName, RectTransform parent, float fontSize, FontStyles fontStyle, Color color, TextAlignmentOptions alignment)
    {
        GameObject labelObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rect = labelObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.fontSize = fontSize;
        label.fontStyle = fontStyle;
        label.color = color;
        label.alignment = alignment;
        label.enableWordWrapping = false;
        return label;
    }

    private void SetVisible(bool visible)
    {
        if (panelRoot != null)
        {
            panelRoot.gameObject.SetActive(visible);
        }
    }

    private bool ShouldKeepChestOpen()
    {
        if (activeChest == null || panelRoot == null || !panelRoot.gameObject.activeSelf)
        {
            return true;
        }

        if (playerInventory == null)
        {
            playerInventory = GetComponent<PlayerInventory>();
        }

        if (playerInventory == null)
        {
            return false;
        }

        float maxDistance = activeChest.InteractDistance + Mathf.Max(0.05f, autoCloseDistancePadding);
        return Vector3.Distance(playerInventory.transform.position, activeChest.transform.position) <= maxDistance;
    }

    private void UnbindChest()
    {
        if (activeChest != null)
        {
            activeChest.ChestChanged -= this.Refresh;
            activeChest = null;
        }
    }
}
