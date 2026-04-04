using UnityEngine;

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
        PickupTextUI textUI = obj.GetComponent<PickupTextUI>();
        if (textUI != null)
        {
            textUI.SetupMessage(message);
        }
    }
}
