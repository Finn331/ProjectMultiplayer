using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    [Serializable]
    public class InventoryEntry
    {
        public ItemType itemType;
        public int amount;

        public bool IsEmpty => amount <= 0;
    }

    [Serializable]
    public class DropBinding
    {
        public ItemType itemType;
        public PickableItem dropPrefab;
    }

    public event Action InventoryChanged;

    [Header("Debug / Inspector")]
    [SerializeField] private List<InventoryEntry> itemEntries = new List<InventoryEntry>();
    [SerializeField] private List<DropBinding> dropBindings = new List<DropBinding>();

    [Header("Capacity")]
    [SerializeField] private int inventorySlotCount = 12;
    [SerializeField] private int hotbarSlotCount = 5;
    [SerializeField] private int maxStackPerSlot = 16;

    [Header("Drop Settings")]
    [SerializeField] private Transform dropOrigin;
    [SerializeField] private float dropForwardDistance = 1.2f;
    [SerializeField] private float dropUpOffset = 0.2f;
    [SerializeField] private float dropImpulse = 2.2f;

    private readonly Dictionary<ItemType, PickableItem> runtimeDropTemplates = new Dictionary<ItemType, PickableItem>();
    private readonly HashSet<ItemType> runtimeCloneTemplateTypes = new HashSet<ItemType>();
    private readonly List<InventoryEntry> populatedEntries = new List<InventoryEntry>();

    public IReadOnlyList<InventoryEntry> Slots => itemEntries;
    public IReadOnlyList<InventoryEntry> Entries => populatedEntries;
    public int InventorySlotCount => Mathf.Max(0, inventorySlotCount);
    public int HotbarSlotCount => Mathf.Max(0, hotbarSlotCount);
    public int HotbarStartIndex => InventorySlotCount;
    public int TotalSlotCount => Mathf.Max(1, InventorySlotCount + HotbarSlotCount);
    public int MaxStackPerSlot => Mathf.Max(1, maxStackPerSlot);
    public int CurrentTotalItems => this.GetCurrentTotalItems();
    public int MaxTotalItems => TotalSlotCount * MaxStackPerSlot;
    public int RemainingCapacity => Mathf.Max(0, MaxTotalItems - CurrentTotalItems);
    public int UsedSlotCount => this.GetUsedSlotCount();

    private void Awake()
    {
        this.SyncSlotsFromInspector();

        if (GetComponent<PlayerInventoryUI>() == null)
        {
            gameObject.AddComponent<PlayerInventoryUI>();
        }
    }

    public int AddItem(PickableItem item)
    {
        if (item == null)
        {
            return 0;
        }

        this.CacheRuntimeDropTemplate(item);

        int requestedAmount = Mathf.Max(1, item.amount);
        int acceptedAmount = this.AddItem(item.itemType, requestedAmount);

        if (PickupUIManager.instance != null && acceptedAmount > 0)
        {
            PickupUIManager.instance.ShowPickup(item.itemName, acceptedAmount);
            if (acceptedAmount < requestedAmount)
            {
                PickupUIManager.instance.ShowInfo("Inventory Full");
            }
        }

        return acceptedAmount;
    }

    public int AddItem(ItemType itemType, int amount)
    {
        int remaining = Mathf.Max(1, amount);
        int acceptedAmount = 0;

        acceptedAmount += this.FillExistingStacks(itemType, remaining, 0, InventorySlotCount, ref remaining);
        acceptedAmount += this.FillExistingStacks(itemType, remaining, HotbarStartIndex, TotalSlotCount, ref remaining);
        acceptedAmount += this.FillEmptySlots(itemType, remaining, 0, InventorySlotCount, ref remaining);
        acceptedAmount += this.FillEmptySlots(itemType, remaining, HotbarStartIndex, TotalSlotCount, ref remaining);

        if (acceptedAmount <= 0)
        {
            if (PickupUIManager.instance != null)
            {
                PickupUIManager.instance.ShowInfo("Inventory Full");
            }
            return 0;
        }

        this.SyncInspectorEntries();
        InventoryChanged?.Invoke();
        return acceptedAmount;
    }

    public bool RemoveItem(ItemType itemType, int amount = 1)
    {
        int remaining = Mathf.Max(1, amount);
        if (this.GetAmount(itemType) < remaining)
        {
            return false;
        }

        for (int i = TotalSlotCount - 1; i >= 0 && remaining > 0; i--)
        {
            InventoryEntry entry = itemEntries[i];
            if (entry == null || entry.IsEmpty || entry.itemType != itemType)
            {
                continue;
            }

            int removed = Mathf.Min(entry.amount, remaining);
            entry.amount -= removed;
            remaining -= removed;
            this.SanitizeSlot(entry);
        }

        this.SyncInspectorEntries();
        InventoryChanged?.Invoke();
        return true;
    }

    public bool RemoveItemFromSlot(int slotIndex, int amount, out ItemType removedItemType)
    {
        removedItemType = default;
        if (!this.IsValidSlot(slotIndex))
        {
            return false;
        }

        InventoryEntry entry = itemEntries[slotIndex];
        if (entry == null || entry.IsEmpty)
        {
            return false;
        }

        int clampedAmount = Mathf.Clamp(amount, 1, entry.amount);
        removedItemType = entry.itemType;
        entry.amount -= clampedAmount;
        this.SanitizeSlot(entry);
        this.SyncInspectorEntries();
        InventoryChanged?.Invoke();
        return true;
    }

    public bool MoveOrSwapSlot(int sourceSlotIndex, int targetSlotIndex)
    {
        if (!this.IsValidSlot(sourceSlotIndex) || !this.IsValidSlot(targetSlotIndex) || sourceSlotIndex == targetSlotIndex)
        {
            return false;
        }

        InventoryEntry sourceEntry = itemEntries[sourceSlotIndex];
        InventoryEntry targetEntry = itemEntries[targetSlotIndex];
        if (sourceEntry == null || sourceEntry.IsEmpty)
        {
            return false;
        }

        if (targetEntry == null)
        {
            targetEntry = new InventoryEntry();
            itemEntries[targetSlotIndex] = targetEntry;
        }

        if (targetEntry.IsEmpty)
        {
            targetEntry.itemType = sourceEntry.itemType;
            targetEntry.amount = sourceEntry.amount;
            this.ClearSlot(sourceEntry);
        }
        else if (targetEntry.itemType == sourceEntry.itemType && targetEntry.amount < MaxStackPerSlot)
        {
            int transferable = Mathf.Min(sourceEntry.amount, MaxStackPerSlot - targetEntry.amount);
            targetEntry.amount += transferable;
            sourceEntry.amount -= transferable;
            this.SanitizeSlot(sourceEntry);
        }
        else
        {
            ItemType tempType = targetEntry.itemType;
            int tempAmount = targetEntry.amount;
            targetEntry.itemType = sourceEntry.itemType;
            targetEntry.amount = sourceEntry.amount;
            sourceEntry.itemType = tempType;
            sourceEntry.amount = tempAmount;
        }

        this.SyncInspectorEntries();
        InventoryChanged?.Invoke();
        return true;
    }

    public bool DropItem(ItemType itemType, int amount = 1)
    {
        int remaining = Mathf.Max(1, amount);
        if (this.GetAmount(itemType) < remaining)
        {
            return false;
        }

        List<(int slotIndex, int removedAmount)> removedStacks = new List<(int slotIndex, int removedAmount)>();
        for (int i = TotalSlotCount - 1; i >= 0 && remaining > 0; i--)
        {
            InventoryEntry entry = itemEntries[i];
            if (entry == null || entry.IsEmpty || entry.itemType != itemType)
            {
                continue;
            }

            int removed = Mathf.Min(entry.amount, remaining);
            entry.amount -= removed;
            remaining -= removed;
            removedStacks.Add((i, removed));
            this.SanitizeSlot(entry);
        }

        int droppedAmount = Mathf.Max(1, amount);
        if (!this.SpawnDropItemWorld(itemType, droppedAmount, out PickableItem droppedItem))
        {
            for (int i = 0; i < removedStacks.Count; i++)
            {
                var rollback = removedStacks[i];
                InventoryEntry entry = itemEntries[rollback.slotIndex];
                if (entry == null)
                {
                    entry = new InventoryEntry();
                    itemEntries[rollback.slotIndex] = entry;
                }

                if (entry.IsEmpty)
                {
                    entry.itemType = itemType;
                }

                entry.amount += rollback.removedAmount;
            }

            this.SyncInspectorEntries();
            InventoryChanged?.Invoke();
            return false;
        }

        this.SyncInspectorEntries();
        InventoryChanged?.Invoke();
        return true;
    }

    public bool DropItemFromSlot(int slotIndex, int amount = 1)
    {
        if (!this.IsValidSlot(slotIndex))
        {
            return false;
        }

        InventoryEntry entry = itemEntries[slotIndex];
        if (entry == null || entry.IsEmpty)
        {
            return false;
        }

        ItemType itemType = entry.itemType;
        int droppedAmount = Mathf.Clamp(amount, 1, entry.amount);
        entry.amount -= droppedAmount;
        this.SanitizeSlot(entry);

        if (!this.SpawnDropItemWorld(itemType, droppedAmount, out PickableItem droppedItem))
        {
            if (entry.IsEmpty)
            {
                entry.itemType = itemType;
            }
            entry.amount += droppedAmount;
            this.SyncInspectorEntries();
            InventoryChanged?.Invoke();
            return false;
        }

        this.SyncInspectorEntries();
        InventoryChanged?.Invoke();
        return true;
    }

    public int GetAmount(ItemType itemType)
    {
        int total = 0;
        for (int i = 0; i < itemEntries.Count; i++)
        {
            InventoryEntry entry = itemEntries[i];
            if (entry == null || entry.IsEmpty || entry.itemType != itemType)
            {
                continue;
            }

            total += entry.amount;
        }

        return total;
    }

    public bool HasItem(ItemType itemType, int minimumAmount = 1)
    {
        return this.GetAmount(itemType) >= Mathf.Max(1, minimumAmount);
    }

    public ItemType? GetSlotItemType(int slotIndex)
    {
        this.EnsureSlotSetup();
        if (!this.IsValidSlot(slotIndex))
        {
            return null;
        }

        InventoryEntry entry = itemEntries[slotIndex];
        return entry != null && !entry.IsEmpty ? entry.itemType : (ItemType?)null;
    }

    public int GetSlotAmount(int slotIndex)
    {
        this.EnsureSlotSetup();
        if (!this.IsValidSlot(slotIndex))
        {
            return 0;
        }

        InventoryEntry entry = itemEntries[slotIndex];
        return entry != null && !entry.IsEmpty ? entry.amount : 0;
    }

    public bool IsSlotEmpty(int slotIndex)
    {
        this.EnsureSlotSetup();
        return this.GetSlotAmount(slotIndex) <= 0;
    }

    public bool IsHotbarSlot(int slotIndex)
    {
        this.EnsureSlotSetup();
        return this.IsValidSlot(slotIndex) && slotIndex >= HotbarStartIndex;
    }

    public int FindFirstSlotWithItemType(ItemType itemType)
    {
        this.EnsureSlotSetup();
        for (int i = 0; i < InventorySlotCount; i++)
        {
            if (this.GetSlotItemType(i) == itemType)
            {
                return i;
            }
        }

        for (int i = HotbarStartIndex; i < TotalSlotCount; i++)
        {
            if (this.GetSlotItemType(i) == itemType)
            {
                return i;
            }
        }

        return -1;
    }

    public string BuildSnapshotString()
    {
        this.EnsureSlotSetup();

        StringBuilder snapshotBuilder = new StringBuilder(160);
        for (int i = 0; i < TotalSlotCount; i++)
        {
            InventoryEntry entry = itemEntries[i];
            int itemTypeValue = entry != null && !entry.IsEmpty ? (int)entry.itemType : -1;
            int amount = entry != null && !entry.IsEmpty ? entry.amount : 0;

            if (snapshotBuilder.Length > 0)
            {
                snapshotBuilder.Append('|');
            }

            snapshotBuilder.Append(itemTypeValue);
            snapshotBuilder.Append(':');
            snapshotBuilder.Append(amount);
        }

        return snapshotBuilder.ToString();
    }

    public bool TryResolveDropPrefab(ItemType itemType, out PickableItem prefab)
    {
        prefab = this.GetDropPrefab(itemType);
        if (prefab != null)
        {
            return true;
        }

        if (this.TryResolveRegisteredNetworkPrefabByItemType(itemType, out PickableItem networkPrefab))
        {
            prefab = networkPrefab;
            return true;
        }

        if (runtimeDropTemplates.TryGetValue(itemType, out PickableItem runtimeTemplate) && runtimeTemplate != null)
        {
            prefab = runtimeTemplate;
            return true;
        }

        prefab = null;
        return false;
    }

    public Vector3 GetDropPositionWorld()
    {
        return this.GetDropPosition();
    }

    public Vector3 GetDropForwardDirection()
    {
        Transform origin = dropOrigin != null ? dropOrigin : transform;
        return origin.forward;
    }

    public void SetInventorySnapshot(int woodAmount, int stoneAmount, int foodAmount)
    {
        this.ClearAllSlots();
        this.AddItem(ItemType.Wood, woodAmount);
        this.AddItem(ItemType.Stone, stoneAmount);
        this.AddItem(ItemType.Food, foodAmount);
        this.SyncInspectorEntries();
        InventoryChanged?.Invoke();
    }

    public void SetInventorySnapshot(string snapshot)
    {
        this.ClearAllSlots();

        if (string.IsNullOrWhiteSpace(snapshot))
        {
            this.SyncInspectorEntries();
            InventoryChanged?.Invoke();
            return;
        }

        string[] segments = snapshot.Split('|');
        if (segments.Length == TotalSlotCount)
        {
            for (int i = 0; i < segments.Length && i < TotalSlotCount; i++)
            {
                this.TryParseSlotSnapshot(segments[i], itemEntries[i]);
            }
        }
        else
        {
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (string.IsNullOrWhiteSpace(segment))
                {
                    continue;
                }

                string[] pair = segment.Split(':');
                if (pair.Length != 2)
                {
                    continue;
                }

                if (!int.TryParse(pair[0], out int itemTypeValue) ||
                    !int.TryParse(pair[1], out int amount) ||
                    !Enum.IsDefined(typeof(ItemType), itemTypeValue))
                {
                    continue;
                }

                this.AddItem((ItemType)itemTypeValue, amount);
            }
        }

        this.SyncInspectorEntries();
        InventoryChanged?.Invoke();
    }

    private void OnDestroy()
    {
        foreach (KeyValuePair<ItemType, PickableItem> pair in runtimeDropTemplates)
        {
            if (pair.Value != null && runtimeCloneTemplateTypes.Contains(pair.Key))
            {
                Destroy(pair.Value.gameObject);
            }
        }

        runtimeCloneTemplateTypes.Clear();
    }

    private void SyncSlotsFromInspector()
    {
        if (this.MigrateLegacyEntriesIfNeeded())
        {
            return;
        }

        this.EnsureSlotSetup();
        for (int i = 0; i < itemEntries.Count; i++)
        {
            this.SanitizeSlot(itemEntries[i]);
        }
        this.SyncInspectorEntries();
    }

    private bool MigrateLegacyEntriesIfNeeded()
    {
        if (itemEntries.Count == TotalSlotCount)
        {
            this.EnsureSlotSetup();
            return false;
        }

        List<InventoryEntry> legacyEntries = new List<InventoryEntry>(itemEntries);
        itemEntries.Clear();
        this.EnsureSlotSetup();

        bool migrated = false;
        for (int i = 0; i < legacyEntries.Count; i++)
        {
            InventoryEntry legacyEntry = legacyEntries[i];
            if (legacyEntry == null || legacyEntry.amount <= 0)
            {
                continue;
            }

            migrated = true;
            this.AddItem(legacyEntry.itemType, legacyEntry.amount);
        }

        return migrated;
    }

    private void SyncInspectorEntries()
    {
        this.EnsureSlotSetup();
        populatedEntries.Clear();

        for (int i = 0; i < itemEntries.Count; i++)
        {
            InventoryEntry entry = itemEntries[i];
            this.SanitizeSlot(entry);
            if (entry != null && !entry.IsEmpty)
            {
                populatedEntries.Add(new InventoryEntry
                {
                    itemType = entry.itemType,
                    amount = entry.amount
                });
            }
        }
    }

    private PickableItem GetDropPrefab(ItemType itemType)
    {
        for (int i = 0; i < dropBindings.Count; i++)
        {
            DropBinding binding = dropBindings[i];
            if (binding != null && binding.itemType == itemType && binding.dropPrefab != null)
            {
                return binding.dropPrefab;
            }
        }

        return null;
    }

    private Vector3 GetDropPosition()
    {
        Transform origin = dropOrigin != null ? dropOrigin : transform;
        return origin.position + (origin.forward * dropForwardDistance) + (Vector3.up * dropUpOffset);
    }

    private void ApplyDropImpulse(GameObject droppedObject)
    {
        if (droppedObject == null)
        {
            return;
        }

        Rigidbody rb = droppedObject.GetComponent<Rigidbody>();
        if (rb == null)
        {
            return;
        }

        Transform origin = dropOrigin != null ? dropOrigin : transform;
        rb.AddForce(origin.forward * dropImpulse, ForceMode.VelocityChange);
    }

    private void ConfigureDroppedItem(PickableItem droppedItem, ItemType itemType, int amount)
    {
        if (droppedItem == null)
        {
            return;
        }

        droppedItem.itemType = itemType;
        droppedItem.amount = amount;
        if (string.IsNullOrWhiteSpace(droppedItem.itemName))
        {
            droppedItem.itemName = itemType.ToString();
        }
    }

    private void CacheRuntimeDropTemplate(PickableItem sourceItem)
    {
        if (sourceItem == null)
        {
            return;
        }

        ItemType type = sourceItem.itemType;
        if (this.GetDropPrefab(type) != null)
        {
            return;
        }

        if (this.TryResolveRegisteredNetworkPrefab(sourceItem, out PickableItem networkRegisteredPrefab))
        {
            if (runtimeDropTemplates.TryGetValue(type, out PickableItem previousTemplate) &&
                previousTemplate != null &&
                runtimeCloneTemplateTypes.Contains(type))
            {
                Destroy(previousTemplate.gameObject);
            }

            runtimeDropTemplates[type] = networkRegisteredPrefab;
            runtimeCloneTemplateTypes.Remove(type);
            return;
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            return;
        }

        if (runtimeDropTemplates.TryGetValue(type, out PickableItem oldTemplate) && oldTemplate != null && runtimeCloneTemplateTypes.Contains(type))
        {
            Destroy(oldTemplate.gameObject);
        }

        PickableItem template = Instantiate(sourceItem);
        template.name = sourceItem.itemType + " RuntimeDropTemplate";
        template.gameObject.SetActive(false);
        DontDestroyOnLoad(template.gameObject);
        runtimeDropTemplates[type] = template;
        runtimeCloneTemplateTypes.Add(type);
    }

    private bool TryResolveRegisteredNetworkPrefab(PickableItem sourceItem, out PickableItem registeredPrefab)
    {
        registeredPrefab = null;

        if (sourceItem == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            return false;
        }

        NetworkObject sourceNetworkObject = sourceItem.GetComponent<NetworkObject>();
        if (sourceNetworkObject == null || NetworkManager.Singleton.NetworkConfig?.Prefabs?.Prefabs == null)
        {
            return false;
        }

        uint sourceHash = sourceNetworkObject.PrefabIdHash;
        var prefabs = NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs;
        for (int i = 0; i < prefabs.Count; i++)
        {
            NetworkPrefab entry = prefabs[i];
            GameObject[] candidates = { entry.Prefab, entry.SourcePrefabToOverride, entry.OverridingTargetPrefab };
            for (int c = 0; c < candidates.Length; c++)
            {
                GameObject candidate = candidates[c];
                if (candidate == null)
                {
                    continue;
                }

                NetworkObject candidateNetworkObject = candidate.GetComponent<NetworkObject>();
                if (candidateNetworkObject == null || candidateNetworkObject.PrefabIdHash != sourceHash)
                {
                    continue;
                }

                PickableItem candidatePickable = candidate.GetComponent<PickableItem>();
                if (candidatePickable == null)
                {
                    continue;
                }

                registeredPrefab = candidatePickable;
                return true;
            }
        }

        return this.TryResolveRegisteredNetworkPrefabByItemType(sourceItem.itemType, out registeredPrefab);
    }

    private bool TryResolveRegisteredNetworkPrefabByItemType(ItemType itemType, out PickableItem registeredPrefab)
    {
        registeredPrefab = null;

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.NetworkConfig?.Prefabs?.Prefabs == null)
        {
            return false;
        }

        var prefabs = NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs;
        for (int i = 0; i < prefabs.Count; i++)
        {
            NetworkPrefab entry = prefabs[i];
            GameObject[] candidates = { entry.Prefab, entry.SourcePrefabToOverride, entry.OverridingTargetPrefab };
            for (int c = 0; c < candidates.Length; c++)
            {
                GameObject candidate = candidates[c];
                if (candidate == null)
                {
                    continue;
                }

                PickableItem candidatePickable = candidate.GetComponent<PickableItem>();
                if (candidatePickable == null || candidatePickable.itemType != itemType)
                {
                    continue;
                }

                NetworkObject candidateNetworkObject = candidate.GetComponent<NetworkObject>();
                if (candidateNetworkObject == null)
                {
                    continue;
                }

                registeredPrefab = candidatePickable;
                return true;
            }
        }

        return false;
    }

    public bool SpawnDropItemWorld(ItemType itemType, int amount, out PickableItem droppedItem)
    {
        droppedItem = null;
        int clampedAmount = Mathf.Max(1, amount);
        bool isNetworkSessionActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        if (this.TryResolveDropPrefab(itemType, out PickableItem prefab))
        {
            droppedItem = Instantiate(prefab, this.GetDropPositionWorld(), Quaternion.identity);
            droppedItem.gameObject.SetActive(true);
            this.ConfigureDroppedItem(droppedItem, itemType, clampedAmount);
            this.ApplyDropImpulse(droppedItem.gameObject);
            return true;
        }

        if (isNetworkSessionActive)
        {
            return false;
        }

        GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fallback.name = itemType + " (Dropped)";
        fallback.transform.position = this.GetDropPositionWorld();
        fallback.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);

        Interactable interactable = fallback.GetComponent<Interactable>();
        if (interactable == null)
        {
            fallback.AddComponent<Interactable>();
        }

        PickableItem fallbackItem = fallback.GetComponent<PickableItem>();
        if (fallbackItem == null)
        {
            fallbackItem = fallback.AddComponent<PickableItem>();
        }

        fallbackItem.itemType = itemType;
        fallbackItem.itemName = itemType.ToString();
        fallbackItem.amount = clampedAmount;

        Rigidbody rb = fallback.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = fallback.AddComponent<Rigidbody>();
        }

        this.ApplyDropImpulse(fallback);
        droppedItem = fallbackItem;
        return true;
    }

    private int FillExistingStacks(ItemType itemType, int requestedAmount, int startIndex, int endIndex, ref int remaining)
    {
        int accepted = 0;
        for (int i = startIndex; i < endIndex && remaining > 0; i++)
        {
            InventoryEntry entry = itemEntries[i];
            if (entry == null || entry.IsEmpty || entry.itemType != itemType || entry.amount >= MaxStackPerSlot)
            {
                continue;
            }

            int addable = Mathf.Min(MaxStackPerSlot - entry.amount, remaining);
            entry.amount += addable;
            remaining -= addable;
            accepted += addable;
        }

        return accepted;
    }

    private int FillEmptySlots(ItemType itemType, int requestedAmount, int startIndex, int endIndex, ref int remaining)
    {
        int accepted = 0;
        for (int i = startIndex; i < endIndex && remaining > 0; i++)
        {
            InventoryEntry entry = itemEntries[i];
            if (entry == null)
            {
                entry = new InventoryEntry();
                itemEntries[i] = entry;
            }

            if (!entry.IsEmpty)
            {
                continue;
            }

            int addable = Mathf.Min(MaxStackPerSlot, remaining);
            entry.itemType = itemType;
            entry.amount = addable;
            remaining -= addable;
            accepted += addable;
        }

        return accepted;
    }

    private void EnsureSlotSetup()
    {
        inventorySlotCount = Mathf.Max(0, inventorySlotCount);
        hotbarSlotCount = Mathf.Max(0, hotbarSlotCount);
        maxStackPerSlot = Mathf.Max(1, maxStackPerSlot);

        while (itemEntries.Count < TotalSlotCount)
        {
            itemEntries.Add(new InventoryEntry());
        }

        while (itemEntries.Count > TotalSlotCount)
        {
            itemEntries.RemoveAt(itemEntries.Count - 1);
        }

        for (int i = 0; i < itemEntries.Count; i++)
        {
            if (itemEntries[i] == null)
            {
                itemEntries[i] = new InventoryEntry();
            }
        }
    }

    private void SanitizeSlot(InventoryEntry entry)
    {
        if (entry == null)
        {
            return;
        }

        if (entry.amount <= 0)
        {
            this.ClearSlot(entry);
            return;
        }

        entry.amount = Mathf.Clamp(entry.amount, 0, MaxStackPerSlot);
        if (entry.amount <= 0)
        {
            this.ClearSlot(entry);
        }
    }

    private void ClearAllSlots()
    {
        this.EnsureSlotSetup();
        for (int i = 0; i < itemEntries.Count; i++)
        {
            this.ClearSlot(itemEntries[i]);
        }
    }

    private void ClearSlot(InventoryEntry entry)
    {
        if (entry == null)
        {
            return;
        }

        entry.amount = 0;
        entry.itemType = default;
    }

    private bool IsValidSlot(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < itemEntries.Count;
    }

    private int GetCurrentTotalItems()
    {
        int total = 0;
        for (int i = 0; i < itemEntries.Count; i++)
        {
            InventoryEntry entry = itemEntries[i];
            if (entry == null || entry.IsEmpty)
            {
                continue;
            }

            total += entry.amount;
        }

        return total;
    }

    private int GetUsedSlotCount()
    {
        int used = 0;
        for (int i = 0; i < itemEntries.Count; i++)
        {
            InventoryEntry entry = itemEntries[i];
            if (entry != null && !entry.IsEmpty)
            {
                used++;
            }
        }

        return used;
    }

    private void TryParseSlotSnapshot(string segment, InventoryEntry entry)
    {
        this.ClearSlot(entry);
        if (string.IsNullOrWhiteSpace(segment))
        {
            return;
        }

        string[] pair = segment.Split(':');
        if (pair.Length != 2)
        {
            return;
        }

        if (!int.TryParse(pair[0], out int itemTypeValue) || !int.TryParse(pair[1], out int amount))
        {
            return;
        }

        if (amount <= 0 || !Enum.IsDefined(typeof(ItemType), itemTypeValue))
        {
            return;
        }

        entry.itemType = (ItemType)itemTypeValue;
        entry.amount = Mathf.Clamp(amount, 1, MaxStackPerSlot);
    }
}
