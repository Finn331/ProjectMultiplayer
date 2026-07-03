using UnityEngine;
using UnityEngine.UI;

public class PickupUIManager : MonoBehaviour
{
    public static PickupUIManager instance;

    public Transform contentParent;
    public GameObject textPrefab;

    private void Awake()
    {
        instance = this;
    }

    public void ShowPickup(string itemName, int amount)
    {
        if (contentParent == null || textPrefab == null)
        {
            return;
        }

        GameObject obj = Instantiate(textPrefab, contentParent);
        DisableLayoutOnText(obj);

        PickupTextUI textUI = obj.GetComponent<PickupTextUI>();
        if (textUI != null)
        {
            textUI.Setup(itemName, amount);
        }
    }

    public void ShowInfo(string message)
    {
        if (contentParent == null || textPrefab == null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        GameObject obj = Instantiate(textPrefab, contentParent);
        DisableLayoutOnText(obj);

        PickupTextUI textUI = obj.GetComponent<PickupTextUI>();
        if (textUI != null)
        {
            textUI.SetupMessage(message);
        }
    }

    private static void DisableLayoutOnText(GameObject obj)
    {
        if (obj == null) return;
        LayoutElement layoutElement = obj.GetComponent<LayoutElement>();
        if (layoutElement == null)
        {
            layoutElement = obj.AddComponent<LayoutElement>();
        }
        layoutElement.ignoreLayout = true;
    }
}
