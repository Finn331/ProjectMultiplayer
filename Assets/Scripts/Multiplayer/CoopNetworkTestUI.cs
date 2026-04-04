using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
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
    [SerializeField] private TextMeshProUGUI endpointText;

    [Header("Auto Build")]
    [SerializeField] private bool autoCreateUI = true;
    [SerializeField] private Vector2 panelSize = new Vector2(420f, 190f);
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
        this.BindBootstrapEvents();
        this.UpdateStatusLabel(true);
    }

    private void OnEnable()
    {
        if (!Application.isPlaying && autoCreateUI)
        {
            if (targetCanvas == null)
            {
                targetCanvas = GetComponent<Canvas>();
            }

            if (targetCanvas == null)
            {
                targetCanvas = FindObjectOfType<Canvas>(true);
            }

            this.EnsureUI();
            this.BindButtons();
            this.BindBootstrapEvents();
            this.UpdateStatusLabel(true);
        }
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
            statusText = this.CreateText("Status Text", panelRoot, 18f, TextAlignmentOptions.Left);
            RectTransform statusRect = statusText.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(0f, -10f);
            statusRect.sizeDelta = new Vector2(-24f, 30f);
        }

        if (endpointText == null)
        {
            endpointText = this.CreateText("Endpoint Text", panelRoot, 16f, TextAlignmentOptions.Left);
            RectTransform endpointRect = endpointText.rectTransform;
            endpointRect.anchorMin = new Vector2(0f, 1f);
            endpointRect.anchorMax = new Vector2(1f, 1f);
            endpointRect.pivot = new Vector2(0.5f, 1f);
            endpointRect.anchoredPosition = new Vector2(0f, -42f);
            endpointRect.sizeDelta = new Vector2(-24f, 24f);
        }

        if (hostButton == null)
        {
            hostButton = this.CreateButton("Host Button", panelRoot, new Vector2(-132f, -118f), "HOST LOCAL");
        }
        this.SetButtonLabel(hostButton, "HOST LOCAL");

        if (joinButton == null)
        {
            joinButton = this.CreateButton("Join Button", panelRoot, new Vector2(0f, -118f), "JOIN VPS");
        }
        this.SetButtonLabel(joinButton, "JOIN VPS");

        if (stopButton == null)
        {
            stopButton = this.CreateButton("Stop Button", panelRoot, new Vector2(132f, -118f), "STOP");
        }
        this.SetButtonLabel(stopButton, "STOP");
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

    private void SetButtonLabel(Button button, string label)
    {
        if (button == null)
        {
            return;
        }

        TextMeshProUGUI labelText = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (labelText != null)
        {
            labelText.text = label;
        }
    }

    private void BindButtons()
    {
        this.BindButton(hostButton, this.TryStartHostLocal);
        this.BindButton(joinButton, this.TryStartClientToVps);
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

    private void TryStartHostLocal()
    {
        this.ResolveBootstrap();
        if (bootstrap != null)
        {
            bootstrap.StartHostLocal();
        }
        this.UpdateStatusLabel(true);
    }

    private void TryStartClientToVps()
    {
        this.ResolveBootstrap();
        if (bootstrap != null)
        {
            bootstrap.StartClientToVps();
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

    private void BindBootstrapEvents()
    {
        if (bootstrap == null)
        {
            return;
        }

        bootstrap.StatusChanged -= this.HandleBootstrapStatusChanged;
        bootstrap.StatusChanged += this.HandleBootstrapStatusChanged;
    }

    private void OnDisable()
    {
        if (bootstrap != null)
        {
            bootstrap.StatusChanged -= this.HandleBootstrapStatusChanged;
        }
    }

    private void HandleBootstrapStatusChanged(string message)
    {
        this.UpdateStatusLabel(true);
    }

    private void UpdateStatusLabel(bool immediate)
    {
        if (statusText == null)
        {
            return;
        }

        this.ResolveBootstrap();
        if (endpointText != null && bootstrap != null)
        {
            endpointText.text = "VPS: " + bootstrap.VpsEndpoint;
        }

        if (bootstrap != null && !string.IsNullOrWhiteSpace(bootstrap.LastStatusMessage))
        {
            statusText.text = "Status: " + bootstrap.LastStatusMessage;
            if (immediate)
            {
                nextStatusUpdateTime = Time.unscaledTime + 0.1f;
            }
            return;
        }

        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening)
        {
            statusText.text = "Status: Offline";
            return;
        }

        if (manager.IsHost)
        {
            statusText.text = "Status: Host (" + manager.ConnectedClientsIds.Count + " clients)";
            return;
        }

        if (manager.IsServer)
        {
            statusText.text = "Status: Server";
            return;
        }

        statusText.text = "Status: Client";

        if (immediate)
        {
            nextStatusUpdateTime = Time.unscaledTime + 0.1f;
        }
    }
}
