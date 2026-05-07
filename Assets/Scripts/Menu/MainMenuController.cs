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
    [SerializeField] private string roomDirectoryBaseUrl = "http://31.56.56.8:9011";
    [SerializeField] private bool showAdvancedEndpointInputs = false;

    private readonly List<GameObject> roomEntryRows = new List<GameObject>();
    private readonly List<GameObject> kickEntryRows = new List<GameObject>();

    private string activeRoomId = string.Empty;
    private RoomPublicInfo cachedPublicRoom;
    private bool hostControlMode;
    private bool isRoomHostSession;
    private float nextHostUiRefreshTime;
    private float nextStagePollTime;
    private bool stagePollInFlight;
    private string lastObservedRoomStage = string.Empty;

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
        this.RefreshHostActionButtons();

        if (!hostControlMode)
        {
            this.TryPollRoomStageFromDirectory();
            return;
        }

        if (Time.unscaledTime >= nextHostUiRefreshTime)
        {
            nextHostUiRefreshTime = Time.unscaledTime + 0.7f;
            this.RefreshKickList();
        }

        this.TryPollRoomStageFromDirectory();
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
        isRoomHostSession = false;
        this.SetHostControlMode(false);
        this.SearchPublicRoomsByName();
    }

    private void PlaySolo()
    {
        string playerName = this.ReadPlayerName();
        isRoomHostSession = false;
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
        this.LoadSceneSafely(officeLobbySceneName);
    }

    private void CreateRoomAsHost()
    {
        this.ResolveBootstrap();
        if (bootstrap == null)
        {
            this.SetStatus("CoopNetworkBootstrap belum ada di scene MainMenu.");
            return;
        }

        if (isRoomHostSession && bootstrap.IsClientActive)
        {
            this.SetHostControlMode(true);
            this.SetStatus("Host room sudah aktif. Gunakan panel host untuk Start/Leave/Kick.");
            this.RefreshRoomInfo();
            return;
        }

        string playerName = this.ReadPlayerName();
        string roomName = this.ReadOrDefault(roomNameInput, "Room Survival");
        string roomCode = this.ReadOrDefault(roomCodeInput, "ROOM01");
        string password = roomPasswordInput != null ? roomPasswordInput.text : string.Empty;
        bool isPrivate = privateRoomToggle != null && privateRoomToggle.isOn;
        int maxPlayers = this.ReadMaxPlayers();
        string serverAddress = this.ReadOrDefault(joinAddressInput, defaultJoinAddress);
        ushort serverPort = this.ReadPort(joinPortInput, defaultJoinPort);

        this.ResolveRoomDirectoryClient();
        if (roomDirectoryClient != null)
        {
            roomDirectoryClient.BaseUrl = roomDirectoryBaseUrl;
        }

        if (useRoomDirectoryApi)
        {
            if (roomDirectoryClient == null)
            {
                this.SetStatus("Room directory client tidak tersedia.");
                return;
            }

            roomDirectoryClient.SearchPublicRooms(string.Empty, (searchResponse, searchError) =>
            {
                string resolvedRoomCode = this.ResolveAvailableRoomCode(roomCode, searchResponse);
                if (!string.Equals(resolvedRoomCode, roomCode, System.StringComparison.OrdinalIgnoreCase))
                {
                    if (roomCodeInput != null) roomCodeInput.text = resolvedRoomCode;
                    if (joinRoomCodeInput != null) joinRoomCodeInput.text = resolvedRoomCode;
                }

                this.CreateRoomOnDirectory(
                    playerName,
                    roomName,
                    resolvedRoomCode,
                    password,
                    isPrivate,
                    maxPlayers,
                    serverAddress,
                    serverPort);
            });
        }
        else
        {
            activeRoomId = string.Empty;
            this.StartDedicatedRoomSession(
                SessionPlayMode.HostRoom,
                playerName,
                roomName,
                roomCode,
                password,
                isPrivate,
                maxPlayers,
                serverAddress,
                serverPort,
                asRoomHost: true);
            this.RefreshRoomInfo();
            this.SetStatus("Room dibuat (tanpa directory API) dan host tersambung ke dedicated server.");
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

        if (!useRoomDirectoryApi)
        {
            this.StartDedicatedRoomSession(
                SessionPlayMode.JoinRoom,
                playerName,
                "Joined Room",
                joinCode,
                joinPassword,
                !string.IsNullOrEmpty(joinPassword),
                4,
                joinAddress,
                joinPort,
                asRoomHost: false);
            this.SetStatus("Mencoba join room...");
            this.SetHostControlMode(true);
            return;
        }

        this.ResolveRoomDirectoryClient();
        if (roomDirectoryClient == null)
        {
            this.StartDedicatedRoomSession(
                SessionPlayMode.JoinRoom,
                playerName,
                "Joined Room",
                joinCode,
                joinPassword,
                !string.IsNullOrEmpty(joinPassword),
                4,
                joinAddress,
                joinPort,
                asRoomHost: false);
            this.SetStatus("Room API tidak tersedia, fallback join langsung ke dedicated server.");
            this.SetHostControlMode(true);
            return;
        }

        RoomJoinRequest joinRequest = new RoomJoinRequest
        {
            roomName = this.ReadOrDefault(publicRoomSearchInput, string.Empty),
            roomCode = joinCode,
            password = joinPassword,
            playerName = playerName
        };

        roomDirectoryClient.JoinRoom(joinRequest, (response, error) =>
        {
            if (!string.IsNullOrWhiteSpace(error) || response == null || !response.success)
            {
                this.SetStatus("Join room gagal: " + (string.IsNullOrWhiteSpace(error) ? response?.message : error));
                return;
            }

            activeRoomId = response.roomId ?? activeRoomId;
            string resolvedAddress = string.IsNullOrWhiteSpace(response.hostAddress) ? joinAddress : response.hostAddress;
            ushort resolvedPort = response.hostPort <= 0 ? joinPort : (ushort)response.hostPort;
            string resolvedCode = string.IsNullOrWhiteSpace(response.roomCode) ? joinCode : response.roomCode;

            if (joinAddressInput != null) joinAddressInput.text = resolvedAddress;
            if (joinPortInput != null) joinPortInput.text = resolvedPort.ToString();
            if (joinRoomCodeInput != null) joinRoomCodeInput.text = resolvedCode;

            this.StartDedicatedRoomSession(
                SessionPlayMode.JoinRoom,
                playerName,
                string.IsNullOrWhiteSpace(response.roomName) ? "Joined Room" : response.roomName,
                resolvedCode,
                joinPassword,
                response.isPrivate,
                response.maxPlayers <= 0 ? 4 : response.maxPlayers,
                resolvedAddress,
                resolvedPort,
                asRoomHost: false);
            this.SetStatus("Mencoba join room...");
            this.SetHostControlMode(true);
        });
    }

    private void CreateRoomOnDirectory(
        string playerName,
        string roomName,
        string roomCode,
        string password,
        bool isPrivate,
        int maxPlayers,
        string serverAddress,
        ushort serverPort)
    {
        RoomCreateRequest createRequest = new RoomCreateRequest
        {
            roomName = roomName,
            roomCode = roomCode,
            password = password,
            isPrivate = isPrivate,
            maxPlayers = maxPlayers,
            hostAddress = serverAddress,
            hostPort = serverPort,
            hostPlayerName = playerName
        };

        roomDirectoryClient.CreateRoom(createRequest, (response, error) =>
        {
            if (!string.IsNullOrWhiteSpace(error) || response == null || !response.success)
            {
                this.SetStatus("Gagal buat room di server directory: " + (string.IsNullOrWhiteSpace(error) ? response?.message : error));
                return;
            }

            activeRoomId = response.roomId ?? string.Empty;
            string responseCode = string.IsNullOrWhiteSpace(response.roomCode) ? roomCode : response.roomCode;
            string responseAddress = string.IsNullOrWhiteSpace(response.hostAddress) ? serverAddress : response.hostAddress;
            ushort responsePort = response.hostPort <= 0 ? serverPort : (ushort)response.hostPort;

            if (joinRoomCodeInput != null) joinRoomCodeInput.text = responseCode;
            if (joinAddressInput != null) joinAddressInput.text = responseAddress;
            if (joinPortInput != null) joinPortInput.text = responsePort.ToString();

            this.StartDedicatedRoomSession(
                SessionPlayMode.HostRoom,
                playerName,
                roomName,
                responseCode,
                password,
                isPrivate,
                maxPlayers,
                responseAddress,
                responsePort,
                asRoomHost: true);

            this.RefreshRoomInfo();
            this.SetStatus("Room dibuat. Sekarang host tersambung sebagai client room owner di VPS.");
            this.SearchPublicRoomsByName();
        });
    }

    private string ResolveAvailableRoomCode(string requestedRoomCode, RoomSearchResponse searchResponse)
    {
        string normalizedRequestedCode = this.NormalizeRoomCode(requestedRoomCode);
        if (string.IsNullOrWhiteSpace(normalizedRequestedCode))
        {
            normalizedRequestedCode = "ROOM01";
        }

        if (!RoomCodeExists(normalizedRequestedCode, searchResponse))
        {
            return normalizedRequestedCode;
        }

        for (int i = 0; i < 100; i++)
        {
            string candidate = "ROOM" + Random.Range(1000, 9999);
            if (!RoomCodeExists(candidate, searchResponse))
            {
                return candidate;
            }
        }

        return normalizedRequestedCode + Random.Range(10, 99);
    }

    private static bool RoomCodeExists(string roomCode, RoomSearchResponse searchResponse)
    {
        if (searchResponse == null || searchResponse.rooms == null || string.IsNullOrWhiteSpace(roomCode))
        {
            return false;
        }

        for (int i = 0; i < searchResponse.rooms.Count; i++)
        {
            RoomPublicInfo room = searchResponse.rooms[i];
            if (room != null && string.Equals(room.roomCode, roomCode, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void SearchPublicRoomsByName()
    {
        if (!useRoomDirectoryApi)
        {
            this.SetStatus("Room Directory API nonaktif.");
            return;
        }

        this.ResolveRoomDirectoryClient();
        if (roomDirectoryClient == null)
        {
            this.SetPublicRoomResult("Room Directory API tidak tersedia.");
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
                this.SetPublicRoomResult(string.IsNullOrWhiteSpace(searchName)
                    ? "Belum ada room public yang bisa di-join."
                    : "Room public tidak ditemukan.");
                return;
            }

            cachedPublicRoom = this.FindFirstJoinablePublicRoom(response.rooms);
            this.RebuildPublicRoomRows(response.rooms);
            int joinableRoomCount = this.CountJoinablePublicRooms(response.rooms);
            this.SetPublicRoomResult(joinableRoomCount > 0
                ? "Ditemukan " + joinableRoomCount + " room public yang bisa di-join."
                : "Tidak ada room public yang masih punya slot kosong.");
        });
    }

    private void RebuildPublicRoomRows(List<RoomPublicInfo> rooms)
    {
        this.ClearPublicRoomRows();
        if (publicRoomListContainer == null)
        {
            return;
        }

        this.EnsureVerticalListLayout(publicRoomListContainer);
        int visibleIndex = 0;
        for (int i = 0; i < rooms.Count; i++)
        {
            RoomPublicInfo room = rooms[i];
            if (!this.IsJoinablePublicRoom(room))
            {
                continue;
            }

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
            string privacy = room.isPrivate ? "Private" : "Public";
            int shownPlayers = this.ResolveDisplayPlayerCount(room);
            int shownMaxPlayers = Mathf.Max(1, room.maxPlayers);
            label.text = room.roomName + " | " + room.roomCode + " | " + privacy + " | " + shownPlayers + "/" + shownMaxPlayers + " player";
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
            RoomPublicInfo roomCapture = room;
            button.onClick.AddListener(() => this.JoinRoomFromPublicRow(roomCapture));

            roomEntryRows.Add(row);
            visibleIndex++;
        }

        if (visibleIndex == 0)
        {
            cachedPublicRoom = null;
            this.SetPublicRoomResult("Tidak ada room public yang masih punya slot kosong.");
        }
    }

    private int ResolveDisplayPlayerCount(RoomPublicInfo room)
    {
        int maxPlayers = Mathf.Max(1, room.maxPlayers);
        return Mathf.Clamp(room.currentPlayers, 0, maxPlayers);
    }

    private bool IsJoinablePublicRoom(RoomPublicInfo room)
    {
        if (room == null || room.isPrivate)
        {
            return false;
        }

        int maxPlayers = Mathf.Max(1, room.maxPlayers);
        return Mathf.Clamp(room.currentPlayers, 0, maxPlayers) < maxPlayers;
    }

    private RoomPublicInfo FindFirstJoinablePublicRoom(List<RoomPublicInfo> rooms)
    {
        if (rooms == null)
        {
            return null;
        }

        for (int i = 0; i < rooms.Count; i++)
        {
            if (this.IsJoinablePublicRoom(rooms[i]))
            {
                return rooms[i];
            }
        }

        return null;
    }

    private int CountJoinablePublicRooms(List<RoomPublicInfo> rooms)
    {
        if (rooms == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < rooms.Count; i++)
        {
            if (this.IsJoinablePublicRoom(rooms[i]))
            {
                count++;
            }
        }

        return count;
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

        if (publicRoomSearchInput != null && string.IsNullOrWhiteSpace(publicRoomSearchInput.text))
        {
            publicRoomSearchInput.text = room.roomName;
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

        string joinCode = string.IsNullOrWhiteSpace(cachedPublicRoom.roomCode)
            ? this.ReadOrDefault(joinRoomCodeInput, "ROOM01")
            : cachedPublicRoom.roomCode;
        string joinAddress = this.ReadOrDefault(joinAddressInput, defaultJoinAddress);
        ushort joinPort = this.ReadPort(joinPortInput, defaultJoinPort);

        if (joinRoomCodeInput != null) joinRoomCodeInput.text = joinCode;
        if (joinAddressInput != null) joinAddressInput.text = joinAddress;
        if (joinPortInput != null) joinPortInput.text = joinPort.ToString();

        this.JoinRoom();
    }

    private void HostStartLobby()
    {
        this.ResolveBootstrap();
        if (!isRoomHostSession || bootstrap == null || !bootstrap.IsClientActive || !bootstrap.IsClientConnected)
        {
            this.SetStatus("Room owner belum terhubung ke dedicated server.");
            return;
        }

        this.TryNotifyRoomStage("office_lobby");
        bootstrap.RequestOfficeLobbySceneAsRoomOwner();
    }

    private void HostStartForest()
    {
        this.ResolveBootstrap();
        if (!isRoomHostSession || bootstrap == null || !bootstrap.IsClientActive || !bootstrap.IsClientConnected)
        {
            this.SetStatus("Room owner belum terhubung ke dedicated server.");
            return;
        }

        this.TryNotifyRoomStage("forest");
        bootstrap.RequestForestSceneAsRoomOwner();
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
        this.EnsureVerticalListLayout(kickListContainer);

        if (!bootstrap.IsHostActive)
        {
            IReadOnlyList<string> memberNames = bootstrap.GetKnownRoomMemberNames();
            if (memberNames != null && memberNames.Count > 0)
            {
                this.CreateKickSectionHeader("Room Members (" + memberNames.Count + ")");
                for (int i = 0; i < memberNames.Count; i++)
                {
                    string memberName = string.IsNullOrWhiteSpace(memberNames[i]) ? $"Player {i + 1}" : memberNames[i];
                    this.CreateKickInfoRow((i + 1) + ". " + memberName);
                }
            }
            else
            {
                this.CreateKickSectionHeader("Room Members");
                this.CreateKickInfoRow("Menunggu data member room dari server...");
            }
            return;
        }

        IReadOnlyList<ulong> clientIds = bootstrap.GetKickableClientIds();
        this.CreateKickSectionHeader("Connected Players (" + (clientIds != null ? clientIds.Count : 0) + ")");
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
        isRoomHostSession = false;
        lastObservedRoomStage = string.Empty;
        stagePollInFlight = false;
        this.SetHostControlMode(false);
        this.SetStatus("Session dihentikan.");
        this.RefreshRoomInfo();
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

        if (bootstrap != null && isRoomHostSession)
        {
            if ((bootstrap.IsHostActive || bootstrap.IsClientActive) && !hostControlMode)
            {
                this.SetHostControlMode(true);
            }
            else if (!bootstrap.IsSessionListening && hostControlMode)
            {
                this.SetHostControlMode(false);
            }
        }
        else if (!isRoomHostSession && MainMenuSessionState.HasSession && MainMenuSessionState.Active.mode == SessionPlayMode.JoinRoom)
        {
            if (bootstrap != null && bootstrap.IsClientActive && !hostControlMode)
            {
                this.SetHostControlMode(true);
            }
            else if ((bootstrap == null || !bootstrap.IsSessionListening) && hostControlMode)
            {
                this.SetHostControlMode(false);
            }
        }
        else if (!isRoomHostSession && hostControlMode)
        {
            this.SetHostControlMode(false);
        }

        if (hostControlMode)
        {
            this.RefreshKickList();
        }
    }

    private void RefreshHostActionButtons()
    {
        this.ResolveBootstrap();
        bool isOwnerConnected = isRoomHostSession && bootstrap != null && bootstrap.IsClientActive && bootstrap.IsClientConnected;
        bool hasSession = bootstrap != null && bootstrap.IsSessionListening;

        if (startLobbyButton != null)
        {
            startLobbyButton.interactable = isOwnerConnected;
        }

        if (startForestButton != null)
        {
            startForestButton.interactable = isOwnerConnected;
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

    private void TryPollRoomStageFromDirectory()
    {
        if (!useRoomDirectoryApi || roomDirectoryClient == null || stagePollInFlight)
        {
            return;
        }

        if (!MainMenuSessionState.HasSession || MainMenuSessionState.Active.mode == SessionPlayMode.Solo)
        {
            return;
        }

        this.ResolveBootstrap();
        if (bootstrap != null && bootstrap.IsClientActive && !bootstrap.IsClientConnected)
        {
            return;
        }

        if (Time.unscaledTime < nextStagePollTime)
        {
            return;
        }

        string activeSceneName = SceneManager.GetActiveScene().name;
        if (!string.Equals(activeSceneName, "MainMenu", System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string roomCode = MainMenuSessionState.Active.roomCode;
        string roomName = MainMenuSessionState.Active.roomName;
        string searchTerm = !string.IsNullOrWhiteSpace(roomName) ? roomName : roomCode;
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return;
        }

        stagePollInFlight = true;
        nextStagePollTime = Time.unscaledTime + 1.25f;
        roomDirectoryClient.SearchPublicRooms(searchTerm, (response, error) =>
        {
            stagePollInFlight = false;
            if (!string.IsNullOrWhiteSpace(error) || response == null || response.rooms == null || response.rooms.Count == 0)
            {
                return;
            }

            RoomPublicInfo matchedRoom = null;
            for (int i = 0; i < response.rooms.Count; i++)
            {
                RoomPublicInfo candidate = response.rooms[i];
                if (candidate == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(activeRoomId) && string.Equals(candidate.roomId, activeRoomId, System.StringComparison.Ordinal))
                {
                    matchedRoom = candidate;
                    break;
                }

                if (string.Equals(candidate.roomCode, roomCode, System.StringComparison.OrdinalIgnoreCase))
                {
                    matchedRoom = candidate;
                }
            }

            if (matchedRoom == null)
            {
                return;
            }

            string stage = string.IsNullOrWhiteSpace(matchedRoom.status) ? string.Empty : matchedRoom.status.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(stage) || string.Equals(stage, lastObservedRoomStage, System.StringComparison.Ordinal))
            {
                return;
            }

            lastObservedRoomStage = stage;
            if (stage.Contains("office") || stage.Contains("lobby"))
            {
                if (bootstrap != null && bootstrap.IsSessionListening)
                {
                    this.SetStatus("Server sedang menyiapkan office lobby...");
                    return;
                }

                this.LoadSceneSafely(officeLobbySceneName);
                return;
            }

            if (stage.Contains("forest"))
            {
                if (bootstrap != null && bootstrap.IsSessionListening)
                {
                    this.SetStatus("Server sedang menyiapkan forest...");
                    return;
                }

                this.LoadSceneSafely(forestSceneName);
            }
        });
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

    private void StartDedicatedRoomSession(
        SessionPlayMode sessionMode,
        string playerName,
        string roomName,
        string roomCode,
        string roomPassword,
        bool roomPrivate,
        int maxPlayers,
        string serverAddress,
        ushort serverPort,
        bool asRoomHost)
    {
        this.ResolveBootstrap();
        if (bootstrap == null)
        {
            this.SetStatus("Bootstrap tidak ditemukan.");
            return;
        }

        if (bootstrap.IsSessionListening)
        {
            bootstrap.StopSession();
        }

        bootstrap.ConfigureRoom(roomName, roomCode, roomPassword, roomPrivate, maxPlayers, officeLobbySceneName);
        bootstrap.JoinRoom(serverAddress, serverPort, roomCode, roomPassword, playerName);

        MainMenuSessionState.Set(new MainMenuSessionState.SessionConfig
        {
            mode = sessionMode,
            playerName = playerName,
            roomName = roomName,
            roomCode = roomCode,
            roomPassword = roomPassword,
            roomPrivate = roomPrivate,
            maxPlayers = maxPlayers,
            hostAddress = serverAddress,
            hostPort = serverPort,
            lobbySceneName = officeLobbySceneName
        });

        isRoomHostSession = asRoomHost;
        lastObservedRoomStage = string.Empty;
        stagePollInFlight = false;
        nextStagePollTime = Time.unscaledTime + 0.5f;
        this.SetHostControlMode(asRoomHost);
        this.RefreshRoomInfo();
    }
}
