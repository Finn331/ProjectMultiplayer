using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class NetworkInventoryBridge : NetworkBehaviour
{
    private const float PickupResolveRadius = 2.5f;
    private const float PickupOwnerDistanceMax = 4.0f;

    [Header("References")]
    [SerializeField] private PlayerInventory inventory;

    private readonly NetworkVariable<FixedString512Bytes> inventorySnapshot =
        new NetworkVariable<FixedString512Bytes>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

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
            inventorySnapshot.OnValueChanged += this.OnInventoryVariableChanged;
            this.PullInventoryToLocalClient();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && inventory != null)
        {
            inventory.InventoryChanged -= this.OnServerInventoryChanged;
        }

        inventorySnapshot.OnValueChanged -= this.OnInventoryVariableChanged;
        base.OnNetworkDespawn();
    }

    public bool TryRequestPickup(PickableItem item)
    {
        if (item == null || !UseNetworkedInventory || !IsOwner)
        {
            return false;
        }

        NetworkObject itemNetworkObject = item.GetComponent<NetworkObject>();
        if (itemNetworkObject != null && itemNetworkObject.IsSpawned)
        {
            this.RequestPickupServerRpc(itemNetworkObject.NetworkObjectId);
            return true;
        }

        this.RequestPickupBySnapshotServerRpc((int)item.itemType, item.transform.position);
        return true;
    }

    public bool TryRequestDrop(ItemType itemType, int amount = 1)
    {
        int clampedAmount = Mathf.Max(1, amount);

        if (!UseNetworkedInventory)
        {
            return inventory != null && inventory.DropItem(itemType, clampedAmount);
        }

        if (!IsOwner || inventory == null)
        {
            Debug.LogWarning($"Drop request rejected on client because this player is not owner. Item={itemType}");
            return false;
        }

        int sourceSlot = inventory.FindFirstSlotWithItemType(itemType);
        if (sourceSlot < 0)
        {
            return false;
        }

        return this.TryRequestDropFromSlot(sourceSlot, clampedAmount);
    }

    public bool TryRequestDropFromSlot(int slotIndex, int amount = 1)
    {
        int clampedAmount = Mathf.Max(1, amount);
        if (!UseNetworkedInventory)
        {
            return inventory != null && inventory.DropItemFromSlot(slotIndex, clampedAmount);
        }

        if (!IsOwner || inventory == null)
        {
            return false;
        }

        ItemType? itemType = inventory.GetSlotItemType(slotIndex);
        if (itemType == null)
        {
            return false;
        }

        this.RequestDropServerRpc(slotIndex, (int)itemType.Value, clampedAmount);
        return true;
    }

    public bool TryRequestMoveSlot(int sourceSlotIndex, int targetSlotIndex)
    {
        if (!UseNetworkedInventory)
        {
            return inventory != null && inventory.MoveOrSwapSlot(sourceSlotIndex, targetSlotIndex);
        }

        if (!IsOwner || inventory == null)
        {
            return false;
        }

        if (sourceSlotIndex == targetSlotIndex)
        {
            return false;
        }

        if (inventory.GetSlotItemType(sourceSlotIndex) == null)
        {
            return false;
        }

        this.RequestMoveSlotServerRpc(sourceSlotIndex, targetSlotIndex);
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

        inventorySnapshot.Value = new FixedString512Bytes(inventory.BuildSnapshotString());
    }

    private void PullInventoryToLocalClient()
    {
        if (inventory == null)
        {
            return;
        }

        inventory.SetInventorySnapshot(inventorySnapshot.Value.ToString());
    }

    private void OnInventoryVariableChanged(FixedString512Bytes previousValue, FixedString512Bytes newValue)
    {
        this.PullInventoryToLocalClient();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestPickupServerRpc(ulong targetNetworkObjectId, ServerRpcParams serverRpcParams = default)
    {
        if (inventory == null || NetworkManager == null || serverRpcParams.Receive.SenderClientId != OwnerClientId)
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

        float ownerDistanceSqr = (item.transform.position - transform.position).sqrMagnitude;
        if (ownerDistanceSqr > PickupOwnerDistanceMax * PickupOwnerDistanceMax)
        {
            this.SendDropFeedbackClientRpc("Pickup gagal: item terlalu jauh.", this.BuildOwnerRpcTarget());
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
    private void RequestPickupBySnapshotServerRpc(int itemTypeValue, Vector3 approximatePosition, ServerRpcParams serverRpcParams = default)
    {
        if (inventory == null || NetworkManager == null || serverRpcParams.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        if (!System.Enum.IsDefined(typeof(ItemType), itemTypeValue))
        {
            return;
        }

        ItemType itemType = (ItemType)itemTypeValue;
        if (!this.TryFindPickupCandidate(itemType, approximatePosition, out PickableItem candidate))
        {
            this.SendDropFeedbackClientRpc("Pickup gagal: item tidak ditemukan di server.", this.BuildOwnerRpcTarget());
            return;
        }

        if (!this.TryProcessPickup(candidate))
        {
            this.SendDropFeedbackClientRpc("Pickup gagal: item belum siap di network.", this.BuildOwnerRpcTarget());
            return;
        }

        this.PushInventoryToNetworkVariables();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestDropServerRpc(int slotIndex, int expectedItemTypeValue, int amount, ServerRpcParams serverRpcParams = default)
    {
        if (inventory == null || NetworkManager == null || serverRpcParams.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        ItemType? slotItemType = inventory.GetSlotItemType(slotIndex);
        if (slotItemType == null)
        {
            this.SendDropFeedbackClientRpc("Drop gagal: slot kosong.", this.BuildOwnerRpcTarget());
            return;
        }

        if (System.Enum.IsDefined(typeof(ItemType), expectedItemTypeValue) && slotItemType.Value != (ItemType)expectedItemTypeValue)
        {
            this.SendDropFeedbackClientRpc("Drop gagal: slot berubah sebelum server memproses.", this.BuildOwnerRpcTarget());
            this.PushInventoryToNetworkVariables();
            return;
        }

        int clampedAmount = Mathf.Max(1, amount);
        if (!inventory.DropItemFromSlot(slotIndex, clampedAmount))
        {
            this.SendDropFeedbackClientRpc(
                $"Drop gagal: prefab network untuk {slotItemType.Value} tidak ditemukan/terdaftar.",
                this.BuildOwnerRpcTarget());
            this.PushInventoryToNetworkVariables();
            return;
        }

        this.PushInventoryToNetworkVariables();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestMoveSlotServerRpc(int sourceSlotIndex, int targetSlotIndex, ServerRpcParams serverRpcParams = default)
    {
        if (inventory == null || serverRpcParams.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        if (!inventory.MoveOrSwapSlot(sourceSlotIndex, targetSlotIndex))
        {
            this.SendDropFeedbackClientRpc("Pindah slot gagal di server.", this.BuildOwnerRpcTarget());
            this.PushInventoryToNetworkVariables();
            return;
        }

        this.PushInventoryToNetworkVariables();
    }

    private bool SpawnDropFromRegisteredPrefabs(ItemType itemType, int amount, out PickableItem droppedItem)
    {
        droppedItem = null;

        if (NetworkManager == null || NetworkManager.NetworkConfig?.Prefabs?.Prefabs == null)
        {
            return false;
        }

        var prefabs = NetworkManager.NetworkConfig.Prefabs.Prefabs;
        for (int i = 0; i < prefabs.Count; i++)
        {
            NetworkPrefab entry = prefabs[i];
            GameObject[] candidates = { entry.Prefab, entry.SourcePrefabToOverride, entry.OverridingTargetPrefab };
            for (int c = 0; c < candidates.Length; c++)
            {
                GameObject prefabObject = candidates[c];
                if (prefabObject == null)
                {
                    continue;
                }

                PickableItem prefabPickable = prefabObject.GetComponent<PickableItem>();
                NetworkObject prefabNetworkObject = prefabObject.GetComponent<NetworkObject>();
                if (prefabPickable == null || prefabNetworkObject == null || prefabPickable.itemType != itemType)
                {
                    continue;
                }

                Vector3 spawnPosition = inventory != null
                    ? inventory.GetDropPositionWorld()
                    : transform.position + transform.forward + (Vector3.up * 0.2f);
                droppedItem = Instantiate(prefabPickable, spawnPosition, Quaternion.identity);
                droppedItem.gameObject.SetActive(true);
                droppedItem.itemType = itemType;
                droppedItem.amount = Mathf.Max(1, amount);
                if (string.IsNullOrWhiteSpace(droppedItem.itemName))
                {
                    droppedItem.itemName = itemType.ToString();
                }

                Rigidbody rb = droppedItem.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    Vector3 forward = inventory != null ? inventory.GetDropForwardDirection() : transform.forward;
                    rb.AddForce(forward * 2.2f, ForceMode.VelocityChange);
                }

                return true;
            }
        }

        return false;
    }

    private bool TryFindPickupCandidate(ItemType itemType, Vector3 approximatePosition, out PickableItem candidate)
    {
        candidate = null;
        float resolveRadiusSqr = PickupResolveRadius * PickupResolveRadius;
        float ownerDistanceLimitSqr = PickupOwnerDistanceMax * PickupOwnerDistanceMax;
        float bestSqr = resolveRadiusSqr;

        PickableItem[] items = FindObjectsOfType<PickableItem>(true);
        for (int i = 0; i < items.Length; i++)
        {
            PickableItem current = items[i];
            if (current == null || current.itemType != itemType || !current.gameObject.activeInHierarchy)
            {
                continue;
            }

            Vector3 currentPosition = current.transform.position;
            float positionSqr = (currentPosition - approximatePosition).sqrMagnitude;
            if (positionSqr > bestSqr)
            {
                continue;
            }

            float ownerDistanceSqr = (currentPosition - transform.position).sqrMagnitude;
            if (ownerDistanceSqr > ownerDistanceLimitSqr)
            {
                continue;
            }

            bestSqr = positionSqr;
            candidate = current;
        }

        return candidate != null;
    }

    private bool TryProcessPickup(PickableItem item)
    {
        if (item == null || inventory == null)
        {
            return false;
        }

        NetworkObject networkObject = item.GetComponent<NetworkObject>();
        if (networkObject != null && !networkObject.IsSpawned)
        {
            if (!this.IsNetworkPrefabRegistered(networkObject.PrefabIdHash))
            {
                return false;
            }

            networkObject.Spawn(true);
        }

        int acceptedAmount = inventory.AddItem(item);
        if (acceptedAmount <= 0)
        {
            return false;
        }

        if (acceptedAmount >= item.amount)
        {
            if (networkObject != null && networkObject.IsSpawned)
            {
                networkObject.Despawn(true);
            }
            else
            {
                Destroy(item.gameObject);
            }
        }
        else
        {
            item.amount -= acceptedAmount;
        }

        return true;
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
            NetworkPrefab entry = entries[i];
            GameObject[] candidates = { entry.Prefab, entry.SourcePrefabToOverride, entry.OverridingTargetPrefab };
            for (int c = 0; c < candidates.Length; c++)
            {
                GameObject prefab = candidates[c];
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
        }

        return false;
    }

    private ClientRpcParams BuildOwnerRpcTarget()
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId }
            }
        };
    }

    [ClientRpc]
    private void SendDropFeedbackClientRpc(string message, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            Debug.LogWarning(message);
            if (PickupUIManager.instance != null)
            {
                PickupUIManager.instance.ShowInfo(message);
            }
        }
    }
}
