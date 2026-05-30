using Fusion;
using UnityEngine;

public class FusionPickableItem : NetworkBehaviour
{
    [Networked] public int ItemTypeValue { get; set; }
    [Networked] public int Amount { get; set; }
    [Networked] public NetworkBool IsInitialized { get; set; }

    [SerializeField] private ItemType defaultItemType;
    [SerializeField] private int defaultAmount = 1;

    public ItemType ItemType => IsValidItemTypeValue(ItemTypeValue) ? (ItemType)ItemTypeValue : defaultItemType;
    public int ClampedAmount => Mathf.Max(1, Amount);

    public override void Spawned()
    {
        if (!HasFusionStateAuthority())
        {
            return;
        }

        if (!IsInitialized)
        {
            ItemTypeValue = (int)defaultItemType;
            IsInitialized = true;
        }
        else if (!IsValidItemTypeValue(ItemTypeValue))
        {
            ItemTypeValue = (int)defaultItemType;
        }

        if (Amount < 1)
        {
            Amount = Mathf.Max(1, defaultAmount);
        }
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

    private static bool IsValidItemTypeValue(int value)
    {
        return System.Enum.IsDefined(typeof(ItemType), value);
    }
}
