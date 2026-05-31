using Fusion;
using UnityEngine;

public class FusionPlayerInventory : NetworkBehaviour
{
    [System.Serializable]
    private class DropPrefabBinding
    {
        public ItemType itemType;
        public NetworkPrefabRef prefab;
    }

    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private float pickupDistance = 4f;
    [SerializeField] private DropPrefabBinding[] dropPrefabs;
    [SerializeField] private NetworkPrefabRef fallbackDropPrefab;
    [SerializeField] private Transform dropOrigin;
    [SerializeField] private float dropForwardDistance = 1.2f;
    [SerializeField] private float dropUpOffset = 0.2f;

    public override void Spawned()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        ResolveReferences();
    }

    public bool RequestPickup(PickableItem item)
    {
        if (!IsNetworkReady() || !HasFusionInputAuthority() || item == null)
        {
            return false;
        }

        var networkObject = item.GetComponent<NetworkObject>();
        if (networkObject == null || !networkObject.IsValid || Runner == null)
        {
            return false;
        }

        RPC_RequestPickup(networkObject);
        return true;
    }

    public bool RequestDrop(ItemType itemType, int amount = 1)
    {
        if (!IsNetworkReady() || !HasFusionInputAuthority() || inventory == null)
        {
            return false;
        }

        int clampedAmount = Mathf.Max(1, amount);
        int sourceSlot = inventory.FindFirstSlotWithItemType(itemType);
        if (sourceSlot < 0)
        {
            return false;
        }

        RPC_RequestDrop(sourceSlot, (int)itemType, clampedAmount);
        return true;
    }

    public bool RequestDropFromSlot(int slotIndex, int amount = 1)
    {
        if (!IsNetworkReady() || !HasFusionInputAuthority() || inventory == null)
        {
            return false;
        }

        ItemType? itemType = inventory.GetSlotItemType(slotIndex);
        if (itemType == null)
        {
            return false;
        }

        RPC_RequestDrop(slotIndex, (int)itemType.Value, Mathf.Max(1, amount));
        return true;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
    private void RPC_ConfirmPickupDespawn(NetworkObject itemObject)
    {
        if (itemObject == null || Runner == null) return;
        
        if (itemObject.HasStateAuthority || (Runner.IsSharedModeMasterClient && itemObject.StateAuthority == PlayerRef.None))
        {
            Runner.Despawn(itemObject);
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
    private void RPC_ConfirmPickupPartial(NetworkObject itemObject, int remainingAmount)
    {
        if (itemObject == null || Runner == null) return;
        
        if (itemObject.HasStateAuthority || (Runner.IsSharedModeMasterClient && itemObject.StateAuthority == PlayerRef.None))
        {
            PickableItem item = itemObject.GetComponent<PickableItem>();
            if (item != null) item.amount = remainingAmount;
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestDrop(int slotIndex, int expectedItemTypeValue, int amount)
    {
        ResolveReferences();

        if (inventory == null || Runner == null)
        {
            return;
        }

        if (!System.Enum.IsDefined(typeof(ItemType), expectedItemTypeValue))
        {
            return;
        }

        ItemType itemType = (ItemType)expectedItemTypeValue;
        if (!TryGetDropPrefab(itemType, out NetworkPrefabRef dropPrefab))
        {
            return;
        }

        if (inventory.GetSlotItemType(slotIndex) != itemType)
        {
            return;
        }

        int clampedAmount = Mathf.Max(1, amount);
        int removedAmount = Mathf.Min(clampedAmount, inventory.GetSlotAmount(slotIndex));
        if (removedAmount <= 0)
        {
            return;
        }

        Vector3 spawnPosition = GetDropPosition();
        Quaternion spawnRotation = Quaternion.identity;
        NetworkObject droppedObject = Runner.Spawn(dropPrefab, spawnPosition, spawnRotation, Object.InputAuthority);
        if (droppedObject == null)
        {
            return;
        }

        FusionPickableItem droppedItem = droppedObject.GetComponent<FusionPickableItem>();
        if (droppedItem == null || !droppedItem.Initialize(itemType, removedAmount))
        {
            Runner.Despawn(droppedObject);
            return;
        }

        if (!inventory.RemoveItemFromSlot(slotIndex, clampedAmount, out ItemType removedItemType))
        {
            Runner.Despawn(droppedObject);
            return;
        }

        if (removedItemType != itemType)
        {
            inventory.AddItem(removedItemType, removedAmount);
            Runner.Despawn(droppedObject);
        }
    }

    private void ResolveReferences()
    {
        if (inventory == null)
        {
            inventory = GetComponent<PlayerInventory>();
        }

        if (dropOrigin == null)
        {
            dropOrigin = transform;
        }
    }

    private Vector3 GetDropPosition()
    {
        Transform origin = dropOrigin != null ? dropOrigin : transform;
        return origin.position + origin.forward * dropForwardDistance + Vector3.up * dropUpOffset;
    }

    private bool TryGetDropPrefab(ItemType itemType, out NetworkPrefabRef prefab)
    {
        if (dropPrefabs != null)
        {
            for (int i = 0; i < dropPrefabs.Length; i++)
            {
                DropPrefabBinding binding = dropPrefabs[i];
                if (binding != null && binding.itemType == itemType && binding.prefab.IsValid)
                {
                    prefab = binding.prefab;
                    return true;
                }
            }
        }

        if (fallbackDropPrefab.IsValid)
        {
            prefab = fallbackDropPrefab;
            return true;
        }

        prefab = default;
        return false;
    }

    private bool IsNetworkReady()
    {
        return Runner != null && Object != null && Object.IsValid;
    }

    private bool HasFusionInputAuthority()
    {
        return Object != null && Object.HasStateAuthority;
    }
}
