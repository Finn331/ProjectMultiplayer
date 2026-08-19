using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

    private readonly List<string> activeMessages = new List<string>();
    private static readonly string NatureName = "Nature";

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
        }
    }

    public string FormatMessageForTest(string killerName, string victimName, bool isKill)
    {
        string killer = string.IsNullOrEmpty(killerName) ? NatureName : killerName;
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

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
