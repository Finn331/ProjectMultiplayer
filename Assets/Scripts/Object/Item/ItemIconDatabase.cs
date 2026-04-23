using UnityEngine;

[CreateAssetMenu(menuName = "Item/Item Icon Database")]
public class ItemIconDatabase : ScriptableObject
{
    [System.Serializable]
    public class ItemIcon
    {
        public ItemType itemType;
        public Sprite icon;
    }

    public ItemIcon[] icons;

    public Sprite GetIcon(ItemType type)
    {
        Sprite resolvedIcon = this.FindIcon(type);
        if (resolvedIcon != null)
        {
            return resolvedIcon;
        }

        if (type == ItemType.HealthConsumable ||
            type == ItemType.HungerConsumable ||
            type == ItemType.ThirstConsumable)
        {
            return this.FindIcon(ItemType.Food);
        }

        return null;
    }

    private Sprite FindIcon(ItemType type)
    {
        if (icons == null)
        {
            return null;
        }

        for (int i = 0; i < icons.Length; i++)
        {
            ItemIcon item = icons[i];
            if (item != null && item.itemType == type)
            {
                return item.icon;
            }
        }

        return null;
    }
}
