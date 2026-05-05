using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerClassSystem : NetworkBehaviour
{
    private struct PlayerClassProfile
    {
        public float attackCooldownMultiplier;
        public float treeDamageMultiplier;
        public float playerDamageMultiplier;
        public float consumeEffectMultiplier;
        public float interactionDetectionMultiplier;
        public float interactionDistanceMultiplier;
    }

    [Header("Class")]
    [SerializeField] private PlayerClassType defaultClass = PlayerClassType.Hunter;

    [Header("References")]
    [SerializeField] private PlayerAxeCombat axeCombat;
    [SerializeField] private PlayerInteractionSystem interactionSystem;
    [SerializeField] private NetworkInventoryBridge networkInventoryBridge;

    private readonly NetworkVariable<int> classValue =
        new NetworkVariable<int>((int)PlayerClassType.Hunter, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public PlayerClassType CurrentClass => this.GetClassFromValue(classValue.Value);
    public float ConsumeEffectMultiplier => this.GetProfile(CurrentClass).consumeEffectMultiplier;

    private void Awake()
    {
        if (axeCombat == null)
        {
            axeCombat = GetComponent<PlayerAxeCombat>();
        }

        if (interactionSystem == null)
        {
            interactionSystem = GetComponent<PlayerInteractionSystem>();
        }

        if (networkInventoryBridge == null)
        {
            networkInventoryBridge = GetComponent<NetworkInventoryBridge>();
        }
    }

    private void Start()
    {
        if (NetworkManager == null || !NetworkManager.IsListening)
        {
            classValue.Value = (int)defaultClass;
            this.ApplyCurrentClassProfile();
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        classValue.OnValueChanged += this.OnClassValueChanged;

        if (IsServer)
        {
            classValue.Value = (int)defaultClass;
        }

        this.ApplyCurrentClassProfile();
    }

    public override void OnNetworkDespawn()
    {
        classValue.OnValueChanged -= this.OnClassValueChanged;
        base.OnNetworkDespawn();
    }

    public void RequestSetClass(PlayerClassType classType)
    {
        if (NetworkManager == null || !NetworkManager.IsListening || !IsSpawned)
        {
            classValue.Value = (int)classType;
            this.ApplyCurrentClassProfile();
            return;
        }

        if (!IsOwner)
        {
            return;
        }

        this.RequestSetClassServerRpc((int)classType);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestSetClassServerRpc(int classTypeValue, ServerRpcParams serverRpcParams = default)
    {
        if (serverRpcParams.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        if (!System.Enum.IsDefined(typeof(PlayerClassType), classTypeValue))
        {
            return;
        }

        classValue.Value = classTypeValue;
    }

    private void OnClassValueChanged(int previousValue, int newValue)
    {
        this.ApplyCurrentClassProfile();
    }

    private void ApplyCurrentClassProfile()
    {
        PlayerClassProfile profile = this.GetProfile(CurrentClass);

        if (axeCombat != null)
        {
            axeCombat.SetRuntimeCombatModifiers(
                profile.attackCooldownMultiplier,
                profile.treeDamageMultiplier,
                profile.playerDamageMultiplier);
        }

        if (interactionSystem != null)
        {
            interactionSystem.SetInteractionRangeMultipliers(
                profile.interactionDetectionMultiplier,
                profile.interactionDistanceMultiplier);
        }

        if (networkInventoryBridge != null)
        {
            networkInventoryBridge.SetConsumeEffectMultiplier(profile.consumeEffectMultiplier);
        }
    }

    private PlayerClassType GetClassFromValue(int value)
    {
        if (!System.Enum.IsDefined(typeof(PlayerClassType), value))
        {
            return PlayerClassType.Hunter;
        }

        return (PlayerClassType)value;
    }

    private PlayerClassProfile GetProfile(PlayerClassType classType)
    {
        switch (classType)
        {
            case PlayerClassType.Hunter:
                return new PlayerClassProfile
                {
                    attackCooldownMultiplier = 0.92f,
                    treeDamageMultiplier = 1f,
                    playerDamageMultiplier = 1.3f,
                    consumeEffectMultiplier = 1f,
                    interactionDetectionMultiplier = 1.08f,
                    interactionDistanceMultiplier = 1f
                };

            case PlayerClassType.Lumberjack:
                return new PlayerClassProfile
                {
                    attackCooldownMultiplier = 0.82f,
                    treeDamageMultiplier = 1.65f,
                    playerDamageMultiplier = 1f,
                    consumeEffectMultiplier = 1f,
                    interactionDetectionMultiplier = 1f,
                    interactionDistanceMultiplier = 1f
                };

            case PlayerClassType.Medic:
                return new PlayerClassProfile
                {
                    attackCooldownMultiplier = 1f,
                    treeDamageMultiplier = 1f,
                    playerDamageMultiplier = 1f,
                    consumeEffectMultiplier = 1.5f,
                    interactionDetectionMultiplier = 1f,
                    interactionDistanceMultiplier = 1.05f
                };

            case PlayerClassType.Builder:
                return new PlayerClassProfile
                {
                    attackCooldownMultiplier = 1f,
                    treeDamageMultiplier = 0.95f,
                    playerDamageMultiplier = 0.95f,
                    consumeEffectMultiplier = 1f,
                    interactionDetectionMultiplier = 1.2f,
                    interactionDistanceMultiplier = 1.35f
                };

            default:
                return new PlayerClassProfile
                {
                    attackCooldownMultiplier = 1f,
                    treeDamageMultiplier = 1f,
                    playerDamageMultiplier = 1f,
                    consumeEffectMultiplier = 1f,
                    interactionDetectionMultiplier = 1f,
                    interactionDistanceMultiplier = 1f
                };
        }
    }
}
