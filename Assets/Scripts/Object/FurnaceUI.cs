using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class FurnaceUI : MonoBehaviour
{
    [Header("Furnace")]
    [SerializeField] private FusionFurnace furnace;

    [Header("Layout")]
    [SerializeField] private Vector2 panelSize = new Vector2(700f, 440f);
    [SerializeField] private float buttonHeight = 52f;

    private PlayerInventory playerInventory;
    private Canvas furnaceCanvas;
    private GameObject panelObject;

    private TextMeshProUGUI fuelTimerText;
    private TextMeshProUGUI[] inputTimerTexts = new TextMeshProUGUI[4];
    private Image[] outputIcons = new Image[4];
    private TextMeshProUGUI inventoryLabel;
    private TextMeshProUGUI[] inventorySlotTexts = new TextMeshProUGUI[24];

    private Button igniteButton;
    private TextMeshProUGUI igniteButtonText;
    private Button closeButton;

    public void Open(PlayerInventory inventory, FusionFurnace targetFurnace)
    {
        playerInventory = inventory;
        furnace = targetFurnace;
        EnsureUI();
        panelObject.SetActive(true);
        RefreshUI();
    }

    public void Close()
    {
        if (panelObject != null)
        {
            panelObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (furnace == null || !panelObject.activeSelf) return;
        RefreshUI();

        float distance = Vector3.Distance(furnace.transform.position, Camera.main.transform.position);
        if (distance > 4f)
        {
            Close();
        }
    }

    private void EnsureUI()
    {
        if (panelObject != null) return;

        Canvas existingCanvas = FindFirstObjectByType<Canvas>();
        furnaceCanvas = existingCanvas != null ? existingCanvas : CreateCanvas();

        panelObject = new GameObject("FurnacePanel", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(furnaceCanvas.transform, false);
        RectTransform pr = panelObject.GetComponent<RectTransform>();
        pr.anchorMin = new Vector2(0.5f, 0.5f);
        pr.anchorMax = new Vector2(0.5f, 0.5f);
        pr.pivot = new Vector2(0.5f, 0.5f);
        pr.sizeDelta = panelSize;
        pr.anchoredPosition = Vector2.zero;
        panelObject.GetComponent<Image>().color = new Color(0.06f, 0.06f, 0.06f, 0.93f);

        float leftX = -panelSize.x * 0.25f;
        float rightX = panelSize.x * 0.25f;

        float inventoryY = 20f;
        CreateLabel(panelObject.transform, "INVENTORY", new Vector2(leftX, panelSize.y * 0.38f), 16);
        BuildInventorySlots(leftX, inventoryY);

        CreateLabel(panelObject.transform, "FURNACE", new Vector2(rightX, panelSize.y * 0.38f), 16);

        CreateLabel(panelObject.transform, "Fuel", new Vector2(rightX, 130f), 13);
        fuelTimerText = CreateLabel(panelObject.transform, "0s", new Vector2(rightX, 105f), 14);

        for (int i = 0; i < 4; i++)
        {
            float x = rightX - 90f + i * 60f;
            inputTimerTexts[i] = CreateLabel(panelObject.transform, "-", new Vector2(x, 60f), 14);
        }

        CreateLabel(panelObject.transform, "Input   Output", new Vector2(rightX, 40f), 12);

        for (int i = 0; i < 4; i++)
        {
            float x = rightX - 90f + i * 60f;
            outputIcons[i] = CreateSlot(panelObject.transform, new Vector2(x, 0f), new Color(0.5f, 0.4f, 0.25f));
        }

        CreateIgniteButton();

        closeButton = CreateButton(panelObject.transform, "CLOSE", new Vector2(0f, -panelSize.y * 0.42f), 160f, 40f, Close);

        panelObject.SetActive(false);
    }

    private void CreateIgniteButton()
    {
        igniteButton = CreateButton(panelObject.transform, "IGNITE", Vector2.zero, 200f, buttonHeight, ToggleIgnite);
        igniteButtonText = igniteButton.GetComponentInChildren<TextMeshProUGUI>();
    }

    private void BuildInventorySlots(float leftX, float startY)
    {
        if (playerInventory == null) return;

        int columns = 4;
        int totalSlots = playerInventory.InventorySlotCount;

        for (int i = 0; i < totalSlots && i < inventorySlotTexts.Length; i++)
        {
            int row = i / columns;
            int col = i % columns;
            float x = leftX - 90f + col * 64f;
            float y = startY - row * 64f;
            inventorySlotTexts[i] = CreateLabel(panelObject.transform, "", new Vector2(x, y), 10);
        }
    }

    private void RefreshUI()
    {
        if (furnace == null) return;

        if (fuelTimerText != null)
        {
            fuelTimerText.text = furnace.HasFuel ? Mathf.CeilToInt(furnace.FuelTimerValue) + "s" : "Empty";
            fuelTimerText.color = furnace.HasFuel ? Color.white : Color.gray;
        }

        if (igniteButtonText != null)
        {
            igniteButtonText.text = furnace.IsLitValue ? "STOP" : "IGNITE";
            igniteButton.image.color = furnace.IsLitValue ? new Color(0.85f, 0.25f, 0.15f, 0.9f) : new Color(0.95f, 0.55f, 0.1f, 0.9f);
        }

        for (int i = 0; i < 4; i++)
        {
            if (inputTimerTexts[i] != null)
            {
                inputTimerTexts[i].text = furnace.HasOutput(i) ? "Ready" : (furnace.GetSlotTimer(i) > 0f ? Mathf.CeilToInt(furnace.GetSlotTimer(i)) + "s" : "-");
            }

            if (outputIcons[i] != null)
            {
            outputIcons[i].color = furnace.HasOutput(i) ? new Color(0.85f, 0.85f, 0.9f) : new Color(0.3f, 0.25f, 0.2f);
        }

        if (playerInventory != null)
        {
            for (int i = 0; i < playerInventory.InventorySlotCount && i < inventorySlotTexts.Length; i++)
            {
                if (inventorySlotTexts[i] != null)
                {
                    ItemType? itemType = playerInventory.GetSlotItemType(i);
                    int amount = playerInventory.GetSlotAmount(i);
                    inventorySlotTexts[i].text = itemType != null && amount > 0
                        ? itemType.Value.ToString() + "\n" + amount
                        : "";
                }
            }
        }
    }
    }

    private void ToggleIgnite()
    {
        if (furnace != null)
        {
            furnace.ToggleLit();
        }
    }

    private Button CreateButton(Transform parent, string label, Vector2 pos, float w, float h, UnityEngine.Events.UnityAction action)
    {
        GameObject go = new GameObject(label + "Btn", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = pos;
        Button b = go.GetComponent<Button>();
        b.image.color = new Color(0.22f, 0.24f, 0.26f, 0.96f);
        b.onClick.AddListener(action);
        TextMeshProUGUI txt = CreateLabel(go.transform, label, Vector2.zero, 18);
        txt.alignment = TextAlignmentOptions.Center;
        return b;
    }

    private TextMeshProUGUI CreateLabel(Transform parent, string text, Vector2 pos, int fontSize)
    {
        GameObject go = new GameObject("Label_" + text, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(200f, 28f);
        rt.anchoredPosition = pos;
        TextMeshProUGUI txt = go.GetComponent<TextMeshProUGUI>();
        txt.text = text; txt.fontSize = fontSize; txt.color = Color.white;
        txt.alignment = TextAlignmentOptions.Center;
        return txt;
    }

    private Image CreateSlot(Transform parent, Vector2 pos, Color color)
    {
        GameObject go = new GameObject("Slot", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(50f, 50f);
        rt.anchoredPosition = pos;
        Image img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    private Canvas CreateCanvas()
    {
        GameObject co = new GameObject("FurnaceCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas c = co.GetComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        co.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        co.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1080f, 1920f);
        return c;
    }
}
