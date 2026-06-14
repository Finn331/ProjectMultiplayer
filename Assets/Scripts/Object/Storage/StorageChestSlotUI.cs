using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum StorageChestSlotKind
{
    PlayerInventory,
    Chest
}

public class StorageChestSlotUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text countText;
    [SerializeField] private Image background;

    private StorageChestUI owner;

    public StorageChestSlotKind Kind { get; private set; }
    public int SlotIndex { get; private set; }

    public void Initialize(StorageChestUI ownerUI, StorageChestSlotKind kind, int slotIndex, Image iconImage, TMP_Text amountText, Image backgroundImage)
    {
        owner = ownerUI;
        Kind = kind;
        SlotIndex = slotIndex;
        icon = iconImage;
        countText = amountText;
        background = backgroundImage;
    }

    public void SetItem(Sprite sprite, int amount)
    {
        bool hasItem = amount > 0;
        if (icon != null)
        {
            icon.sprite = hasItem && sprite != null ? sprite : null;
            icon.enabled = hasItem && sprite != null;
            icon.raycastTarget = false;
        }

        if (countText != null)
        {
            countText.text = hasItem ? amount.ToString() : string.Empty;
            countText.gameObject.SetActive(hasItem);
            countText.raycastTarget = false;
        }
    }

    public void SetHighlight(bool highlighted, Color normalColor, Color highlightColor)
    {
        if (background != null)
        {
            background.color = highlighted ? highlightColor : normalColor;
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        owner?.BeginSlotDrag(this, eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        owner?.UpdateSlotDrag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        owner?.EndSlotDrag(this, eventData);
    }

    public void OnDrop(PointerEventData eventData)
    {
        owner?.HandleSlotDrop(this);
    }
}
