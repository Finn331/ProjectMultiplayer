using Fusion;
using UnityEngine;
using UnityEngine.UI;

public class GameplayPingIndicator : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float updateInterval = 1f;
    [SerializeField] private int warningThresholdMs = 100;
    [SerializeField] private int criticalThresholdMs = 200;

    private Text pingText;
    private NetworkRunner runner;
    private float nextUpdateTime;

    private void Awake()
    {
        pingText = GetComponent<Text>();
        if (pingText != null)
        {
            pingText.text = "Ping: -- ms";
        }
    }

    private void Update()
    {
        // Find runner if not found yet
        if (runner == null)
        {
            runner = FindObjectOfType<NetworkRunner>();
        }

        if (runner == null || !runner.IsRunning || pingText == null)
        {
            return;
        }

        if (Time.unscaledTime >= nextUpdateTime)
        {
            nextUpdateTime = Time.unscaledTime + updateInterval;
            UpdatePingDisplay();
        }
    }

    private void UpdatePingDisplay()
    {
        if (runner.LocalPlayer.IsNone)
        {
            return;
        }

        try
        {
            double rtt = runner.GetPlayerRtt(runner.LocalPlayer);
            int pingMs = Mathf.RoundToInt((float)(rtt * 1000.0));

            pingText.text = $"Ping: {pingMs} ms";

            if (pingMs <= warningThresholdMs)
            {
                pingText.color = Color.green;
            }
            else if (pingMs <= criticalThresholdMs)
            {
                pingText.color = Color.yellow;
            }
            else
            {
                pingText.color = Color.red;
            }
        }
        catch
        {
            pingText.text = "Ping: -- ms";
            pingText.color = Color.gray;
        }
    }
}