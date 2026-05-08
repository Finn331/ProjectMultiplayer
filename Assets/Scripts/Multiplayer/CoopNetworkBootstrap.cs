using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class CoopNetworkBootstrap : MonoBehaviour
{
    private const string DefaultPlayerPrefabPath = "Assets/Assets/Prefabs/NetworkPlayer.prefab";
    private const string RoomRosterMessageName = "RoomRosterSync";
    private const string RoomRosterRequestMessageName = "RoomRosterRequest";
    private const string RoomStageRequestMessageName = "RoomStageRequest";
    private const string RoomStageResponseMessageName = "RoomStageResponse";
    private static CoopNetworkBootstrap instance;

    [Serializable]
    private struct RoomJoinPayload
    {
        public string roomCode;
        public string password;
        public string playerName;
    }

    [Serializable]
    private class RoomRosterEntry
    {
        public string roomCode;
        public string[] playerNames;
    }

    [Serializable]
    private class RoomRosterSnapshot
    {
        public RoomRosterEntry[] rooms;
    }

    [Serializable]
    private struct RoomRosterRequestPayload
    {
        public string roomCode;
    }

    [Serializable]
    private struct RoomStageRequestPayload
    {
        public string roomCode;
        public string sceneName;
        public string stage;
    }

    [Serializable]
    private struct RoomStageResponsePayload
    {
        public bool accepted;
        public string stage;
        public string message;
    }

    public enum AutoStartMode
    {
        Manual,
        Host,
        Client,
        Server
    }

    [Header("Connection")]
    [SerializeField] private string serverAddress = "31.56.56.8";
    [SerializeField] private ushort serverPort = 9005;
    [SerializeField] private string listenAddress = "0.0.0.0";
    [SerializeField] private AutoStartMode autoStartMode = AutoStartMode.Manual;
    [SerializeField] private string vpsAddress = "31.56.56.8";
    [SerializeField] private ushort vpsPort = 9005;
    [SerializeField] private bool forceDedicatedServerInBatchMode = true;
    [SerializeField] private float clientConnectTimeoutSeconds = 10f;
    [SerializeField] private int connectTimeoutMs = 1000;
    [SerializeField] private int disconnectTimeoutMs = 5000;
    [SerializeField] private int maxConnectAttempts = 10;
    [SerializeField] private int maxPacketQueueSize = 1024;
    [SerializeField] private int maxSendQueueSize = 4 * 1024 * 1024;
    [SerializeField] private int maxPayloadSize = 6 * 1024;
    [SerializeField] private int heartbeatTimeoutMs = 1500;

    [Header("Room Settings")]
    [SerializeField] private string activeRoomName = "New Room";
    [SerializeField] private string activeRoomCode = "ROOM";
    [SerializeField] private string activeRoomPassword = string.Empty;
    [SerializeField] private bool activeRoomIsPrivate;
    [SerializeField, Range(1, 4)] private int roomMaxPlayers = 4;
    [SerializeField] private string roomLobbySceneName = "Gameplay";
    [SerializeField] private string forestSceneName = "Environment";
    [SerializeField] private bool enableRoomConnectionApproval = true;
    [SerializeField] private bool requireRoomCodeForClients = false;
    [SerializeField] private bool dedicatedServerMultiRoom = true;
    [SerializeField] private bool dedicatedServerEnforceRoomPassword = true;

    [Header("Networking")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private UnityTransport unityTransport;
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private List<GameObject> additionalNetworkPrefabs = new List<GameObject>();
    [SerializeField] private bool spawnScenePickablesOnServerStart = true;
    [SerializeField] private bool spawnSceneStorageChestsOnServerStart = true;
    [SerializeField] private bool disableScenePlayerBeforeStart = true;
    [SerializeField] private GameObject scenePlayerObject;

    [Header("Optional UI Buttons")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button serverButton;
    [SerializeField] private Button stopButton;

    private readonly HashSet<int> runtimeRegisteredPrefabIds = new HashSet<int>();
    private readonly Dictionary<ulong, string> approvedClientNames = new Dictionary<ulong, string>();
    private readonly Dictionary<ulong, string> connectedClientNames = new Dictionary<ulong, string>();
    private readonly Dictionary<ulong, string> approvedClientRoomCodes = new Dictionary<ulong, string>();
    private readonly Dictionary<ulong, string> connectedClientRoomCodes = new Dictionary<ulong, string>();
    private readonly Dictionary<string, HashSet<ulong>> roomMembers = new Dictionary<string, HashSet<ulong>>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> roomPasswords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ulong> roomOwners = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> knownRoomMemberNames = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    private bool callbacksBound;
    private bool waitingClientConnect;
    private bool approvalCallbackBound;
    private float connectDeadline;
    private float nextRosterBroadcastTime;
    private float nextRosterRequestTime;
    private string pendingJoinRoomCode = string.Empty;
    private string pendingJoinPassword = string.Empty;
    private string pendingJoinPlayerName = "Player";

    public event Action<string> StatusChanged;
    public event Action<string> RoomStageAccepted;

    public string ServerAddress
    {
        get => serverAddress;
        set => serverAddress = value;
    }

    public ushort ServerPort
    {
        get => serverPort;
        set => serverPort = value;
    }

    public int RoomMaxPlayers => Mathf.Clamp(roomMaxPlayers, 1, 4);
    public string ActiveRoomCode => this.NormalizeRoomCode(activeRoomCode);
    public int CurrentConnectedPlayers
    {
        get
        {
            if (networkManager == null || !networkManager.IsListening)
            {
                return 0;
            }

            if (networkManager.ConnectedClientsIds == null)
            {
                return networkManager.IsHost ? 1 : 0;
            }

            return networkManager.ConnectedClientsIds.Count;
        }
    }
    public string CurrentEndpoint => $"{serverAddress}:{serverPort}";
    public string VpsEndpoint => $"{vpsAddress}:{vpsPort}";
    public string LastStatusMessage { get; private set; } = "Offline";
    public bool IsSessionListening => networkManager != null && networkManager.IsListening;
    public bool IsHostActive => networkManager != null && networkManager.IsHost;
    public bool IsClientActive => networkManager != null && networkManager.IsClient;
    public bool IsClientConnected => networkManager != null && networkManager.IsConnectedClient;

    public string ActiveRoomSummary =>
        $"{activeRoomName} | Code: {activeRoomCode} | {(activeRoomIsPrivate ? "Private" : "Public")} | {RoomMaxPlayers}p";

    public string GetClientDisplayName(ulong clientId)
    {
        if (connectedClientNames.TryGetValue(clientId, out string playerName) && !string.IsNullOrWhiteSpace(playerName))
        {
            return playerName;
        }

        if (clientId == NetworkManager.ServerClientId)
        {
            return "Host";
        }

        return $"Client {clientId}";
    }

    public string GetClientRoomCode(ulong clientId)
    {
        if (connectedClientRoomCodes.TryGetValue(clientId, out string roomCode))
        {
            return roomCode ?? string.Empty;
        }

        return string.Empty;
    }

    public IReadOnlyList<ulong> GetKickableClientIds()
    {
        if (networkManager == null || !networkManager.IsHost || !networkManager.IsListening)
        {
            return Array.Empty<ulong>();
        }

        List<ulong> result = new List<ulong>();
        if (networkManager.ConnectedClientsIds == null)
        {
            return result;
        }

        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            if (clientId == NetworkManager.ServerClientId)
            {
                continue;
            }

            result.Add(clientId);
        }

        return result;
    }

    public IReadOnlyList<string> GetKnownRoomMemberNames()
    {
        string roomCode = this.GetLocalRoomCodeForUi();
        if (string.IsNullOrWhiteSpace(roomCode))
        {
            return Array.Empty<string>();
        }

        if (!knownRoomMemberNames.TryGetValue(roomCode, out List<string> names) || names == null || names.Count == 0)
        {
            return Array.Empty<string>();
        }

        return names;
    }

    public int GetRoomMemberCount(string roomCode)
    {
        string normalized = this.NormalizeRoomCode(roomCode);
        if (string.IsNullOrEmpty(normalized))
        {
            return 0;
        }

        if (!roomMembers.TryGetValue(normalized, out HashSet<ulong> members) || members == null)
        {
            return 0;
        }

        return members.Count;
    }

    public bool TryKickClient(ulong clientId, out string reason)
    {
        reason = string.Empty;
        if (networkManager == null || !networkManager.IsHost || !networkManager.IsListening)
        {
            reason = "Host belum aktif.";
            return false;
        }

        if (clientId == NetworkManager.ServerClientId)
        {
            reason = "Tidak bisa kick host.";
            return false;
        }

        bool exists = false;
        foreach (ulong connectedId in networkManager.ConnectedClientsIds)
        {
            if (connectedId != clientId)
            {
                continue;
            }

            exists = true;
            break;
        }

        if (!exists)
        {
            reason = "Client tidak ditemukan.";
            return false;
        }

        networkManager.DisconnectClient(clientId);
        approvedClientRoomCodes.Remove(clientId);
        connectedClientRoomCodes.Remove(clientId);
        this.RemoveClientFromRooms(clientId);
        this.SetStatus($"Host kick client {clientId}.");
        return true;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        this.EnsureNetworkStack();
        if (Application.isPlaying)
        {
            DontDestroyOnLoad(gameObject);
            if (networkManager != null)
            {
                DontDestroyOnLoad(networkManager.gameObject);
            }

            SceneManager.sceneLoaded -= this.OnUnitySceneLoaded;
            SceneManager.sceneLoaded += this.OnUnitySceneLoaded;
        }

        this.BindButtons();
        this.SetStatus("Offline");
    }

    private void Start()
    {
        if (forceDedicatedServerInBatchMode && Application.isBatchMode)
        {
            if (networkManager == null || !networkManager.IsListening)
            {
                this.StartServer();
            }

            return;
        }

        if (autoStartMode == AutoStartMode.Manual)
        {
            return;
        }

        if (networkManager != null && networkManager.IsListening)
        {
            return;
        }

        switch (autoStartMode)
        {
            case AutoStartMode.Host:
                this.StartHost();
                break;
            case AutoStartMode.Client:
                this.StartClient();
                break;
            case AutoStartMode.Server:
                this.StartServer();
                break;
        }
    }

    private void Update()
    {
        if (networkManager != null && networkManager.IsListening)
        {
            this.DisableScenePlayerIfNeeded();
            if (networkManager.IsServer && Time.unscaledTime >= nextRosterBroadcastTime)
            {
                nextRosterBroadcastTime = Time.unscaledTime + 1f;
                this.BroadcastRoomRosterSnapshot();
            }

            if (networkManager.IsClient &&
                networkManager.IsConnectedClient &&
                !networkManager.IsServer &&
                Time.unscaledTime >= nextRosterRequestTime)
            {
                nextRosterRequestTime = Time.unscaledTime + 1f;
                this.RequestRoomRosterFromServer();
            }
        }

        if (!waitingClientConnect)
        {
            return;
        }

        if (networkManager == null || !networkManager.IsClient)
        {
            waitingClientConnect = false;
            return;
        }

        if (networkManager.IsConnectedClient)
        {
            waitingClientConnect = false;
            return;
        }

        if (Time.unscaledTime >= connectDeadline)
        {
            waitingClientConnect = false;
            this.SetStatus($"Join timeout ke {CurrentEndpoint}. Pastikan host/server aktif dan UDP {serverPort} terbuka.");
        }
    }

    [ContextMenu("Start Host")]
    public void StartHost()
    {
        if (!this.PrepareNetworkManager())
        {
            return;
        }

        approvedClientNames.Clear();
        connectedClientNames.Clear();
        approvedClientRoomCodes.Clear();
        connectedClientRoomCodes.Clear();
        roomMembers.Clear();
        roomPasswords.Clear();
        roomOwners.Clear();
        knownRoomMemberNames.Clear();
        this.ApplyClientConnectionPayload(activeRoomCode, activeRoomPassword, pendingJoinPlayerName);
        bool started = networkManager.StartHost();
        if (started)
        {
            this.BindRosterMessageHandler();
            connectedClientNames[NetworkManager.ServerClientId] = string.IsNullOrWhiteSpace(pendingJoinPlayerName) ? "Host" : pendingJoinPlayerName;
            string hostRoomCode = this.NormalizeRoomCode(activeRoomCode);
            connectedClientRoomCodes[NetworkManager.ServerClientId] = hostRoomCode;
            this.AddClientToRoom(hostRoomCode, NetworkManager.ServerClientId);
            roomPasswords[hostRoomCode] = activeRoomPassword ?? string.Empty;
            this.BroadcastRoomRosterSnapshot();
        }

        this.SetStatus(started ? $"Room host aktif: {ActiveRoomSummary} @ {CurrentEndpoint}" : "Gagal start Host");
    }

    [ContextMenu("Start Client")]
    public void StartClient()
    {
        if (!this.PrepareNetworkManager())
        {
            return;
        }

        this.ApplyClientConnectionPayload(pendingJoinRoomCode, pendingJoinPassword, pendingJoinPlayerName);
        bool started = networkManager.StartClient();
        this.SetStatus(started ? $"Mencoba join {CurrentEndpoint}..." : $"Gagal mulai koneksi ke {CurrentEndpoint}");
        if (started)
        {
            this.BindRosterMessageHandler();
            waitingClientConnect = true;
            connectDeadline = Time.unscaledTime + Mathf.Max(3f, clientConnectTimeoutSeconds);
        }
    }

    [ContextMenu("Start Server")]
    public void StartServer()
    {
        if (!this.PrepareNetworkManager())
        {
            return;
        }

        approvedClientNames.Clear();
        connectedClientNames.Clear();
        approvedClientRoomCodes.Clear();
        connectedClientRoomCodes.Clear();
        roomMembers.Clear();
        roomPasswords.Clear();
        roomOwners.Clear();
        knownRoomMemberNames.Clear();
        bool started = networkManager.StartServer();
        if (started)
        {
            this.BindRosterMessageHandler();
        }

        this.SetStatus(started ? $"Server aktif di {CurrentEndpoint}" : "Gagal start Server");
    }

    [ContextMenu("Start Host Local")]
    public void StartHostLocal()
    {
        this.SetEndpoint("127.0.0.1", serverPort);
        this.StartHost();
    }

    [ContextMenu("Start Client To VPS")]
    public void StartClientToVps()
    {
        this.SetEndpoint(vpsAddress, vpsPort);
        this.StartClient();
    }

    public void ConfigureRoom(string roomName, string roomCode, string roomPassword, bool isPrivate, int maxPlayers, string lobbySceneName)
    {
        activeRoomName = string.IsNullOrWhiteSpace(roomName) ? "New Room" : roomName.Trim();
        activeRoomCode = this.NormalizeRoomCode(roomCode);
        activeRoomPassword = roomPassword ?? string.Empty;
        activeRoomIsPrivate = isPrivate;
        roomMaxPlayers = Mathf.Clamp(maxPlayers, 1, 4);
        if (!string.IsNullOrWhiteSpace(lobbySceneName))
        {
            roomLobbySceneName = lobbySceneName.Trim();
        }

        requireRoomCodeForClients = true;
    }

    public void ConfigureJoinCredentials(string roomCode, string roomPassword, string playerName)
    {
        pendingJoinRoomCode = this.NormalizeRoomCode(roomCode);
        pendingJoinPassword = roomPassword ?? string.Empty;
        pendingJoinPlayerName = string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim();
    }

    public void StartHostRoom(string hostAddress, ushort hostPort)
    {
        this.SetEndpoint(hostAddress, hostPort);
        this.StartHost();
    }

    public void JoinRoom(string hostAddress, ushort hostPort, string roomCode, string roomPassword, string playerName)
    {
        this.SetEndpoint(hostAddress, hostPort);
        this.ConfigureJoinCredentials(roomCode, roomPassword, playerName);
        this.StartClient();
    }

    public void StartLobbySceneAsHost()
    {
        this.StartOfficeLobbySceneAsHost();
    }

    public void RequestOfficeLobbySceneAsRoomOwner()
    {
        string targetScene = string.IsNullOrWhiteSpace(roomLobbySceneName) ? "Gameplay" : roomLobbySceneName.Trim();
        this.RequestSceneStageFromServer(targetScene, "office_lobby", "office lobby");
    }

    public void RequestForestSceneAsRoomOwner()
    {
        string targetScene = string.IsNullOrWhiteSpace(forestSceneName) ? "Environment" : forestSceneName.Trim();
        this.RequestSceneStageFromServer(targetScene, "forest", "forest");
    }

    [ContextMenu("Start Office Lobby Scene As Host")]
    public void StartOfficeLobbySceneAsHost()
    {
        string targetScene = string.IsNullOrWhiteSpace(roomLobbySceneName) ? "Gameplay" : roomLobbySceneName.Trim();
        this.StartSceneAsHostInternal(targetScene, "office lobby");
    }

    [ContextMenu("Start Forest Scene As Host")]
    public void StartForestSceneAsHost()
    {
        string targetScene = string.IsNullOrWhiteSpace(forestSceneName) ? "Environment" : forestSceneName.Trim();
        this.StartSceneAsHostInternal(targetScene, "forest");
    }

    private bool StartSceneAsHostInternal(string targetScene, string stageLabel)
    {
        if (networkManager == null || !networkManager.IsServer)
        {
            this.SetStatus($"Hanya server/host yang bisa Start ke {stageLabel}.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(targetScene))
        {
            this.SetStatus($"Scene untuk {stageLabel} belum diisi.");
            return false;
        }

        if (!this.IsSceneInBuildSettings(targetScene))
        {
            this.SetStatus($"Scene '{targetScene}' belum ada di Build Settings, jadi client tidak bisa ikut pindah.");
            return false;
        }

        if (networkManager.SceneManager != null && networkManager.NetworkConfig != null && networkManager.NetworkConfig.EnableSceneManagement)
        {
            var status = networkManager.SceneManager.LoadScene(targetScene, LoadSceneMode.Single);
            if (status == SceneEventProgressStatus.Started)
            {
                this.SetStatus($"Host memulai {stageLabel}: pindah ke scene '{targetScene}'.");
                this.RoomStageAccepted?.Invoke(stageLabel);
                return true;
            }

            this.SetStatus($"Gagal load scene network '{targetScene}' ({status}). Cek Build Settings / nama scene.");
            return false;
        }

        this.RoomStageAccepted?.Invoke(stageLabel);
        SceneManager.LoadScene(targetScene, LoadSceneMode.Single);
        this.SetStatus($"Scene management NGO nonaktif, fallback local load '{targetScene}' untuk {stageLabel}.");
        return true;
    }

    private void RequestSceneStageFromServer(string targetScene, string stage, string stageLabel)
    {
        if (networkManager == null || !networkManager.IsClient || !networkManager.IsConnectedClient)
        {
            this.SetStatus($"Belum tersambung ke server untuk memulai {stageLabel}.");
            return;
        }

        if (networkManager.IsServer)
        {
            this.StartSceneAsHostInternal(targetScene, stageLabel);
            return;
        }

        if (networkManager.CustomMessagingManager == null)
        {
            this.SetStatus("Custom messaging belum siap.");
            return;
        }

        RoomStageRequestPayload payload = new RoomStageRequestPayload
        {
            roomCode = this.GetLocalRoomCodeForUi(),
            sceneName = targetScene,
            stage = stage
        };

        string json = JsonUtility.ToJson(payload);
        int capacity = this.GetStringMessageCapacity(json);
        using (FastBufferWriter writer = new FastBufferWriter(capacity, Allocator.Temp))
        {
            writer.WriteValueSafe(json);
            networkManager.CustomMessagingManager.SendNamedMessage(
                RoomStageRequestMessageName,
                NetworkManager.ServerClientId,
                writer,
                NetworkDelivery.ReliableSequenced);
        }

        this.SetStatus($"Meminta server memulai {stageLabel}...");
    }

    public void SetEndpoint(string address, ushort port)
    {
        serverAddress = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
        serverPort = port;
    }

    [ContextMenu("Stop Network Session")]
    public void StopSession()
    {
        if (networkManager == null || !networkManager.IsListening)
        {
            return;
        }

        networkManager.Shutdown();
        approvedClientNames.Clear();
        connectedClientNames.Clear();
        approvedClientRoomCodes.Clear();
        connectedClientRoomCodes.Clear();
        roomMembers.Clear();
        roomPasswords.Clear();
        roomOwners.Clear();
        knownRoomMemberNames.Clear();
        waitingClientConnect = false;
        this.SetStatus("Offline");
    }

    private bool PrepareNetworkManager()
    {
        this.EnsureNetworkStack();
        if (networkManager == null)
        {
            Debug.LogError("NetworkManager is missing. Cannot start multiplayer.");
            return false;
        }

        if (networkManager.IsListening)
        {
            return false;
        }

        this.DisableScenePlayerIfNeeded();
        this.ConfigureTransport();
        this.RegisterNetworkPrefabs();
        this.ConfigureConnectionApproval();
        return true;
    }

    private void EnsureNetworkStack()
    {
        if (NetworkManager.Singleton != null)
        {
            networkManager = NetworkManager.Singleton;
        }

        if (networkManager == null)
        {
            networkManager = FindObjectOfType<NetworkManager>(true);
        }

        if (networkManager == null)
        {
            GameObject managerObject = new GameObject("NetworkManager");
            networkManager = managerObject.AddComponent<NetworkManager>();
        }

        if (unityTransport == null && networkManager != null)
        {
            unityTransport = networkManager.GetComponent<UnityTransport>();
        }

        if (unityTransport == null && networkManager != null)
        {
            unityTransport = networkManager.gameObject.AddComponent<UnityTransport>();
        }

        if (networkManager != null)
        {
            if (networkManager.NetworkConfig == null)
            {
                networkManager.NetworkConfig = new NetworkConfig();
            }

            if (networkManager.NetworkConfig.Prefabs == null)
            {
                networkManager.NetworkConfig.Prefabs = new NetworkPrefabs();
            }

            networkManager.NetworkConfig.EnableSceneManagement = true;
            networkManager.NetworkConfig.NetworkTransport = unityTransport;
            this.BindNetworkCallbacks();
        }
    }

    private void ConfigureTransport()
    {
        if (unityTransport == null)
        {
            return;
        }

        string targetAddress = string.IsNullOrWhiteSpace(serverAddress) ? "127.0.0.1" : serverAddress.Trim();
        unityTransport.MaxPacketQueueSize = Mathf.Max(UnityTransport.InitialMaxPacketQueueSize, maxPacketQueueSize);
        unityTransport.MaxSendQueueSize = Mathf.Max(0, maxSendQueueSize);
        unityTransport.MaxPayloadSize = Mathf.Max(UnityTransport.InitialMaxPayloadSize, maxPayloadSize);
        unityTransport.HeartbeatTimeoutMS = Mathf.Max(500, heartbeatTimeoutMs);
        unityTransport.ConnectTimeoutMS = Mathf.Max(100, connectTimeoutMs);
        unityTransport.DisconnectTimeoutMS = Mathf.Max(1000, disconnectTimeoutMs);
        unityTransport.MaxConnectAttempts = Mathf.Max(1, maxConnectAttempts);
        unityTransport.SetConnectionData(targetAddress, serverPort, listenAddress);
    }

    private void ConfigureConnectionApproval()
    {
        if (networkManager == null || networkManager.NetworkConfig == null)
        {
            return;
        }

        networkManager.NetworkConfig.ConnectionApproval = enableRoomConnectionApproval;
        if (!enableRoomConnectionApproval)
        {
            if (approvalCallbackBound)
            {
                networkManager.ConnectionApprovalCallback = null;
                approvalCallbackBound = false;
            }

            return;
        }

        networkManager.ConnectionApprovalCallback = this.HandleConnectionApproval;
        approvalCallbackBound = true;
    }

    private void HandleConnectionApproval(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        response.CreatePlayerObject = true;
        response.Position = null;
        response.Rotation = null;
        response.Pending = false;

        if (networkManager == null)
        {
            response.Approved = false;
            response.Reason = "NetworkManager null.";
            return;
        }

        if (request.ClientNetworkId == NetworkManager.ServerClientId)
        {
            response.Approved = true;
            return;
        }

        RoomJoinPayload payload = this.DecodeJoinPayload(request.Payload);
        string requestedPlayerName = string.IsNullOrWhiteSpace(payload.playerName) ? $"Client {request.ClientNetworkId}" : payload.playerName.Trim();
        approvedClientNames[request.ClientNetworkId] = requestedPlayerName;
        string incomingRoomCode = this.NormalizeRoomCode(payload.roomCode);
        string incomingPassword = payload.password ?? string.Empty;

        if (networkManager.IsServer && !networkManager.IsHost && dedicatedServerMultiRoom)
        {
            if (string.IsNullOrWhiteSpace(incomingRoomCode))
            {
                response.Approved = false;
                response.Reason = "Room code wajib diisi.";
                approvedClientNames.Remove(request.ClientNetworkId);
                return;
            }

            if (dedicatedServerEnforceRoomPassword)
            {
                if (roomPasswords.TryGetValue(incomingRoomCode, out string expectedPassword))
                {
                    if (!string.Equals(expectedPassword ?? string.Empty, incomingPassword, StringComparison.Ordinal))
                    {
                        response.Approved = false;
                        response.Reason = "Password room salah.";
                        approvedClientNames.Remove(request.ClientNetworkId);
                        return;
                    }
                }
                else
                {
                    roomPasswords[incomingRoomCode] = incomingPassword;
                }
            }

            int roomMemberCount = this.GetRoomMemberCount(incomingRoomCode);
            if (roomMemberCount >= this.RoomMaxPlayers)
            {
                response.Approved = false;
                response.Reason = $"Room {incomingRoomCode} penuh ({RoomMaxPlayers} pemain).";
                approvedClientNames.Remove(request.ClientNetworkId);
                return;
            }

            approvedClientRoomCodes[request.ClientNetworkId] = incomingRoomCode;
            response.Approved = true;
            return;
        }

        int currentPlayers = networkManager.ConnectedClientsList != null
            ? networkManager.ConnectedClientsList.Count
            : networkManager.ConnectedClientsIds.Count;

        if (currentPlayers >= this.RoomMaxPlayers)
        {
            response.Approved = false;
            response.Reason = $"Room penuh ({RoomMaxPlayers} pemain).";
            approvedClientNames.Remove(request.ClientNetworkId);
            return;
        }

        if (requireRoomCodeForClients && !string.Equals(incomingRoomCode, this.NormalizeRoomCode(activeRoomCode), StringComparison.OrdinalIgnoreCase))
        {
            response.Approved = false;
            response.Reason = "Room code salah.";
            approvedClientNames.Remove(request.ClientNetworkId);
            return;
        }

        if (activeRoomIsPrivate)
        {
            if (!string.Equals(incomingPassword, activeRoomPassword ?? string.Empty, StringComparison.Ordinal))
            {
                response.Approved = false;
                response.Reason = "Password room salah.";
                approvedClientNames.Remove(request.ClientNetworkId);
                return;
            }
        }

        approvedClientRoomCodes[request.ClientNetworkId] = incomingRoomCode;
        response.Approved = true;
    }

    private RoomJoinPayload DecodeJoinPayload(byte[] payloadBytes)
    {
        if (payloadBytes == null || payloadBytes.Length == 0)
        {
            return default;
        }

        try
        {
            string json = Encoding.UTF8.GetString(payloadBytes);
            if (string.IsNullOrWhiteSpace(json))
            {
                return default;
            }

            return JsonUtility.FromJson<RoomJoinPayload>(json);
        }
        catch
        {
            return default;
        }
    }

    private void ApplyClientConnectionPayload(string roomCode, string password, string playerName)
    {
        if (networkManager == null || networkManager.NetworkConfig == null)
        {
            return;
        }

        RoomJoinPayload payload = new RoomJoinPayload
        {
            roomCode = this.NormalizeRoomCode(roomCode),
            password = password ?? string.Empty,
            playerName = string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim()
        };

        string json = JsonUtility.ToJson(payload);
        networkManager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(json);
    }

    private string NormalizeRoomCode(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        return source.Trim().ToUpperInvariant();
    }

    private bool IsSceneInBuildSettings(string sceneNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(sceneNameOrPath))
        {
            return false;
        }

        string normalized = sceneNameOrPath.Trim();
        if (normalized.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
        {
            return SceneUtility.GetBuildIndexByScenePath(normalized) >= 0;
        }

        int total = SceneManager.sceneCountInBuildSettings;
        for (int i = 0; i < total; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string byFileName = System.IO.Path.GetFileNameWithoutExtension(path);
            if (string.Equals(byFileName, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(path, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void RegisterNetworkPrefabs()
    {
        if (networkManager == null)
        {
            return;
        }

        if (networkManager.NetworkConfig != null && playerPrefab != null)
        {
            networkManager.NetworkConfig.PlayerPrefab = playerPrefab;
        }

        runtimeRegisteredPrefabIds.Clear();
        this.TryRegisterPrefab(playerPrefab);

        for (int i = 0; i < additionalNetworkPrefabs.Count; i++)
        {
            this.TryRegisterPrefab(additionalNetworkPrefabs[i]);
        }
    }

    private void TryRegisterPrefab(GameObject prefab)
    {
        if (prefab == null)
        {
            return;
        }

        if (prefab.GetComponent<NetworkObject>() == null)
        {
            return;
        }

        int instanceId = prefab.GetInstanceID();
        if (runtimeRegisteredPrefabIds.Contains(instanceId))
        {
            return;
        }

        if (this.IsPrefabAlreadyRegistered(prefab))
        {
            runtimeRegisteredPrefabIds.Add(instanceId);
            return;
        }

        runtimeRegisteredPrefabIds.Add(instanceId);
        try
        {
            networkManager.AddNetworkPrefab(prefab);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Skip registering prefab '{prefab.name}' for network: {exception.Message}");
        }
    }

    private bool IsPrefabAlreadyRegistered(GameObject prefab)
    {
        if (networkManager == null || networkManager.NetworkConfig == null || networkManager.NetworkConfig.Prefabs == null)
        {
            return false;
        }

        var entries = networkManager.NetworkConfig.Prefabs.Prefabs;
        if (entries == null)
        {
            return false;
        }

        NetworkObject targetNetworkObject = prefab.GetComponent<NetworkObject>();
        uint targetHash = targetNetworkObject != null ? targetNetworkObject.PrefabIdHash : 0u;

        for (int i = 0; i < entries.Count; i++)
        {
            NetworkPrefab entry = entries[i];
            if (entry.Prefab == prefab || entry.SourcePrefabToOverride == prefab || entry.OverridingTargetPrefab == prefab)
            {
                return true;
            }

            if (targetHash == 0u)
            {
                continue;
            }

            if (this.HasMatchingHash(entry.Prefab, targetHash) ||
                this.HasMatchingHash(entry.SourcePrefabToOverride, targetHash) ||
                this.HasMatchingHash(entry.OverridingTargetPrefab, targetHash))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasMatchingHash(GameObject prefab, uint hash)
    {
        if (prefab == null || hash == 0u)
        {
            return false;
        }

        NetworkObject networkObject = prefab.GetComponent<NetworkObject>();
        return networkObject != null && networkObject.PrefabIdHash == hash;
    }

    private void BindButtons()
    {
        if (Application.isBatchMode)
        {
            return;
        }

        this.BindButton(hostButton, this.StartHostLocal);
        this.BindButton(joinButton, this.StartClientToVps);
        this.BindButton(serverButton, this.StartServer);
        this.BindButton(stopButton, this.StopSession);
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

    private void BindNetworkCallbacks()
    {
        if (networkManager == null || callbacksBound)
        {
            return;
        }

        networkManager.OnServerStarted += this.OnServerStarted;
        networkManager.OnClientConnectedCallback += this.OnClientConnected;
        networkManager.OnClientDisconnectCallback += this.OnClientDisconnected;
        networkManager.OnTransportFailure += this.OnTransportFailure;
        this.BindRosterMessageHandler();
        callbacksBound = true;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }

        SceneManager.sceneLoaded -= this.OnUnitySceneLoaded;

        if (networkManager == null || !callbacksBound)
        {
            return;
        }

        networkManager.OnServerStarted -= this.OnServerStarted;
        networkManager.OnClientConnectedCallback -= this.OnClientConnected;
        networkManager.OnClientDisconnectCallback -= this.OnClientDisconnected;
        networkManager.OnTransportFailure -= this.OnTransportFailure;
        SceneManager.sceneLoaded -= this.OnUnitySceneLoaded;
        this.UnbindRosterMessageHandler();
        callbacksBound = false;
    }

    private void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!Application.isPlaying || networkManager == null || !networkManager.IsListening)
        {
            return;
        }

        string sceneName = scene.name;
        bool isRoomScene =
            string.Equals(sceneName, roomLobbySceneName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sceneName, forestSceneName, StringComparison.OrdinalIgnoreCase);
        if (!isRoomScene)
        {
            return;
        }

        scenePlayerObject = null;

        if (networkManager.IsServer)
        {
            this.RepositionConnectedPlayersForLoadedScene(scene);
            this.SpawnScenePickablesForNetwork();
            this.SpawnSceneStorageChestsForNetwork();
            this.BroadcastRoomRosterSnapshot();
        }

        this.RefreshLocalPlayerForLoadedScene();
        this.SetStatus($"Scene '{sceneName}' siap. Player sudah masuk lobby.");
    }

    private void OnServerStarted()
    {
        if (networkManager == null || !networkManager.IsServer)
        {
            return;
        }

        if (spawnScenePickablesOnServerStart)
        {
            this.SpawnScenePickablesForNetwork();
        }

        if (spawnSceneStorageChestsOnServerStart)
        {
            this.SpawnSceneStorageChestsForNetwork();
        }

        this.BroadcastRoomRosterSnapshot();
    }

    private void RepositionConnectedPlayersForLoadedScene(Scene scene)
    {
        if (networkManager == null || !networkManager.IsServer || networkManager.ConnectedClients == null)
        {
            return;
        }

        this.GetSceneSpawnPose(scene, out Vector3 spawnPosition, out Quaternion spawnRotation);
        int index = 0;
        foreach (var pair in networkManager.ConnectedClients)
        {
            NetworkClient client = pair.Value;
            if (client == null || client.PlayerObject == null)
            {
                continue;
            }

            Vector3 offset = this.GetPlayerSpawnOffset(index, spawnRotation);
            this.TeleportPlayerObject(client.PlayerObject, spawnPosition + offset, spawnRotation);
            index++;
        }
    }

    private void RefreshLocalPlayerForLoadedScene()
    {
        if (networkManager == null || !networkManager.IsClient || networkManager.LocalClient == null || networkManager.LocalClient.PlayerObject == null)
        {
            return;
        }

        NetworkObject playerObject = networkManager.LocalClient.PlayerObject;
        playerObject.DestroyWithScene = false;

        NetworkPlayerOwnerSetup ownerSetup = playerObject.GetComponent<NetworkPlayerOwnerSetup>();
        if (ownerSetup != null)
        {
            ownerSetup.RefreshOwnerState();
        }

        FPSControllerMobile controller = playerObject.GetComponent<FPSControllerMobile>();
        if (controller != null)
        {
            controller.RefreshSceneInputBindings();
        }
    }

    private void TeleportPlayerObject(NetworkObject playerObject, Vector3 position, Quaternion rotation)
    {
        if (playerObject == null)
        {
            return;
        }

        CharacterController characterController = playerObject.GetComponent<CharacterController>();
        bool restoreController = characterController != null && characterController.enabled;
        if (restoreController)
        {
            characterController.enabled = false;
        }

        playerObject.transform.SetPositionAndRotation(position, rotation);

        OwnerDrivenNetworkTransform networkTransform = playerObject.GetComponent<OwnerDrivenNetworkTransform>();
        if (networkTransform != null)
        {
            networkTransform.ServerTeleport(position, rotation);
        }

        if (restoreController)
        {
            characterController.enabled = true;
        }
    }

    private Vector3 GetPlayerSpawnOffset(int index, Quaternion spawnRotation)
    {
        const float spacing = 1.35f;
        int column = index % 2;
        int row = index / 2;
        Vector3 localOffset = new Vector3((column - 0.5f) * spacing, 0f, row * spacing);
        return spawnRotation * localOffset;
    }

    private void GetSceneSpawnPose(Scene scene, out Vector3 position, out Quaternion rotation)
    {
        FPSControllerMobile[] controllers = FindObjectsOfType<FPSControllerMobile>(true);
        FPSControllerMobile fallback = null;
        for (int i = 0; i < controllers.Length; i++)
        {
            FPSControllerMobile controller = controllers[i];
            if (controller == null || controller.gameObject.scene != scene)
            {
                continue;
            }

            NetworkObject networkObject = controller.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSpawned)
            {
                continue;
            }

            if (string.Equals(controller.gameObject.name, "Player", StringComparison.OrdinalIgnoreCase))
            {
                position = controller.transform.position;
                rotation = controller.transform.rotation;
                return;
            }

            fallback = controller;
        }

        if (fallback != null)
        {
            position = fallback.transform.position;
            rotation = fallback.transform.rotation;
            return;
        }

        position = new Vector3(0f, 1f, -8.06f);
        rotation = Quaternion.identity;
    }

    private void SpawnScenePickablesForNetwork()
    {
        PickableItem[] pickables = FindObjectsOfType<PickableItem>(true);
        for (int i = 0; i < pickables.Length; i++)
        {
            PickableItem pickable = pickables[i];
            if (pickable == null || pickable.gameObject == null || !pickable.gameObject.activeInHierarchy)
            {
                continue;
            }

            NetworkObject networkObject = pickable.GetComponent<NetworkObject>();
            if (networkObject == null || networkObject.IsSpawned)
            {
                continue;
            }

            bool isSceneObject = networkObject.IsSceneObject == true;
            if (!isSceneObject && !this.IsPrefabAlreadyRegistered(pickable.gameObject))
            {
                continue;
            }

            try
            {
                networkObject.Spawn(true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Failed to spawn scene pickable '{pickable.name}': {exception.Message}");
            }
        }
    }

    private void SpawnSceneStorageChestsForNetwork()
    {
        StorageChest[] chests = FindObjectsOfType<StorageChest>(true);
        for (int i = 0; i < chests.Length; i++)
        {
            StorageChest chest = chests[i];
            if (chest == null || chest.gameObject == null || !chest.gameObject.activeInHierarchy)
            {
                continue;
            }

            NetworkObject networkObject = chest.GetComponent<NetworkObject>();
            if (networkObject == null || networkObject.IsSpawned)
            {
                continue;
            }

            bool isSceneObject = networkObject.IsSceneObject == true;
            if (!isSceneObject && !this.IsPrefabAlreadyRegistered(chest.gameObject))
            {
                continue;
            }

            try
            {
                networkObject.Spawn(true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Failed to spawn scene chest '{chest.name}': {exception.Message}");
            }
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (networkManager == null)
        {
            return;
        }

        if (networkManager.IsServer)
        {
            if (approvedClientNames.TryGetValue(clientId, out string approvedName))
            {
                connectedClientNames[clientId] = string.IsNullOrWhiteSpace(approvedName) ? $"Client {clientId}" : approvedName;
                approvedClientNames.Remove(clientId);
            }
            else if (!connectedClientNames.ContainsKey(clientId))
            {
                connectedClientNames[clientId] = clientId == NetworkManager.ServerClientId ? "Host" : $"Client {clientId}";
            }

            string roomCode = string.Empty;
            if (approvedClientRoomCodes.TryGetValue(clientId, out string approvedRoomCode))
            {
                roomCode = this.NormalizeRoomCode(approvedRoomCode);
                approvedClientRoomCodes.Remove(clientId);
            }

            if (!string.IsNullOrWhiteSpace(roomCode))
            {
                connectedClientRoomCodes[clientId] = roomCode;
                this.AddClientToRoom(roomCode, clientId);
            }

            this.EnsureConnectedPlayerDoesNotDestroyWithScene(clientId);
            this.BroadcastRoomRosterSnapshot();
        }

        if (networkManager.IsHost)
        {
            if (clientId == NetworkManager.ServerClientId)
            {
                connectedClientNames[clientId] = "Host";
            }
            else if (approvedClientNames.TryGetValue(clientId, out string approvedName))
            {
                connectedClientNames[clientId] = string.IsNullOrWhiteSpace(approvedName) ? $"Client {clientId}" : approvedName;
                approvedClientNames.Remove(clientId);
            }
            else
            {
                connectedClientNames[clientId] = $"Client {clientId}";
            }

            this.SetStatus($"Host aktif ({networkManager.ConnectedClientsIds.Count}/{RoomMaxPlayers})");
            return;
        }

        if (networkManager.IsClient && clientId == networkManager.LocalClientId)
        {
            this.BindRosterMessageHandler();
            waitingClientConnect = false;
            string localRoomCode = this.NormalizeRoomCode(pendingJoinRoomCode);
            if (!string.IsNullOrWhiteSpace(localRoomCode))
            {
                connectedClientRoomCodes[clientId] = localRoomCode;
                this.ApplyLocalRoomRosterFallback(localRoomCode);
            }

            this.EnsureLocalPlayerDoesNotDestroyWithScene();
            this.RequestRoomRosterFromServer();
            this.SetStatus($"Berhasil join room di {CurrentEndpoint}");
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (networkManager == null)
        {
            return;
        }

        approvedClientNames.Remove(clientId);
        approvedClientRoomCodes.Remove(clientId);
        connectedClientNames.Remove(clientId);
        connectedClientRoomCodes.Remove(clientId);
        this.RemoveClientFromRooms(clientId);
        if (networkManager.IsServer)
        {
            this.BroadcastRoomRosterSnapshot();
        }

        if (networkManager.IsClient && clientId == networkManager.LocalClientId)
        {
            waitingClientConnect = false;
            string reason = string.IsNullOrWhiteSpace(networkManager.DisconnectReason)
                ? "Tidak ada response dari server."
                : networkManager.DisconnectReason;
            this.SetStatus($"Join gagal / terputus dari {CurrentEndpoint}. Reason: {reason}");
            return;
        }

        if (networkManager.IsHost)
        {
            this.SetStatus($"Host aktif ({networkManager.ConnectedClientsIds.Count}/{RoomMaxPlayers})");
        }
    }

    private void BindRosterMessageHandler()
    {
        if (networkManager == null || networkManager.CustomMessagingManager == null)
        {
            return;
        }

        networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(RoomRosterMessageName);
        networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(RoomRosterRequestMessageName);
        networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(RoomStageRequestMessageName);
        networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(RoomStageResponseMessageName);
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(RoomRosterMessageName, this.OnRoomRosterMessageReceived);
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(RoomRosterRequestMessageName, this.OnRoomRosterRequestMessageReceived);
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(RoomStageRequestMessageName, this.OnRoomStageRequestMessageReceived);
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(RoomStageResponseMessageName, this.OnRoomStageResponseMessageReceived);
    }

    private void UnbindRosterMessageHandler()
    {
        if (networkManager == null || networkManager.CustomMessagingManager == null)
        {
            return;
        }

        networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(RoomRosterMessageName);
        networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(RoomRosterRequestMessageName);
        networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(RoomStageRequestMessageName);
        networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(RoomStageResponseMessageName);
    }

    private void OnRoomRosterMessageReceived(ulong senderClientId, FastBufferReader messagePayload)
    {
        messagePayload.ReadValueSafe(out string json);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            RoomRosterSnapshot snapshot = JsonUtility.FromJson<RoomRosterSnapshot>(json);
            this.ApplyRoomRosterSnapshot(snapshot);
        }
        catch
        {
        }
    }

    private void OnRoomRosterRequestMessageReceived(ulong senderClientId, FastBufferReader messagePayload)
    {
        if (networkManager == null || !networkManager.IsServer)
        {
            return;
        }

        string requestedRoomCode = string.Empty;
        messagePayload.ReadValueSafe(out string json);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                RoomRosterRequestPayload payload = JsonUtility.FromJson<RoomRosterRequestPayload>(json);
                requestedRoomCode = this.NormalizeRoomCode(payload.roomCode);
            }
            catch
            {
                requestedRoomCode = string.Empty;
            }
        }

        if (!connectedClientRoomCodes.TryGetValue(senderClientId, out string connectedRoomCode))
        {
            return;
        }

        string senderRoomCode = this.NormalizeRoomCode(connectedRoomCode);
        if (string.IsNullOrWhiteSpace(senderRoomCode))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(requestedRoomCode) &&
            !string.Equals(requestedRoomCode, senderRoomCode, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        this.SendRoomRosterSnapshotToClient(senderClientId, senderRoomCode);
    }

    private void OnRoomStageRequestMessageReceived(ulong senderClientId, FastBufferReader messagePayload)
    {
        if (networkManager == null || !networkManager.IsServer)
        {
            return;
        }

        messagePayload.ReadValueSafe(out string json);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        RoomStageRequestPayload payload;
        try
        {
            payload = JsonUtility.FromJson<RoomStageRequestPayload>(json);
        }
        catch
        {
            return;
        }

        string roomCode = this.NormalizeRoomCode(payload.roomCode);
        if (!this.IsRoomStageRequestAuthorized(senderClientId, roomCode))
        {
            string reason = $"Client {senderClientId} tidak punya izin start scene untuk room {roomCode}.";
            this.SetStatus(reason);
            this.SendRoomStageResponse(senderClientId, false, payload.stage, reason);
            return;
        }

        string sceneName = string.IsNullOrWhiteSpace(payload.sceneName) ? roomLobbySceneName : payload.sceneName.Trim();
        string stageLabel = string.IsNullOrWhiteSpace(payload.stage) ? "room stage" : payload.stage.Trim();
        bool started = this.StartSceneAsHostInternal(sceneName, stageLabel);
        this.SendRoomStageResponse(
            senderClientId,
            started,
            payload.stage,
            started ? $"Server memulai {stageLabel}." : LastStatusMessage);
    }

    private void OnRoomStageResponseMessageReceived(ulong senderClientId, FastBufferReader messagePayload)
    {
        if (networkManager == null || networkManager.IsServer)
        {
            return;
        }

        messagePayload.ReadValueSafe(out string json);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            RoomStageResponsePayload payload = JsonUtility.FromJson<RoomStageResponsePayload>(json);
            if (!string.IsNullOrWhiteSpace(payload.message))
            {
                this.SetStatus(payload.message);
            }

            if (payload.accepted)
            {
                this.RoomStageAccepted?.Invoke(payload.stage);
            }
        }
        catch
        {
        }
    }

    private void BroadcastRoomRosterSnapshot()
    {
        if (networkManager == null || !networkManager.IsServer || !networkManager.IsListening)
        {
            return;
        }

        RoomRosterSnapshot snapshot = this.BuildRoomRosterSnapshot();
        this.ApplyRoomRosterSnapshot(snapshot);

        if (networkManager.CustomMessagingManager == null)
        {
            return;
        }

        string json = JsonUtility.ToJson(snapshot);
        int capacity = this.GetStringMessageCapacity(json);
        using (FastBufferWriter writer = new FastBufferWriter(capacity, Allocator.Temp))
        {
            writer.WriteValueSafe(json);
            networkManager.CustomMessagingManager.SendNamedMessageToAll(RoomRosterMessageName, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    private void SendRoomRosterSnapshotToClient(ulong targetClientId, string roomCode)
    {
        if (networkManager == null ||
            !networkManager.IsServer ||
            !networkManager.IsListening ||
            networkManager.CustomMessagingManager == null)
        {
            return;
        }

        RoomRosterSnapshot snapshot = this.BuildRoomRosterSnapshot(roomCode);
        string json = JsonUtility.ToJson(snapshot);
        int capacity = this.GetStringMessageCapacity(json);
        using (FastBufferWriter writer = new FastBufferWriter(capacity, Allocator.Temp))
        {
            writer.WriteValueSafe(json);
            networkManager.CustomMessagingManager.SendNamedMessage(
                RoomRosterMessageName,
                targetClientId,
                writer,
                NetworkDelivery.ReliableSequenced);
        }
    }

    private void RequestRoomRosterFromServer()
    {
        if (networkManager == null ||
            !networkManager.IsClient ||
            !networkManager.IsConnectedClient ||
            networkManager.IsServer ||
            networkManager.CustomMessagingManager == null)
        {
            return;
        }

        string roomCode = this.GetLocalRoomCodeForUi();
        if (string.IsNullOrWhiteSpace(roomCode))
        {
            return;
        }

        RoomRosterRequestPayload payload = new RoomRosterRequestPayload
        {
            roomCode = roomCode
        };

        string json = JsonUtility.ToJson(payload);
        int capacity = this.GetStringMessageCapacity(json);
        using (FastBufferWriter writer = new FastBufferWriter(capacity, Allocator.Temp))
        {
            writer.WriteValueSafe(json);
            networkManager.CustomMessagingManager.SendNamedMessage(
                RoomRosterRequestMessageName,
                NetworkManager.ServerClientId,
                writer,
                NetworkDelivery.ReliableSequenced);
        }
    }

    private void SendRoomStageResponse(ulong targetClientId, bool accepted, string stage, string message)
    {
        if (networkManager == null ||
            !networkManager.IsServer ||
            !networkManager.IsListening ||
            networkManager.CustomMessagingManager == null ||
            targetClientId == NetworkManager.ServerClientId)
        {
            return;
        }

        RoomStageResponsePayload payload = new RoomStageResponsePayload
        {
            accepted = accepted,
            stage = string.IsNullOrWhiteSpace(stage) ? string.Empty : stage.Trim(),
            message = message ?? string.Empty
        };

        string json = JsonUtility.ToJson(payload);
        int capacity = this.GetStringMessageCapacity(json);
        using (FastBufferWriter writer = new FastBufferWriter(capacity, Allocator.Temp))
        {
            writer.WriteValueSafe(json);
            networkManager.CustomMessagingManager.SendNamedMessage(
                RoomStageResponseMessageName,
                targetClientId,
                writer,
                NetworkDelivery.ReliableSequenced);
        }
    }

    private RoomRosterSnapshot BuildRoomRosterSnapshot()
    {
        return this.BuildRoomRosterSnapshot(string.Empty);
    }

    private RoomRosterSnapshot BuildRoomRosterSnapshot(string onlyRoomCode)
    {
        List<RoomRosterEntry> entries = new List<RoomRosterEntry>();
        string normalizedOnlyRoomCode = this.NormalizeRoomCode(onlyRoomCode);
        foreach (var pair in roomMembers)
        {
            string roomCode = pair.Key;
            HashSet<ulong> members = pair.Value;
            if (string.IsNullOrWhiteSpace(roomCode) || members == null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(normalizedOnlyRoomCode) &&
                !string.Equals(this.NormalizeRoomCode(roomCode), normalizedOnlyRoomCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            List<string> names = new List<string>();
            foreach (ulong memberId in members)
            {
                names.Add(this.GetClientDisplayName(memberId));
            }

            entries.Add(new RoomRosterEntry
            {
                roomCode = roomCode,
                playerNames = names.ToArray()
            });
        }

        return new RoomRosterSnapshot
        {
            rooms = entries.ToArray()
        };
    }

    private int GetStringMessageCapacity(string value)
    {
        int charCount = string.IsNullOrEmpty(value) ? 0 : value.Length;
        int utf8Bytes = string.IsNullOrEmpty(value) ? 0 : Encoding.UTF8.GetByteCount(value);
        return Mathf.Max(1024, Mathf.Max(utf8Bytes, charCount * 4) + 256);
    }

    private void ApplyRoomRosterSnapshot(RoomRosterSnapshot snapshot)
    {
        knownRoomMemberNames.Clear();
        if (snapshot == null || snapshot.rooms == null)
        {
            return;
        }

        for (int i = 0; i < snapshot.rooms.Length; i++)
        {
            RoomRosterEntry entry = snapshot.rooms[i];
            if (entry == null)
            {
                continue;
            }

            string roomCode = this.NormalizeRoomCode(entry.roomCode);
            if (string.IsNullOrWhiteSpace(roomCode))
            {
                continue;
            }

            List<string> names = new List<string>();
            if (entry.playerNames != null)
            {
                for (int j = 0; j < entry.playerNames.Length; j++)
                {
                    string name = entry.playerNames[j];
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name);
                    }
                }
            }

            knownRoomMemberNames[roomCode] = names;
        }
    }

    private void ApplyLocalRoomRosterFallback(string roomCode)
    {
        string normalizedRoomCode = this.NormalizeRoomCode(roomCode);
        if (string.IsNullOrWhiteSpace(normalizedRoomCode))
        {
            return;
        }

        if (!knownRoomMemberNames.TryGetValue(normalizedRoomCode, out List<string> names) || names == null)
        {
            names = new List<string>();
            knownRoomMemberNames[normalizedRoomCode] = names;
        }

        string localName = string.IsNullOrWhiteSpace(pendingJoinPlayerName) ? "Player" : pendingJoinPlayerName.Trim();
        if (!names.Contains(localName))
        {
            names.Add(localName);
        }
    }

    private string GetLocalRoomCodeForUi()
    {
        if (networkManager != null && networkManager.IsListening)
        {
            ulong localClientId = networkManager.LocalClientId;
            if (connectedClientRoomCodes.TryGetValue(localClientId, out string connectedRoomCode) && !string.IsNullOrWhiteSpace(connectedRoomCode))
            {
                return this.NormalizeRoomCode(connectedRoomCode);
            }
        }

        if (!string.IsNullOrWhiteSpace(pendingJoinRoomCode))
        {
            return this.NormalizeRoomCode(pendingJoinRoomCode);
        }

        return this.NormalizeRoomCode(activeRoomCode);
    }

    private void EnsureConnectedPlayerDoesNotDestroyWithScene(ulong clientId)
    {
        if (networkManager == null || !networkManager.IsServer || networkManager.ConnectedClients == null)
        {
            return;
        }

        if (!networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient networkClient) || networkClient == null || networkClient.PlayerObject == null)
        {
            return;
        }

        networkClient.PlayerObject.DestroyWithScene = false;
    }

    private void EnsureLocalPlayerDoesNotDestroyWithScene()
    {
        if (networkManager == null || !networkManager.IsClient)
        {
            return;
        }

        NetworkClient localClient = networkManager.LocalClient;
        if (localClient == null || localClient.PlayerObject == null)
        {
            return;
        }

        localClient.PlayerObject.DestroyWithScene = false;
    }

    private void OnTransportFailure()
    {
        waitingClientConnect = false;
        this.SetStatus($"Transport gagal ke {CurrentEndpoint}. Pastikan server aktif dan UDP {serverPort} terbuka.");
    }

    private void AddClientToRoom(string roomCode, ulong clientId)
    {
        string normalized = this.NormalizeRoomCode(roomCode);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (!roomMembers.TryGetValue(normalized, out HashSet<ulong> members) || members == null)
        {
            members = new HashSet<ulong>();
            roomMembers[normalized] = members;
        }

        members.Add(clientId);
        if (!roomOwners.ContainsKey(normalized))
        {
            roomOwners[normalized] = clientId;
        }
    }

    private void RemoveClientFromRooms(ulong clientId)
    {
        if (roomMembers.Count == 0)
        {
            return;
        }

        List<string> emptyRooms = null;
        foreach (var pair in roomMembers)
        {
            HashSet<ulong> members = pair.Value;
            if (members == null)
            {
                continue;
            }

            members.Remove(clientId);
            if (roomOwners.TryGetValue(pair.Key, out ulong ownerClientId) && ownerClientId == clientId)
            {
                if (members.Count > 0)
                {
                    roomOwners[pair.Key] = this.GetFirstRoomMember(members);
                }
                else
                {
                    roomOwners.Remove(pair.Key);
                }
            }

            if (members.Count > 0)
            {
                continue;
            }

            if (emptyRooms == null)
            {
                emptyRooms = new List<string>();
            }

            emptyRooms.Add(pair.Key);
        }

        if (emptyRooms == null)
        {
            return;
        }

        for (int i = 0; i < emptyRooms.Count; i++)
        {
            string roomCode = emptyRooms[i];
            roomMembers.Remove(roomCode);
            roomPasswords.Remove(roomCode);
            roomOwners.Remove(roomCode);
        }
    }

    private ulong GetFirstRoomMember(HashSet<ulong> members)
    {
        foreach (ulong memberId in members)
        {
            return memberId;
        }

        return NetworkManager.ServerClientId;
    }

    private bool IsRoomStageRequestAuthorized(ulong senderClientId, string roomCode)
    {
        if (string.IsNullOrWhiteSpace(roomCode))
        {
            return false;
        }

        if (!connectedClientRoomCodes.TryGetValue(senderClientId, out string senderRoomCode) ||
            !string.Equals(this.NormalizeRoomCode(senderRoomCode), roomCode, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !roomOwners.TryGetValue(roomCode, out ulong ownerClientId) || ownerClientId == senderClientId;
    }

    private void SetStatus(string message)
    {
        LastStatusMessage = message;
        StatusChanged?.Invoke(message);
    }

    private void DisableScenePlayerIfNeeded()
    {
        if (!disableScenePlayerBeforeStart)
        {
            return;
        }

        if (scenePlayerObject == null)
        {
            GameObject namedPlayer = GameObject.Find("Player");
            if (namedPlayer != null)
            {
                scenePlayerObject = namedPlayer;
            }
        }

        if (scenePlayerObject == null)
        {
            FPSControllerMobile sceneController = FindObjectOfType<FPSControllerMobile>(true);
            if (sceneController != null)
            {
                scenePlayerObject = sceneController.gameObject;
            }
        }

        if (scenePlayerObject == null)
        {
            return;
        }

        NetworkObject sceneNetworkObject = scenePlayerObject.GetComponent<NetworkObject>();
        if (sceneNetworkObject != null && sceneNetworkObject.IsSpawned)
        {
            return;
        }

        if (!scenePlayerObject.activeSelf)
        {
            return;
        }

        scenePlayerObject.SetActive(false);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (playerPrefab == null)
        {
            playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(DefaultPlayerPrefabPath);
        }

        roomMaxPlayers = Mathf.Clamp(roomMaxPlayers, 1, 4);
    }
#endif
}
