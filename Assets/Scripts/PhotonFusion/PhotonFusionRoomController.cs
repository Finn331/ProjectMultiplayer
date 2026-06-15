using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PhotonFusionRoomController : MonoBehaviour
{
    [SerializeField] private PhotonFusionBootstrap bootstrap;
    [SerializeField] private TMP_InputField playerNameInput;
    [SerializeField] private TMP_InputField roomCodeInput;
    [SerializeField] private Slider maxPlayersSlider;
    [SerializeField] private Button createRoomButton;
    [SerializeField] private Button joinRoomButton;
    [SerializeField] private Button leaveRoomButton;
    [SerializeField] private Button startLobbyButton;
    [SerializeField] private Button startForestButton;
    [SerializeField] private TMP_Text statusText;

    private PhotonFusionBootstrap subscribedBootstrap;

    private void Awake()
    {
        ResolveBootstrap();
        Bind(createRoomButton, CreateRoom);
        Bind(joinRoomButton, JoinRoom);
        Bind(leaveRoomButton, LeaveRoom);
        Bind(startLobbyButton, StartLobby);
        Bind(startForestButton, StartForest);
        RefreshButtons();
    }

    private void OnEnable()
    {
        ResolveBootstrap();
        SubscribeToBootstrap(bootstrap);
    }

    private void OnDisable()
    {
        SubscribeToBootstrap(null);
    }

    private void Update()
    {
        RefreshButtons();
    }

    private void CreateRoom()
    {
        ResolveBootstrap();
        if (bootstrap == null)
        {
            SetStatus("Photon bootstrap not found.");
            return;
        }

        bootstrap.CreateRoom(ReadRoomCode(), ReadPlayerName(), ReadMaxPlayers());
    }

    private void JoinRoom()
    {
        ResolveBootstrap();
        if (bootstrap == null)
        {
            SetStatus("Photon bootstrap not found.");
            return;
        }

        bootstrap.JoinRoom(ReadRoomCode(), ReadPlayerName());
    }

    private void LeaveRoom()
    {
        ResolveBootstrap();
        if (bootstrap == null)
        {
            SetStatus("Photon bootstrap not found.");
            return;
        }

        bootstrap.LeaveRoom();
    }

    private void StartLobby()
    {
        SendSceneLoaderMessage("LoadGameplayLobby");
    }

    private void StartForest()
    {
        SendSceneLoaderMessage("LoadForest");
    }

    private void RefreshButtons()
    {
        ResolveBootstrap();
        bool running = bootstrap != null && bootstrap.IsRunning;
        bool canStart = running && bootstrap.IsMasterClient;

        if (createRoomButton != null) createRoomButton.interactable = !running;
        if (joinRoomButton != null) joinRoomButton.interactable = !running;
        if (leaveRoomButton != null) leaveRoomButton.interactable = running;
        if (startLobbyButton != null) startLobbyButton.interactable = canStart;
        if (startForestButton != null) startForestButton.interactable = canStart;
    }

    private string ReadPlayerName()
    {
        return playerNameInput != null && !string.IsNullOrWhiteSpace(playerNameInput.text)
            ? playerNameInput.text.Trim()
            : "Player";
    }

    private string ReadRoomCode()
    {
        return roomCodeInput != null && !string.IsNullOrWhiteSpace(roomCodeInput.text)
            ? roomCodeInput.text.Trim().ToUpperInvariant()
            : string.Empty;
    }

    private int ReadMaxPlayers()
    {
        return maxPlayersSlider != null ? Mathf.Clamp(Mathf.RoundToInt(maxPlayersSlider.value), 1, 8) : 8;
    }

    private void ResolveBootstrap()
    {
        PhotonFusionBootstrap resolvedBootstrap = bootstrap;
        if (resolvedBootstrap == null)
        {
            resolvedBootstrap = FindObjectOfType<PhotonFusionBootstrap>(true);
        }

        bootstrap = resolvedBootstrap;
        if (isActiveAndEnabled)
        {
            SubscribeToBootstrap(bootstrap);
        }
    }

    private void SubscribeToBootstrap(PhotonFusionBootstrap newBootstrap)
    {
        if (subscribedBootstrap == newBootstrap)
        {
            return;
        }

        if (subscribedBootstrap != null)
        {
            subscribedBootstrap.StatusChanged -= SetStatus;
        }

        subscribedBootstrap = newBootstrap;

        if (subscribedBootstrap != null)
        {
            subscribedBootstrap.StatusChanged -= SetStatus;
            subscribedBootstrap.StatusChanged += SetStatus;
        }
    }

    private void SendSceneLoaderMessage(string methodName)
    {
        Component loader = FindSceneLoader();
        if (loader == null)
        {
            SetStatus("Fusion scene loader not found.");
            return;
        }

        MethodInfo method = loader.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        if (method == null)
        {
            SetStatus("Fusion scene loader not ready.");
            return;
        }

        try
        {
            method.Invoke(loader, null);
        }
        catch (Exception exception)
        {
            SetStatus("Fusion scene load failed: " + exception.GetBaseException().Message);
        }
    }

    private static Component FindSceneLoader()
    {
        MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] != null && behaviours[i].GetType().Name == "PhotonFusionSceneLoader")
            {
                return behaviours[i];
            }
        }

        return null;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private static void Bind(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }
}
