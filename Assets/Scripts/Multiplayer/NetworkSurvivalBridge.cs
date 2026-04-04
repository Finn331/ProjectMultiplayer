using Unity.Netcode;
using UnityEngine;

public class NetworkSurvivalBridge : NetworkBehaviour
{
    [SerializeField] private PlayerSurvivalSystem survivalSystem;
    [SerializeField] private float syncIntervalSeconds = 0.2f;
    [SerializeField] private float minDeltaToSync = 0.1f;

    private readonly NetworkVariable<float> healthValue =
        new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> hungerValue =
        new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> thirstValue =
        new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private bool hasPendingServerSnapshot;
    private float pendingHealth;
    private float pendingHunger;
    private float pendingThirst;
    private float lastSentHealth;
    private float lastSentHunger;
    private float lastSentThirst;
    private float nextSyncTime;
    private bool hasLastSentSnapshot;

    private void Awake()
    {
        if (survivalSystem == null)
        {
            survivalSystem = GetComponent<PlayerSurvivalSystem>();
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (survivalSystem == null)
        {
            survivalSystem = GetComponent<PlayerSurvivalSystem>();
        }

        if (survivalSystem != null && !IsServer)
        {
            survivalSystem.SetLocalSimulationEnabled(false);
        }

        if (IsServer && survivalSystem != null)
        {
            survivalSystem.StatsChanged += this.OnServerSurvivalChanged;
            this.QueueServerSnapshot(
                survivalSystem.CurrentHealth,
                survivalSystem.CurrentHunger,
                survivalSystem.CurrentThirst);
            this.TryFlushServerSnapshot(true);
        }

        if (!IsServer)
        {
            healthValue.OnValueChanged += this.OnNetworkValuesChanged;
            hungerValue.OnValueChanged += this.OnNetworkValuesChanged;
            thirstValue.OnValueChanged += this.OnNetworkValuesChanged;
            this.PullSurvivalToLocal();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && survivalSystem != null)
        {
            survivalSystem.StatsChanged -= this.OnServerSurvivalChanged;
        }

        healthValue.OnValueChanged -= this.OnNetworkValuesChanged;
        hungerValue.OnValueChanged -= this.OnNetworkValuesChanged;
        thirstValue.OnValueChanged -= this.OnNetworkValuesChanged;

        if (survivalSystem != null && !IsServer)
        {
            survivalSystem.SetLocalSimulationEnabled(true);
        }

        base.OnNetworkDespawn();
    }

    private void OnServerSurvivalChanged(float health, float hunger, float thirst)
    {
        this.QueueServerSnapshot(health, hunger, thirst);
    }

    private void Update()
    {
        if (!IsServer)
        {
            return;
        }

        this.TryFlushServerSnapshot(false);
    }

    private void QueueServerSnapshot(float health, float hunger, float thirst)
    {
        pendingHealth = health;
        pendingHunger = hunger;
        pendingThirst = thirst;
        hasPendingServerSnapshot = true;
    }

    private void TryFlushServerSnapshot(bool force)
    {
        if (!IsServer || !hasPendingServerSnapshot)
        {
            return;
        }

        if (!force && Time.unscaledTime < nextSyncTime)
        {
            return;
        }

        if (!force && hasLastSentSnapshot)
        {
            bool isSmallChange =
                Mathf.Abs(pendingHealth - lastSentHealth) < minDeltaToSync &&
                Mathf.Abs(pendingHunger - lastSentHunger) < minDeltaToSync &&
                Mathf.Abs(pendingThirst - lastSentThirst) < minDeltaToSync;

            if (isSmallChange)
            {
                return;
            }
        }

        this.PushSurvivalToNetworkVariables(pendingHealth, pendingHunger, pendingThirst);
        lastSentHealth = pendingHealth;
        lastSentHunger = pendingHunger;
        lastSentThirst = pendingThirst;
        hasLastSentSnapshot = true;
        hasPendingServerSnapshot = false;
        nextSyncTime = Time.unscaledTime + Mathf.Max(0.05f, syncIntervalSeconds);
    }

    private void PushSurvivalToNetworkVariables(float health, float hunger, float thirst)
    {
        if (!IsServer)
        {
            return;
        }

        healthValue.Value = health;
        hungerValue.Value = hunger;
        thirstValue.Value = thirst;
    }

    private void OnNetworkValuesChanged(float previousValue, float newValue)
    {
        this.PullSurvivalToLocal();
    }

    private void PullSurvivalToLocal()
    {
        if (survivalSystem == null)
        {
            return;
        }

        survivalSystem.ApplyNetworkSnapshot(healthValue.Value, hungerValue.Value, thirstValue.Value);
    }
}
