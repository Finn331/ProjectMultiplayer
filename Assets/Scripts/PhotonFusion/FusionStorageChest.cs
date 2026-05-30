using Fusion;
using UnityEngine;

public class FusionStorageChest : NetworkBehaviour
{
    private const int EmptyItemType = -1;
    private const int SlotCount = 12;

    [SerializeField] private int maxStackPerSlot = 16;
    [SerializeField] private float interactDistance = 3f;

    [Networked, Capacity(SlotCount)] private NetworkArray<int> ItemTypes => default;
    [Networked, Capacity(SlotCount)] private NetworkArray<int> Amounts => default;

    public int Slots => SlotCount;
    public int MaxStackPerSlot => Mathf.Max(1, maxStackPerSlot);
    public float InteractDistance => Mathf.Max(0f, interactDistance);

    public override void Spawned()
    {
        if (!HasFusionStateAuthority())
        {
            return;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (Amounts[i] <= 0)
            {
                ClearSlot(i);
            }
        }
    }

    public bool TryReadSlot(int slot, out ItemType itemType, out int amount)
    {
        itemType = default;
        amount = 0;

        if (!IsValidSlot(slot) || Amounts[slot] <= 0 || !IsValidItemType(ItemTypes[slot]))
        {
            return false;
        }

        itemType = (ItemType)ItemTypes[slot];
        amount = Amounts[slot];
        return true;
    }

    public bool RequestTakeFromChest(NetworkObject playerObject, int chestSlot, int preferredPlayerSlot)
    {
        if (!CanSendTransaction(playerObject))
        {
            return false;
        }

        RPC_TakeFromChest(playerObject, chestSlot, preferredPlayerSlot);
        return true;
    }

    public bool RequestDepositToChest(NetworkObject playerObject, int playerSlot, int chestSlot)
    {
        if (!CanSendTransaction(playerObject))
        {
            return false;
        }

        RPC_DepositToChest(playerObject, playerSlot, chestSlot);
        return true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_TakeFromChest(NetworkObject playerObject, int chestSlot, int preferredPlayerSlot, RpcInfo info = default)
    {
        if (!IsAuthorizedForPlayer(playerObject, info) ||
            !TryGetPlayerInventory(playerObject, out PlayerInventory playerInventory) ||
            !TryReadSlot(chestSlot, out ItemType itemType, out int chestAmount))
        {
            return;
        }

        if (!IsPlayerInRange(playerObject))
        {
            return;
        }

        int targetPlayerSlot = playerInventory.FindPreferredInventorySlot(itemType, preferredPlayerSlot, false);
        if (targetPlayerSlot < 0)
        {
            return;
        }

        int acceptedAmount = playerInventory.AddItemToSlot(itemType, chestAmount, targetPlayerSlot);
        if (acceptedAmount <= 0)
        {
            return;
        }

        int remainingAmount = chestAmount - acceptedAmount;
        Amounts.Set(chestSlot, Mathf.Max(0, remainingAmount));
        if (remainingAmount <= 0)
        {
            ClearSlot(chestSlot);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_DepositToChest(NetworkObject playerObject, int playerSlot, int chestSlot, RpcInfo info = default)
    {
        if (!IsAuthorizedForPlayer(playerObject, info) || !TryGetPlayerInventory(playerObject, out PlayerInventory playerInventory) || !IsValidSlot(chestSlot))
        {
            return;
        }

        if (!IsPlayerInRange(playerObject))
        {
            return;
        }

        ItemType? sourceItemType = playerInventory.GetSlotItemType(playerSlot);
        if (sourceItemType == null)
        {
            return;
        }

        int currentChestAmount = Amounts[chestSlot];
        if (currentChestAmount > 0 && (!IsValidItemType(ItemTypes[chestSlot]) || (ItemType)ItemTypes[chestSlot] != sourceItemType.Value))
        {
            return;
        }

        int transferableAmount = Mathf.Min(playerInventory.GetSlotAmount(playerSlot), MaxStackPerSlot - currentChestAmount);
        if (transferableAmount <= 0)
        {
            return;
        }

        if (!playerInventory.RemoveItemFromSlot(playerSlot, transferableAmount, out ItemType removedItemType))
        {
            return;
        }

        if (removedItemType != sourceItemType.Value)
        {
            RestoreRemovedItems(playerInventory, playerSlot, removedItemType, transferableAmount);
            return;
        }

        currentChestAmount = Amounts[chestSlot];
        if (currentChestAmount > 0 && (!IsValidItemType(ItemTypes[chestSlot]) || (ItemType)ItemTypes[chestSlot] != removedItemType))
        {
            RestoreRemovedItems(playerInventory, playerSlot, removedItemType, transferableAmount);
            return;
        }

        int acceptedAmount = Mathf.Min(transferableAmount, MaxStackPerSlot - currentChestAmount);
        if (acceptedAmount <= 0)
        {
            RestoreRemovedItems(playerInventory, playerSlot, removedItemType, transferableAmount);
            return;
        }

        ItemTypes.Set(chestSlot, (int)removedItemType);
        Amounts.Set(chestSlot, currentChestAmount + acceptedAmount);

        if (acceptedAmount < transferableAmount)
        {
            RestoreRemovedItems(playerInventory, playerSlot, removedItemType, transferableAmount - acceptedAmount);
        }
    }

    public void SetSlotStateForStateAuthority(int slot, ItemType itemType, int amount)
    {
        if (!HasFusionStateAuthority() || !IsValidSlot(slot))
        {
            return;
        }

        int clampedAmount = Mathf.Clamp(amount, 0, MaxStackPerSlot);
        if (clampedAmount <= 0)
        {
            ClearSlot(slot);
            return;
        }

        if (!IsValidItemType((int)itemType))
        {
            Debug.LogWarning($"FusionStorageChest rejected invalid item type value {(int)itemType} for slot {slot}.", this);
            ClearSlot(slot);
            return;
        }

        ItemTypes.Set(slot, (int)itemType);
        Amounts.Set(slot, clampedAmount);
    }

    private bool CanSendTransaction(NetworkObject playerObject)
    {
        return Runner != null && Object != null && Object.IsValid && playerObject != null && playerObject.IsValid;
    }

    private bool TryGetPlayerInventory(NetworkObject playerObject, out PlayerInventory playerInventory)
    {
        playerInventory = null;
        if (playerObject == null)
        {
            return false;
        }

        if (playerObject.GetComponent<FusionPlayerInventory>() == null)
        {
            return false;
        }

        playerInventory = playerObject.GetComponent<PlayerInventory>();
        return playerInventory != null;
    }

    private static bool IsAuthorizedForPlayer(NetworkObject playerObject, RpcInfo info)
    {
        if (playerObject == null)
        {
            return false;
        }

        if (info.Source == PlayerRef.None)
        {
            return playerObject.HasInputAuthority;
        }

        return playerObject.InputAuthority == info.Source;
    }

    private void RestoreRemovedItems(PlayerInventory playerInventory, int preferredSlot, ItemType itemType, int amount)
    {
        int remainingAmount = Mathf.Max(0, amount);
        if (remainingAmount <= 0 || playerInventory == null)
        {
            return;
        }

        remainingAmount -= playerInventory.AddItemToSlot(itemType, remainingAmount, preferredSlot);
        if (remainingAmount > 0)
        {
            playerInventory.AddItem(itemType, remainingAmount);
        }
    }

    private void ClearSlot(int slot)
    {
        ItemTypes.Set(slot, EmptyItemType);
        Amounts.Set(slot, 0);
    }

    private bool IsPlayerInRange(NetworkObject playerObject)
    {
        return playerObject != null && Vector3.Distance(playerObject.transform.position, transform.position) <= InteractDistance;
    }

    private static bool IsValidSlot(int slot)
    {
        return slot >= 0 && slot < SlotCount;
    }

    private static bool IsValidItemType(int itemTypeValue)
    {
        return System.Enum.IsDefined(typeof(ItemType), itemTypeValue);
    }

    private bool HasFusionStateAuthority()
    {
        return Object != null && Object.HasStateAuthority;
    }
}
