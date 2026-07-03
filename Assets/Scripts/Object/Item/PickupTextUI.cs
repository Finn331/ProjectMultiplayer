using UnityEngine;
using TMPro;

public class PickupTextUI : MonoBehaviour
{
    public TextMeshProUGUI text;
    public float lifeTime = 1f;
    public float floatDistance = 80f;
    public LeanTweenType easeType = LeanTweenType.easeOutQuad;

    private RectTransform rectTransform;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();

        if (text == null)
        {
            text = GetComponent<TextMeshProUGUI>();
        }
    }

    private void Start()
    {
        if (rectTransform == null) return;

        Vector3 targetPos = rectTransform.anchoredPosition;
        targetPos.y += floatDistance;

        LeanTween.move(rectTransform, targetPos, lifeTime)
            .setEase(easeType);

        if (text != null)
        {
            CanvasGroup cg = GetComponent<CanvasGroup>();
            if (cg == null)
            {
                cg = gameObject.AddComponent<CanvasGroup>();
            }

            cg.alpha = 1f;
            LeanTween.alphaCanvas(rectTransform, 0f, lifeTime)
                .setEase(LeanTweenType.easeInQuad)
                .setOnComplete(() => Destroy(gameObject));
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
