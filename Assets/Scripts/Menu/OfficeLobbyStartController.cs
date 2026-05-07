using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class OfficeLobbyStartController : MonoBehaviour
{
    [SerializeField] private CoopNetworkBootstrap bootstrap;
    [SerializeField] private Button startForestButton;
    [SerializeField] private TMP_Text statusText;

    private void Awake()
    {
        this.ResolveBootstrap();
        this.BindButton();
        this.RefreshButtonState();
    }

    private void Update()
    {
        this.RefreshButtonState();
    }

    private void ResolveBootstrap()
    {
        if (bootstrap == null)
        {
            bootstrap = FindObjectOfType<CoopNetworkBootstrap>(true);
        }
    }

    private void BindButton()
    {
        if (startForestButton == null)
        {
            return;
        }

        startForestButton.onClick.RemoveListener(this.StartForestAsHost);
        startForestButton.onClick.AddListener(this.StartForestAsHost);
    }

    private void RefreshButtonState()
    {
        if (startForestButton == null)
        {
            return;
        }

        bool isRoomOwner = MainMenuSessionState.HasSession && MainMenuSessionState.Active.mode == SessionPlayMode.HostRoom;
        bool canStart = bootstrap != null && bootstrap.IsClientActive && bootstrap.IsClientConnected && isRoomOwner;
        startForestButton.interactable = canStart;
        if (statusText != null)
        {
            int memberCount = 0;
            if (bootstrap != null)
            {
                memberCount = bootstrap.GetKnownRoomMemberNames().Count;
            }

            statusText.text = canStart
                ? $"Room siap ({Mathf.Max(1, memberCount)} player). Host bisa start ke forest."
                : "Menunggu room owner terhubung dan start ke forest.";
        }
    }

    private void StartForestAsHost()
    {
        this.ResolveBootstrap();
        if (bootstrap == null)
        {
            return;
        }

        if (!MainMenuSessionState.HasSession || MainMenuSessionState.Active.mode != SessionPlayMode.HostRoom)
        {
            return;
        }

        this.LoadSceneSafely("Environment");
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
}
