using UnityEngine;
using TMPro;

public class PickupTextUI : MonoBehaviour
{
    public TextMeshProUGUI text;
    public float lifeTime = 1f;

    void Start()
    {
        Destroy(gameObject, lifeTime);
    }

    public void Setup(string itemName, int amount)
    {
        text.text = itemName + " +" + amount;
    }

    public void SetupMessage(string message)
    {
        text.text = message;
    }
}
