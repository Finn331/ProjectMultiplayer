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
        GameObject obj = Instantiate(textPrefab, contentParent);

        PickupTextUI textUI = obj.GetComponent<PickupTextUI>();
        textUI.Setup(itemName, amount);
    }
}