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
        foreach (var item in icons)
        {
            if (item.itemType == type)
                return item.icon;
        }

        return null;
    }
}