using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class FurnaceUI : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private Vector2 panelSize = new Vector2(760f, 560f);

    private FusionFurnace furnace;
    private PlayerInventory playerInventory;
    private Canvas furnaceCanvas;
    private GameObject panelObject;

    private TextMeshProUGUI fuelTimerText;
    private FurnaceSlotUI fuelSlot;
    private FurnaceSlotUI[] inputSlots = new FurnaceSlotUI[4];
    private FurnaceSlotUI[] outputSlots = new FurnaceSlotUI[4];
    private FurnaceSlotUI[] inventorySlots = new FurnaceSlotUI[24];

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
        if (panelObject != null) panelObject.SetActive(false);
    }

    private void Update()
    {
        if (furnace == null || !panelObject.activeSelf) return;
        RefreshUI();
        if (Camera.main != null && Vector3.Distance(furnace.transform.position, Camera.main.transform.position) > 4f)
            Close();
    }

    private void EnsureUI()
    {
        if (panelObject != null) return;

        Canvas existingCanvas = FindFirstObjectByType<Canvas>();
        furnaceCanvas = existingCanvas != null ? existingCanvas : CreateCanvas();

        panelObject = new GameObject("FurnacePanel", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(furnaceCanvas.transform, false);
        RectTransform pr = panelObject.GetComponent<RectTransform>();
        pr.anchorMin = new Vector2(0.5f, 0.5f); pr.anchorMax = new Vector2(0.5f, 0.5f);
        pr.pivot = new Vector2(0.5f, 0.5f); pr.sizeDelta = panelSize; pr.anchoredPosition = Vector2.zero;
        panelObject.GetComponent<Image>().color = new Color(0.06f, 0.06f, 0.06f, 0.93f);

        float left = -panelSize.x * 0.28f;
        float right = panelSize.x * 0.28f;

        MakeLabel(panelObject.transform, "INVENTORY", new Vector2(left, 190f), 14);
        int cols = 4;
        int invSlotCount = playerInventory != null ? playerInventory.InventorySlotCount : 12;
        for (int i = 0; i < invSlotCount && i < inventorySlots.Length; i++)
        {
            float x = left - 78f + (i % cols) * 56f;
            float y = 165f - (i / cols) * 56f;
            inventorySlots[i] = CreateSlot("InvSlot" + i, FurnaceSlotUI.SlotKind.Inventory, i, new Vector2(x, y));
        }

        MakeLabel(panelObject.transform, "HOTBAR", new Vector2(left, 165f - Mathf.Ceil(invSlotCount / (float)cols) * 56f - 8f), 12);
        int hotbarStart = playerInventory != null ? playerInventory.HotbarStartIndex : 12;
        int hotbarEnd = playerInventory != null ? playerInventory.TotalSlotCount : 17;
        for (int i = hotbarStart; i < hotbarEnd && i < inventorySlots.Length; i++)
        {
            int hotbarIdx = i - hotbarStart;
            float x = left - 78f + (hotbarIdx % 5) * 50f;
            float y = 165f - Mathf.Ceil(invSlotCount / (float)cols) * 56f - 40f;
            inventorySlots[i] = CreateSlot("HotbarSlot" + hotbarIdx, FurnaceSlotUI.SlotKind.Inventory, i, new Vector2(x, y));
        }

        MakeLabel(panelObject.transform, "FURNACE", new Vector2(right, 165f), 14);

        MakeLabel(panelObject.transform, "FUEL", new Vector2(right, 130f), 12);
        fuelSlot = CreateSlot("FuelSlot", FurnaceSlotUI.SlotKind.FurnaceFuel, 0, new Vector2(right, 100f));
        fuelTimerText = MakeLabel(panelObject.transform, "0s", new Vector2(right, 75f), 13);

        MakeLabel(panelObject.transform, "INPUT", new Vector2(right, 50f), 12);
        for (int i = 0; i < 4; i++)
        {
            float x = right - 84f + i * 56f;
            inputSlots[i] = CreateSlot("InputSlot" + i, FurnaceSlotUI.SlotKind.FurnaceInput, i, new Vector2(x, 12f));
        }

        MakeLabel(panelObject.transform, "OUTPUT", new Vector2(right, -60f), 12);
        for (int i = 0; i < 4; i++)
        {
            float x = right - 84f + i * 56f;
            outputSlots[i] = CreateSlot("OutputSlot" + i, FurnaceSlotUI.SlotKind.FurnaceOutput, i, new Vector2(x, -98f));
        }

        igniteButton = CreateButton("IGNITE", Vector2.zero, 200f, 48f, ToggleIgnite);
        igniteButtonText = igniteButton.GetComponentInChildren<TextMeshProUGUI>();
        closeButton = CreateButton("CLOSE", new Vector2(0f, -panelSize.y * 0.46f), 160f, 36f, Close);

        panelObject.SetActive(false);
    }

    private FurnaceSlotUI CreateSlot(string name, FurnaceSlotUI.SlotKind kind, int index, Vector2 pos)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(FurnaceSlotUI), typeof(CanvasGroup));
        go.transform.SetParent(panelObject.transform, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(50f, 50f); rt.anchoredPosition = pos;
        go.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.16f, 0.95f);

        FurnaceSlotUI slot = go.GetComponent<FurnaceSlotUI>();
        slot.Setup(kind, index, this);

        GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        RectTransform lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.sizeDelta = Vector2.zero; lrt.anchoredPosition = Vector2.zero;
        TextMeshProUGUI txt = labelGo.GetComponent<TextMeshProUGUI>();
        txt.fontSize = 9; txt.color = Color.white; txt.alignment = TextAlignmentOptions.Center;
        txt.raycastTarget = false;

        return slot;
    }

    private void RefreshUI()
    {
        if (furnace == null) return;

        if (fuelTimerText != null)
        {
            float fuel = furnace.FuelTimerValue;
            fuelTimerText.text = furnace.HasFuel ? Mathf.CeilToInt(fuel) + "s" : "Empty";
            fuelTimerText.color = furnace.HasFuel ? Color.white : Color.gray;
        }

        if (fuelSlot != null)
        {
            int fuelAmount = furnace.FuelStackAmount;
            string fuelLabel = fuelAmount > 0 ? "Wood\n" + fuelAmount : "Wood";
            fuelSlot.UpdateVisual(furnace.HasFuel ? ItemType.Wood : null, fuelAmount, fuelLabel);
        }

        for (int i = 0; i < 4; i++)
        {
            if (inputSlots[i] != null)
            {
                float t = furnace.GetSlotTimer(i);
                int inputType = furnace.GetSlotInputType(i);
                int qty = furnace.GetSlotQuantity(i);
                ItemType? visualType = inputType >= 0 ? (ItemType)inputType : (ItemType?)null;
                string label = t > 0f ? Mathf.CeilToInt(t) + "s" : "";
                if (qty > 0) label += "\n" + qty;
                inputSlots[i].UpdateVisual(t >= 0f || qty > 0 ? visualType : null, qty, label);
            }

            if (outputSlots[i] != null)
            {
                int outputTypeValue = furnace.GetOutputType(i);
                int outCount = furnace.GetOutputCount(i);
                ItemType? outputType = outputTypeValue >= 0 ? (ItemType)outputTypeValue : (ItemType?)null;
                outputSlots[i].UpdateVisual(furnace.HasOutput(i) ? outputType : null, outCount, outCount > 0 ? "READY\n" + outCount : "");
            }
        }

        if (igniteButtonText != null)
        {
            igniteButtonText.text = furnace.IsLitValue ? "STOP" : "IGNITE";
        }

        if (playerInventory != null)
        {
            for (int i = 0; i < playerInventory.TotalSlotCount && i < inventorySlots.Length; i++)
            {
                if (inventorySlots[i] != null)
                {
                    ItemType? it = playerInventory.GetSlotItemType(i);
                    int amt = playerInventory.GetSlotAmount(i);
                    inventorySlots[i].UpdateVisual(it, amt, it != null && amt > 0 ? it.Value.ToString() + "\n" + amt : "");
                }
            }
        }
    }

    public bool HasValidItem(FurnaceSlotUI.SlotKind kind, int index)
    {
        if (playerInventory == null) return false;

        switch (kind)
        {
            case FurnaceSlotUI.SlotKind.Inventory:
                ItemType? it = playerInventory.GetSlotItemType(index);
                int amt = playerInventory.GetSlotAmount(index);
                return it != null && amt > 0;
            case FurnaceSlotUI.SlotKind.FurnaceOutput:
                return furnace != null && furnace.HasOutput(index);
            default:
                return false;
        }
    }

    public void HandleSlotDrop(FurnaceSlotUI.SlotKind fromKind, int fromIndex, FurnaceSlotUI.SlotKind toKind, int toIndex)
    {
        if (playerInventory == null || furnace == null) return;
        if (fromKind != FurnaceSlotUI.SlotKind.Inventory && fromKind != FurnaceSlotUI.SlotKind.FurnaceOutput) return;

        if (fromKind == FurnaceSlotUI.SlotKind.Inventory)
        {
            ItemType? itemType = playerInventory.GetSlotItemType(fromIndex);
            if (itemType == null) return;

            if (itemType == ItemType.Wood && toKind == FurnaceSlotUI.SlotKind.FurnaceFuel)
                furnace.TryAddToFurnaceFromSlot(playerInventory, fromIndex, true, -1);
            else if ((itemType == ItemType.Iron || itemType == ItemType.RawChicken || itemType == ItemType.RawFish || itemType == ItemType.Wood) && toKind == FurnaceSlotUI.SlotKind.FurnaceInput)
                furnace.TryAddToFurnaceFromSlot(playerInventory, fromIndex, false, -1);
        }
        else if (fromKind == FurnaceSlotUI.SlotKind.FurnaceOutput)
        {
            furnace.TryPickupOutput(playerInventory, fromIndex);
        }
    }

    private void ToggleIgnite() { furnace?.ToggleLit(); }

    private Button CreateButton(string label, Vector2 pos, float w, float h, UnityEngine.Events.UnityAction action)
    {
        GameObject go = new GameObject(label + "Btn", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(panelObject.transform, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(w, h); rt.anchoredPosition = pos;
        Button b = go.GetComponent<Button>(); b.targetGraphic = go.GetComponent<Image>();
        go.GetComponent<Image>().color = new Color(0.22f, 0.24f, 0.26f, 0.96f);
        b.onClick.AddListener(action);
        TextMeshProUGUI txt = MakeLabel(go.transform, label, Vector2.zero, 16);
        txt.alignment = TextAlignmentOptions.Center;
        return b;
    }

    private TextMeshProUGUI MakeLabel(Transform parent, string text, Vector2 pos, int fontSize)
    {
        GameObject go = new GameObject("Lbl", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(180f, 24f); rt.anchoredPosition = pos;
        TextMeshProUGUI txt = go.GetComponent<TextMeshProUGUI>();
        txt.text = text; txt.fontSize = fontSize; txt.color = Color.white; txt.alignment = TextAlignmentOptions.Center;
        txt.raycastTarget = false;
        return txt;
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
