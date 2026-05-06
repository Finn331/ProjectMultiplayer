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

    private string activeRoomId = string.Empty;
    private RoomPublicInfo cachedPublicRoom;

    private void Awake()
    {
        this.ResolveBootstrap();
        this.ResolveRoomDirectoryClient();
        this.BindButtons();
        this.SetupDefaults();
        this.ApplyFlowStep(isInPlayGate: true);
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
            if (!maxPlayersSlider.wholeNumbers)
            {
                maxPlayersSlider.wholeNumbers = true;
            }

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

    private void OpenRoomFlow()
    {
        this.ApplyFlowStep(isInPlayGate: false);
        this.SetStatus("Pilih Solo atau Multiplayer.");
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
                if (string.IsNullOrWhiteSpace(error) && response != null && response.success)
                {
                    activeRoomId = response.roomId ?? string.Empty;
                }
            });
        }

        this.RefreshRoomInfo();
        this.SetStatus("Room dibuat. Bagikan Room Name/Code/Password ke teman.");
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
                this.SetPublicRoomResult("Search gagal: " + (string.IsNullOrWhiteSpace(error) ? "unknown" : error));
                return;
            }

            if (response.rooms == null || response.rooms.Count == 0)
            {
                cachedPublicRoom = null;
                this.SetPublicRoomResult("Room public tidak ditemukan.");
                return;
            }

            cachedPublicRoom = response.rooms[0];
            this.SetPublicRoomResult($"Ditemukan: {cachedPublicRoom.roomName} ({cachedPublicRoom.currentPlayers}/{cachedPublicRoom.maxPlayers})");
        });
    }

    private void JoinFoundPublicRoom()
    {
        if (cachedPublicRoom == null)
        {
            this.SetStatus("Belum ada hasil search room.");
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
            this.SetStatus($"Mencoba join public room '{response.roomName}'...");
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

    private void StopSession()
    {
        this.ResolveBootstrap();
        if (bootstrap != null)
        {
            bootstrap.StopSession();
        }

        MainMenuSessionState.Clear();
        activeRoomId = string.Empty;
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
