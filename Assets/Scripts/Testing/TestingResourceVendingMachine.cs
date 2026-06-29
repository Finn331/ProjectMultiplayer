using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class TestingResourceVendingMachine : MonoBehaviour
{
    private const int DispenseAmount = 5;

    [SerializeField] private string panelTitle = "Testing Resources";
    [SerializeField] private Vector2 panelSize = new Vector2(360f, 420f);

    private PlayerInventory currentInventory;
    private Canvas vendingCanvas;
    private GameObject panelObject;

    public void OpenForInteractor(PlayerInteractionSystem interactor)
    {
        currentInventory = null;
        Close();

        if (interactor == null)
        {
            ShowInfo("No player selected");
            return;
        }

        currentInventory = interactor.GetComponent<PlayerInventory>();
        if (currentInventory == null)
        {
            ShowInfo("No player inventory found");
            return;
        }

        EnsureUI();
        panelObject.SetActive(true);
    }

    public void Close()
    {
        if (panelObject != null)
        {
            panelObject.SetActive(false);
        }
    }

    private void DispenseWood()
    {
        Dispense(ItemType.Wood, "Wood");
    }

    private void DispenseFiber()
    {
        Dispense(ItemType.Fiber, "Fiber");
    }

    private void DispenseStone()
    {
        Dispense(ItemType.Stone, "Stone");
    }

    private void DispenseCloth()
    {
        Dispense(ItemType.Cloth, "Cloth");
    }

    private void Dispense(ItemType itemType, string label)
    {
        if (currentInventory == null)
        {
            ShowInfo("Open vending first");
            return;
        }

        int accepted = currentInventory.AddItem(itemType, DispenseAmount);
        if (accepted > 0)
        {
            ShowInfo(label + " +" + accepted);
        }

        if (accepted < DispenseAmount)
        {
            ShowInfo("Inventory Full");
        }
    }

    private void EnsureUI()
    {
        if (panelObject != null)
        {
            return;
        }

        vendingCanvas = FindFirstObjectByType<Canvas>();
        if (vendingCanvas == null)
        {
            GameObject canvasObject = new GameObject("Testing Resource Vending Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            vendingCanvas = canvasObject.GetComponent<Canvas>();
            vendingCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
        }

        panelObject = new GameObject("Testing Resource Vending Panel", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(vendingCanvas.transform, false);

        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = panelSize;
        panelRect.anchoredPosition = Vector2.zero;

        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = new Color(0.08f, 0.08f, 0.08f, 0.92f);

        CreateText(panelObject.transform, panelTitle, new Vector2(0f, 160f), 30, TextAnchor.MiddleCenter);
        CreateButton(panelObject.transform, "WOOD x5", new Vector2(0f, 90f), DispenseWood);
        CreateButton(panelObject.transform, "FIBER x5", new Vector2(0f, 30f), DispenseFiber);
        CreateButton(panelObject.transform, "STONE x5", new Vector2(0f, -30f), DispenseStone);
        CreateButton(panelObject.transform, "CLOTH x5", new Vector2(0f, -90f), DispenseCloth);
        CreateButton(panelObject.transform, "CLOSE", new Vector2(0f, -160f), Close);

        panelObject.SetActive(false);
    }

    private static void CreateButton(Transform parent, string label, Vector2 anchoredPosition, UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = new GameObject(label + " Button", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(260f, 48f);
        rect.anchoredPosition = anchoredPosition;

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.22f, 0.24f, 0.26f, 0.96f);

        Button button = buttonObject.GetComponent<Button>();
        button.onClick.AddListener(action);

        CreateText(buttonObject.transform, label, Vector2.zero, 22, TextAnchor.MiddleCenter);
    }

    private static Text CreateText(Transform parent, string text, Vector2 anchoredPosition, int fontSize, TextAnchor alignment)
    {
        GameObject textObject = new GameObject(text + " Text", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(320f, 44f);
        rect.anchoredPosition = anchoredPosition;

        Text uiText = textObject.GetComponent<Text>();
        uiText.text = text;
        uiText.alignment = alignment;
        uiText.fontSize = fontSize;
        uiText.color = Color.white;
        uiText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return uiText;
    }

    private static void ShowInfo(string message)
    {
        if (PickupUIManager.instance != null)
        {
            PickupUIManager.instance.ShowInfo(message);
        }
        else
        {
            Debug.Log(message);
        }
    }
}
