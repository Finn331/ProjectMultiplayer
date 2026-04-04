using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    [Serializable]
    public class InventoryEntry
    {
        public ItemType itemType;
        public int amount;
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
    [SerializeField] private int maxTotalItems = 5;

    [Header("Drop Settings")]
    [SerializeField] private Transform dropOrigin;
    [SerializeField] private float dropForwardDistance = 1.2f;
    [SerializeField] private float dropUpOffset = 0.2f;
    [SerializeField] private float dropImpulse = 2.2f;

    private readonly Dictionary<ItemType, int> items = new Dictionary<ItemType, int>();
    private readonly Dictionary<ItemType, PickableItem> runtimeDropTemplates = new Dictionary<ItemType, PickableItem>();
    private readonly HashSet<ItemType> runtimeCloneTemplateTypes = new HashSet<ItemType>();

    public IReadOnlyList<InventoryEntry> Entries => itemEntries;
    public int MaxTotalItems => Mathf.Max(1, maxTotalItems);
    public int CurrentTotalItems => this.GetCurrentTotalItems();
    public int RemainingCapacity => Mathf.Max(0, maxTotalItems - this.GetCurrentTotalItems());

    private void Awake()
    {
        this.SyncDictionaryFromInspector();

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
        int acceptedAmount = Mathf.Min(requestedAmount, this.RemainingCapacity);
        if (acceptedAmount <= 0)
        {
            if (PickupUIManager.instance != null)
            {
                PickupUIManager.instance.ShowInfo("Inventory Full");
            }
            return 0;
        }

        if (!items.ContainsKey(item.itemType))
        {
            items[item.itemType] = 0;
        }

        items[item.itemType] += acceptedAmount;
        this.SyncInspectorEntries();

        if (PickupUIManager.instance != null)
        {
            PickupUIManager.instance.ShowPickup(item.itemName, acceptedAmount);
            if (acceptedAmount < requestedAmount)
            {
                PickupUIManager.instance.ShowInfo("Inventory Full");
            }
        }

        InventoryChanged?.Invoke();
        return acceptedAmount;
    }

    public bool RemoveItem(ItemType itemType, int amount = 1)
    {
        if (amount <= 0 || !items.ContainsKey(itemType) || items[itemType] < amount)
        {
            return false;
        }

        items[itemType] -= amount;
        if (items[itemType] <= 0)
        {
            items.Remove(itemType);
        }

        this.SyncInspectorEntries();
        InventoryChanged?.Invoke();
        return true;
    }

    public int GetAmount(ItemType itemType)
    {
        return items.TryGetValue(itemType, out int value) ? value : 0;
    }

    public bool HasItem(ItemType itemType, int minimumAmount = 1)
    {
        return this.GetAmount(itemType) >= Mathf.Max(1, minimumAmount);
    }

    public bool DropItem(ItemType itemType, int amount = 1)
    {
        int clampedAmount = Mathf.Max(1, amount);
        if (!this.RemoveItem(itemType, clampedAmount))
        {
            return false;
        }

        PickableItem droppedItem;
        return this.SpawnDropItemWorld(itemType, clampedAmount, out droppedItem);
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
            // In multiplayer we must spawn a registered NetworkObject prefab.
            // Returning false allows caller to rollback inventory state instead of
            // silently dropping an unsynchronized local object.
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

    public bool TryResolveDropPrefab(ItemType itemType, out PickableItem prefab)
    {
        prefab = this.GetDropPrefab(itemType);
        if (prefab != null)
        {
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
        items.Clear();
        this.SetInventoryAmountInternal(ItemType.Wood, woodAmount);
        this.SetInventoryAmountInternal(ItemType.Stone, stoneAmount);
        this.SetInventoryAmountInternal(ItemType.Food, foodAmount);
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

    private void SyncDictionaryFromInspector()
    {
        items.Clear();

        for (int i = 0; i < itemEntries.Count; i++)
        {
            InventoryEntry entry = itemEntries[i];
            if (entry == null || entry.amount <= 0)
            {
                continue;
            }

            if (!items.ContainsKey(entry.itemType))
            {
                items[entry.itemType] = 0;
            }

            items[entry.itemType] += entry.amount;
        }

        this.ApplyCapacityLimit();
    }

    private void SyncInspectorEntries()
    {
        itemEntries.Clear();

        foreach (KeyValuePair<ItemType, int> pair in items)
        {
            if (pair.Value <= 0)
            {
                continue;
            }

            itemEntries.Add(new InventoryEntry
            {
                itemType = pair.Key,
                amount = pair.Value
            });
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
            GameObject candidate = prefabs[i].Prefab;
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

        return false;
    }

    private int GetCurrentTotalItems()
    {
        int total = 0;
        foreach (KeyValuePair<ItemType, int> pair in items)
        {
            total += Mathf.Max(0, pair.Value);
        }
        return total;
    }

    private void ApplyCapacityLimit()
    {
        if (maxTotalItems < 1)
        {
            maxTotalItems = 1;
        }

        int overflow = this.GetCurrentTotalItems() - maxTotalItems;
        if (overflow <= 0)
        {
            return;
        }

        List<ItemType> keys = new List<ItemType>(items.Keys);
        keys.Sort((a, b) => b.CompareTo(a));

        for (int i = 0; i < keys.Count && overflow > 0; i++)
        {
            ItemType key = keys[i];
            int value = items[key];
            if (value <= 0)
            {
                continue;
            }

            int reduced = Mathf.Min(value, overflow);
            value -= reduced;
            overflow -= reduced;

            if (value <= 0)
            {
                items.Remove(key);
            }
            else
            {
                items[key] = value;
            }
        }
    }

    private void SetInventoryAmountInternal(ItemType itemType, int amount)
    {
        int clamped = Mathf.Max(0, amount);
        if (clamped <= 0)
        {
            return;
        }

        items[itemType] = clamped;
    }
}
