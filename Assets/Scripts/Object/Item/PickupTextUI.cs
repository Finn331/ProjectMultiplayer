using UnityEngine;
using TMPro;

public class PickupTextUI : MonoBehaviour
{
    public TextMeshProUGUI text;
    public float lifeTime = 1f;
    public float floatDistance = 80f;
    public LeanTweenType easeType = LeanTweenType.easeOutQuad;

    private void Start()
    {
        if (text == null)
        {
            text = GetComponent<TextMeshProUGUI>();
        }

        RectTransform rect = GetComponent<RectTransform>();
        if (rect != null)
        {
            LeanTween.moveY(rect, rect.anchoredPosition.y + floatDistance, lifeTime)
                .setEase(easeType);
        }

        if (text != null)
        {
            text.color = new Color(text.color.r, text.color.g, text.color.b, 1f);
            LeanTween.value(gameObject, 1f, 0f, lifeTime)
                .setEase(LeanTweenType.easeInQuad)
                .setOnUpdate((float val) =>
                {
                    text.color = new Color(text.color.r, text.color.g, text.color.b, val);
                })
                .setOnComplete(() =>
                {
                    Destroy(gameObject);
                });
        }
        else
        {
            Destroy(gameObject, lifeTime);
        }
    }

    public void Setup(string itemName, int amount)
    {
        if (text != null)
        {
            text.text = itemName + " +" + amount;
        }
    }

    public void SetupMessage(string message)
    {
        if (text != null)
        {
            text.text = message;
        }
    }
}
