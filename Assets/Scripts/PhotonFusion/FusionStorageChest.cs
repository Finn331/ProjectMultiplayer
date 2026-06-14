using Fusion;
using System;
using UnityEngine;

public class FusionStorageChest : NetworkBehaviour, IStateAuthorityChanged
{
    private const int EmptyItemType = -1;
    private const int SlotCount = 12;

    [SerializeField] private int maxStackPerSlot = 16;
    [SerializeField] private float interactDistance = 3f;
    [SerializeField] private string chestName = "Storage Chest";

    private ChangeDetector changeDetector;
    private bool hasChangeDetector;
    private PendingTransaction pendingTransaction;

    public event Action ChestChanged;

    [Networked, Capacity(SlotCount)] private NetworkArray<int> ItemTypes => default;
    [Networked, Capacity(SlotCount)] private NetworkArray<int> Amounts => default;

    private enum PendingTransactionType
    {
        None,
        Take,
        Deposit
    }

    private struct PendingTransaction
    {
        public PendingTransactionType Type;
        public NetworkObject PlayerObject;
        public int PlayerSlot;
        public int ChestSlot;
        public int PreferredPlayerSlot;
        public int Amount;
    }

    public string ChestName => string.IsNullOrWhiteSpace(chestName) ? "Storage Chest" : chestName;
    public int Slots => SlotCount;
    public int SlotCountValue => SlotCount;
    public int MaxStackPerSlot => Mathf.Max(1, maxStackPerSlot);
    public float InteractDistance => Mathf.Max(0f, interactDistance);
    public int UsedSlotCount
    {
        get
        {
            int used = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                if (Amounts[i] > 0 && IsValidItemType(ItemTypes[i]))
                {
                    used++;
                }
            }

            return used;
        }
    }

    public override void Spawned()
    {
        EnsureStateAuthorityOverrideAllowed();
        changeDetector = GetChangeDetector(ChangeDetector.Source.SnapshotFrom);
        hasChangeDetector = true;

        if (!HasFusionStateAuthority())
        {
            ChestChanged?.Invoke();
            return;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (Amounts[i] <= 0)
            {
                ClearSlot(i);
            }
        }

        ChestChanged?.Invoke();
    }

    public override void Render()
    {
        if (!hasChangeDetector)
        {
            return;
        }

        foreach (string changedProperty in changeDetector.DetectChanges(this))
        {
            if (changedProperty == nameof(ItemTypes) || changedProperty == nameof(Amounts))
            {
                ChestChanged?.Invoke();
                break;
            }
        }
    }

    public bool TryInteract(PlayerInteractionSystem interactor)
    {
        if (interactor == null)
        {
            return false;
        }

        if (Vector3.Distance(interactor.transform.position, transform.position) > InteractDistance)
        {
            return false;
        }

        StorageChestUI chestUI = interactor.GetComponent<StorageChestUI>();
        if (chestUI == null)
        {
            chestUI = interactor.gameObject.AddComponent<StorageChestUI>();
        }

        chestUI.OpenChest(this);
        return true;
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
        return RequestTakeFromChest(playerObject, chestSlot, preferredPlayerSlot, int.MaxValue);
    }

    public bool RequestTakeFromChest(NetworkObject playerObject, int chestSlot, int preferredPlayerSlot, int amount)
    {
        if (!CanSendTransaction(playerObject) || amount <= 0)
        {
            return false;
        }

        if (!HasFusionStateAuthority())
        {
            RequestStateAuthorityForTransaction(new PendingTransaction
            {
                Type = PendingTransactionType.Take,
                PlayerObject = playerObject,
                ChestSlot = chestSlot,
                PreferredPlayerSlot = preferredPlayerSlot,
                Amount = amount
            });
            return true;
        }

        RPC_TakeFromChest(playerObject, chestSlot, preferredPlayerSlot, amount);
        return true;
    }

    public bool RequestDepositToChest(NetworkObject playerObject, int playerSlot, int chestSlot)
    {
        return RequestDepositToChest(playerObject, playerSlot, chestSlot, int.MaxValue);
    }

    public bool RequestDepositToChest(NetworkObject playerObject, int playerSlot, int chestSlot, int amount)
    {
        if (!CanSendTransaction(playerObject) || amount <= 0)
        {
            return false;
        }

        if (!HasFusionStateAuthority())
        {
            RequestStateAuthorityForTransaction(new PendingTransaction
            {
                Type = PendingTransactionType.Deposit,
                PlayerObject = playerObject,
                PlayerSlot = playerSlot,
                ChestSlot = chestSlot,
                Amount = amount
            });
            return true;
        }

        RPC_DepositToChest(playerObject, playerSlot, chestSlot, amount);
        return true;
    }

    public void StateAuthorityChanged()
    {
        if (HasFusionStateAuthority())
        {
            ExecutePendingTransaction();
        }
    }

    public void RPC_TakeFromChest(NetworkObject playerObject, int chestSlot, int preferredPlayerSlot, RpcInfo info = default)
    {
        RPC_TakeFromChest(playerObject, chestSlot, preferredPlayerSlot, int.MaxValue, info);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_TakeFromChest(NetworkObject playerObject, int chestSlot, int preferredPlayerSlot, int requestedAmount, RpcInfo info = default)
    {
        if (requestedAmount <= 0 ||
            !IsAuthorizedForPlayer(playerObject, info) ||
            !TryGetPlayerInventory(playerObject, out PlayerInventory playerInventory) ||
            !TryReadSlot(chestSlot, out ItemType itemType, out int chestAmount))
        {
            return;
        }

        if (!IsPlayerInRange(playerObject))
        {
            return;
        }

        int targetPlayerSlot = preferredPlayerSlot;
        int targetRemainingCapacity = int.MaxValue;
        if (requestedAmount == int.MaxValue)
        {
            bool includeHotbar = playerInventory.IsHotbarSlot(preferredPlayerSlot);
            targetPlayerSlot = playerInventory.FindPreferredInventorySlot(itemType, preferredPlayerSlot, includeHotbar);
        }

        if (targetPlayerSlot < 0)
        {
            return;
        }

        if (requestedAmount != int.MaxValue)
        {
            if (targetPlayerSlot >= playerInventory.TotalSlotCount)
            {
                return;
            }

            ItemType? targetItemType = playerInventory.GetSlotItemType(targetPlayerSlot);
            int targetAmount = playerInventory.GetSlotAmount(targetPlayerSlot);
            if ((targetItemType != null && targetItemType.Value != itemType) || targetAmount >= playerInventory.MaxStackPerSlot)
            {
                return;
            }

            targetRemainingCapacity = playerInventory.MaxStackPerSlot - targetAmount;
        }

        int amountToMove = Mathf.Min(chestAmount, requestedAmount, targetRemainingCapacity);
        int acceptedAmount = playerInventory.AddItemToSlot(itemType, amountToMove, targetPlayerSlot);
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

        ChestChanged?.Invoke();
    }

    public void RPC_DepositToChest(NetworkObject playerObject, int playerSlot, int chestSlot, RpcInfo info = default)
    {
        RPC_DepositToChest(playerObject, playerSlot, chestSlot, int.MaxValue, info);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_DepositToChest(NetworkObject playerObject, int playerSlot, int chestSlot, int requestedAmount, RpcInfo info = default)
    {
        if (requestedAmount <= 0 || !IsAuthorizedForPlayer(playerObject, info) || !TryGetPlayerInventory(playerObject, out PlayerInventory playerInventory) || !IsValidSlot(chestSlot))
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

        int transferableAmount = Mathf.Min(playerInventory.GetSlotAmount(playerSlot), requestedAmount, MaxStackPerSlot - currentChestAmount);
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

        ChestChanged?.Invoke();
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
        ChestChanged?.Invoke();
    }

    private bool CanSendTransaction(NetworkObject playerObject)
    {
        return Runner != null && Object != null && Object.IsValid && playerObject != null && playerObject.IsValid;
    }

    private void RequestStateAuthorityForTransaction(PendingTransaction transaction)
    {
        EnsureStateAuthorityOverrideAllowed();
        pendingTransaction = transaction;
        Object.RequestStateAuthority();
    }

    private void EnsureStateAuthorityOverrideAllowed()
    {
        if (Object == null)
        {
            return;
        }

        Object.Flags |= NetworkObjectFlags.AllowStateAuthorityOverride;
    }

    private void ExecutePendingTransaction()
    {
        PendingTransaction transaction = pendingTransaction;
        pendingTransaction = default;

        if (transaction.Type == PendingTransactionType.Take)
        {
            RPC_TakeFromChest(transaction.PlayerObject, transaction.ChestSlot, transaction.PreferredPlayerSlot, transaction.Amount);
            return;
        }

        if (transaction.Type == PendingTransactionType.Deposit)
        {
            RPC_DepositToChest(transaction.PlayerObject, transaction.PlayerSlot, transaction.ChestSlot, transaction.Amount);
        }
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

        if (playerObject.InputAuthority == info.Source)
        {
            return true;
        }

        return playerObject.HasStateAuthority && info.Source.IsNone;
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
