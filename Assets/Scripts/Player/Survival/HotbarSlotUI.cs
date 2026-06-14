using UnityEngine;
using UnityEngine.EventSystems;

public class HotbarSlotUI : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public int slotIndex;
    public MobileHotbarUI hotbar;

    [SerializeField] private bool enableHoldToDrop;

    private const float holdTime = 2f;
    private const float splitHoldTime = 0.45f;
    private float timer;
    private bool isHolding;
    private bool splitHoldAttempted;
    private bool splitHoldAccepted;

    private static int dragFrom = -1;

    private void Awake()
    {
        if (hotbar == null)
        {
            hotbar = GetComponentInParent<MobileHotbarUI>();
        }
    }

    private void ResolveHotbar()
    {
        if (hotbar == null)
        {
            hotbar = GetComponentInParent<MobileHotbarUI>();
        }

        MobileHotbarUI activeHotbar = MobileHotbarUI.ActiveLocalInstance;
        if (activeHotbar != null && (hotbar == null || !hotbar.isActiveAndEnabled || hotbar != activeHotbar))
        {
            hotbar = activeHotbar;
        }
    }

    void Update()
    {
        ResolveHotbar();

        if (!isHolding)
        {
            return;
        }

        timer += Time.deltaTime;

        if (!splitHoldAttempted && timer >= splitHoldTime)
        {
            splitHoldAttempted = true;
            splitHoldAccepted = hotbar != null && hotbar.NotifySlotLongPressForSplit(slotIndex);
        }

        if (!splitHoldAccepted && enableHoldToDrop && timer >= holdTime)
        {
            isHolding = false;
            if (hotbar != null)
            {
                hotbar.DropFromSlot(slotIndex);
            }
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        ResolveHotbar();
        isHolding = true;
        splitHoldAttempted = false;
        splitHoldAccepted = false;
        timer = 0f;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        ResolveHotbar();
        isHolding = false;
        if (hotbar != null)
        {
            hotbar.NotifySlotPointerUpForSplit(slotIndex);
        }

        if (!splitHoldAccepted && timer < holdTime)
        {
            if (hotbar != null)
            {
                hotbar.SelectSlot(slotIndex);
            }
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        ResolveHotbar();
        dragFrom = slotIndex;
        isHolding = false;
    }

    public void OnDrag(PointerEventData eventData) { }

    public void OnEndDrag(PointerEventData eventData)
    {
        ResolveHotbar();

        GameObject target = eventData.pointerCurrentRaycast.gameObject;
        if (target != null)
        {
            HotbarSlotUI targetSlot = target.GetComponentInParent<HotbarSlotUI>();
            if (targetSlot != null && dragFrom != -1 && targetSlot.slotIndex != slotIndex)
            {
                if (hotbar != null)
                {
                    hotbar.SwapSlot(dragFrom, targetSlot.slotIndex);
                }
            }
        }

        dragFrom = -1;
    }
}
