using System.Collections;
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
        bool canStart = bootstrap != null && bootstrap.IsClientActive && isRoomOwner;
        startForestButton.interactable = canStart;
        if (statusText != null)
        {
            statusText.text = canStart
                ? "Host bisa start ke forest."
                : "Menunggu room owner start ke forest.";
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

        this.ResolveBootstrap();
        if (bootstrap != null && bootstrap.IsSessionListening)
        {
            bootstrap.StopSession();
            StartCoroutine(this.LoadSceneAfterSessionStops(sceneName));
            return;
        }

        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }

    private IEnumerator LoadSceneAfterSessionStops(string sceneName)
    {
        float timeoutAt = Time.unscaledTime + 2f;
        while (bootstrap != null && bootstrap.IsSessionListening && Time.unscaledTime < timeoutAt)
        {
            yield return null;
        }

        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }
}
