using Fusion;
using UnityEngine;
using UnityEngine.UI;

public class GameplayPingIndicator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkRunner runner;
    [SerializeField] private Text pingText;

    [Header("Settings")]
    [SerializeField] private float updateInterval = 1f;
    [SerializeField] private int warningThresholdMs = 100;
    [SerializeField] private int criticalThresholdMs = 200;

    private float nextUpdateTime;

    private void Awake()
    {
        if (runner == null)
        {
            runner = FindObjectOfType<NetworkRunner>();
        }
        if (pingText == null)
        {
            pingText = GetComponent<Text>();
        }
        if (pingText != null)
        {
            pingText.text = "Ping: -- ms";
        }
    }

    private void Update()
    {
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
            int pingMs = Mathf.RoundToInt((float)rtt);

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