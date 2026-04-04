using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;

public class CoopNetworkBootstrap : MonoBehaviour
{
    private const string DefaultPlayerPrefabPath = "Assets/Assets/Prefabs/NetworkPlayer.prefab";

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

    [Header("Networking")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private UnityTransport unityTransport;
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private List<GameObject> additionalNetworkPrefabs = new List<GameObject>();
    [SerializeField] private bool spawnScenePickablesOnServerStart = true;
    [SerializeField] private bool disableScenePlayerBeforeStart = true;
    [SerializeField] private GameObject scenePlayerObject;

    [Header("Optional UI Buttons")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button serverButton;
    [SerializeField] private Button stopButton;

    private readonly HashSet<int> runtimeRegisteredPrefabIds = new HashSet<int>();
    private bool callbacksBound;
    private bool waitingClientConnect;
    private float connectDeadline;

    public event System.Action<string> StatusChanged;

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

    public string CurrentEndpoint => $"{serverAddress}:{serverPort}";
    public string VpsEndpoint => $"{vpsAddress}:{vpsPort}";
    public string LastStatusMessage { get; private set; } = "Offline";

    private void Awake()
    {
        this.EnsureNetworkStack();
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
            this.SetStatus($"Join timeout ke {CurrentEndpoint}. Pastikan dedicated server VPS aktif dan UDP {serverPort} terbuka.");
        }
    }

    [ContextMenu("Start Host")]
    public void StartHost()
    {
        if (!this.PrepareNetworkManager())
        {
            return;
        }

        bool started = networkManager.StartHost();
        this.SetStatus(started ? $"Host aktif di {CurrentEndpoint}" : "Gagal start Host");
    }

    [ContextMenu("Start Client")]
    public void StartClient()
    {
        if (!this.PrepareNetworkManager())
        {
            return;
        }

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
        return true;
    }

    private void EnsureNetworkStack()
    {
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
        catch (System.Exception exception)
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
        if (!spawnScenePickablesOnServerStart || networkManager == null || !networkManager.IsServer)
        {
            return;
        }

        this.SpawnScenePickablesForNetwork();
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

            if (!this.IsPrefabAlreadyRegistered(pickable.gameObject))
            {
                continue;
            }

            try
            {
                networkObject.Spawn(true);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"Failed to spawn scene pickable '{pickable.name}': {exception.Message}");
            }
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (networkManager == null)
        {
            return;
        }

        if (networkManager.IsHost)
        {
            this.SetStatus($"Host aktif ({networkManager.ConnectedClientsIds.Count} klien)");
            return;
        }

        if (networkManager.IsClient && clientId == networkManager.LocalClientId)
        {
            waitingClientConnect = false;
            this.SetStatus($"Berhasil join ke {CurrentEndpoint}");
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (networkManager == null)
        {
            return;
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
            this.SetStatus($"Host aktif ({networkManager.ConnectedClientsIds.Count} klien)");
        }
    }

    private void OnTransportFailure()
    {
        waitingClientConnect = false;
        this.SetStatus($"Transport gagal ke {CurrentEndpoint}. Pastikan dedicated server Unity aktif di VPS dan UDP {serverPort} terbuka.");
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
    }
#endif
}
