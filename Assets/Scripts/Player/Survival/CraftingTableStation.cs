using UnityEngine;

[DisallowMultipleComponent]
public class CraftingTableStation : MonoBehaviour
{
    [SerializeField] private float interactionRadius = 3f;

    public float InteractionRadius => Mathf.Max(0.5f, interactionRadius);

    public bool IsInRange(Vector3 worldPosition)
    {
        return Vector3.Distance(transform.position, worldPosition) <= InteractionRadius;
    }

    private void OnValidate()
    {
        interactionRadius = Mathf.Max(0.5f, interactionRadius);
    }
}
