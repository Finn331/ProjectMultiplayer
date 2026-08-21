using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PhotonFusionBootstrap bootstrap;
    [SerializeField] private RoomDirectoryClient roomDirectoryClient;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text roomInfoText;
    [SerializeField] private TMP_Text currencyText;

    [Header("Flow Panels")]
    [SerializeField] private GameObject playGatePanel;
    [SerializeField] private GameObject roomFlowPanel;
    [SerializeField] private GameObject hostControlPanel;

    [Header("Profile")]
    [SerializeField] private TMP_InputField playerNameInput;
    [SerializeField] private int initialCurrency = 200;

    [Header("Create Room")]
    [SerializeField] private TMP_InputField roomNameInput;
    [SerializeField] private TMP_InputField roomCodeInput;
    [SerializeField] private TMP_InputField roomPasswordInput;
    [SerializeField] private Toggle privateRoomToggle;
    [SerializeField] private Slider maxPlayersSlider;
    [SerializeField] private TMP_InputField hostAddressInput;
    [SerializeField] private TMP_InputField hostPortInput;

    [Header("Join Room")]
    [SerializeField] private TMP_InputField joinAddressInput;
    [SerializeField] private TMP_InputField joinPortInput;
    [SerializeField] private TMP_InputField joinRoomCodeInput;
    [SerializeField] private TMP_InputField joinPasswordInput;
    [SerializeField] private TMP_InputField publicRoomSearchInput;
    [SerializeField] private TMP_Text publicRoomResultText;
    [SerializeField] private RectTransform publicRoomListContainer;

    [Header("Host Controls")]
    [SerializeField] private RectTransform kickListContainer;
    [SerializeField] private Button hostLeaveButton;

    [Header("Buttons")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button soloButton;
    [SerializeField] private Button createRoomButton;
    [SerializeField] private Button joinRoomButton;
    [SerializeField] private Button searchPublicRoomButton;
    [SerializeField] private Button joinBySearchButton;
    [SerializeField] private Button startLobbyButton;
    [SerializeField] private Button startForestButton;
    [SerializeField] private Button stopButton;

    [Header("Scene")]
    [SerializeField] private string officeLobbySceneName = "Gameplay";
    [SerializeField] private string forestSceneName = "Environment";

    [Header("Defaults")]
    [SerializeField] private string defaultHostAddress = "2.27.165.46";
    [SerializeField] private ushort defaultHostPort = 9005;
    [SerializeField] private string defaultJoinAddress = "2.27.165.46";
    [SerializeField] private ushort defaultJoinPort = 9005;

    [Header("Room Directory (Optional API)")]

    [SerializeField] private string roomDirectoryBaseUrl = "http://2.27.165.46:9011";
    [SerializeField] private bool showAdvancedEndpointInputs = false;

    private readonly List<GameObject> roomEntryRows = new List<GameObject>();
    private readonly List<GameObject> kickEntryRows = new List<GameObject>();

    private bool hostControlMode;
    private float nextHostUiRefreshTime;
    private const string LegacyDefaultRoomCode = "ROOM01";

    private void Awake()
    {
        this.ResolveBootstrap();
        this.ResolveRoomDirectoryClient();
        this.BindButtons();
        this.SetupDefaults();
        this.ApplyEndpointFieldVisibility();
        this.ApplyFlowStep(isInPlayGate: true);
        this.SetHostControlMode(false);
        this.RefreshHostActionButtons();
        this.RefreshCurrency();
        this.RefreshRoomInfo();
        this.SetStatus("Siap. Tekan Play untuk mulai.");
        this.TryAutoJoinFromCommandLine();
    }

    private void TryAutoJoinFromCommandLine()
    {
        if (!Application.isEditor && TryGetCommandLineValue("-autoJoin", out string roomCode) && !string.IsNullOrEmpty(roomCode))
        {
            string playerName = TryGetCommandLineValue("-playerName", out string name) && !string.IsNullOrEmpty(name)
                ? name
                : "Player";
            if (joinRoomCodeInput != null)
            {
                joinRoomCodeInput.text = roomCode;
            }
            if (playerNameInput != null)
            {
                playerNameInput.text = playerName;
            }
            this.SetStatus("Auto-join room " + roomCode + " sebagai " + playerName + "...");
            bootstrap?.JoinRoom(roomCode, playerName);
        }
    }

    private static bool TryGetCommandLineValue(string key, out string value)
    {
        value = null;
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, System.StringComparison.OrdinalIgnoreCase))
            {
                value = args[i + 1];
                return true;
            }
        }
        return false;
    }

    private void OnEnable()
    {
        this.ResolveBootstrap();
        if (bootstrap != null)
        {
            bootstrap.StatusChanged -= this.OnBootstrapStatus;
            bootstrap.StatusChanged += this.OnBootstrapStatus;
            bootstrap.SessionListUpdated -= this.OnSessionListUpdated;
            bootstrap.SessionListUpdated += this.OnSessionListUpdated;
            bootstrap.RunnerStarted -= this.HandleRunnerStarted;
            bootstrap.RunnerStarted += this.HandleRunnerStarted;
        }
    }

    private void OnDisable()
    {
        if (bootstrap != null)
        {
            bootstrap.StatusChanged -= this.OnBootstrapStatus;
            bootstrap.SessionListUpdated -= this.OnSessionListUpdated;
            bootstrap.RunnerStarted -= this.HandleRunnerStarted;
        }
    }

    private void HandleRunnerStarted(Fusion.NetworkRunner runner)
    {
        if (runner == null || !runner.IsRunning)
        {
            return;
        }

        string activeScene = SceneManager.GetActiveScene().name;
        if (!string.Equals(activeScene, "MainMenu", System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Host (MasterClient) drives scene sync via Runner.LoadScene; clients fallback to local load
        // so build clients (PM2) don't stay stuck in MainMenu without a FusionPlayerSpawner.
        if (bootstrap != null && bootstrap.IsMasterClient)
        {
            this.SetStatus("Memuat " + officeLobbySceneName + " (host auto-transition)...");
            try
            {
                runner.LoadScene(Fusion.SceneRef.FromIndex(1));
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[MainMenu] Runner.LoadScene failed: " + exception.Message);
                this.LoadSceneSafely(officeLobbySceneName);
            }
        }
        else
        {
            this.SetStatus("Join OK — memuat " + officeLobbySceneName + "...");
            this.LoadSceneSafely(officeLobbySceneName);
        }
    }

    private void OnApplicationQuit()
    {
        if (bootstrap != null)
        {
            bootstrap.LeaveRoom();
        }
    }

    private void Update()
    {
        this.RefreshHostActionButtons();

        if (!hostControlMode)
        {
            return;
        }

        if (Time.unscaledTime >= nextHostUiRefreshTime)
        {
            nextHostUiRefreshTime = Time.unscaledTime + 0.3f;
            this.RefreshKickList();
        }
    }

    private void ResolveBootstrap()
    {
        if (bootstrap == null)
        {
            bootstrap = FindObjectOfType<PhotonFusionBootstrap>(true);
        }
        if (bootstrap == null && PhotonFusionBootstrap.Instance != null)
        {
            bootstrap = PhotonFusionBootstrap.Instance;
        }
    }

    private void ResolveRoomDirectoryClient()
    {
        if (roomDirectoryClient == null)
        {
            roomDirectoryClient = FindObjectOfType<RoomDirectoryClient>(true);
        }

        if (roomDirectoryClient == null)
        {
            roomDirectoryClient = gameObject.AddComponent<RoomDirectoryClient>();
        }

        roomDirectoryClient.BaseUrl = roomDirectoryBaseUrl;
    }

    private void BindButtons()
    {
        this.BindButton(playButton, this.OpenRoomFlow);
        this.BindButton(soloButton, this.PlaySolo);
        this.BindButton(createRoomButton, this.CreateRoomAsHost);
        this.BindButton(joinRoomButton, this.JoinRoom);
        this.BindButton(searchPublicRoomButton, this.SearchPublicRoomsByName);
        this.BindButton(joinBySearchButton, this.JoinFoundPublicRoom);
        this.BindButton(startLobbyButton, this.HostStartLobby);
        this.BindButton(startForestButton, this.HostStartForest);
        this.BindButton(stopButton, this.StopSession);
        this.BindButton(hostLeaveButton, this.StopSession);

        if (privateRoomToggle != null)
        {
            privateRoomToggle.onValueChanged.RemoveListener(this.OnPrivateToggleChanged);
            privateRoomToggle.onValueChanged.AddListener(this.OnPrivateToggleChanged);
            this.OnPrivateToggleChanged(privateRoomToggle.isOn);
        }
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

    private void SetupDefaults()
    {
        PlayerCurrencyWallet.InitializeIfNeeded(initialCurrency);

        this.SetInputIfEmpty(playerNameInput, "Player");
        this.SetInputIfEmpty(roomNameInput, "Room Survival");
        this.SetInputIfEmpty(hostAddressInput, defaultHostAddress);
        this.SetInputIfEmpty(hostPortInput, defaultHostPort.ToString());
        this.SetInputIfEmpty(joinAddressInput, defaultJoinAddress);
        this.SetInputIfEmpty(joinPortInput, defaultJoinPort.ToString());
        this.ClearLegacyRoomCodeDefault(roomCodeInput);
        this.ClearLegacyRoomCodeDefault(joinRoomCodeInput);
        if (publicRoomSearchInput != null)
        {
            publicRoomSearchInput.text = string.Empty;
        }

        if (maxPlayersSlider != null)
        {
            maxPlayersSlider.minValue = 1f;
            maxPlayersSlider.maxValue = 4f;
            maxPlayersSlider.wholeNumbers = true;
            if (maxPlayersSlider.value < 1f || maxPlayersSlider.value > 4f)
            {
                maxPlayersSlider.value = 4f;
            }
        }
    }

    private void ApplyEndpointFieldVisibility()
    {
        bool show = showAdvancedEndpointInputs;
        this.SetFieldVisible(hostAddressInput, show);
        this.SetFieldVisible(hostPortInput, show);
        this.SetFieldVisible(joinAddressInput, show);
        this.SetFieldVisible(joinPortInput, show);
    }

    private void SetFieldVisible(TMP_InputField field, bool visible)
    {
        if (field == null)
        {
            return;
        }

        Transform parent = field.transform.parent;
        GameObject target = parent != null ? parent.gameObject : field.gameObject;
        target.SetActive(visible);
    }

    private void SetInputIfEmpty(TMP_InputField input, string value)
    {
        if (input == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(input.text))
        {
            input.text = value;
        }
    }

    private void ClearLegacyRoomCodeDefault(TMP_InputField input)
    {
        if (input != null && string.Equals(input.text, LegacyDefaultRoomCode, System.StringComparison.OrdinalIgnoreCase))
        {
            input.text = string.Empty;
        }
    }

    private void ApplyFlowStep(bool isInPlayGate)
    {
        if (playGatePanel != null)
        {
            playGatePanel.SetActive(isInPlayGate);
        }

        if (roomFlowPanel != null)
        {
            roomFlowPanel.SetActive(!isInPlayGate);
        }
    }

    private void SetHostControlMode(bool enabled)
    {
        hostControlMode = enabled;
        if (hostControlPanel != null)
        {
            hostControlPanel.SetActive(enabled);
        }

        if (!enabled)
        {
            this.ClearKickRows();
            this.RefreshHostActionButtons();
            return;
        }

        this.RefreshKickList();
        this.RefreshHostActionButtons();
    }

    private void OpenRoomFlow()
    {
        this.ApplyFlowStep(isInPlayGate: false);
        this.SetStatus("Pilih Solo atau Multiplayer.");
        this.SetHostControlMode(false);
        this.SetPublicRoomResult("Tekan SEARCH untuk mencari room.");
    }

    private void PlaySolo()
    {
        this.ResolveBootstrap();
        if (bootstrap == null)
        {
            this.SetStatus("PhotonFusionBootstrap belum ada.");
            return;
        }

        string playerName = this.ReadPlayerName();
        
        bootstrap.CreateRoom(string.Empty, playerName, 1, isPrivate: true);
        this.SetStatus("Masuk Solo mode (Photon Shared)...");
        this.SetHostControlMode(true);
    }

    private void CreateRoomAsHost()
    {
        this.ResolveBootstrap();
        if (bootstrap == null)
        {
            this.SetStatus("PhotonFusionBootstrap belum ada di scene MainMenu.");
            return;
        }

        string playerName = this.ReadPlayerName();
        string roomCode = this.ReadOrDefault(roomCodeInput, string.Empty);
        int maxPlayers = this.ReadMaxPlayers();
        bool isPrivate = privateRoomToggle != null && privateRoomToggle.isOn;

        bootstrap.CreateRoom(roomCode, playerName, maxPlayers, isPrivate);
        if (roomCodeInput != null && PhotonFusionSessionState.HasSession)
        {
            roomCodeInput.text = PhotonFusionSessionState.Active.RoomCode;
        }

        string createdRoomCode = PhotonFusionSessionState.HasSession ? PhotonFusionSessionState.Active.RoomCode : "-";
        this.SetStatus("Membuat room Photon: " + createdRoomCode);
        this.SetHostControlMode(true);
    }

    private void JoinRoom()
    {
        this.ResolveBootstrap();
        if (bootstrap == null)
        {
            this.SetStatus("PhotonFusionBootstrap belum ada di scene MainMenu.");
            return;
        }

        string playerName = this.ReadPlayerName();
        string joinCode = this.ReadOrDefault(joinRoomCodeInput, string.Empty);
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            this.SetStatus("Masukkan room code untuk join.");
            this.SetHostControlMode(false);
            return;
        }

        bootstrap.JoinRoom(joinCode, playerName);
        this.SetStatus("Join room Photon...");
        this.SetHostControlMode(true);
    }

    private void SearchPublicRoomsByName()
    {
        this.ResolveBootstrap();
        
        if (bootstrap != null && bootstrap.IsRunning)
        {
            this.SetPublicRoomResult("Tidak bisa mencari room saat sedang berada di dalam room. Silakan LEAVE terlebih dahulu.");
            return;
        }

        string query = this.ReadOrDefault(publicRoomSearchInput, string.Empty);
        this.SetPublicRoomResult("Mencari room di Photon Lobby...");

        this.ClearPublicRoomRows();

        if (bootstrap != null)
        {
            bootstrap.JoinLobby();
        }
    }

    private void OnSessionListUpdated(List<Fusion.SessionInfo> sessions)
    {
        this.ClearPublicRoomRows();
        
        string query = this.ReadOrDefault(publicRoomSearchInput, string.Empty).ToUpperInvariant();
        int roomCount = 0;

        if (sessions != null)
        {
            foreach (var session in sessions)
            {
                if (string.IsNullOrEmpty(query) || session.Name.ToUpperInvariant().Contains(query))
                {
                    this.CreatePublicRoomRow(session.Name, session.PlayerCount, session.MaxPlayers);
                    roomCount++;
                }
            }
        }
        
        if (roomCount == 0)
        {
            this.SetPublicRoomResult("Tidak ada room public yang tersedia.");
        }
        else
        {
            this.SetPublicRoomResult("");
        }
    }

    private void CreatePublicRoomRow(string roomName, int currentPlayers, int maxPlayers)
    {
        if (publicRoomListContainer == null) return;
        this.EnsureVerticalListLayout(publicRoomListContainer);

        int visibleIndex = roomEntryRows.Count;
        GameObject row = new GameObject("RoomRow_" + visibleIndex, typeof(RectTransform), typeof(Image));
        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.SetParent(publicRoomListContainer, false);
        LayoutElement layoutElement = row.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = 34f;
        layoutElement.minHeight = 34f;

        Image rowImage = row.GetComponent<Image>();
        rowImage.color = visibleIndex % 2 == 0
            ? new Color(1f, 1f, 1f, 0.08f)
            : new Color(1f, 1f, 1f, 0.03f);

        HorizontalLayoutGroup rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.padding = new RectOffset(10, 10, 4, 4);
        rowLayout.spacing = 8f;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlHeight = true;
        rowLayout.childControlWidth = true;
        rowLayout.childForceExpandWidth = false;

        GameObject labelObj = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObj.transform.SetParent(rowRect, false);
        TextMeshProUGUI label = labelObj.GetComponent<TextMeshProUGUI>();
        label.fontSize = 13f;
        label.color = new Color(0.94f, 0.97f, 1f, 1f);
        label.alignment = TextAlignmentOptions.Left;
        
        string privacy = "Public";
        label.text = roomName + " | " + privacy + " | " + currentPlayers + "/" + maxPlayers + " player";
        LayoutElement labelLayout = labelObj.AddComponent<LayoutElement>();
        labelLayout.flexibleWidth = 1f;
        labelLayout.minWidth = 200f;

        GameObject btnObj = new GameObject("JoinButton", typeof(RectTransform), typeof(Image), typeof(Button));
        btnObj.transform.SetParent(rowRect, false);
        Image btnImage = btnObj.GetComponent<Image>();
        btnImage.color = new Color(0.15f, 0.8f, 0.65f, 1f);
        LayoutElement btnLayout = btnObj.AddComponent<LayoutElement>();
        btnLayout.preferredWidth = 68f;
        btnLayout.minWidth = 68f;

        GameObject btnLabelObj = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform btnLabelRect = btnLabelObj.GetComponent<RectTransform>();
        btnLabelRect.SetParent(btnObj.transform, false);
        btnLabelRect.anchorMin = Vector2.zero;
        btnLabelRect.anchorMax = Vector2.one;
        btnLabelRect.offsetMin = Vector2.zero;
        btnLabelRect.offsetMax = Vector2.zero;
        TextMeshProUGUI btnLabel = btnLabelObj.GetComponent<TextMeshProUGUI>();
        btnLabel.text = "JOIN";
        btnLabel.fontSize = 13f;
        btnLabel.alignment = TextAlignmentOptions.Center;
        btnLabel.color = Color.white;

        Button button = btnObj.GetComponent<Button>();
        string capturedRoomName = roomName;
        button.onClick.AddListener(() => this.JoinPhotonRoom(capturedRoomName));

        roomEntryRows.Add(row);
    }
    
    private void JoinPhotonRoom(string roomName)
    {
        this.ResolveBootstrap();
        if (bootstrap == null) return;
        string playerName = this.ReadPlayerName();
        bootstrap.JoinRoom(roomName, playerName);
        this.SetStatus("Bergabung ke room Photon...");
        this.SetHostControlMode(false);
    }

    private void ClearPublicRoomRows()
    {
        for (int i = 0; i < roomEntryRows.Count; i++)
        {
            if (roomEntryRows[i] != null)
            {
                Destroy(roomEntryRows[i]);
            }
        }

        roomEntryRows.Clear();
    }

    private void RefreshCachedPublicRoomRows()
    {
    }

    private void JoinFoundPublicRoom()
    {
        this.JoinRoom();
    }

    private void HostStartLobby()
    {
        this.ResolveBootstrap();
        if (bootstrap != null && bootstrap.Runner != null && bootstrap.IsMasterClient)
        {
            this.SetStatus("Memuat scene Lobby (Gameplay)...");
            // Index 1 = Assets/Scenes/Gameplay.unity (berdasarkan EditorBuildSettings)
            bootstrap.Runner.LoadScene(Fusion.SceneRef.FromIndex(1));
        }
    }

    private void HostStartForest()
    {
        this.ResolveBootstrap();
        if (bootstrap != null && bootstrap.Runner != null && bootstrap.IsMasterClient)
        {
            this.SetStatus("Memuat scene Forest (Environment)...");
            // Index 2 = Assets/Scenes/Environment.unity (berdasarkan EditorBuildSettings)
            bootstrap.Runner.LoadScene(Fusion.SceneRef.FromIndex(2));
        }
    }

    private void RefreshKickList()
    {
        this.ResolveBootstrap();
        
        if (!hostControlMode || kickListContainer == null)
        {
            return;
        }

        this.ClearKickRows();
        this.EnsureVerticalListLayout(kickListContainer);

        // Try to get player list from Photon runner
        int playerCount = 0;
        var playerList = new System.Collections.Generic.List<Fusion.PlayerRef>();
        
        try
        {
            if (bootstrap != null && bootstrap.Runner != null)
            {
                foreach (var player in bootstrap.ActivePlayers)
                {
                    playerList.Add(player);
                    playerCount++;
                }
            }
        }
        catch (System.Exception)
        {
            // Runner might not be fully ready yet, that's OK
            playerCount = 0;
            playerList.Clear();
        }

        if (playerCount > 0)
        {
            this.CreateKickSectionHeader("Connected Players (" + playerCount + ")");
            foreach (var player in playerList)
            {
                bool isLocal = bootstrap.Runner != null && player == bootstrap.Runner.LocalPlayer;
                string memberName = isLocal 
                    ? "You (Player " + player.PlayerId + ")" 
                    : "Player " + player.PlayerId;
                double pingMs = 0;
                if (bootstrap.Runner != null && !isLocal)
                {
                    try
                    {
                        pingMs = bootstrap.Runner.GetPlayerRtt(player);
                    }
                    catch
                    {
                        pingMs = 0;
                    }
                }
                this.CreateKickRowVisual(player.PlayerId, memberName, pingMs, isLocal);
            }
        }
        else
        {
            // Fallback: if we know we're in host mode, at least show ourselves
            this.CreateKickSectionHeader("Connected Players (1)");
            this.CreateKickRowVisual(0, "You (Host)", 0, true);
        }
    }

    private void CreateKickRowVisual(int playerId, string text, double pingMs, bool isLocal)
    {
        GameObject row = new GameObject("KickRow_" + playerId, typeof(RectTransform), typeof(Image));
        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.SetParent(kickListContainer, false);

        LayoutElement layout = row.AddComponent<LayoutElement>();
        layout.preferredHeight = 34f;

        Image bg = row.GetComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.06f);

        HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(10, 8, 4, 4);
        h.spacing = 8f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true;
        h.childForceExpandWidth = false;

        GameObject labelObj = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObj.transform.SetParent(rowRect, false);
        TextMeshProUGUI label = labelObj.GetComponent<TextMeshProUGUI>();
        label.fontSize = 15f;
        label.alignment = TextAlignmentOptions.Left;
        label.color = Color.white;
        label.text = text;
        LayoutElement labelLayout = labelObj.AddComponent<LayoutElement>();
        labelLayout.flexibleWidth = 1f;

        if (!isLocal && pingMs > 0)
        {
            GameObject pingObj = new GameObject("Ping", typeof(RectTransform), typeof(TextMeshProUGUI));
            pingObj.transform.SetParent(rowRect, false);
            TextMeshProUGUI pingLabel = pingObj.GetComponent<TextMeshProUGUI>();
            pingLabel.fontSize = 13f;
            pingLabel.alignment = TextAlignmentOptions.Right;
            Color pingColor = pingMs < 100 ? new Color(0.3f, 1f, 0.3f) : pingMs < 200 ? new Color(1f, 0.9f, 0.2f) : new Color(1f, 0.3f, 0.3f);
            pingLabel.color = pingColor;
            pingLabel.text = Mathf.RoundToInt((float)pingMs) + " ms";
            LayoutElement pingLayout = pingObj.AddComponent<LayoutElement>();
            pingLayout.preferredWidth = 60f;
            pingLayout.minWidth = 50f;
        }

        kickEntryRows.Add(row);
    }

    private void CreateKickSectionHeader(string text)
    {
        GameObject row = new GameObject("KickHeader", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.SetParent(kickListContainer, false);

        LayoutElement layout = row.AddComponent<LayoutElement>();
        layout.preferredHeight = 24f;

        TextMeshProUGUI label = row.GetComponent<TextMeshProUGUI>();
        label.fontSize = 14f;
        label.alignment = TextAlignmentOptions.Left;
        label.color = new Color(0.72f, 0.88f, 1f, 1f);
        label.text = text;

        kickEntryRows.Add(row);
    }

    private void ClearKickRows()
    {
        for (int i = 0; i < kickEntryRows.Count; i++)
        {
            if (kickEntryRows[i] != null)
            {
                Destroy(kickEntryRows[i]);
            }
        }

        kickEntryRows.Clear();
    }

    private void EnsureVerticalListLayout(RectTransform container)
    {
        if (container == null)
        {
            return;
        }

        VerticalLayoutGroup layout = container.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
        {
            layout = container.gameObject.AddComponent<VerticalLayoutGroup>();
        }

        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.spacing = 4f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        RectMask2D mask = container.GetComponent<RectMask2D>();
        if (mask == null)
        {
            container.gameObject.AddComponent<RectMask2D>();
        }

        ContentSizeFitter fitter = container.GetComponent<ContentSizeFitter>();
        if (fitter != null)
        {
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        }
    }

    private void StopSession()
    {
        this.ResolveBootstrap();
        if (bootstrap != null)
        {
            bootstrap.LeaveRoom();
        }

        MainMenuSessionState.Clear();
        this.SetHostControlMode(false);
        this.ApplyFlowStep(isInPlayGate: true);
        this.SetStatus("Disconnected dari Photon.");
    }

    private string ReadPlayerName()
    {
        return this.ReadOrDefault(playerNameInput, "Player");
    }

    private string NormalizeRoomCode(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        return source.Trim().ToUpperInvariant();
    }

    private string ReadOrDefault(TMP_InputField input, string fallback)
    {
        if (input == null || string.IsNullOrWhiteSpace(input.text))
        {
            return fallback;
        }

        return input.text.Trim();
    }

    private ushort ReadPort(TMP_InputField input, ushort fallback)
    {
        string raw = this.ReadOrDefault(input, fallback.ToString());
        if (!ushort.TryParse(raw, out ushort port))
        {
            return fallback;
        }

        return port;
    }

    private int ReadMaxPlayers()
    {
        if (maxPlayersSlider == null)
        {
            return 4;
        }

        return Mathf.Clamp(Mathf.RoundToInt(maxPlayersSlider.value), 1, 4);
    }

    private void OnPrivateToggleChanged(bool isPrivate)
    {
        if (roomPasswordInput != null)
        {
            roomPasswordInput.gameObject.SetActive(isPrivate);
        }
    }

    private void OnBootstrapStatus(string message)
    {
        this.SetStatus(message);
        this.RefreshRoomInfo();
        this.RefreshHostActionButtons();

        // Force immediate kick list refresh on next Update
        nextHostUiRefreshTime = 0f;

        if (hostControlMode)
        {
            this.RefreshKickList();
        }
    }

    private void OnBootstrapRoomStageAccepted(string stage)
    {
        if (bootstrap == null || !bootstrap.IsMasterClient)
        {
            return;
        }

        string normalizedStage = string.IsNullOrWhiteSpace(stage) ? string.Empty : stage.Trim().ToLowerInvariant();
        if (normalizedStage.Contains("forest"))
        {
            this.LoadSceneSafely(forestSceneName);
            return;
        }

        if (normalizedStage.Contains("office") || normalizedStage.Contains("lobby"))
        {
            this.LoadSceneSafely(officeLobbySceneName);
        }
    }

    private void RefreshHostActionButtons()
    {
        this.ResolveBootstrap();
        bool isRunning = bootstrap != null && bootstrap.IsRunning;
        // Use hostControlMode as our own flag for "this user created/joined a room"
        bool canControlRoom = hostControlMode && isRunning;
        bool hasSession = isRunning;

        if (startLobbyButton != null)
        {
            startLobbyButton.interactable = canControlRoom;
        }

        if (startForestButton != null)
        {
            startForestButton.interactable = canControlRoom;
        }

        if (hostLeaveButton != null)
        {
            hostLeaveButton.interactable = hasSession;
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private void RefreshRoomInfo()
    {
        if (roomInfoText == null)
        {
            return;
        }

        this.ResolveBootstrap();
        if (bootstrap == null)
        {
            roomInfoText.text = "Room: -";
            return;
        }

        roomInfoText.text = "Room: " + "";
    }

    private void RefreshCurrency()
    {
        if (currencyText == null)
        {
            return;
        }

        currencyText.text = "Currency: " + PlayerCurrencyWallet.GetBalance();
    }

    private void SetPublicRoomResult(string text)
    {
        if (publicRoomResultText != null)
        {
            publicRoomResultText.text = text;
        }
    }

    private void TryPollRoomStageFromDirectory()
    {
        // Disabled
    }

    private void LoadSceneSafely(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (string.Equals(activeScene.name, sceneName, System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }

    private void StartDedicatedRoomSession()
    {
        // Migrated
    }
}
