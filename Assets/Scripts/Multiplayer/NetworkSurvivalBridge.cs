using Unity.Netcode;
using UnityEngine;

public class NetworkSurvivalBridge : NetworkBehaviour
{
    [SerializeField] private PlayerSurvivalSystem survivalSystem;

    private readonly NetworkVariable<float> healthValue =
        new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> hungerValue =
        new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> thirstValue =
        new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

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
            this.PushSurvivalToNetworkVariables(
                survivalSystem.CurrentHealth,
                survivalSystem.CurrentHunger,
                survivalSystem.CurrentThirst);
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
        this.PushSurvivalToNetworkVariables(health, hunger, thirst);
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
