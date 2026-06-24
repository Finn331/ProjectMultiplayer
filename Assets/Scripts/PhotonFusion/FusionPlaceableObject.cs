using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class FusionPlaceableObject : NetworkBehaviour
{
    [Networked] public int ItemTypeValue { get; private set; }
    [Networked] public PlayerRef Placer { get; private set; }

    public ItemType ItemType => System.Enum.IsDefined(typeof(ItemType), ItemTypeValue)
        ? (ItemType)ItemTypeValue
        : default;

    public bool Initialize(ItemType itemType, PlayerRef placer)
    {
        if (Object == null || !Object.HasStateAuthority)
        {
            return false;
        }

        ItemTypeValue = (int)itemType;
        Placer = placer;
        return true;
    }
}
