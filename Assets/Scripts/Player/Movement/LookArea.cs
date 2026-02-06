using UnityEngine;
using UnityEngine.EventSystems;

public class LookArea : MonoBehaviour, IDragHandler, IPointerDownHandler
{
    public Vector2 LookDelta { get; private set; }

    public void OnPointerDown(PointerEventData eventData)
    {
        LookDelta = Vector2.zero;
    }

    public void OnDrag(PointerEventData eventData)
    {
        LookDelta = eventData.delta;
    }

    public void ResetDelta()
    {
        LookDelta = Vector2.zero;
    }
}
