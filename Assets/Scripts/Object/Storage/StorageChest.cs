using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class StorageChest : NetworkBehaviour
{
    public event Action ChestChanged;

    [Header("Chest")]
    [SerializeField] private string chestName = "Storage Chest";
    [SerializeField] private int slotCount = 12;
    [SerializeField] private int maxStackPerSlot = 16;
    [SerializeField] private float interactDistance = 3f;
    [SerializeField] private List<PlayerInventory.InventoryEntry> slotEntries = new List<PlayerInventory.InventoryEntry>();

    private readonly NetworkVariable<FixedString512Bytes> chestSnapshot =
        new NetworkVariable<FixedString512Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public string ChestName => string.IsNullOrWhiteSpace(chestName) ? "Storage Chest" : chestName;
    public int SlotCount => Mathf.Max(1, slotCount);
    public int MaxStackPerSlot => Mathf.Max(1, maxStackPerSlot);
    public int UsedSlotCount => this.GetUsedSlotCount();
    public float InteractDistance => Mathf.Max(0.5f, interactDistance);

    private void Awake()
    {
        this.EnsureSlotSetup();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        this.EnsureSlotSetup();
        chestSnapshot.OnValueChanged -= this.OnChestSnapshotChanged;
        chestSnapshot.OnValueChanged += this.OnChestSnapshotChanged;

        if (IsServer)
        {
            this.PushSnapshotToNetwork();
        }

        this.ApplySnapshot(chestSnapshot.Value.ToString());
    }

    public override void OnNetworkDespawn()
    {
        chestSnapshot.OnValueChanged -= this.OnChestSnapshotChanged;
        base.OnNetworkDespawn();
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

    public ItemType? GetSlotItemType(int slotIndex)
    {
        this.EnsureSlotSetup();
        if (!this.IsValidSlot(slotIndex))
        {
            return null;
        }

        PlayerInventory.InventoryEntry entry = slotEntries[slotIndex];
        return entry != null && !entry.IsEmpty ? entry.itemType : (ItemType?)null;
    }

    public int GetSlotAmount(int slotIndex)
    {
        this.EnsureSlotSetup();
        if (!this.IsValidSlot(slotIndex))
        {
            return 0;
        }

        PlayerInventory.InventoryEntry entry = slotEntries[slotIndex];
        return entry != null && !entry.IsEmpty ? entry.amount : 0;
    }

    public bool IsSlotEmpty(int slotIndex)
    {
        return this.GetSlotAmount(slotIndex) <= 0;
    }

    public bool TryRequestStore(PlayerInventory playerInventory, int playerSlotIndex, int chestSlotIndex)
    {
        if (playerInventory == null || !this.IsValidSlot(chestSlotIndex))
        {
            return false;
        }

        if (this.IsNetworkSessionActiveButChestNotSpawned())
        {
            this.ShowChestSyncWarning();
            return false;
        }

        if (!this.UseNetworkedChest())
        {
            return this.StoreFromPlayer(playerInventory, playerSlotIndex, chestSlotIndex);
        }

        if (!this.HasLocalAuthority())
        {
            return false;
        }

        this.RequestStoreServerRpc(playerSlotIndex, chestSlotIndex);
        return true;
    }

    public bool TryRequestTake(PlayerInventory playerInventory, int chestSlotIndex, int preferredPlayerSlotIndex)
    {
        if (playerInventory == null || !this.IsValidSlot(chestSlotIndex))
        {
            return false;
        }

        if (this.IsNetworkSessionActiveButChestNotSpawned())
        {
            this.ShowChestSyncWarning();
            return false;
        }

        if (!this.UseNetworkedChest())
        {
            return this.TakeToPlayer(playerInventory, chestSlotIndex, preferredPlayerSlotIndex);
        }

        if (!this.HasLocalAuthority())
        {
            return false;
        }

        this.RequestTakeServerRpc(chestSlotIndex, preferredPlayerSlotIndex);
        return true;
    }

    private void OnChestSnapshotChanged(FixedString512Bytes previousValue, FixedString512Bytes newValue)
    {
        this.ApplySnapshot(newValue.ToString());
    }

    private bool StoreFromPlayer(PlayerInventory playerInventory, int playerSlotIndex, int chestSlotIndex)
    {
        ItemType? itemType = playerInventory.GetSlotItemType(playerSlotIndex);
        if (itemType == null)
        {
            return false;
        }

        PlayerInventory.InventoryEntry targetEntry = slotEntries[chestSlotIndex];
        if (targetEntry == null)
        {
            targetEntry = new PlayerInventory.InventoryEntry();
            slotEntries[chestSlotIndex] = targetEntry;
        }

        if (!targetEntry.IsEmpty && targetEntry.itemType != itemType.Value)
        {
            return false;
        }

        int playerAmount = playerInventory.GetSlotAmount(playerSlotIndex);
        int currentChestAmount = targetEntry.IsEmpty ? 0 : targetEntry.amount;
        int transferable = Mathf.Min(playerAmount, MaxStackPerSlot - currentChestAmount);
        if (transferable <= 0)
        {
            return false;
        }

        if (!playerInventory.RemoveItemFromSlot(playerSlotIndex, transferable, out ItemType removedType))
        {
            return false;
        }

        targetEntry.itemType = removedType;
        targetEntry.amount = currentChestAmount + transferable;
        this.MarkChanged();
        return true;
    }

    private bool TakeToPlayer(PlayerInventory playerInventory, int chestSlotIndex, int preferredPlayerSlotIndex)
    {
        ItemType? itemType = this.GetSlotItemType(chestSlotIndex);
        int chestAmount = this.GetSlotAmount(chestSlotIndex);
        if (itemType == null || chestAmount <= 0)
        {
            return false;
        }

        int targetPlayerSlot = playerInventory.FindPreferredInventorySlot(itemType.Value, preferredPlayerSlotIndex, false);
        if (targetPlayerSlot < 0)
        {
            return false;
        }

        int acceptedAmount = playerInventory.AddItemToSlot(itemType.Value, chestAmount, targetPlayerSlot);
        if (acceptedAmount <= 0)
        {
            return false;
        }

        PlayerInventory.InventoryEntry chestEntry = slotEntries[chestSlotIndex];
        chestEntry.amount -= acceptedAmount;
        if (chestEntry.amount <= 0)
        {
            chestEntry.amount = 0;
            chestEntry.itemType = default;
        }

        this.MarkChanged();
        return true;
    }

    private void MarkChanged()
    {
        this.EnsureSlotSetup();
        if (IsServer)
        {
            this.PushSnapshotToNetwork();
        }

        ChestChanged?.Invoke();
    }

    private void PushSnapshotToNetwork()
    {
        chestSnapshot.Value = new FixedString512Bytes(this.BuildSnapshotString());
    }

    private string BuildSnapshotString()
    {
        this.EnsureSlotSetup();
        StringBuilder builder = new StringBuilder(slotEntries.Count * 6);
        for (int i = 0; i < slotEntries.Count; i++)
        {
            PlayerInventory.InventoryEntry entry = slotEntries[i];
            int typeValue = entry != null && !entry.IsEmpty ? (int)entry.itemType : -1;
            int amount = entry != null && !entry.IsEmpty ? entry.amount : 0;
            if (builder.Length > 0)
            {
                builder.Append('|');
            }

            builder.Append(typeValue);
            builder.Append(':');
            builder.Append(amount);
        }

        return builder.ToString();
    }

    private void ApplySnapshot(string snapshot)
    {
        this.EnsureSlotSetup();
        for (int i = 0; i < slotEntries.Count; i++)
        {
            slotEntries[i].itemType = default;
            slotEntries[i].amount = 0;
        }

        if (!string.IsNullOrWhiteSpace(snapshot))
        {
            string[] segments = snapshot.Split('|');
            for (int i = 0; i < segments.Length && i < slotEntries.Count; i++)
            {
                string[] pair = segments[i].Split(':');
                if (pair.Length != 2)
                {
                    continue;
                }

                if (!int.TryParse(pair[0], out int typeValue) || !int.TryParse(pair[1], out int amount))
                {
                    continue;
                }

                if (amount <= 0 || !Enum.IsDefined(typeof(ItemType), typeValue))
                {
                    continue;
                }

                slotEntries[i].itemType = (ItemType)typeValue;
                slotEntries[i].amount = Mathf.Clamp(amount, 1, MaxStackPerSlot);
            }
        }

        ChestChanged?.Invoke();
    }

    private void EnsureSlotSetup()
    {
        slotCount = Mathf.Max(1, slotCount);
        maxStackPerSlot = Mathf.Max(1, maxStackPerSlot);
        while (slotEntries.Count < slotCount)
        {
            slotEntries.Add(new PlayerInventory.InventoryEntry());
        }

        while (slotEntries.Count > slotCount)
        {
            slotEntries.RemoveAt(slotEntries.Count - 1);
        }

        for (int i = 0; i < slotEntries.Count; i++)
        {
            if (slotEntries[i] == null)
            {
                slotEntries[i] = new PlayerInventory.InventoryEntry();
            }

            slotEntries[i].amount = Mathf.Clamp(slotEntries[i].amount, 0, MaxStackPerSlot);
            if (slotEntries[i].amount <= 0)
            {
                slotEntries[i].amount = 0;
                slotEntries[i].itemType = default;
            }
        }
    }

    private int GetUsedSlotCount()
    {
        int used = 0;
        for (int i = 0; i < slotEntries.Count; i++)
        {
            if (slotEntries[i] != null && !slotEntries[i].IsEmpty)
            {
                used++;
            }
        }

        return used;
    }

    private bool IsValidSlot(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < slotEntries.Count;
    }

    private bool UseNetworkedChest()
    {
        return this.IsNetworkSessionActive() && IsSpawned;
    }

    private bool HasLocalAuthority()
    {
        return !this.UseNetworkedChest() || (NetworkManager != null && NetworkManager.IsClient);
    }

    private bool IsNetworkSessionActive()
    {
        return NetworkManager != null && NetworkManager.IsListening;
    }

    private bool IsNetworkSessionActiveButChestNotSpawned()
    {
        return this.IsNetworkSessionActive() && !IsSpawned;
    }

    private void ShowChestSyncWarning()
    {
        if (PickupUIManager.instance != null)
        {
            PickupUIManager.instance.ShowInfo("Chest belum sinkron ke network. Coba host ulang/server restart.");
        }
    }

    private bool TryGetPlayerInventoryForClient(ulong clientId, out PlayerInventory playerInventory)
    {
        playerInventory = null;
        if (NetworkManager == null || !NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client.PlayerObject == null)
        {
            return false;
        }

        playerInventory = client.PlayerObject.GetComponent<PlayerInventory>();
        return playerInventory != null;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestStoreServerRpc(int playerSlotIndex, int chestSlotIndex, ServerRpcParams serverRpcParams = default)
    {
        if (!this.TryGetPlayerInventoryForClient(serverRpcParams.Receive.SenderClientId, out PlayerInventory playerInventory))
        {
            return;
        }

        if (Vector3.Distance(playerInventory.transform.position, transform.position) > interactDistance)
        {
            return;
        }

        if (this.StoreFromPlayer(playerInventory, playerSlotIndex, chestSlotIndex))
        {
            // Snapshot sudah dipush oleh MarkChanged() di StoreFromPlayer.
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestTakeServerRpc(int chestSlotIndex, int preferredPlayerSlotIndex, ServerRpcParams serverRpcParams = default)
    {
        if (!this.TryGetPlayerInventoryForClient(serverRpcParams.Receive.SenderClientId, out PlayerInventory playerInventory))
        {
            return;
        }

        if (Vector3.Distance(playerInventory.transform.position, transform.position) > interactDistance)
        {
            return;
        }

        if (this.TakeToPlayer(playerInventory, chestSlotIndex, preferredPlayerSlotIndex))
        {
            // Snapshot sudah dipush oleh MarkChanged() di TakeToPlayer.
        }
    }
}
