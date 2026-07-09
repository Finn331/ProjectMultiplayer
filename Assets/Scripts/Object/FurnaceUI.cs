using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class FurnaceUI : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private Vector2 panelSize = new Vector2(780f, 600f);

    private FusionFurnace furnace;
    private PlayerInventory playerInventory;
    private Canvas furnaceCanvas;
    private GameObject panelObject;

    private FurnaceSlotUI fuelSlot;
    private FurnaceSlotUI[] inputSlots = new FurnaceSlotUI[4];
    private FurnaceSlotUI[] outputSlots = new FurnaceSlotUI[4];
    private FurnaceSlotUI[] inventorySlots = new FurnaceSlotUI[24];

    private Image fuelBarFill;
    private Image[] cookBarFills = new Image[4];

    private Button igniteButton;
    private TextMeshProUGUI igniteButtonText;
    private Button closeButton;

    private GameObject splitDialog;
    private TextMeshProUGUI splitTitleText;
    private UnityEngine.UI.Slider splitSlider;
    private TextMeshProUGUI splitValueText;
    private Button splitConfirmButton;
    private Button splitCancelButton;

    private FurnaceSlotUI pendingSplitSource;
    private FurnaceSlotUI.SlotKind pendingSplitTargetKind;
    private int pendingSplitTargetIndex;

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
        if (splitDialog != null) splitDialog.SetActive(false);
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

        MakeLabel(panelObject.transform, "INVENTORY", new Vector2(left, 210f), 14);
        int cols = 4;
        int invSlotCount = playerInventory != null ? playerInventory.InventorySlotCount : 12;
        for (int i = 0; i < invSlotCount && i < inventorySlots.Length; i++)
        {
            float x = left - 78f + (i % cols) * 56f;
            float y = 185f - (i / cols) * 56f;
            inventorySlots[i] = CreateSlot("InvSlot" + i, FurnaceSlotUI.SlotKind.Inventory, i, new Vector2(x, y));
        }

        MakeLabel(panelObject.transform, "HOTBAR", new Vector2(left, 185f - Mathf.Ceil(invSlotCount / (float)cols) * 56f - 8f), 12);
        int hotbarStart = playerInventory != null ? playerInventory.HotbarStartIndex : 12;
        int hotbarEnd = playerInventory != null ? playerInventory.TotalSlotCount : 17;
        for (int i = hotbarStart; i < hotbarEnd && i < inventorySlots.Length; i++)
        {
            int hotbarIdx = i - hotbarStart;
            float x = left - 78f + (hotbarIdx % 5) * 50f;
            float y = 185f - Mathf.Ceil(invSlotCount / (float)cols) * 56f - 40f;
            inventorySlots[i] = CreateSlot("HotbarSlot" + hotbarIdx, FurnaceSlotUI.SlotKind.Inventory, i, new Vector2(x, y));
        }

        MakeLabel(panelObject.transform, "FURNACE", new Vector2(right, 210f), 14);

        MakeLabel(panelObject.transform, "FUEL", new Vector2(right, 170f), 12);
        fuelSlot = CreateSlot("FuelSlot", FurnaceSlotUI.SlotKind.FurnaceFuel, 0, new Vector2(right, 140f));

        GameObject fuelBarGo = new GameObject("FuelBar", typeof(RectTransform), typeof(Image));
        fuelBarGo.transform.SetParent(panelObject.transform, false);
        RectTransform fbRect = fuelBarGo.GetComponent<RectTransform>();
        fbRect.anchorMin = new Vector2(0.5f, 0.5f); fbRect.anchorMax = new Vector2(0.5f, 0.5f);
        fbRect.pivot = new Vector2(0.5f, 0.5f); fbRect.sizeDelta = new Vector2(120f, 6f);
        fbRect.anchoredPosition = new Vector2(right, 110f);
        fuelBarGo.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 0.9f);

        GameObject fuelFillGo = new GameObject("FuelBarFill", typeof(RectTransform), typeof(Image));
        fuelFillGo.transform.SetParent(fuelBarGo.transform, false);
        RectTransform ffRect = fuelFillGo.GetComponent<RectTransform>();
        ffRect.anchorMin = Vector2.zero; ffRect.anchorMax = Vector2.one;
        ffRect.pivot = new Vector2(0f, 0.5f); ffRect.sizeDelta = Vector2.zero; ffRect.anchoredPosition = Vector2.zero;
        fuelBarFill = fuelFillGo.GetComponent<Image>();
        fuelBarFill.color = new Color(0.9f, 0.5f, 0.1f, 0.9f);
        fuelBarFill.raycastTarget = false;

        MakeLabel(panelObject.transform, "INPUT", new Vector2(right, 85f), 12);
        for (int i = 0; i < 4; i++)
        {
            float x = right - 84f + i * 56f;
            inputSlots[i] = CreateSlot("InputSlot" + i, FurnaceSlotUI.SlotKind.FurnaceInput, i, new Vector2(x, 55f));

            GameObject cookBarGo = new GameObject("CookBar" + i, typeof(RectTransform), typeof(Image));
            cookBarGo.transform.SetParent(panelObject.transform, false);
            RectTransform cbRect = cookBarGo.GetComponent<RectTransform>();
            cbRect.anchorMin = new Vector2(0.5f, 0.5f); cbRect.anchorMax = new Vector2(0.5f, 0.5f);
            cbRect.pivot = new Vector2(0.5f, 0.5f); cbRect.sizeDelta = new Vector2(48f, 4f);
            cbRect.anchoredPosition = new Vector2(x, 30f);
            cookBarGo.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 0.9f);

            GameObject cookFillGo = new GameObject("CookBarFill" + i, typeof(RectTransform), typeof(Image));
            cookFillGo.transform.SetParent(cookBarGo.transform, false);
            RectTransform cfRect = cookFillGo.GetComponent<RectTransform>();
            cfRect.anchorMin = Vector2.zero; cfRect.anchorMax = Vector2.one;
            cfRect.pivot = new Vector2(0f, 0.5f); cfRect.sizeDelta = Vector2.zero; cfRect.anchoredPosition = Vector2.zero;
            cookBarFills[i] = cookFillGo.GetComponent<Image>();
            cookBarFills[i].color = new Color(0.2f, 0.8f, 0.3f, 0.9f);
            cookBarFills[i].raycastTarget = false;
        }

        MakeLabel(panelObject.transform, "OUTPUT", new Vector2(right, 0f), 12);
        for (int i = 0; i < 4; i++)
        {
            float x = right - 84f + i * 56f;
            outputSlots[i] = CreateSlot("OutputSlot" + i, FurnaceSlotUI.SlotKind.FurnaceOutput, i, new Vector2(x, -30f));
        }

        igniteButton = CreateButton("IGNITE", new Vector2(right, -80f), 160f, 44f, ToggleIgnite);
        igniteButtonText = igniteButton.GetComponentInChildren<TextMeshProUGUI>();
        closeButton = CreateButton("CLOSE", new Vector2(0f, -panelSize.y * 0.46f), 160f, 36f, Close);

        CreateSplitDialog();

        panelObject.SetActive(false);
    }

    private void CreateSplitDialog()
    {
        splitDialog = new GameObject("SplitDialog", typeof(RectTransform), typeof(Image));
        splitDialog.transform.SetParent(panelObject.transform, false);
        RectTransform sdRect = splitDialog.GetComponent<RectTransform>();
        sdRect.anchorMin = new Vector2(0.5f, 0.5f); sdRect.anchorMax = new Vector2(0.5f, 0.5f);
        sdRect.pivot = new Vector2(0.5f, 0.5f); sdRect.sizeDelta = new Vector2(300f, 160f);
        sdRect.anchoredPosition = Vector2.zero;
        splitDialog.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.97f);

        splitTitleText = MakeLabel(splitDialog.transform, "Transfer Amount", new Vector2(0f, 55f), 16);
        splitSlider = CreateSlider(splitDialog.transform, new Vector2(0f, 15f));
        splitValueText = MakeLabel(splitDialog.transform, "1", new Vector2(0f, -15f), 18);
        splitConfirmButton = CreateButton("OK", new Vector2(-50f, -55f), 80f, 32f, ConfirmSplit);
        splitCancelButton = CreateButton("Cancel", new Vector2(50f, -55f), 80f, 32f, CancelSplit);

        splitDialog.SetActive(false);
    }

    private UnityEngine.UI.Slider CreateSlider(Transform parent, Vector2 pos)
    {
        GameObject go = new GameObject("Slider", typeof(RectTransform), typeof(UnityEngine.UI.Slider));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(240f, 24f); rt.anchoredPosition = pos;

        GameObject bg = new GameObject("BG", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(go.transform, false);
        RectTransform bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one; bgRt.sizeDelta = Vector2.zero; bgRt.anchoredPosition = Vector2.zero;
        bg.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 0.9f);

        GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(bg.transform, false);
        RectTransform fillRt = fill.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one; fillRt.sizeDelta = Vector2.zero; fillRt.anchoredPosition = Vector2.zero;
        fill.GetComponent<Image>().color = new Color(0.3f, 0.6f, 0.9f, 0.9f);

        GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(go.transform, false);
        RectTransform handleRt = handle.GetComponent<RectTransform>();
        handleRt.anchorMin = new Vector2(0.5f, 0.5f); handleRt.anchorMax = new Vector2(0.5f, 0.5f);
        handleRt.pivot = new Vector2(0.5f, 0.5f); handleRt.sizeDelta = new Vector2(20f, 20f);
        handle.GetComponent<Image>().color = Color.white;

        UnityEngine.UI.Slider slider = go.GetComponent<UnityEngine.UI.Slider>();
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.handleRect = handleRt;
        slider.minValue = 1; slider.maxValue = 1; slider.value = 1;
        slider.onValueChanged.AddListener((v) => { if (splitValueText != null) splitValueText.text = Mathf.RoundToInt(v).ToString(); });

        return slider;
    }

    private void ShowSplitDialog(FurnaceSlotUI source, FurnaceSlotUI.SlotKind targetKind, int targetIndex, int maxAmount)
    {
        if (maxAmount <= 1)
        {
            HandleTransfer(source, targetKind, targetIndex, maxAmount);
            return;
        }

        pendingSplitSource = source;
        pendingSplitTargetKind = targetKind;
        pendingSplitTargetIndex = targetIndex;

        splitSlider.maxValue = maxAmount;
        splitSlider.value = maxAmount;
        splitValueText.text = maxAmount.ToString();
        splitDialog.SetActive(true);
    }

    private void ConfirmSplit()
    {
        if (pendingSplitSource != null)
        {
            int amount = Mathf.RoundToInt(splitSlider.value);
            HandleTransfer(pendingSplitSource, pendingSplitTargetKind, pendingSplitTargetIndex, amount);
        }
        splitDialog.SetActive(false);
        pendingSplitSource = null;
    }

    private void CancelSplit()
    {
        splitDialog.SetActive(false);
        pendingSplitSource = null;
    }

    private void HandleTransfer(FurnaceSlotUI source, FurnaceSlotUI.SlotKind targetKind, int targetIndex, int amount)
    {
        if (playerInventory == null || furnace == null) return;
        if (source.Kind == FurnaceSlotUI.SlotKind.Inventory)
        {
            ItemType? itemType = playerInventory.GetSlotItemType(source.SlotIndex);
            if (itemType == null) return;

            if (targetKind == FurnaceSlotUI.SlotKind.FurnaceFuel && itemType == ItemType.Wood)
                furnace.TryAddToFurnaceFromSlot(playerInventory, source.SlotIndex, true, -1);
            else if (targetKind == FurnaceSlotUI.SlotKind.FurnaceInput && IsValidInput(itemType))
                furnace.TryAddToFurnaceFromSlot(playerInventory, source.SlotIndex, false, -1);
        }
        else if (source.Kind == FurnaceSlotUI.SlotKind.FurnaceOutput)
        {
            furnace.TryPickupOutput(playerInventory, source.SlotIndex);
        }
        else if (source.Kind == FurnaceSlotUI.SlotKind.FurnaceInput)
        {
            furnace.TryPickupInput(playerInventory, source.SlotIndex);
        }
        else if (source.Kind == FurnaceSlotUI.SlotKind.FurnaceFuel)
        {
            furnace.TryPickupFuel(playerInventory);
        }
    }

    private static bool IsValidInput(ItemType? itemType)
    {
        return itemType == ItemType.Iron || itemType == ItemType.RawChicken
            || itemType == ItemType.RawFish || itemType == ItemType.Wood;
    }

    private void RefreshUI()
    {
        if (furnace == null) return;

        if (fuelBarFill != null)
        {
            float burnPct = furnace.FuelTimerValue > 0f ? Mathf.Clamp01(furnace.FuelTimerValue / 30f) : 0f;
            fuelBarFill.fillAmount = burnPct;
        }

        if (fuelSlot != null)
        {
            int fuelAmt = furnace.FuelStackAmount;
            fuelSlot.UpdateVisual(fuelAmt > 0 ? ItemType.Wood : null, fuelAmt, fuelAmt > 0 ? "Wood\n" + fuelAmt : "Wood");
        }

        for (int i = 0; i < 4; i++)
        {
            if (inputSlots[i] != null)
            {
                int inputType = furnace.GetSlotInputType(i);
                int qty = furnace.GetSlotQuantity(i);
                float t = furnace.GetSlotTimer(i);
                ItemType? visualType = inputType >= 0 ? (ItemType)inputType : (ItemType?)null;
                string label = t > 0f ? Mathf.CeilToInt(t) + "s" : "";
                if (qty > 0) label += "\n" + qty;
                inputSlots[i].UpdateVisual(t >= 0f || qty > 0 ? visualType : null, qty, label);
            }

            if (cookBarFills[i] != null)
            {
                float t = furnace.GetSlotTimer(i);
                int inputType = furnace.GetSlotInputType(i);
                float cookTime = inputType >= 0 ? GetCookTimeForDisplay(inputType) : 1f;
                cookBarFills[i].fillAmount = t > 0f ? Mathf.Clamp01(t / cookTime) : 0f;
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

    private static float GetCookTimeForDisplay(int inputType)
    {
        ItemType itemType = (ItemType)inputType;
        if (itemType == ItemType.Iron) return 10f;
        if (itemType == ItemType.Wood) return 8f;
        return 8f;
    }

    public bool HasValidItem(FurnaceSlotUI.SlotKind kind, int index)
    {
        if (playerInventory == null || furnace == null) return false;

        switch (kind)
        {
            case FurnaceSlotUI.SlotKind.Inventory:
                ItemType? it = playerInventory.GetSlotItemType(index);
                int amt = playerInventory.GetSlotAmount(index);
                return it != null && amt > 0;
            case FurnaceSlotUI.SlotKind.FurnaceFuel:
                return furnace.FuelStackAmount > 0;
            case FurnaceSlotUI.SlotKind.FurnaceInput:
                return furnace.GetSlotQuantity(index) > 0;
            case FurnaceSlotUI.SlotKind.FurnaceOutput:
                return furnace.HasOutput(index);
            default:
                return false;
        }
    }

    public void HandleSlotDrop(FurnaceSlotUI.SlotKind fromKind, int fromIndex, FurnaceSlotUI.SlotKind toKind, int toIndex)
    {
        if (playerInventory == null || furnace == null) return;
        if (splitDialog != null && splitDialog.activeSelf) return;

        FurnaceSlotUI source = null;
        if (fromKind == FurnaceSlotUI.SlotKind.Inventory && fromIndex < inventorySlots.Length) source = inventorySlots[fromIndex];
        else if (fromKind == FurnaceSlotUI.SlotKind.FurnaceFuel) source = fuelSlot;
        else if (fromKind == FurnaceSlotUI.SlotKind.FurnaceInput && fromIndex < inputSlots.Length) source = inputSlots[fromIndex];
        else if (fromKind == FurnaceSlotUI.SlotKind.FurnaceOutput && fromIndex < outputSlots.Length) source = outputSlots[fromIndex];

        if (source == null) return;

        int maxAmount = 1;
        if (fromKind == FurnaceSlotUI.SlotKind.Inventory)
            maxAmount = playerInventory.GetSlotAmount(fromIndex);
        else if (fromKind == FurnaceSlotUI.SlotKind.FurnaceFuel)
            maxAmount = furnace.FuelStackAmount;
        else if (fromKind == FurnaceSlotUI.SlotKind.FurnaceInput)
            maxAmount = furnace.GetSlotQuantity(fromIndex);
        else if (fromKind == FurnaceSlotUI.SlotKind.FurnaceOutput)
            maxAmount = furnace.GetOutputCount(fromIndex);

        if (toKind == FurnaceSlotUI.SlotKind.Inventory)
        {
            if (fromKind == FurnaceSlotUI.SlotKind.FurnaceOutput)
                furnace.TryPickupOutput(playerInventory, fromIndex);
            else if (fromKind == FurnaceSlotUI.SlotKind.FurnaceInput)
                furnace.TryPickupInput(playerInventory, fromIndex);
            else if (fromKind == FurnaceSlotUI.SlotKind.FurnaceFuel)
                furnace.TryPickupFuel(playerInventory);
            return;
        }

        ShowSplitDialog(source, toKind, toIndex, maxAmount);
    }

    private void ToggleIgnite() { furnace?.ToggleLit(); }

    private FurnaceSlotUI CreateSlot(string name, FurnaceSlotUI.SlotKind kind, int index, Vector2 pos)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(FurnaceSlotUI), typeof(CanvasGroup));
        go.transform.SetParent(panelObject.transform, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(50f, 50f); rt.anchoredPosition = pos;
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
