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

    [Header("Networking")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private UnityTransport unityTransport;
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private List<GameObject> additionalNetworkPrefabs = new List<GameObject>();
    [SerializeField] private bool disableScenePlayerBeforeStart = true;
    [SerializeField] private GameObject scenePlayerObject;

    [Header("Optional UI Buttons")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button serverButton;
    [SerializeField] private Button stopButton;

    private readonly HashSet<int> runtimeRegisteredPrefabIds = new HashSet<int>();

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

    private void Awake()
    {
        this.EnsureNetworkStack();
        this.BindButtons();
    }

    private void Start()
    {
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

    [ContextMenu("Start Host")]
    public void StartHost()
    {
        if (!this.PrepareNetworkManager())
        {
            return;
        }

        networkManager.StartHost();
    }

    [ContextMenu("Start Client")]
    public void StartClient()
    {
        if (!this.PrepareNetworkManager())
        {
            return;
        }

        networkManager.StartClient();
    }

    [ContextMenu("Start Server")]
    public void StartServer()
    {
        if (!this.PrepareNetworkManager())
        {
            return;
        }

        networkManager.StartServer();
    }

    [ContextMenu("Stop Network Session")]
    public void StopSession()
    {
        if (networkManager == null || !networkManager.IsListening)
        {
            return;
        }

        networkManager.Shutdown();
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
        }
    }

    private void ConfigureTransport()
    {
        if (unityTransport == null)
        {
            return;
        }

        string targetAddress = string.IsNullOrWhiteSpace(serverAddress) ? "127.0.0.1" : serverAddress.Trim();
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
        this.BindButton(hostButton, this.StartHost);
        this.BindButton(joinButton, this.StartClient);
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

    private void DisableScenePlayerIfNeeded()
    {
        if (!disableScenePlayerBeforeStart)
        {
            return;
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
