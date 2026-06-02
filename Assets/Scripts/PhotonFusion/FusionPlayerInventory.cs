using Fusion;
using UnityEngine;

public class FusionPlayerInventory : NetworkBehaviour
{
    private static readonly System.Collections.Generic.HashSet<int> ClaimedLocalPickupIds = new System.Collections.Generic.HashSet<int>();

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
        if (!IsNetworkReady() || !HasFusionInputAuthority() || item == null || inventory == null)
        {
            return false;
        }

        var networkObject = item.GetComponent<NetworkObject>();
        if (networkObject == null || !networkObject.IsValid || Runner == null)
        {
            return RequestScenePickup(item);
        }

        float distance = Vector3.Distance(transform.position, item.transform.position);
        if (distance > pickupDistance)
        {
            return false;
        }

        int requestedAmount = Mathf.Max(1, item.amount);
        int acceptedAmount = inventory.AddItem(item.itemType, requestedAmount);

        if (acceptedAmount <= 0)
        {
            return false;
        }

        if (acceptedAmount >= requestedAmount)
        {
            RPC_ConfirmPickupDespawn(networkObject);
        }
        else
        {
            RPC_ConfirmPickupPartial(networkObject, requestedAmount - acceptedAmount);
        }

        return true;
    }

    private bool RequestScenePickup(PickableItem item)
    {
        if (item == null || Runner == null || inventory == null)
        {
            return false;
        }

        float distance = Vector3.Distance(transform.position, item.transform.position);
        if (distance > pickupDistance)
        {
            return false;
        }

        int itemObjectId = item.gameObject.GetInstanceID();
        if (ClaimedLocalPickupIds.Contains(itemObjectId))
        {
            return false;
        }

        int requestedAmount = Mathf.Max(1, item.amount);
        int acceptedAmount = inventory.AddItem(item.itemType, requestedAmount);
        if (acceptedAmount <= 0)
        {
            return false;
        }

        ClaimedLocalPickupIds.Add(itemObjectId);
        if (acceptedAmount >= requestedAmount)
        {
            RPC_ConfirmScenePickup(item.transform.position, item.itemType, requestedAmount);
        }
        else
        {
            item.amount = requestedAmount - acceptedAmount;
            ClaimedLocalPickupIds.Remove(itemObjectId);
        }

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

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_ConfirmPickupDespawn(NetworkObject itemObject)
    {
        if (itemObject == null || Runner == null) return;
        
        if (itemObject.HasStateAuthority || (Runner.IsSharedModeMasterClient && itemObject.StateAuthority == PlayerRef.None))
        {
            Runner.Despawn(itemObject);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_ConfirmPickupPartial(NetworkObject itemObject, int remainingAmount)
    {
        if (itemObject == null || Runner == null) return;
        
        if (itemObject.HasStateAuthority || (Runner.IsSharedModeMasterClient && itemObject.StateAuthority == PlayerRef.None))
        {
            PickableItem item = itemObject.GetComponent<PickableItem>();
            if (item != null) item.amount = remainingAmount;
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
    private void RPC_ConfirmScenePickup(Vector3 itemPosition, ItemType itemType, int requestedAmount)
    {
        PickableItem item = FindMatchingScenePickup(itemPosition, itemType, requestedAmount);
        if (item == null)
        {
            return;
        }

        ClaimedLocalPickupIds.Add(item.gameObject.GetInstanceID());
        Destroy(item.gameObject);
    }

    private static PickableItem FindMatchingScenePickup(Vector3 itemPosition, ItemType itemType, int requestedAmount)
    {
        PickableItem[] items = FindObjectsOfType<PickableItem>(true);
        PickableItem bestMatch = null;
        float bestDistanceSqr = 0.25f;

        for (int i = 0; i < items.Length; i++)
        {
            PickableItem candidate = items[i];
            if (candidate == null || candidate.itemType != itemType || candidate.amount != requestedAmount)
            {
                continue;
            }

            float distanceSqr = (candidate.transform.position - itemPosition).sqrMagnitude;
            if (distanceSqr > bestDistanceSqr)
            {
                continue;
            }

            bestDistanceSqr = distanceSqr;
            bestMatch = candidate;
        }

        return bestMatch;
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
