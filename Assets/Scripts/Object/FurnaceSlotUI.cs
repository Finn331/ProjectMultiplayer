using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class FurnaceSlotUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    public enum SlotKind { Inventory, FurnaceFuel, FurnaceInput, FurnaceOutput }

    public SlotKind Kind;
    public int SlotIndex;
    public FurnaceUI Owner;

    private Image slotImage;
    private TextMeshProUGUI labelText;
    private CanvasGroup canvasGroup;
    private static GameObject dragGhost;

    public void Setup(SlotKind kind, int index, FurnaceUI owner)
    {
        Kind = kind;
        SlotIndex = index;
        Owner = owner;
        slotImage = GetComponent<Image>();
        labelText = GetComponentInChildren<TextMeshProUGUI>();
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    public void UpdateVisual(ItemType? itemType, int amount, string label)
    {
        if (labelText != null) labelText.text = label;
        if (slotImage != null)
        {
            slotImage.color = itemType != null ? new Color(0.3f, 0.35f, 0.4f) : new Color(0.16f, 0.16f, 0.16f, 0.95f);
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (Owner == null) return;
        if (!Owner.HasValidItem(Kind, SlotIndex)) return;

        canvasGroup.alpha = 0.6f;
        canvasGroup.blocksRaycasts = false;

        dragGhost = new GameObject("DragGhost", typeof(RectTransform), typeof(Image));
        dragGhost.transform.SetParent(Owner.transform.root, false);
        dragGhost.GetComponent<Image>().sprite = slotImage != null ? slotImage.sprite : null;
        dragGhost.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.7f);
        RectTransform rt = dragGhost.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(52f, 52f);
        dragGhost.transform.position = eventData.position;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (dragGhost != null)
        {
            dragGhost.transform.position = eventData.position;
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;

        if (dragGhost != null)
        {
            Destroy(dragGhost);
            dragGhost = null;
        }

        if (Owner == null) return;

        GameObject target = eventData.pointerCurrentRaycast.gameObject;
        if (target != null)
        {
            FurnaceSlotUI targetSlot = target.GetComponentInParent<FurnaceSlotUI>();
            if (targetSlot != null)
            {
                Owner.HandleSlotDrop(Kind, SlotIndex, targetSlot.Kind, targetSlot.SlotIndex);
            }
        }
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (Owner == null) return;

        FurnaceSlotUI sourceSlot = eventData.pointerDrag != null
            ? eventData.pointerDrag.GetComponentInParent<FurnaceSlotUI>()
            : null;

        if (sourceSlot != null)
        {
            Owner.HandleSlotDrop(sourceSlot.Kind, sourceSlot.SlotIndex, Kind, SlotIndex);
        }
    }
}
