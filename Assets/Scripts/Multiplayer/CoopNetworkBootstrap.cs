using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class CoopNetworkBootstrap : MonoBehaviour
{
    private const string DefaultPlayerPrefabPath = "Assets/Assets/Prefabs/NetworkPlayer.prefab";
    private static CoopNetworkBootstrap instance;

    [Serializable]
    private struct RoomJoinPayload
    {
        public string roomCode;
        public string password;
        public string playerName;
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
    private bool callbacksBound;
    private bool waitingClientConnect;
    private bool approvalCallbackBound;
    private float connectDeadline;
    private string pendingJoinRoomCode = string.Empty;
    private string pendingJoinPassword = string.Empty;
    private string pendingJoinPlayerName = "Player";

    public event Action<string> StatusChanged;

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
        this.ApplyClientConnectionPayload(activeRoomCode, activeRoomPassword, pendingJoinPlayerName);
        bool started = networkManager.StartHost();
        if (started)
        {
            connectedClientNames[NetworkManager.ServerClientId] = string.IsNullOrWhiteSpace(pendingJoinPlayerName) ? "Host" : pendingJoinPlayerName;
            string hostRoomCode = this.NormalizeRoomCode(activeRoomCode);
            connectedClientRoomCodes[NetworkManager.ServerClientId] = hostRoomCode;
            this.AddClientToRoom(hostRoomCode, NetworkManager.ServerClientId);
            roomPasswords[hostRoomCode] = activeRoomPassword ?? string.Empty;
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
        bool started = networkManager.StartServer();
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

    private void StartSceneAsHostInternal(string targetScene, string stageLabel)
    {
        if (networkManager == null || !networkManager.IsHost)
        {
            this.SetStatus($"Hanya host yang bisa Start ke {stageLabel}.");
            return;
        }

        if (string.IsNullOrWhiteSpace(targetScene))
        {
            this.SetStatus($"Scene untuk {stageLabel} belum diisi.");
            return;
        }

        if (!this.IsSceneInBuildSettings(targetScene))
        {
            this.SetStatus($"Scene '{targetScene}' belum ada di Build Settings, jadi client tidak bisa ikut pindah.");
            return;
        }

        if (networkManager.SceneManager != null && networkManager.NetworkConfig != null && networkManager.NetworkConfig.EnableSceneManagement)
        {
            var status = networkManager.SceneManager.LoadScene(targetScene, LoadSceneMode.Single);
            if (status == SceneEventProgressStatus.Started)
            {
                this.SetStatus($"Host memulai {stageLabel}: pindah ke scene '{targetScene}'.");
                return;
            }

            this.SetStatus($"Gagal load scene network '{targetScene}' ({status}). Cek Build Settings / nama scene.");
            return;
        }

        SceneManager.LoadScene(targetScene, LoadSceneMode.Single);
        this.SetStatus($"Scene management NGO nonaktif, fallback local load '{targetScene}' untuk {stageLabel}.");
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
        callbacksBound = true;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }

        if (networkManager == null || !callbacksBound)
        {
            return;
        }

        networkManager.OnServerStarted -= this.OnServerStarted;
        networkManager.OnClientConnectedCallback -= this.OnClientConnected;
        networkManager.OnClientDisconnectCallback -= this.OnClientDisconnected;
        networkManager.OnTransportFailure -= this.OnTransportFailure;
        callbacksBound = false;
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
            waitingClientConnect = false;
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
        }
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
