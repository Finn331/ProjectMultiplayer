using System;
using System.Threading.Tasks;
using Fusion;
using UnityEngine;

/// <summary>
/// Dev-only helper: creates a Fusion shared session automatically when the scene
/// is entered directly (Play button) without going through the MainMenu bootstrap.
/// Harmless in builds: it only acts when running inside the Editor and no NetworkRunner
/// is already running (e.g. a session created by PhotonFusionBootstrap).
/// </summary>
public class DevAutoSessionStarter : MonoBehaviour
{
    private const string DefaultPlayerName = "DevPlayer";
    private const int DefaultMaxPlayers = 8;

    private NetworkRunner runner;
    private bool shutdownInProgress;

    private async void Start()
    {
        if (!Application.isEditor)
        {
            Destroy(gameObject);
            return;
        }

        NetworkRunner existing = FindObjectOfType<NetworkRunner>();
        if (existing != null && existing.IsRunning)
        {
            Destroy(gameObject);
            return;
        }

        string roomCode = GenerateRoomCode();
        PhotonFusionSessionState.Set(new PhotonFusionSessionState.Session
        {
            PlayerName = DefaultPlayerName,
            RoomCode = roomCode,
            RoomName = roomCode,
            MaxPlayers = DefaultMaxPlayers,
            Stage = PhotonFusionRoomStage.Waiting,
            IsRoomCreator = true,
            IsPrivate = false
        });

        await StartSharedRunner(roomCode);
    }

    private async Task StartSharedRunner(string sessionName)
    {
        try
        {
            runner = new GameObject("PhotonFusionRunner").AddComponent<NetworkRunner>();
            runner.name = "PhotonFusionRunner";
            runner.ProvideInput = true;
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
                EnableClientSessionCreation = true,
                PlayerCount = PhotonFusionSessionState.Active.MaxPlayers,
                IsOpen = true,
                IsVisible = true,
                Scene = new NetworkSceneInfo(),
                SceneManager = sceneManager
            });

            if (!this || !isActiveAndEnabled)
            {
                return;
            }

            if (!result.Ok)
            {
                Debug.LogError("[DevAutoSession] Photon start failed: " + result.ShutdownReason);
                DestroyRunner();
                return;
            }

            Debug.Log("[DevAutoSession] Session ready, room code: " + sessionName);

            FusionPlayerSpawner spawner = FindObjectOfType<FusionPlayerSpawner>();
            if (spawner != null)
            {
                spawner.TrySpawnLocalPlayer(runner);
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("[DevAutoSession] Photon start failed: " + exception.Message);
            DestroyRunner();
        }
    }

    private async void OnApplicationQuit()
    {
        await ShutdownRunnerAsync();
    }

    private async void OnDestroy()
    {
        if (Application.isPlaying)
        {
            await ShutdownRunnerAsync();
        }
    }

    private void DestroyRunner()
    {
        if (runner != null && runner.gameObject != null)
        {
            Destroy(runner.gameObject);
        }

        runner = null;
    }

    private async Task ShutdownRunnerAsync()
    {
        if (shutdownInProgress)
        {
            return;
        }

        NetworkRunner runnerToShutdown = runner;
        if (runnerToShutdown == null)
        {
            return;
        }

        shutdownInProgress = true;
        runner = null;

        try
        {
            if (runnerToShutdown.IsRunning)
            {
                await runnerToShutdown.Shutdown(true, ShutdownReason.Ok, true);
            }
            else if (runnerToShutdown.gameObject != null)
            {
                Destroy(runnerToShutdown.gameObject);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[DevAutoSession] Photon shutdown failed: " + exception.Message);
        }
        finally
        {
            shutdownInProgress = false;
        }
    }

    private static string GenerateRoomCode()
    {
        return "ROOM-" + Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
    }
}
