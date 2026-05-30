using System;
using System.Collections.Generic;
using System.IO;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PhotonFusionSceneLoader : MonoBehaviour
{
    [SerializeField] private PhotonFusionBootstrap bootstrap;
    [SerializeField] private string gameplaySceneName = "Gameplay";
    [SerializeField] private string forestSceneName = "Environment";

    private static readonly Dictionary<long, PendingSceneStage> PendingSceneStages = new Dictionary<long, PendingSceneStage>();
    private static long nextLoadToken;
    private static long currentLoadToken;
    private static int currentSessionGeneration;
    private static string currentSessionRoomCode = string.Empty;

    private void Awake()
    {
        ResolveBootstrap();
    }

    public void LoadGameplayLobby()
    {
        LoadNetworkScene(GetGameplaySceneName(), PhotonFusionRoomStage.Lobby);
    }

    public void LoadForest()
    {
        LoadNetworkScene(GetForestSceneName(), PhotonFusionRoomStage.Forest);
    }

    private void LoadNetworkScene(string sceneName, PhotonFusionRoomStage stage)
    {
        ResolveBootstrap();
        if (bootstrap == null || bootstrap.Runner == null || !bootstrap.Runner.IsRunning)
        {
            Debug.LogWarning("Cannot load Fusion scene because runner is not running.");
            return;
        }

        if (!bootstrap.IsMasterClient)
        {
            Debug.LogWarning("Only room master can start scene transitions.");
            return;
        }

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("Cannot load Fusion scene because scene name is empty.");
            return;
        }

        int buildIndex = GetBuildIndex(sceneName);
        if (buildIndex < 0)
        {
            Debug.LogWarning("Scene is not in Build Settings: " + sceneName);
            return;
        }

        try
        {
            string roomCode = PhotonFusionSessionState.HasSession ? PhotonFusionSessionState.Active.RoomCode : string.Empty;
            int runnerInstanceId = bootstrap.Runner.GetInstanceID();
            int sessionGeneration = GetSessionGeneration(roomCode, runnerInstanceId);
            long loadToken = ++nextLoadToken;
            SceneRef sceneRef = SceneRef.FromIndex(buildIndex);
            NetworkSceneAsyncOp sceneLoad = bootstrap.Runner.LoadScene(sceneRef, LoadSceneMode.Single, LocalPhysicsMode.None, true);
            if (!sceneLoad.IsValid)
            {
                Debug.LogWarning("Fusion scene load did not start: " + sceneName);
                return;
            }

            currentLoadToken = loadToken;
            PendingSceneStages[loadToken] = new PendingSceneStage(sceneName, roomCode, runnerInstanceId, loadToken, sessionGeneration, stage);
            sceneLoad.AddOnCompleted(new LoadCompletionCallback(loadToken).OnCompleted);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Fusion scene load failed for " + sceneName + ": " + exception.Message);
        }
    }

    private string GetGameplaySceneName()
    {
        return bootstrap != null && !string.IsNullOrWhiteSpace(bootstrap.GameplaySceneName)
            ? bootstrap.GameplaySceneName
            : gameplaySceneName;
    }

    private string GetForestSceneName()
    {
        return bootstrap != null && !string.IsNullOrWhiteSpace(bootstrap.ForestSceneName)
            ? bootstrap.ForestSceneName
            : forestSceneName;
    }

    private void ResolveBootstrap()
    {
        if (bootstrap == null)
        {
            bootstrap = FindObjectOfType<PhotonFusionBootstrap>(true);
        }
    }

    private static int GetBuildIndex(string sceneName)
    {
        int buildIndex = SceneUtility.GetBuildIndexByScenePath(sceneName);
        if (buildIndex >= 0)
        {
            return buildIndex;
        }

        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (Path.GetFileNameWithoutExtension(path) == sceneName)
            {
                return i;
            }
        }

        return -1;
    }

    private static int GetSessionGeneration(string roomCode, int runnerInstanceId)
    {
        string sessionKey = roomCode + ":" + runnerInstanceId;
        if (currentSessionRoomCode != sessionKey)
        {
            currentSessionRoomCode = sessionKey;
            currentSessionGeneration++;
        }

        return currentSessionGeneration;
    }

    private static void OnSceneLoadCompleted(NetworkSceneAsyncOp operation, long loadToken)
    {
        PendingSceneStages.TryGetValue(loadToken, out PendingSceneStage pendingStage);
        PendingSceneStages.Remove(loadToken);

        if (pendingStage.Token != loadToken)
        {
            Debug.LogWarning("Ignoring unknown Fusion scene load completion for token " + loadToken + ".");
            return;
        }

        if (operation.Error != null)
        {
            string sceneName = string.IsNullOrEmpty(pendingStage.SceneName) ? operation.SceneRef.ToString() : pendingStage.SceneName;
            Debug.LogWarning("Fusion scene load failed for " + sceneName + ": " + operation.Error.Message);
            return;
        }

        if (currentLoadToken != pendingStage.Token || !PhotonFusionSessionState.HasSession || PhotonFusionSessionState.Active.RoomCode != pendingStage.RoomCode || GetActiveRunnerInstanceId() != pendingStage.RunnerInstanceId || currentSessionGeneration != pendingStage.SessionGeneration)
        {
            string sceneName = string.IsNullOrEmpty(pendingStage.SceneName) ? operation.SceneRef.ToString() : pendingStage.SceneName;
            Debug.LogWarning("Ignoring stale Fusion scene load completion for " + sceneName + ".");
            return;
        }

        PhotonFusionSessionState.SetStage(pendingStage.Stage);
    }

    private static int GetActiveRunnerInstanceId()
    {
        PhotonFusionBootstrap activeBootstrap = FindObjectOfType<PhotonFusionBootstrap>(true);
        return activeBootstrap != null && activeBootstrap.Runner != null ? activeBootstrap.Runner.GetInstanceID() : 0;
    }

    private sealed class LoadCompletionCallback
    {
        private readonly long loadToken;

        public LoadCompletionCallback(long loadToken)
        {
            this.loadToken = loadToken;
        }

        public void OnCompleted(NetworkSceneAsyncOp operation)
        {
            OnSceneLoadCompleted(operation, loadToken);
        }
    }

    private readonly struct PendingSceneStage
    {
        public PendingSceneStage(string sceneName, string roomCode, int runnerInstanceId, long token, int sessionGeneration, PhotonFusionRoomStage stage)
        {
            SceneName = sceneName;
            RoomCode = roomCode;
            RunnerInstanceId = runnerInstanceId;
            Token = token;
            SessionGeneration = sessionGeneration;
            Stage = stage;
        }

        public string SceneName { get; }
        public string RoomCode { get; }
        public int RunnerInstanceId { get; }
        public long Token { get; }
        public int SessionGeneration { get; }
        public PhotonFusionRoomStage Stage { get; }
    }
}
