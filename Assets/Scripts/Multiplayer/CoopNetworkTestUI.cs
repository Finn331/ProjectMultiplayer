using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class CoopNetworkTestUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CoopNetworkBootstrap bootstrap;
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private RectTransform panelRoot;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button stopButton;

    [Header("Auto Build")]
    [SerializeField] private bool autoCreateUI = true;
    [SerializeField] private Vector2 panelSize = new Vector2(360f, 150f);
    [SerializeField] private Vector2 panelOffset = new Vector2(24f, -24f);
    [SerializeField] private Vector2 buttonSize = new Vector2(104f, 40f);
    [SerializeField] private Color panelColor = new Color(0f, 0f, 0f, 0.62f);
    [SerializeField] private Color buttonColor = new Color(0.14f, 0.24f, 0.32f, 0.95f);

    private float nextStatusUpdateTime;

    private void Awake()
    {
        if (targetCanvas == null)
        {
            targetCanvas = GetComponent<Canvas>();
        }

        if (targetCanvas == null)
        {
            targetCanvas = FindObjectOfType<Canvas>(true);
        }

        if (bootstrap == null)
        {
            bootstrap = FindObjectOfType<CoopNetworkBootstrap>(true);
        }

        if (autoCreateUI)
        {
            this.EnsureUI();
        }

        this.BindButtons();
        this.UpdateStatusLabel(true);
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextStatusUpdateTime)
        {
            nextStatusUpdateTime = Time.unscaledTime + 0.25f;
            this.UpdateStatusLabel(false);
        }
    }

    private void EnsureUI()
    {
        if (targetCanvas == null)
        {
            return;
        }

        if (panelRoot == null)
        {
            panelRoot = this.CreatePanel(targetCanvas.transform as RectTransform);
        }

        if (statusText == null)
        {
            statusText = this.CreateText("Status Text", panelRoot, 20f, TextAlignmentOptions.Left);
            RectTransform statusRect = statusText.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(0f, -10f);
            statusRect.sizeDelta = new Vector2(-24f, 36f);
        }

        if (hostButton == null)
        {
            hostButton = this.CreateButton("Host Button", panelRoot, new Vector2(-112f, -88f), "HOST");
        }

        if (joinButton == null)
        {
            joinButton = this.CreateButton("Join Button", panelRoot, new Vector2(0f, -88f), "JOIN");
        }

        if (stopButton == null)
        {
            stopButton = this.CreateButton("Stop Button", panelRoot, new Vector2(112f, -88f), "STOP");
        }
    }

    private RectTransform CreatePanel(RectTransform parent)
    {
        GameObject panelObject = new GameObject("Coop Test Panel", typeof(RectTransform), typeof(Image));
        RectTransform rect = panelObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = panelSize;
        rect.anchoredPosition = panelOffset;

        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = panelColor;
        return rect;
    }

    private Button CreateButton(string objectName, RectTransform parent, Vector2 anchoredPosition, string label)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = buttonSize;
        rect.anchoredPosition = anchoredPosition;

        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.color = buttonColor;

        TextMeshProUGUI labelText = this.CreateText("Label", rect, 20f, TextAlignmentOptions.Center);
        RectTransform labelRect = labelText.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        labelText.text = label;

        return buttonObject.GetComponent<Button>();
    }

    private TextMeshProUGUI CreateText(string objectName, RectTransform parent, float size, TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = alignment;
        text.enableAutoSizing = false;
        text.enableWordWrapping = false;
        return text;
    }

    private void BindButtons()
    {
        this.BindButton(hostButton, this.TryStartHost);
        this.BindButton(joinButton, this.TryStartClient);
        this.BindButton(stopButton, this.TryStopSession);
    }

    private void BindButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private void TryStartHost()
    {
        this.ResolveBootstrap();
        if (bootstrap != null)
        {
            bootstrap.StartHost();
        }
        this.UpdateStatusLabel(true);
    }

    private void TryStartClient()
    {
        this.ResolveBootstrap();
        if (bootstrap != null)
        {
            bootstrap.StartClient();
        }
        this.UpdateStatusLabel(true);
    }

    private void TryStopSession()
    {
        this.ResolveBootstrap();
        if (bootstrap != null)
        {
            bootstrap.StopSession();
        }
        this.UpdateStatusLabel(true);
    }

    private void ResolveBootstrap()
    {
        if (bootstrap == null)
        {
            bootstrap = FindObjectOfType<CoopNetworkBootstrap>(true);
        }
    }

    private void UpdateStatusLabel(bool immediate)
    {
        if (statusText == null)
        {
            return;
        }

        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening)
        {
            statusText.text = "Network: Offline";
            return;
        }

        if (manager.IsHost)
        {
            statusText.text = "Network: Host (" + manager.ConnectedClientsIds.Count + " clients)";
            return;
        }

        if (manager.IsServer)
        {
            statusText.text = "Network: Server";
            return;
        }

        statusText.text = "Network: Client";

        if (immediate)
        {
            nextStatusUpdateTime = Time.unscaledTime + 0.1f;
        }
    }
}
