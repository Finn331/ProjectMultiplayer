using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

public class PhotonFusionBootstrap : MonoBehaviour, INetworkRunnerCallbacks
{
    private static PhotonFusionBootstrap instance;

    [SerializeField] private NetworkRunner runnerPrefab;
    [SerializeField] private string gameplaySceneName = "Gameplay";
    [SerializeField] private string forestSceneName = "Environment";

    private NetworkRunner runner;
    private bool startInProgress;

    public event Action<string> StatusChanged;
    public event Action<NetworkRunner> RunnerStarted;
    public event Action RunnerStopped;

    public NetworkRunner Runner => runner;
    public bool IsRunning => runner != null && runner.IsRunning;
    public bool IsMasterClient => runner != null && runner.IsSharedModeMasterClient;
    public string GameplaySceneName => gameplaySceneName;
    public string ForestSceneName => forestSceneName;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        CleanupRunner(true, notifyStopped: false);

        if (instance == this)
        {
            instance = null;
        }
    }

    public async void CreateRoom(string roomCode, string playerName, int maxPlayers)
    {
        if (startInProgress)
        {
            SetStatus("Photon room start already in progress.");
            return;
        }

        string normalizedRoomCode = NormalizeRoomCode(roomCode);

        PhotonFusionSessionState.Set(new PhotonFusionSessionState.Session
        {
            PlayerName = NormalizePlayerName(playerName),
            RoomCode = normalizedRoomCode,
            RoomName = normalizedRoomCode,
            MaxPlayers = Mathf.Clamp(maxPlayers, 1, 8),
            Stage = PhotonFusionRoomStage.Waiting,
            IsRoomCreator = true
        });

        await StartSharedRunner(normalizedRoomCode);
    }

    public async void JoinRoom(string roomCode, string playerName)
    {
        if (startInProgress)
        {
            SetStatus("Photon room start already in progress.");
            return;
        }

        string normalizedRoomCode = NormalizeRoomCode(roomCode);

        PhotonFusionSessionState.Set(new PhotonFusionSessionState.Session
        {
            PlayerName = NormalizePlayerName(playerName),
            RoomCode = normalizedRoomCode,
            RoomName = normalizedRoomCode,
            MaxPlayers = 8,
            Stage = PhotonFusionRoomStage.Waiting,
            IsRoomCreator = false
        });

        await StartSharedRunner(normalizedRoomCode);
    }

    public async void LeaveRoom()
    {
        if (startInProgress)
        {
            SetStatus("Photon room start already in progress.");
            return;
        }

        try
        {
            if (runner != null)
            {
                await runner.Shutdown();
            }
        }
        catch (Exception exception)
        {
            SetStatus("Photon shutdown failed: " + exception.Message);
        }
        finally
        {
            CleanupRunner(true);
        }

        SetStatus("Disconnected from Photon room.");
    }

    private async Task StartSharedRunner(string sessionName)
    {
        startInProgress = true;

        try
        {
            await CleanupRunnerAsync(false);

            if (!this || !isActiveAndEnabled)
            {
                return;
            }

            runner = runnerPrefab != null
                ? Instantiate(runnerPrefab)
                : new GameObject("PhotonFusionRunner").AddComponent<NetworkRunner>();
            runner.name = "PhotonFusionRunner";
            runner.ProvideInput = true;
            runner.AddCallbacks(this);
            DontDestroyOnLoad(runner.gameObject);

            INetworkSceneManager sceneManager = runner.GetComponent<INetworkSceneManager>();
            if (sceneManager == null)
            {
                sceneManager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
            }

            StartGameResult result = await runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = sessionName,
                PlayerCount = PhotonFusionSessionState.Active.MaxPlayers,
                Scene = new NetworkSceneInfo(),
                SceneManager = sceneManager
            });

            if (!this || !isActiveAndEnabled)
            {
                CleanupRunner(true);
                return;
            }

            if (!result.Ok)
            {
                SetStatus("Photon start failed: " + result.ShutdownReason);
                CleanupRunner(true);
                return;
            }

            SetStatus("Photon room ready: " + sessionName);
            RunnerStarted?.Invoke(runner);
        }
        catch (Exception exception)
        {
            SetStatus("Photon start failed: " + exception.Message);
            CleanupRunner(true);
        }
        finally
        {
            startInProgress = false;
        }
    }

    private async Task CleanupRunnerAsync(bool clearSession)
    {
        NetworkRunner runnerToCleanup = runner;
        if (!clearSession && runnerToCleanup != null)
        {
            runnerToCleanup.RemoveCallbacks(this);

            if (runner == runnerToCleanup)
            {
                runner = null;
            }
        }

        if (runnerToCleanup != null && runnerToCleanup.IsRunning)
        {
            await runnerToCleanup.Shutdown();
        }

        CleanupRunner(clearSession, runnerToCleanup);
    }

    private void CleanupRunner(bool clearSession, NetworkRunner runnerToCleanup = null, bool notifyStopped = true)
    {
        runnerToCleanup ??= runner;
        bool cleanedActiveRunner = runnerToCleanup != null && runner == runnerToCleanup;

        if (runnerToCleanup != null)
        {
            runnerToCleanup.RemoveCallbacks(this);

            if (runnerToCleanup.gameObject != null)
            {
                Destroy(runnerToCleanup.gameObject);
            }
        }

        if (runner == runnerToCleanup)
        {
            runner = null;
        }

        if (clearSession)
        {
            PhotonFusionSessionState.Clear();
        }

        if (notifyStopped && (cleanedActiveRunner || clearSession))
        {
            RunnerStopped?.Invoke();
        }
    }

    private static string NormalizePlayerName(string playerName)
    {
        return string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim();
    }

    private static string NormalizeRoomCode(string roomCode)
    {
        return string.IsNullOrWhiteSpace(roomCode) ? "ROOM01" : roomCode.Trim().ToUpperInvariant();
    }

    private void SetStatus(string message)
    {
        StatusChanged?.Invoke(message);
        Debug.Log(message);
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        SetStatus("Photon shutdown: " + shutdownReason);
        if (this.runner == runner)
        {
            CleanupRunner(true, runner);
            return;
        }

        RunnerStopped?.Invoke();
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { request.Accept(); }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { SetStatus("Photon connect failed: " + reason); }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}
