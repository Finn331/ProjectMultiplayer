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
    private float timer;
    private bool isHolding;

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
            hotbar = MobileHotbarUI.ActiveLocalInstance;
        }
    }

    void Update()
    {
        ResolveHotbar();

        if (isHolding)
        {
            timer += Time.deltaTime;

            if (enableHoldToDrop && timer >= holdTime)
            {
                isHolding = false;
                if (hotbar != null)
                {
                    hotbar.DropFromSlot(slotIndex);
                }
            }
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        ResolveHotbar();
        isHolding = true;
        timer = 0f;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        ResolveHotbar();
        isHolding = false;

        if (timer < holdTime)
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
