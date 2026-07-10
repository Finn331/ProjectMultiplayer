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
    private Image iconImage;
    private TextMeshProUGUI labelText;
    private CanvasGroup canvasGroup;
    private static GameObject dragGhost;
    private static Vector2 dragStartPos;
    private static bool dropHandled;
    private static ItemIconDatabase cachedIconDb;

    public void Setup(SlotKind kind, int index, FurnaceUI owner)
    {
        Kind = kind;
        SlotIndex = index;
        Owner = owner;
        slotImage = GetComponent<Image>();
        labelText = GetComponentInChildren<TextMeshProUGUI>();
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();

        GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(transform, false);
        iconImage = iconGo.GetComponent<Image>();
        iconImage.raycastTarget = false;
        RectTransform irt = iconGo.GetComponent<RectTransform>();
        irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
        irt.sizeDelta = new Vector2(-8f, -8f);
        irt.anchoredPosition = Vector2.zero;
        iconImage.preserveAspect = true;

        if (cachedIconDb == null) cachedIconDb = Resources.Load<ItemIconDatabase>("ItemIconDatabase");
    }

    public void UpdateVisual(ItemType? itemType, int amount, string label)
    {
        if (labelText == null) labelText = GetComponentInChildren<TextMeshProUGUI>();
        if (labelText != null) labelText.text = label;

        Sprite icon = null;
        if (itemType != null && cachedIconDb != null)
            icon = cachedIconDb.GetIcon(itemType.Value);

        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }

        if (slotImage != null)
        {
            slotImage.color = itemType != null ? new Color(0.3f, 0.35f, 0.4f, 0.95f) : new Color(0.16f, 0.16f, 0.16f, 0.95f);
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (Owner == null) return;
        if (!Owner.HasValidItem(Kind, SlotIndex)) return;

        dropHandled = false;
        dragStartPos = eventData.position;

        canvasGroup.alpha = 0.6f;
        canvasGroup.blocksRaycasts = false;

        dragGhost = new GameObject("DragGhost", typeof(RectTransform), typeof(Image));
        Canvas canvas = GetComponentInParent<Canvas>();
        dragGhost.transform.SetParent(canvas != null ? canvas.transform : transform, false);
        dragGhost.GetComponent<Image>().raycastTarget = false;
        dragGhost.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.7f);
        RectTransform rt = dragGhost.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(50f, 50f);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (dragGhost != null && dragGhost.transform is RectTransform rt)
        {
            Vector2 localPoint;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rt.parent as RectTransform, eventData.position, null, out localPoint);
            rt.localPosition = localPoint;
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

        if (!dropHandled && Vector2.Distance(dragStartPos, eventData.position) > 15f)
        {
            var results = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);
            foreach (var result in results)
            {
                FurnaceSlotUI target = result.gameObject.GetComponentInParent<FurnaceSlotUI>();
                if (target != null && target != this)
                {
                    Owner.HandleSlotDrop(Kind, SlotIndex, target.Kind, target.SlotIndex);
                    dropHandled = true;
                    break;
                }
            }
        }
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (Owner == null) return;
        if (eventData.pointerDrag == null) return;

        FurnaceSlotUI source = eventData.pointerDrag.GetComponentInParent<FurnaceSlotUI>();
        if (source != null && source != this)
        {
            Owner.HandleSlotDrop(source.Kind, source.SlotIndex, Kind, SlotIndex);
            dropHandled = true;
        }
    }
}
