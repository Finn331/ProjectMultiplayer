using Unity.Netcode;
using UnityEngine;

public class NetworkInventoryBridge : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerInventory inventory;

    private readonly NetworkVariable<int> woodAmount =
        new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> stoneAmount =
        new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> foodAmount =
        new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool UseNetworkedInventory => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    public bool HasInputAuthority => !UseNetworkedInventory || IsOwner;

    private void Awake()
    {
        if (inventory == null)
        {
            inventory = GetComponent<PlayerInventory>();
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (inventory == null)
        {
            inventory = GetComponent<PlayerInventory>();
        }

        if (IsServer && inventory != null)
        {
            inventory.InventoryChanged += this.OnServerInventoryChanged;
            this.PushInventoryToNetworkVariables();
        }

        if (!IsServer)
        {
            woodAmount.OnValueChanged += this.OnInventoryVariableChanged;
            stoneAmount.OnValueChanged += this.OnInventoryVariableChanged;
            foodAmount.OnValueChanged += this.OnInventoryVariableChanged;
            this.PullInventoryToLocalClient();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && inventory != null)
        {
            inventory.InventoryChanged -= this.OnServerInventoryChanged;
        }

        woodAmount.OnValueChanged -= this.OnInventoryVariableChanged;
        stoneAmount.OnValueChanged -= this.OnInventoryVariableChanged;
        foodAmount.OnValueChanged -= this.OnInventoryVariableChanged;
        base.OnNetworkDespawn();
    }

    public bool TryRequestPickup(PickableItem item)
    {
        if (item == null)
        {
            return false;
        }

        if (!UseNetworkedInventory)
        {
            return false;
        }

        if (!IsOwner)
        {
            return false;
        }

        NetworkObject itemNetworkObject = item.GetComponent<NetworkObject>();
        if (itemNetworkObject == null || !itemNetworkObject.IsSpawned)
        {
            Debug.LogWarning("Pickable item requires spawned NetworkObject for multiplayer pickup.");
            return false;
        }

        this.RequestPickupServerRpc(itemNetworkObject.NetworkObjectId);
        return true;
    }

    public bool TryRequestDrop(ItemType itemType, int amount = 1)
    {
        int clampedAmount = Mathf.Max(1, amount);

        if (!UseNetworkedInventory)
        {
            return inventory != null && inventory.DropItem(itemType, clampedAmount);
        }

        if (!IsOwner)
        {
            return false;
        }

        this.RequestDropServerRpc((int)itemType, clampedAmount);
        return true;
    }

    private void OnServerInventoryChanged()
    {
        this.PushInventoryToNetworkVariables();
    }

    private void PushInventoryToNetworkVariables()
    {
        if (!IsServer || inventory == null)
        {
            return;
        }

        woodAmount.Value = inventory.GetAmount(ItemType.Wood);
        stoneAmount.Value = inventory.GetAmount(ItemType.Stone);
        foodAmount.Value = inventory.GetAmount(ItemType.Food);
    }

    private void PullInventoryToLocalClient()
    {
        if (inventory == null)
        {
            return;
        }

        inventory.SetInventorySnapshot(woodAmount.Value, stoneAmount.Value, foodAmount.Value);
    }

    private void OnInventoryVariableChanged(int previousValue, int newValue)
    {
        this.PullInventoryToLocalClient();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestPickupServerRpc(ulong targetNetworkObjectId, ServerRpcParams serverRpcParams = default)
    {
        if (inventory == null || NetworkManager == null)
        {
            return;
        }

        if (serverRpcParams.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject itemNetworkObject))
        {
            return;
        }

        PickableItem item = itemNetworkObject.GetComponent<PickableItem>();
        if (item == null)
        {
            return;
        }

        int acceptedAmount = inventory.AddItem(item);
        if (acceptedAmount <= 0)
        {
            return;
        }

        if (acceptedAmount >= item.amount)
        {
            itemNetworkObject.Despawn(true);
        }
        else
        {
            item.amount -= acceptedAmount;
        }

        this.PushInventoryToNetworkVariables();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestDropServerRpc(int itemTypeValue, int amount, ServerRpcParams serverRpcParams = default)
    {
        if (inventory == null || NetworkManager == null)
        {
            return;
        }

        if (serverRpcParams.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        if (!System.Enum.IsDefined(typeof(ItemType), itemTypeValue))
        {
            return;
        }

        ItemType itemType = (ItemType)itemTypeValue;
        int clampedAmount = Mathf.Max(1, amount);

        if (!inventory.RemoveItem(itemType, clampedAmount))
        {
            return;
        }

        if (inventory.SpawnDropItemWorld(itemType, clampedAmount, out PickableItem droppedItem))
        {
            NetworkObject droppedNetworkObject = droppedItem != null ? droppedItem.GetComponent<NetworkObject>() : null;
            if (droppedNetworkObject != null && !droppedNetworkObject.IsSpawned)
            {
                if (this.IsNetworkPrefabRegistered(droppedNetworkObject.PrefabIdHash))
                {
                    droppedNetworkObject.Spawn(true);
                }
                else
                {
                    Debug.LogWarning(
                        $"Skipped network spawn for dropped item '{droppedItem.name}' " +
                        $"(hash={droppedNetworkObject.PrefabIdHash}) because prefab is not registered in NetworkManager.");
                }
            }
        }

        this.PushInventoryToNetworkVariables();
    }

    private bool IsNetworkPrefabRegistered(uint prefabHash)
    {
        if (prefabHash == 0u || NetworkManager == null || NetworkManager.NetworkConfig?.Prefabs?.Prefabs == null)
        {
            return false;
        }

        var entries = NetworkManager.NetworkConfig.Prefabs.Prefabs;
        for (int i = 0; i < entries.Count; i++)
        {
            GameObject prefab = entries[i].Prefab;
            if (prefab == null)
            {
                continue;
            }

            NetworkObject networkObject = prefab.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.PrefabIdHash == prefabHash)
            {
                return true;
            }
        }

        return false;
    }
}
