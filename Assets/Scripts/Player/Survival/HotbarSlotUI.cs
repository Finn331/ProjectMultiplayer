using UnityEngine;
using UnityEngine.EventSystems;

public class HotbarSlotUI : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public int slotIndex;
    public MobileHotbarUI hotbar;

    private float holdTime = 2f;
    private float minHoldDetect = 0.2f;
    private float timer;
    private bool isHolding;
    private bool isDragging;

    private static int dragFrom = -1;

    void Update()
    {
        if (isHolding)
        {
            timer += Time.deltaTime;

            if (timer >= holdTime && timer > minHoldDetect)
            {
                isHolding = false;
                hotbar.DropFromSlot(slotIndex);
            }
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        isHolding = true;
        timer = 0f;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isHolding = false;

        if (!isDragging && timer < holdTime)
        {
            hotbar.SelectSlot(slotIndex);
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        dragFrom = slotIndex;
        isDragging = true;
        isHolding = false;
    }

    public void OnDrag(PointerEventData eventData) { }

    public void OnEndDrag(PointerEventData eventData)
    {
        isDragging = false;

        GameObject target = eventData.pointerCurrentRaycast.gameObject;
        if (target == null) return;

        HotbarSlotUI targetSlot = target.GetComponentInParent<HotbarSlotUI>();

        if (targetSlot != null && dragFrom != -1 && targetSlot.slotIndex != slotIndex)
        {
            hotbar.SwapSlot(dragFrom, targetSlot.slotIndex);
        }

        dragFrom = -1;
    }
}