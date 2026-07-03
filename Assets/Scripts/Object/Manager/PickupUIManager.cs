using UnityEngine;

public class PickupUIManager : MonoBehaviour
{
    public static PickupUIManager instance;

    public Transform contentParent;
    public GameObject textPrefab;

    private Transform canvasRoot;

    private void Awake()
    {
        instance = this;

        if (contentParent != null)
        {
            canvasRoot = contentParent;
            while (canvasRoot.parent != null && canvasRoot.GetComponent<Canvas>() == null)
            {
                canvasRoot = canvasRoot.parent;
            }
        }
    }

    public void ShowPickup(string itemName, int amount)
    {
        if (textPrefab == null)
        {
            return;
        }

        Transform parent = canvasRoot != null ? canvasRoot : contentParent;
        if (parent == null) return;

        GameObject obj = Instantiate(textPrefab, parent);

        RectTransform objRect = obj.GetComponent<RectTransform>();
        if (objRect != null)
        {
            objRect.anchoredPosition = Vector2.zero;
        }

        PickupTextUI textUI = obj.GetComponent<PickupTextUI>();
        if (textUI != null)
        {
            textUI.Setup(itemName, amount);
        }
    }

    public void ShowInfo(string message)
    {
        if (textPrefab == null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Transform parent = canvasRoot != null ? canvasRoot : contentParent;
        if (parent == null) return;

        GameObject obj = Instantiate(textPrefab, parent);

        RectTransform objRect = obj.GetComponent<RectTransform>();
        if (objRect != null)
        {
            objRect.anchoredPosition = Vector2.zero;
        }

        PickupTextUI textUI = obj.GetComponent<PickupTextUI>();
        if (textUI != null)
        {
            textUI.SetupMessage(message);
        }
    }
}
