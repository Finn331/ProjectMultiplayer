using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class FusionPickableItem : NetworkBehaviour
{
    [Networked] public int ItemTypeValue { get; set; }
    [Networked] public int Amount { get; set; }
    [Networked] public NetworkBool IsInitialized { get; set; }

    [SerializeField] private ItemType defaultItemType;
    [SerializeField] private int defaultAmount = 1;

    private PickableItem pickableItem;

    public ItemType ItemType => IsSpawnedNetworkObject() && IsInitialized && IsValidItemTypeValue(ItemTypeValue) ? (ItemType)ItemTypeValue : defaultItemType;
    public int ClampedAmount => IsSpawnedNetworkObject() && Amount > 0 ? Mathf.Max(1, Amount) : Mathf.Max(1, defaultAmount);

    public override void Spawned()
    {
        ResolveReferences();
        if (HasFusionStateAuthority())
        {
            InitializeDefaultsIfNeeded();
        }

        ApplyToPickableItem();
    }

    public override void Render()
    {
        ApplyToPickableItem();
    }

    private void Awake()
    {
        ResolveReferences();
    }

    public bool Initialize(ItemType itemType, int amount)
    {
        if (!HasFusionStateAuthority())
        {
            return false;
        }

        ItemTypeValue = (int)itemType;
        Amount = Mathf.Max(1, amount);
        IsInitialized = true;
        ApplyToPickableItem();
        return true;
    }

    public bool CanPickup(Transform player, float maxDistance)
    {
        return player != null && Vector3.Distance(transform.position, player.position) <= Mathf.Max(0f, maxDistance);
    }

    private bool HasFusionStateAuthority()
    {
        return Object != null && Object.HasStateAuthority;
    }

    private bool IsSpawnedNetworkObject()
    {
        return Object != null && Object.IsValid;
    }

    private void ResolveReferences()
    {
        if (pickableItem == null)
        {
            pickableItem = GetComponent<PickableItem>();
        }
    }

    private void InitializeDefaultsIfNeeded()
    {
        if (!IsInitialized || !IsValidItemTypeValue(ItemTypeValue))
        {
            ItemTypeValue = (int)defaultItemType;
            IsInitialized = true;
        }

        if (Amount < 1)
        {
            Amount = Mathf.Max(1, defaultAmount);
        }
    }

    private void ApplyToPickableItem()
    {
        ResolveReferences();
        if (pickableItem == null)
        {
            return;
        }

        pickableItem.itemType = ItemType;
        pickableItem.amount = ClampedAmount;
        if (string.IsNullOrWhiteSpace(pickableItem.itemName))
        {
            pickableItem.itemName = pickableItem.itemType.ToString();
        }
    }

    private static bool IsValidItemTypeValue(int value)
    {
        return System.Enum.IsDefined(typeof(ItemType), value);
    }
}
