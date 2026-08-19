using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class KillFeedHUD : MonoBehaviour
{
    public static KillFeedHUD Instance { get; private set; }

    [Header("Feed Settings")]
    [SerializeField] private float messageLifetimeSeconds = 5f;
    [SerializeField] private int maxQueuedMessages = 6;

    [Header("Colors")]
    [SerializeField] private Color downedColor = new Color(0.4f, 0.9f, 0.4f);
    [SerializeField] private Color killColor = new Color(0.95f, 0.35f, 0.35f);

    [Header("UI")]
    [SerializeField] private RectTransform feedRoot;

    [Header("Respawn")]
    [SerializeField] private Button respawnButton;

    private readonly List<string> activeMessages = new List<string>();
    private static readonly string NatureName = "Nature";

    private FusionPlayerDeath localPlayerDeath;

    public void EnqueueMessage(string killerName, string victimName, bool isKill)
    {
        string message = FormatMessageForTest(killerName, victimName, isKill);
        if (activeMessages.Count >= maxQueuedMessages)
        {
            activeMessages.RemoveAt(0);
        }
        activeMessages.Add(message);
        if (feedRoot == null)
        {
            Debug.Log("[KillFeed] " + message);
            return;
        }
        PruneRows();
        SpawnRow(message, isKill);
    }

    private void PruneRows()
    {
        if (feedRoot == null || maxQueuedMessages <= 0)
        {
            return;
        }
        int overage = feedRoot.childCount - (maxQueuedMessages - 1);
        for (int i = 0; i < overage && i < feedRoot.childCount; i++)
        {
            Destroy(feedRoot.GetChild(i).gameObject);
        }
    }

    private void SpawnRow(string message, bool isKill)
    {
        GameObject rowObject = new GameObject("KillFeedRow", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rowRect = rowObject.GetComponent<RectTransform>();
        rowRect.SetParent(feedRoot, false);
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.anchoredPosition = Vector2.zero;
        rowRect.sizeDelta = new Vector2(0f, 26f);

        TextMeshProUGUI label = rowObject.GetComponent<TextMeshProUGUI>();
        label.text = message;
        label.fontSize = 18f;
        label.color = isKill ? killColor : downedColor;
        label.alignment = TextAlignmentOptions.Left;
        label.raycastTarget = false;

        Destroy(rowObject, messageLifetimeSeconds);
    }

    public string FormatMessageForTest(string killerName, string victimName, bool isKill)
    {
        string killer = string.IsNullOrWhiteSpace(killerName) ? NatureName : killerName;
        string verb = isKill ? "killed" : "downed";
        return killer + " " + verb + " " + victimName;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        if (respawnButton != null)
        {
            respawnButton.onClick.AddListener(RequestRespawnFromLocalPlayer);
            respawnButton.gameObject.SetActive(false);
        }
        StartCoroutine(EnsureLocalPlayerDeath());
    }

    private IEnumerator EnsureLocalPlayerDeath()
    {
        while (localPlayerDeath == null)
        {
            localPlayerDeath = GetLocalPlayerDeath();
            if (localPlayerDeath == null)
            {
                yield return new WaitForSeconds(0.25f);
            }
        }

        localPlayerDeath.OnDownedChanged += HandleDownedChanged;
        HandleDownedChanged(localPlayerDeath.IsDowned);
    }

    private void HandleDownedChanged(bool downed)
    {
        if (respawnButton != null)
        {
            respawnButton.gameObject.SetActive(downed);
        }
    }

    private void RequestRespawnFromLocalPlayer()
    {
        FusionPlayerDeath death = GetLocalPlayerDeath();
        if (death != null)
        {
            death.RequestRespawnNow();
        }
    }

    private FusionPlayerDeath GetLocalPlayerDeath()
    {
        Fusion.NetworkRunner runner = FindObjectOfType<Fusion.NetworkRunner>();
        if (runner == null || runner.LocalPlayer.IsNone)
        {
            return null;
        }
        if (runner.TryGetPlayerObject(runner.LocalPlayer, out Fusion.NetworkObject playerObject) && playerObject != null)
        {
            return playerObject.GetComponent<FusionPlayerDeath>();
        }
        return null;
    }

    private void OnDestroy()
    {
        if (localPlayerDeath != null)
        {
            localPlayerDeath.OnDownedChanged -= HandleDownedChanged;
            localPlayerDeath = null;
        }
        if (Instance == this)
        {
            Instance = null;
        }
    }
}