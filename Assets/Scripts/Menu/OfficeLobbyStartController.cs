using TMPro;
using UnityEngine;
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

        bool canStart = bootstrap != null && bootstrap.IsHostActive;
        startForestButton.interactable = canStart;
        if (statusText != null)
        {
            statusText.text = canStart
                ? "Host bisa start ke forest."
                : "Menunggu host start ke forest.";
        }
    }

    private void StartForestAsHost()
    {
        this.ResolveBootstrap();
        if (bootstrap == null)
        {
            return;
        }

        bootstrap.StartForestSceneAsHost();
    }
}
