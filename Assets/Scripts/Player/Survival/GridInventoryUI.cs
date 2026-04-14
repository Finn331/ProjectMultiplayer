using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GridInventoryUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private ItemIconDatabase iconDatabase;

    [Header("Grid Settings")]
    [SerializeField] private int columns = 4;
    [SerializeField] private int rows = 4;
    [SerializeField] private float slotSize = 64f;
    [SerializeField] private float slotSpacing = 8f;
    [SerializeField] private float padding = 16f;

    [Header("Position")]
    [SerializeField] private Vector2 anchoredPosition = new Vector2(-180f, -100f);

    [Header("Colors")]
    [SerializeField] private Color slotBackgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);
    [SerializeField] private Color slotBorderColor = new Color(0.4f, 0.4f, 0.4f, 1f);
    [SerializeField] private Color itemAmountColor = Color.white;

    private RectTransform gridPanel;
    private Image[] slotBackgrounds;
    private Image[] slotItemImages;
    private TextMeshProUGUI[] slotAmountTexts;
    private bool initialized;

    private void Start()
    {
        if (inventory != null)
        {
            inventory.InventoryChanged += Refresh;
        }
        EnsureUI();
        Refresh();
    }

    private void OnEnable()
    {
        EnsureUI();
        Refresh();
    }

    private void OnDestroy()
    {
        if (inventory != null)
        {
            inventory.InventoryChanged -= Refresh;
        }
    }

    private void EnsureUI()
    {
        if (initialized) return;

        if (targetCanvas == null)
        {
            targetCanvas = FindObjectOfType<Canvas>();
        }

        if (targetCanvas == null || iconDatabase == null || inventory == null) return;

        CreateGridPanel();
        initialized = true;
    }

    private void CreateGridPanel()
    {
        GameObject panelObj = new GameObject("Inventory Grid Panel", typeof(RectTransform), typeof(Image));
        gridPanel = panelObj.GetComponent<RectTransform>();
        gridPanel.SetParent(targetCanvas.transform, false);

        gridPanel.anchorMin = new Vector2(1f, 0f);
        gridPanel.anchorMax = new Vector2(1f, 0f);
        gridPanel.pivot = new Vector2(1f, 0f);
        gridPanel.anchoredPosition = anchoredPosition;

        float panelWidth = (columns * slotSize) + ((columns - 1) * slotSpacing) + (padding * 2);
        float panelHeight = (rows * slotSize) + ((rows - 1) * slotSpacing) + (padding * 2);
        gridPanel.sizeDelta = new Vector2(panelWidth, panelHeight);

        Image panelImage = panelObj.GetComponent<Image>();
        panelImage.color = new Color(0.1f, 0.1f, 0.15f, 0.9f);

        slotBackgrounds = new Image[columns * rows];
        slotItemImages = new Image[columns * rows];
        slotAmountTexts = new TextMeshProUGUI[columns * rows];

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                int index = row * columns + col;

                GameObject slotObj = new GameObject($"Slot_{row}_{col}", typeof(RectTransform), typeof(Image));
                RectTransform slotRect = slotObj.GetComponent<RectTransform>();
                slotRect.SetParent(gridPanel, false);

                float xPos = padding + (col * (slotSize + slotSpacing)) + (slotSize / 2f);
                float yPos = -(padding + (row * (slotSize + slotSpacing)) + (slotSize / 2f));
                slotRect.anchoredPosition = new Vector2(xPos, yPos);
                slotRect.sizeDelta = new Vector2(slotSize, slotSize);

                Image slotBg = slotObj.GetComponent<Image>();
                slotBg.color = slotBackgroundColor;
                slotBackgrounds[index] = slotBg;

                GameObject itemObj = new GameObject($"ItemIcon_{row}_{col}", typeof(RectTransform), typeof(Image));
                RectTransform itemRect = itemObj.GetComponent<RectTransform>();
                itemRect.SetParent(slotRect, false);
                itemRect.anchorMin = Vector2.zero;
                itemRect.anchorMax = Vector2.one;
                itemRect.offsetMin = new Vector2(4f, 4f);
                itemRect.offsetMax = new Vector2(-4f, -4f);

                Image itemImage = itemObj.GetComponent<Image>();
                itemImage.preserveAspect = true;
                itemImage.raycastTarget = false;
                slotItemImages[index] = itemImage;

                GameObject amountObj = new GameObject($"Amount_{row}_{col}", typeof(RectTransform), typeof(TextMeshProUGUI));
                RectTransform amountRect = amountObj.GetComponent<RectTransform>();
                amountRect.SetParent(slotRect, false);
                amountRect.anchorMin = new Vector2(1f, 0f);
                amountRect.anchorMax = new Vector2(1f, 0f);
                amountRect.pivot = new Vector2(1f, 0f);
                amountRect.anchoredPosition = new Vector2(-2f, 2f);
                amountRect.sizeDelta = new Vector2(30f, 20f);

                TextMeshProUGUI amountText = amountObj.GetComponent<TextMeshProUGUI>();
                amountText.fontSize = 16;
                amountText.fontStyle = FontStyles.Bold;
                amountText.color = itemAmountColor;
                amountText.alignment = TextAlignmentOptions.Right;
                amountText.raycastTarget = false;
                slotAmountTexts[index] = amountText;
            }
        }
    }

    public void Refresh()
    {
        if (!initialized || slotItemImages == null) return;

        for (int i = 0; i < slotItemImages.Length; i++)
        {
            if (slotItemImages[i] == null) continue;

            if (inventory != null && i < inventory.InventorySlotCount)
            {
                ItemType? itemType = inventory.GetSlotItemType(i);
                int amount = inventory.GetSlotAmount(i);
                if (itemType != null && amount > 0)
                {
                    Sprite icon = iconDatabase != null ? iconDatabase.GetIcon(itemType.Value) : null;
                    slotItemImages[i].sprite = icon;
                    slotItemImages[i].enabled = icon != null;

                    if (slotAmountTexts[i] != null)
                    {
                        slotAmountTexts[i].text = amount.ToString();
                    }
                }
                else
                {
                    slotItemImages[i].sprite = null;
                    slotItemImages[i].enabled = false;

                    if (slotAmountTexts[i] != null)
                    {
                        slotAmountTexts[i].text = "";
                    }
                }
            }
            else
            {
                slotItemImages[i].sprite = null;
                slotItemImages[i].enabled = false;

                if (slotAmountTexts[i] != null)
                {
                    slotAmountTexts[i].text = "";
                }
            }
        }
    }
}
