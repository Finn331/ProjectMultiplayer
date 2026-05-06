using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CoopNetworkBootstrap bootstrap;
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
    [SerializeField] private string defaultHostAddress = "31.56.56.8";
    [SerializeField] private ushort defaultHostPort = 9005;
    [SerializeField] private string defaultJoinAddress = "31.56.56.8";
    [SerializeField] private ushort defaultJoinPort = 9005;

    [Header("Room Directory (Optional API)")]
    [SerializeField] private bool useRoomDirectoryApi = true;
    [SerializeField] private string roomDirectoryBaseUrl = "http://31.56.56.8:9010";

    private readonly List<GameObject> roomEntryRows = new List<GameObject>();
    private readonly List<GameObject> kickEntryRows = new List<GameObject>();

    private string activeRoomId = string.Empty;
    private RoomPublicInfo cachedPublicRoom;
    private bool hostControlMode;
    private float nextHostUiRefreshTime;

    private void Awake()
    {
        this.ResolveBootstrap();
        this.ResolveRoomDirectoryClient();
        this.BindButtons();
        this.SetupDefaults();
        this.ApplyFlowStep(isInPlayGate: true);
        this.SetHostControlMode(false);
        this.RefreshCurrency();
        this.RefreshRoomInfo();
        this.SetStatus("Siap. Tekan Play untuk mulai.");
    }

    private void OnEnable()
    {
        this.ResolveBootstrap();
        if (bootstrap != null)
        {
            bootstrap.StatusChanged -= this.OnBootstrapStatus;
            bootstrap.StatusChanged += this.OnBootstrapStatus;
        }
    }

    private void OnDisable()
    {
        if (bootstrap != null)
        {
            bootstrap.StatusChanged -= this.OnBootstrapStatus;
        }
    }

    private void Update()
    {
        if (!hostControlMode)
        {
            return;
        }

        if (Time.unscaledTime < nextHostUiRefreshTime)
        {
            return;
        }

        nextHostUiRefreshTime = Time.unscaledTime + 0.7f;
        this.RefreshKickList();
    }

    private void ResolveBootstrap()
    {
        if (bootstrap == null)
        {
            bootstrap = FindObjectOfType<CoopNetworkBootstrap>(true);
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
        this.SetInputIfEmpty(roomCodeInput, "ROOM01");
        this.SetInputIfEmpty(hostAddressInput, defaultHostAddress);
        this.SetInputIfEmpty(hostPortInput, defaultHostPort.ToString());
        this.SetInputIfEmpty(joinAddressInput, defaultJoinAddress);
        this.SetInputIfEmpty(joinPortInput, defaultJoinPort.ToString());
        this.SetInputIfEmpty(joinRoomCodeInput, "ROOM01");
        this.SetInputIfEmpty(publicRoomSearchInput, "Room Survival");

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
            return;
        }

        this.RefreshKickList();
    }

    private void OpenRoomFlow()
    {
        this.ApplyFlowStep(isInPlayGate: false);
        this.SetStatus("Pilih Solo atau Multiplayer.");
        this.SetHostControlMode(false);
    }

    private void PlaySolo()
    {
        string playerName = this.ReadPlayerName();
        MainMenuSessionState.Set(new MainMenuSessionState.SessionConfig
        {
            mode = SessionPlayMode.Solo,
            playerName = playerName,
            roomName = "Solo Run",
            roomCode = "SOLO",
            roomPassword = string.Empty,
            roomPrivate = true,
            maxPlayers = 1,
            hostAddress = "127.0.0.1",
            hostPort = 0,
            lobbySceneName = officeLobbySceneName
        });

        this.SetStatus("Masuk Solo mode...");
        SceneManager.LoadScene(officeLobbySceneName, LoadSceneMode.Single);
    }

    private void CreateRoomAsHost()
    {
        this.ResolveBootstrap();
        if (bootstrap == null)
        {
            this.SetStatus("CoopNetworkBootstrap belum ada di scene MainMenu.");
            return;
        }

        if (bootstrap.IsHostActive)
        {
            this.SetHostControlMode(true);
            this.SetStatus("Host sudah aktif. Gunakan panel host untuk Start/Leave/Kick.");
            this.RefreshRoomInfo();
            return;
        }

        string playerName = this.ReadPlayerName();
        string roomName = this.ReadOrDefault(roomNameInput, "Room Survival");
        string roomCode = this.ReadOrDefault(roomCodeInput, "ROOM01");
        string password = roomPasswordInput != null ? roomPasswordInput.text : string.Empty;
        bool isPrivate = privateRoomToggle != null && privateRoomToggle.isOn;
        int maxPlayers = this.ReadMaxPlayers();
        string hostAddress = this.ReadOrDefault(hostAddressInput, defaultHostAddress);
        ushort hostPort = this.ReadPort(hostPortInput, defaultHostPort);

        bootstrap.ConfigureRoom(roomName, roomCode, password, isPrivate, maxPlayers, officeLobbySceneName);
        bootstrap.ConfigureJoinCredentials(roomCode, password, playerName);
        bootstrap.StartHostRoom(hostAddress, hostPort);
        this.SetHostControlMode(bootstrap.IsHostActive);

        MainMenuSessionState.Set(new MainMenuSessionState.SessionConfig
        {
            mode = SessionPlayMode.HostRoom,
            playerName = playerName,
            roomName = roomName,
            roomCode = roomCode,
            roomPassword = password,
            roomPrivate = isPrivate,
            maxPlayers = maxPlayers,
            hostAddress = hostAddress,
            hostPort = hostPort,
            lobbySceneName = officeLobbySceneName
        });

        if (useRoomDirectoryApi)
        {
            RoomCreateRequest createRequest = new RoomCreateRequest
            {
                roomName = roomName,
                roomCode = roomCode,
                password = password,
                isPrivate = isPrivate,
                maxPlayers = maxPlayers,
                hostAddress = hostAddress,
                hostPort = hostPort,
                hostPlayerName = playerName
            };

            roomDirectoryClient.CreateRoom(createRequest, (response, error) =>
            {
                if (!string.IsNullOrWhiteSpace(error) || response == null || !response.success)
                {
                    this.SetStatus("Room local jadi, tapi gagal daftar room ke server directory: " + (string.IsNullOrWhiteSpace(error) ? response?.message : error));
                    return;
                }

                activeRoomId = response.roomId ?? string.Empty;
                this.RefreshRoomInfo();
                this.SetStatus("Room dibuat. Sekarang host bisa Start/Leave/Kick dari panel host.");
                this.SetHostControlMode(true);
            });
        }
        else
        {
            this.SetHostControlMode(bootstrap.IsHostActive);
            this.RefreshRoomInfo();
            this.SetStatus("Room dibuat (tanpa room directory API).");
        }
    }

    private void JoinRoom()
    {
        this.ResolveBootstrap();
        if (bootstrap == null)
        {
            this.SetStatus("CoopNetworkBootstrap belum ada di scene MainMenu.");
            return;
        }

        string playerName = this.ReadPlayerName();
        string joinAddress = this.ReadOrDefault(joinAddressInput, defaultJoinAddress);
        ushort joinPort = this.ReadPort(joinPortInput, defaultJoinPort);
        string joinCode = this.ReadOrDefault(joinRoomCodeInput, "ROOM01");
        string joinPassword = joinPasswordInput != null ? joinPasswordInput.text : string.Empty;

        bootstrap.JoinRoom(joinAddress, joinPort, joinCode, joinPassword, playerName);

        MainMenuSessionState.Set(new MainMenuSessionState.SessionConfig
        {
            mode = SessionPlayMode.JoinRoom,
            playerName = playerName,
            roomName = "Joined Room",
            roomCode = joinCode,
            roomPassword = joinPassword,
            roomPrivate = !string.IsNullOrEmpty(joinPassword),
            maxPlayers = 4,
            hostAddress = joinAddress,
            hostPort = joinPort,
            lobbySceneName = officeLobbySceneName
        });

        this.SetHostControlMode(false);
        this.SetStatus("Mencoba join room...");
    }

    private void SearchPublicRoomsByName()
    {
        if (!useRoomDirectoryApi)
        {
            this.SetStatus("Room Directory API nonaktif.");
            return;
        }

        string searchName = this.ReadOrDefault(publicRoomSearchInput, string.Empty);
        roomDirectoryClient.SearchPublicRooms(searchName, (response, error) =>
        {
            if (!string.IsNullOrWhiteSpace(error) || response == null)
            {
                this.ClearPublicRoomRows();
                this.SetPublicRoomResult("Search gagal: " + (string.IsNullOrWhiteSpace(error) ? "unknown" : error));
                return;
            }

            if (response.rooms == null || response.rooms.Count == 0)
            {
                cachedPublicRoom = null;
                this.ClearPublicRoomRows();
                this.SetPublicRoomResult("Room public tidak ditemukan.");
                return;
            }

            cachedPublicRoom = response.rooms[0];
            this.RebuildPublicRoomRows(response.rooms);
            this.SetPublicRoomResult("Pilih room dari list, lalu tekan Join.");
        });
    }

    private void RebuildPublicRoomRows(List<RoomPublicInfo> rooms)
    {
        this.ClearPublicRoomRows();
        if (publicRoomListContainer == null)
        {
            return;
        }

        for (int i = 0; i < rooms.Count; i++)
        {
            RoomPublicInfo room = rooms[i];
            if (room == null)
            {
                continue;
            }

            GameObject row = new GameObject("RoomRow_" + i, typeof(RectTransform), typeof(Image));
            RectTransform rowRect = row.GetComponent<RectTransform>();
            rowRect.SetParent(publicRoomListContainer, false);
            LayoutElement layoutElement = row.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 38f;

            Image rowImage = row.GetComponent<Image>();
            rowImage.color = new Color(1f, 1f, 1f, 0.06f);

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
            label.fontSize = 16f;
            label.color = new Color(0.94f, 0.97f, 1f, 1f);
            label.alignment = TextAlignmentOptions.Left;
            string privacy = room.isPrivate ? "Private" : "Public";
            int shownPlayers = this.ResolveDisplayPlayerCount(room);
            int shownMaxPlayers = Mathf.Max(1, room.maxPlayers);
            label.text = room.roomName + " | " + room.roomCode + " | " + privacy + " | " + shownPlayers + "/" + shownMaxPlayers;
            LayoutElement labelLayout = labelObj.AddComponent<LayoutElement>();
            labelLayout.flexibleWidth = 1f;
            labelLayout.minWidth = 200f;

            GameObject btnObj = new GameObject("JoinButton", typeof(RectTransform), typeof(Image), typeof(Button));
            btnObj.transform.SetParent(rowRect, false);
            Image btnImage = btnObj.GetComponent<Image>();
            btnImage.color = new Color(0.15f, 0.8f, 0.65f, 1f);
            LayoutElement btnLayout = btnObj.AddComponent<LayoutElement>();
            btnLayout.preferredWidth = 80f;
            btnLayout.minWidth = 80f;

            GameObject btnLabelObj = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform btnLabelRect = btnLabelObj.GetComponent<RectTransform>();
            btnLabelRect.SetParent(btnObj.transform, false);
            btnLabelRect.anchorMin = Vector2.zero;
            btnLabelRect.anchorMax = Vector2.one;
            btnLabelRect.offsetMin = Vector2.zero;
            btnLabelRect.offsetMax = Vector2.zero;
            TextMeshProUGUI btnLabel = btnLabelObj.GetComponent<TextMeshProUGUI>();
            btnLabel.text = "JOIN";
            btnLabel.fontSize = 16f;
            btnLabel.alignment = TextAlignmentOptions.Center;
            btnLabel.color = Color.white;

            Button button = btnObj.GetComponent<Button>();
            RoomPublicInfo roomCapture = room;
            button.onClick.AddListener(() => this.JoinRoomFromPublicRow(roomCapture));

            bool fullRoom = shownPlayers >= shownMaxPlayers;
            button.interactable = !fullRoom;
            if (fullRoom)
            {
                btnImage.color = new Color(0.5f, 0.5f, 0.5f, 1f);
                btnLabel.text = "FULL";
            }

            roomEntryRows.Add(row);
        }
    }

    private int ResolveDisplayPlayerCount(RoomPublicInfo room)
    {
        int maxPlayers = Mathf.Max(1, room.maxPlayers);
        int serverReported = Mathf.Clamp(room.currentPlayers, 0, maxPlayers);

        this.ResolveBootstrap();
        if (bootstrap == null || !bootstrap.IsHostActive)
        {
            return serverReported;
        }

        string roomCode = room != null ? room.roomCode : string.Empty;
        if (!string.Equals((roomCode ?? string.Empty).Trim().ToUpperInvariant(), bootstrap.ActiveRoomCode, System.StringComparison.Ordinal))
        {
            return serverReported;
        }

        int realtimeCount = Mathf.Clamp(bootstrap.CurrentConnectedPlayers, 0, maxPlayers);
        return realtimeCount;
    }

    private void JoinRoomFromPublicRow(RoomPublicInfo room)
    {
        if (room == null)
        {
            this.SetStatus("Data room tidak valid.");
            return;
        }

        cachedPublicRoom = room;
        if (joinRoomCodeInput != null)
        {
            joinRoomCodeInput.text = room.roomCode;
        }

        this.JoinFoundPublicRoom();
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

    private void JoinFoundPublicRoom()
    {
        if (cachedPublicRoom == null)
        {
            this.SetStatus("Belum ada room public yang dipilih.");
            return;
        }

        string playerName = this.ReadPlayerName();
        string password = joinPasswordInput != null ? joinPasswordInput.text : string.Empty;

        if (!useRoomDirectoryApi)
        {
            this.SetStatus("Room Directory API nonaktif.");
            return;
        }

        RoomJoinRequest request = new RoomJoinRequest
        {
            roomName = cachedPublicRoom.roomName,
            roomCode = cachedPublicRoom.roomCode,
            password = password,
            playerName = playerName
        };

        roomDirectoryClient.JoinRoom(request, (response, error) =>
        {
            if (!string.IsNullOrWhiteSpace(error) || response == null || !response.success)
            {
                this.SetStatus("Join public room gagal: " + (response != null ? response.message : error));
                return;
            }

            activeRoomId = response.roomId ?? string.Empty;
            string joinAddress = string.IsNullOrWhiteSpace(response.hostAddress) ? defaultJoinAddress : response.hostAddress;
            ushort joinPort = response.hostPort > 0 ? (ushort)response.hostPort : defaultJoinPort;
            string joinCode = string.IsNullOrWhiteSpace(response.roomCode) ? cachedPublicRoom.roomCode : response.roomCode;

            if (joinAddressInput != null) joinAddressInput.text = joinAddress;
            if (joinPortInput != null) joinPortInput.text = joinPort.ToString();
            if (joinRoomCodeInput != null) joinRoomCodeInput.text = joinCode;

            bootstrap.JoinRoom(joinAddress, joinPort, joinCode, password, playerName);
            this.SetHostControlMode(false);
            this.SetStatus("Mencoba join public room '" + response.roomName + "'...");
        });
    }

    private void HostStartLobby()
    {
        this.ResolveBootstrap();
        if (bootstrap == null)
        {
            this.SetStatus("Bootstrap tidak ditemukan.");
            return;
        }

        bootstrap.StartOfficeLobbySceneAsHost();
        this.TryNotifyRoomStage("office_lobby");
    }

    private void HostStartForest()
    {
        this.ResolveBootstrap();
        if (bootstrap == null)
        {
            this.SetStatus("Bootstrap tidak ditemukan.");
            return;
        }

        bootstrap.StartForestSceneAsHost();
        this.TryNotifyRoomStage("forest");
    }

    private void TryNotifyRoomStage(string stage)
    {
        if (!useRoomDirectoryApi || roomDirectoryClient == null || string.IsNullOrWhiteSpace(activeRoomId))
        {
            return;
        }

        roomDirectoryClient.UpdateRoomStage(activeRoomId, stage, (_, _) => { });
    }

    private void RefreshKickList()
    {
        this.ResolveBootstrap();
        if (!hostControlMode || bootstrap == null || kickListContainer == null)
        {
            return;
        }

        this.ClearKickRows();

        IReadOnlyList<ulong> clientIds = bootstrap.GetKickableClientIds();
        if (clientIds == null || clientIds.Count == 0)
        {
            this.CreateKickInfoRow("Belum ada player lain di room.");
            return;
        }

        for (int i = 0; i < clientIds.Count; i++)
        {
            ulong clientId = clientIds[i];
            this.CreateKickRow(clientId);
        }
    }

    private void CreateKickInfoRow(string text)
    {
        GameObject row = new GameObject("KickInfo", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.SetParent(kickListContainer, false);

        LayoutElement layout = row.AddComponent<LayoutElement>();
        layout.preferredHeight = 28f;

        TextMeshProUGUI label = row.GetComponent<TextMeshProUGUI>();
        label.fontSize = 15f;
        label.alignment = TextAlignmentOptions.Left;
        label.color = new Color(0.9f, 0.95f, 1f, 1f);
        label.text = text;

        kickEntryRows.Add(row);
    }

    private void CreateKickRow(ulong clientId)
    {
        GameObject row = new GameObject("KickRow_" + clientId, typeof(RectTransform), typeof(Image));
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
        label.text = bootstrap != null ? bootstrap.GetClientDisplayName(clientId) : ("Client " + clientId);
        LayoutElement labelLayout = labelObj.AddComponent<LayoutElement>();
        labelLayout.flexibleWidth = 1f;

        GameObject btnObj = new GameObject("KickButton", typeof(RectTransform), typeof(Image), typeof(Button));
        btnObj.transform.SetParent(rowRect, false);
        Image btnImage = btnObj.GetComponent<Image>();
        btnImage.color = new Color(0.72f, 0.24f, 0.24f, 1f);
        LayoutElement btnLayout = btnObj.AddComponent<LayoutElement>();
        btnLayout.preferredWidth = 72f;

        GameObject btnLabelObj = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform btnLabelRect = btnLabelObj.GetComponent<RectTransform>();
        btnLabelRect.SetParent(btnObj.transform, false);
        btnLabelRect.anchorMin = Vector2.zero;
        btnLabelRect.anchorMax = Vector2.one;
        btnLabelRect.offsetMin = Vector2.zero;
        btnLabelRect.offsetMax = Vector2.zero;
        TextMeshProUGUI btnLabel = btnLabelObj.GetComponent<TextMeshProUGUI>();
        btnLabel.text = "KICK";
        btnLabel.fontSize = 14f;
        btnLabel.alignment = TextAlignmentOptions.Center;
        btnLabel.color = Color.white;

        Button button = btnObj.GetComponent<Button>();
        ulong captureId = clientId;
        button.onClick.AddListener(() => this.KickClient(captureId));

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

    private void KickClient(ulong clientId)
    {
        this.ResolveBootstrap();
        if (bootstrap == null)
        {
            this.SetStatus("Bootstrap tidak ditemukan.");
            return;
        }

        if (bootstrap.TryKickClient(clientId, out string reason))
        {
            this.SetStatus("Client " + clientId + " dikeluarkan dari room.");
            this.RefreshKickList();
            return;
        }

        this.SetStatus("Gagal kick client " + clientId + ": " + reason);
    }

    private void StopSession()
    {
        this.ResolveBootstrap();
        if (bootstrap != null)
        {
            bootstrap.StopSession();
        }

        MainMenuSessionState.Clear();
        activeRoomId = string.Empty;
        cachedPublicRoom = null;
        this.SetHostControlMode(false);
        this.SetStatus("Session dihentikan.");
        this.RefreshRoomInfo();
    }

    private string ReadPlayerName()
    {
        return this.ReadOrDefault(playerNameInput, "Player");
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
        if (hostControlMode)
        {
            this.RefreshKickList();
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

        roomInfoText.text = "Room: " + bootstrap.ActiveRoomSummary;
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
}
