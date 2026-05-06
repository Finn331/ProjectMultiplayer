using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[ExecuteAlways]
public class MainMenuPrototypeUIBuilder : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private MainMenuController targetController;
    [SerializeField] private CoopNetworkBootstrap targetBootstrap;

    [Header("Build")]
    [SerializeField] private bool buildOnAwake;
    [SerializeField] private bool clearExistingRootBeforeBuild = true;
    [SerializeField] private string rootName = "MainMenuPrototypeRoot";

    [Header("Theme")]
    [SerializeField] private Color backgroundColor = new Color(0.08f, 0.1f, 0.14f, 1f);
    [SerializeField] private Color cardColor = new Color(0.13f, 0.17f, 0.24f, 0.95f);
    [SerializeField] private Color buttonColor = new Color(0.2f, 0.42f, 0.8f, 1f);
    [SerializeField] private Color accentColor = new Color(0.15f, 0.8f, 0.65f, 1f);
    [SerializeField] private Color textColor = new Color(0.95f, 0.97f, 1f, 1f);

    private Canvas canvas;
    private RectTransform rootRect;

    private void Awake()
    {
        if (!buildOnAwake)
        {
            return;
        }

        BuildPrototype();
    }

    private void OnEnable()
    {
        if (Application.isPlaying || !buildOnAwake)
        {
            return;
        }

        BuildPrototype();
    }

    [ContextMenu("Build Main Menu Prototype UI")]
    public void BuildPrototype()
    {
        ResolveTargets();
        EnsureCanvasAndEventSystem();
        EnsureRoot();

        if (clearExistingRootBeforeBuild)
        {
            for (int i = rootRect.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(rootRect.GetChild(i).gameObject);
            }
        }

        Image rootImage = rootRect.GetComponent<Image>();
        rootImage.color = backgroundColor;

        RectTransform headerBar = CreateCard("HeaderBar", rootRect, new Vector2(0.03f, 0.88f), new Vector2(0.97f, 0.97f));
        CreateHeader(headerBar);

        RectTransform playGatePanel = CreateCard("PlayGatePanel", rootRect, new Vector2(0.22f, 0.33f), new Vector2(0.78f, 0.78f));
        Button playButton = BuildPlayGate(playGatePanel);

        RectTransform roomFlowPanel = CreateCard("RoomFlowPanel", rootRect, new Vector2(0.03f, 0.06f), new Vector2(0.97f, 0.86f));
        RoomFlowRefs refs = BuildRoomFlow(roomFlowPanel);
        roomFlowPanel.gameObject.SetActive(false);

        BindController(playGatePanel.gameObject, roomFlowPanel.gameObject, playButton, refs);
    }

    private void ResolveTargets()
    {
        if (targetController == null)
        {
            targetController = FindObjectOfType<MainMenuController>(true);
        }

        if (targetController == null)
        {
            targetController = gameObject.AddComponent<MainMenuController>();
        }

        if (targetBootstrap == null)
        {
            targetBootstrap = FindObjectOfType<CoopNetworkBootstrap>(true);
        }

        if (targetBootstrap == null)
        {
            GameObject bootstrapObject = new GameObject("CoopBootstrap");
            targetBootstrap = bootstrapObject.AddComponent<CoopNetworkBootstrap>();
        }
    }

    private void EnsureCanvasAndEventSystem()
    {
        canvas = FindObjectOfType<Canvas>(true);
        if (canvas == null)
        {
            GameObject canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        if (FindObjectOfType<EventSystem>(true) == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }
    }

    private void EnsureRoot()
    {
        Transform existing = canvas.transform.Find(rootName);
        if (existing != null)
        {
            rootRect = (RectTransform)existing;
        }
        else
        {
            GameObject root = new GameObject(rootName, typeof(RectTransform), typeof(Image));
            rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(canvas.transform, false);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
        }
    }

    private RectTransform CreateCard(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image img = go.GetComponent<Image>();
        img.color = cardColor;
        return rect;
    }

    private void CreateHeader(RectTransform parent)
    {
        TMP_Text title = CreateText(parent, "Title", "PROJECT MULTIPLAYER", 40f, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(title.rectTransform, new Vector2(0f, 0f), new Vector2(0.6f, 1f), new Vector2(20f, 0f), new Vector2(-10f, 0f));

        TMP_Text subtitle = CreateText(parent, "Subtitle", "Play -> Create/Join Room -> Office Lobby -> Forest", 22f, FontStyles.Normal, TextAlignmentOptions.Right);
        subtitle.color = new Color(0.8f, 0.9f, 1f, 1f);
        SetRect(subtitle.rectTransform, new Vector2(0.4f, 0f), new Vector2(1f, 1f), new Vector2(10f, 0f), new Vector2(-20f, 0f));
    }

    private Button BuildPlayGate(RectTransform panel)
    {
        TMP_Text bigTitle = CreateText(panel, "PlayTitle", "READY TO PLAY", 56f, FontStyles.Bold, TextAlignmentOptions.Center);
        SetRect(bigTitle.rectTransform, new Vector2(0f, 0.55f), new Vector2(1f, 0.95f), new Vector2(0f, 0f), new Vector2(0f, 0f));

        TMP_Text desc = CreateText(panel, "PlayDesc", "Masuk ke room browser untuk Solo / Multiplayer", 24f, FontStyles.Normal, TextAlignmentOptions.Center);
        desc.color = new Color(0.85f, 0.92f, 1f, 1f);
        SetRect(desc.rectTransform, new Vector2(0f, 0.38f), new Vector2(1f, 0.55f), new Vector2(40f, 0f), new Vector2(-40f, 0f));

        return CreateButton(panel, "PlayButton", "PLAY", new Vector2(0.32f, 0.1f), new Vector2(0.68f, 0.3f), accentColor);
    }

    private RoomFlowRefs BuildRoomFlow(RectTransform roomFlowPanel)
    {
        RoomFlowRefs refs = new RoomFlowRefs();

        RectTransform leftCard = CreateCard("CreateCard", roomFlowPanel, new Vector2(0.01f, 0.02f), new Vector2(0.49f, 0.98f));
        RectTransform rightCard = CreateCard("JoinCard", roomFlowPanel, new Vector2(0.51f, 0.30f), new Vector2(0.99f, 0.98f));
        RectTransform hostPanel = CreateCard("HostControlPanel", roomFlowPanel, new Vector2(0.51f, 0.02f), new Vector2(0.99f, 0.28f));
        refs.hostControlPanel = hostPanel.gameObject;

        refs.currencyText = CreateText(roomFlowPanel, "CurrencyText", "Currency: 0", 24f, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(refs.currencyText.rectTransform, new Vector2(0.01f, 0.92f), new Vector2(0.45f, 0.99f), new Vector2(10f, 0f), new Vector2(-10f, 0f));

        refs.statusText = CreateText(roomFlowPanel, "StatusText", "Status: Offline", 19f, FontStyles.Normal, TextAlignmentOptions.Right);
        SetRect(refs.statusText.rectTransform, new Vector2(0.45f, 0.92f), new Vector2(0.99f, 0.99f), new Vector2(10f, 0f), new Vector2(-10f, 0f));

        refs.roomInfoText = CreateText(roomFlowPanel, "RoomInfoText", "Room: -", 19f, FontStyles.Normal, TextAlignmentOptions.Right);
        SetRect(refs.roomInfoText.rectTransform, new Vector2(0.45f, 0.86f), new Vector2(0.99f, 0.93f), new Vector2(10f, 0f), new Vector2(-10f, 0f));

        BuildCreateRoomSection(leftCard, refs);
        BuildJoinRoomSection(rightCard, refs);
        BuildHostControlSection(hostPanel, refs);

        hostPanel.gameObject.SetActive(false);
        return refs;
    }

    private void BuildCreateRoomSection(RectTransform parent, RoomFlowRefs refs)
    {
        CreateText(parent, "CreateHeader", "CREATE ROOM", 28f, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect((RectTransform)parent.Find("CreateHeader"), new Vector2(0f, 0.92f), new Vector2(1f, 0.99f), new Vector2(18f, 0f), new Vector2(-18f, 0f));

        refs.playerNameInput = CreateInput(parent, "PlayerNameInput", "Player Name", "Player", 0.84f);
        refs.roomNameInput = CreateInput(parent, "RoomNameInput", "Room Name", "Room Survival", 0.74f);
        refs.roomCodeInput = CreateInput(parent, "RoomCodeInput", "Room Code", "ROOM01", 0.64f);
        refs.roomPasswordInput = CreateInput(parent, "RoomPasswordInput", "Password (private)", "", 0.54f);

        refs.privateRoomToggle = CreateToggle(parent, "PrivateRoomToggle", "Private Room", 0.46f);
        refs.maxPlayersSlider = CreateSlider(parent, "MaxPlayersSlider", "Max Players (1-4)", 0.36f);

        refs.hostAddressInput = null;
        refs.hostPortInput = null;

        TMP_Text hostInfo = CreateText(parent, "HostFixedInfo", "Semua room otomatis memakai VPS server.", 16f, FontStyles.Italic, TextAlignmentOptions.Left);
        hostInfo.color = new Color(0.84f, 0.94f, 1f, 1f);
        SetRect(hostInfo.rectTransform, new Vector2(0.03f, 0.22f), new Vector2(0.97f, 0.30f), Vector2.zero, Vector2.zero);

        refs.soloButton = CreateButton(parent, "SoloButton", "SOLO", new Vector2(0.03f, 0.03f), new Vector2(0.22f, 0.12f), buttonColor);
        refs.createRoomButton = CreateButton(parent, "CreateRoomButton", "CREATE ROOM", new Vector2(0.24f, 0.03f), new Vector2(0.60f, 0.12f), accentColor);
        refs.stopButton = CreateButton(parent, "StopButton", "LEAVE", new Vector2(0.62f, 0.03f), new Vector2(0.82f, 0.12f), new Color(0.72f, 0.24f, 0.24f, 1f));
    }

    private void BuildJoinRoomSection(RectTransform parent, RoomFlowRefs refs)
    {
        CreateText(parent, "JoinHeader", "JOIN ROOM", 28f, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect((RectTransform)parent.Find("JoinHeader"), new Vector2(0f, 0.92f), new Vector2(1f, 0.99f), new Vector2(18f, 0f), new Vector2(-18f, 0f));

        refs.publicRoomSearchInput = CreateInput(parent, "PublicRoomSearchInput", "Search Public Room Name", "Room Survival", 0.84f);
        refs.searchPublicRoomButton = CreateButton(parent, "SearchPublicRoomButton", "SEARCH", new Vector2(0.70f, 0.80f), new Vector2(0.97f, 0.88f), buttonColor);

        refs.publicRoomResultText = CreateText(parent, "PublicRoomResultText", "Hasil search room tampil di sini.", 16f, FontStyles.Italic, TextAlignmentOptions.Left);
        refs.publicRoomResultText.color = new Color(0.84f, 0.94f, 1f, 1f);
        SetRect(refs.publicRoomResultText.rectTransform, new Vector2(0.03f, 0.72f), new Vector2(0.97f, 0.79f), Vector2.zero, Vector2.zero);

        RectTransform listViewport = CreateCard("PublicRoomList", parent, new Vector2(0.03f, 0.50f), new Vector2(0.97f, 0.70f));
        listViewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
        VerticalLayoutGroup listLayout = listViewport.gameObject.AddComponent<VerticalLayoutGroup>();
        listLayout.padding = new RectOffset(6, 6, 6, 6);
        listLayout.spacing = 6f;
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = false;
        listLayout.childForceExpandHeight = false;
        refs.publicRoomListContainer = listViewport;

        refs.joinAddressInput = null;
        refs.joinPortInput = null;
        refs.joinRoomCodeInput = CreateInput(parent, "JoinRoomCodeInput", "Room Code", "ROOM01", 0.40f);
        refs.joinPasswordInput = CreateInput(parent, "JoinPasswordInput", "Password", "", 0.30f);

        TMP_Text vpsInfo = CreateText(parent, "JoinFixedInfo", "Endpoint server sudah fixed ke VPS.", 16f, FontStyles.Italic, TextAlignmentOptions.Left);
        vpsInfo.color = new Color(0.84f, 0.94f, 1f, 1f);
        SetRect(vpsInfo.rectTransform, new Vector2(0.03f, 0.21f), new Vector2(0.97f, 0.27f), Vector2.zero, Vector2.zero);

        refs.joinRoomButton = CreateButton(parent, "JoinRoomButton", "JOIN DIRECT", new Vector2(0.03f, 0.05f), new Vector2(0.32f, 0.15f), buttonColor);
        refs.joinBySearchButton = CreateButton(parent, "JoinBySearchButton", "JOIN SEARCH RESULT", new Vector2(0.34f, 0.05f), new Vector2(0.74f, 0.15f), accentColor);
    }

    private void BuildHostControlSection(RectTransform parent, RoomFlowRefs refs)
    {
        TMP_Text header = CreateText(parent, "HostHeader", "HOST CONTROL PANEL", 20f, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(header.rectTransform, new Vector2(0.03f, 0.76f), new Vector2(0.97f, 0.97f), Vector2.zero, Vector2.zero);

        refs.startLobbyButton = CreateButton(parent, "StartLobbyButton", "START LOBBY", new Vector2(0.03f, 0.48f), new Vector2(0.36f, 0.72f), new Color(0.23f, 0.62f, 0.87f, 1f));
        refs.startForestButton = CreateButton(parent, "StartForestButton", "START FOREST", new Vector2(0.38f, 0.48f), new Vector2(0.71f, 0.72f), new Color(0.18f, 0.72f, 0.52f, 1f));
        refs.hostLeaveButton = CreateButton(parent, "HostLeaveButton", "LEAVE", new Vector2(0.73f, 0.48f), new Vector2(0.97f, 0.72f), new Color(0.72f, 0.24f, 0.24f, 1f));

        TMP_Text kickLabel = CreateText(parent, "KickLabel", "Kick Players", 16f, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(kickLabel.rectTransform, new Vector2(0.03f, 0.34f), new Vector2(0.97f, 0.46f), Vector2.zero, Vector2.zero);

        RectTransform kickList = CreateCard("KickPlayerList", parent, new Vector2(0.03f, 0.05f), new Vector2(0.97f, 0.32f));
        kickList.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
        VerticalLayoutGroup kickLayout = kickList.gameObject.AddComponent<VerticalLayoutGroup>();
        kickLayout.padding = new RectOffset(6, 6, 6, 6);
        kickLayout.spacing = 6f;
        kickLayout.childControlWidth = true;
        kickLayout.childControlHeight = false;
        kickLayout.childForceExpandHeight = false;
        refs.kickListContainer = kickList;
    }

    private TMP_Text CreateText(RectTransform parent, string name, string value, float fontSize, FontStyles fontStyle, TextAlignmentOptions align)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = align;
        text.color = textColor;
        text.enableWordWrapping = true;
        return text;
    }

    private TMP_InputField CreateInput(RectTransform parent, string name, string label, string placeholder, float topNormalized)
    {
        TMP_Text labelText = CreateText(parent, name + "Label", label, 16f, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(labelText.rectTransform, new Vector2(0.03f, topNormalized + 0.035f), new Vector2(0.97f, topNormalized + 0.07f), Vector2.zero, Vector2.zero);

        GameObject inputObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        RectTransform rect = inputObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        SetRect(rect, new Vector2(0.03f, topNormalized - 0.03f), new Vector2(0.97f, topNormalized + 0.03f), Vector2.zero, Vector2.zero);

        Image bg = inputObject.GetComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.1f);

        GameObject viewportObject = new GameObject("Text Area", typeof(RectTransform));
        RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
        viewportRect.SetParent(rect, false);
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(10f, 6f);
        viewportRect.offsetMax = new Vector2(-10f, -6f);

        TextMeshProUGUI placeholderText = CreateText(viewportRect, "Placeholder", placeholder, 18f, FontStyles.Italic, TextAlignmentOptions.Left) as TextMeshProUGUI;
        placeholderText.color = new Color(1f, 1f, 1f, 0.45f);
        SetRect(placeholderText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        TextMeshProUGUI inputText = CreateText(viewportRect, "Text", string.Empty, 18f, FontStyles.Normal, TextAlignmentOptions.Left) as TextMeshProUGUI;
        SetRect(inputText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        TMP_InputField input = inputObject.GetComponent<TMP_InputField>();
        input.textViewport = viewportRect;
        input.textComponent = inputText;
        input.placeholder = placeholderText;
        input.caretWidth = 2;
        return input;
    }

    private Toggle CreateToggle(RectTransform parent, string name, string label, float topNormalized)
    {
        GameObject toggleObject = new GameObject(name, typeof(RectTransform), typeof(Toggle));
        RectTransform rect = toggleObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        SetRect(rect, new Vector2(0.03f, topNormalized - 0.03f), new Vector2(0.45f, topNormalized + 0.03f), Vector2.zero, Vector2.zero);

        GameObject bgObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
        RectTransform bgRect = bgObject.GetComponent<RectTransform>();
        bgRect.SetParent(rect, false);
        bgRect.anchorMin = new Vector2(0f, 0.5f);
        bgRect.anchorMax = new Vector2(0f, 0.5f);
        bgRect.anchoredPosition = new Vector2(14f, 0f);
        bgRect.sizeDelta = new Vector2(24f, 24f);
        Image bg = bgObject.GetComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.35f);

        GameObject checkObject = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
        RectTransform checkRect = checkObject.GetComponent<RectTransform>();
        checkRect.SetParent(bgRect, false);
        checkRect.anchorMin = new Vector2(0.2f, 0.2f);
        checkRect.anchorMax = new Vector2(0.8f, 0.8f);
        checkRect.offsetMin = Vector2.zero;
        checkRect.offsetMax = Vector2.zero;
        Image check = checkObject.GetComponent<Image>();
        check.color = accentColor;

        TMP_Text labelText = CreateText(rect, "Label", label, 18f, FontStyles.Normal, TextAlignmentOptions.Left);
        labelText.rectTransform.anchorMin = new Vector2(0f, 0f);
        labelText.rectTransform.anchorMax = new Vector2(1f, 1f);
        labelText.rectTransform.offsetMin = new Vector2(44f, 0f);
        labelText.rectTransform.offsetMax = Vector2.zero;

        Toggle toggle = toggleObject.GetComponent<Toggle>();
        toggle.targetGraphic = bg;
        toggle.graphic = check;
        return toggle;
    }

    private Slider CreateSlider(RectTransform parent, string name, string label, float topNormalized)
    {
        TMP_Text labelText = CreateText(parent, name + "Label", label, 16f, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(labelText.rectTransform, new Vector2(0.03f, topNormalized + 0.03f), new Vector2(0.97f, topNormalized + 0.07f), Vector2.zero, Vector2.zero);

        GameObject sliderObject = new GameObject(name, typeof(RectTransform), typeof(Slider));
        RectTransform rect = sliderObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        SetRect(rect, new Vector2(0.03f, topNormalized - 0.025f), new Vector2(0.97f, topNormalized + 0.015f), Vector2.zero, Vector2.zero);

        GameObject bgObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
        RectTransform bgRect = bgObject.GetComponent<RectTransform>();
        bgRect.SetParent(rect, false);
        bgRect.anchorMin = new Vector2(0f, 0.5f);
        bgRect.anchorMax = new Vector2(1f, 0.5f);
        bgRect.sizeDelta = new Vector2(0f, 10f);
        Image bg = bgObject.GetComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.2f);

        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.SetParent(rect, false);
        fillAreaRect.anchorMin = new Vector2(0f, 0f);
        fillAreaRect.anchorMax = new Vector2(1f, 1f);
        fillAreaRect.offsetMin = new Vector2(6f, 0f);
        fillAreaRect.offsetMax = new Vector2(-6f, 0f);

        GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.SetParent(fillAreaRect, false);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fillImage = fill.GetComponent<Image>();
        fillImage.color = accentColor;

        GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
        handleAreaRect.SetParent(rect, false);
        handleAreaRect.anchorMin = Vector2.zero;
        handleAreaRect.anchorMax = Vector2.one;

        GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        handleRect.SetParent(handleAreaRect, false);
        handleRect.sizeDelta = new Vector2(16f, 24f);
        Image handleImage = handle.GetComponent<Image>();
        handleImage.color = Color.white;

        Slider slider = sliderObject.GetComponent<Slider>();
        slider.targetGraphic = handleImage;
        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 1f;
        slider.maxValue = 4f;
        slider.wholeNumbers = true;
        slider.value = 4f;
        return slider;
    }

    private Button CreateButton(RectTransform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = buttonObject.GetComponent<Image>();
        image.color = color;

        TMP_Text text = CreateText(rect, "Label", label, 20f, FontStyles.Bold, TextAlignmentOptions.Center);
        SetRect(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        return buttonObject.GetComponent<Button>();
    }

    private void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private void BindController(GameObject playGatePanel, GameObject roomFlowPanel, Button playButton, RoomFlowRefs refs)
    {
        if (targetController == null)
        {
            return;
        }

        SetPrivateField(targetController, "bootstrap", targetBootstrap);
        SetPrivateField(targetController, "statusText", refs.statusText);
        SetPrivateField(targetController, "roomInfoText", refs.roomInfoText);
        SetPrivateField(targetController, "currencyText", refs.currencyText);

        SetPrivateField(targetController, "playGatePanel", playGatePanel);
        SetPrivateField(targetController, "roomFlowPanel", roomFlowPanel);
        SetPrivateField(targetController, "hostControlPanel", refs.hostControlPanel);

        SetPrivateField(targetController, "playerNameInput", refs.playerNameInput);
        SetPrivateField(targetController, "roomNameInput", refs.roomNameInput);
        SetPrivateField(targetController, "roomCodeInput", refs.roomCodeInput);
        SetPrivateField(targetController, "roomPasswordInput", refs.roomPasswordInput);
        SetPrivateField(targetController, "privateRoomToggle", refs.privateRoomToggle);
        SetPrivateField(targetController, "maxPlayersSlider", refs.maxPlayersSlider);
        SetPrivateField(targetController, "hostAddressInput", refs.hostAddressInput);
        SetPrivateField(targetController, "hostPortInput", refs.hostPortInput);

        SetPrivateField(targetController, "joinAddressInput", refs.joinAddressInput);
        SetPrivateField(targetController, "joinPortInput", refs.joinPortInput);
        SetPrivateField(targetController, "joinRoomCodeInput", refs.joinRoomCodeInput);
        SetPrivateField(targetController, "joinPasswordInput", refs.joinPasswordInput);

        SetPrivateField(targetController, "publicRoomSearchInput", refs.publicRoomSearchInput);
        SetPrivateField(targetController, "publicRoomResultText", refs.publicRoomResultText);
        SetPrivateField(targetController, "publicRoomListContainer", refs.publicRoomListContainer);
        SetPrivateField(targetController, "kickListContainer", refs.kickListContainer);
        SetPrivateField(targetController, "hostLeaveButton", refs.hostLeaveButton);

        SetPrivateField(targetController, "playButton", playButton);
        SetPrivateField(targetController, "soloButton", refs.soloButton);
        SetPrivateField(targetController, "createRoomButton", refs.createRoomButton);
        SetPrivateField(targetController, "joinRoomButton", refs.joinRoomButton);
        SetPrivateField(targetController, "searchPublicRoomButton", refs.searchPublicRoomButton);
        SetPrivateField(targetController, "joinBySearchButton", refs.joinBySearchButton);
        SetPrivateField(targetController, "startLobbyButton", refs.startLobbyButton);
        SetPrivateField(targetController, "startForestButton", refs.startForestButton);
        SetPrivateField(targetController, "stopButton", refs.stopButton);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        if (target == null)
        {
            return;
        }

        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
        {
            return;
        }

        field.SetValue(target, value);
    }

    private sealed class RoomFlowRefs
    {
        public TMP_Text statusText;
        public TMP_Text roomInfoText;
        public TMP_Text currencyText;

        public TMP_InputField playerNameInput;
        public TMP_InputField roomNameInput;
        public TMP_InputField roomCodeInput;
        public TMP_InputField roomPasswordInput;
        public Toggle privateRoomToggle;
        public Slider maxPlayersSlider;
        public TMP_InputField hostAddressInput;
        public TMP_InputField hostPortInput;

        public TMP_InputField joinAddressInput;
        public TMP_InputField joinPortInput;
        public TMP_InputField joinRoomCodeInput;
        public TMP_InputField joinPasswordInput;

        public TMP_InputField publicRoomSearchInput;
        public TMP_Text publicRoomResultText;
        public RectTransform publicRoomListContainer;

        public GameObject hostControlPanel;
        public RectTransform kickListContainer;
        public Button hostLeaveButton;

        public Button soloButton;
        public Button createRoomButton;
        public Button joinRoomButton;
        public Button searchPublicRoomButton;
        public Button joinBySearchButton;
        public Button startLobbyButton;
        public Button startForestButton;
        public Button stopButton;
    }
}
