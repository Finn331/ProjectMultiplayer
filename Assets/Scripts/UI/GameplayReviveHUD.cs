using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class GameplayReviveHUD : MonoBehaviour
{
    public static GameplayReviveHUD Instance { get; private set; }

    [SerializeField] private Text promptText;
    [SerializeField] private Slider progressSlider;
    [SerializeField] private GameObject reviveButtonObject;
    [SerializeField] private ReviveHoldButton reviveHoldButton;

    public bool IsMobileReviveHeld => reviveHoldButton != null && reviveHoldButton.IsHeld;

    private void Awake()
    {
        Instance = this;
        EnsureUI();
        Clear();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void ShowPrompt(string message, bool showButton)
    {
        EnsureUI();
        if (promptText != null)
        {
            promptText.gameObject.SetActive(!string.IsNullOrWhiteSpace(message));
            promptText.text = message;
        }

        if (reviveButtonObject != null)
        {
            reviveButtonObject.SetActive(showButton);
        }
    }

    public void SetProgress(float normalizedProgress)
    {
        EnsureUI();
        if (progressSlider != null)
        {
            float value = Mathf.Clamp01(normalizedProgress);
            progressSlider.gameObject.SetActive(value > 0f && value < 1f);
            progressSlider.value = value;
        }
    }

    public void Clear()
    {
        if (promptText != null)
        {
            promptText.text = string.Empty;
            promptText.gameObject.SetActive(false);
        }

        if (progressSlider != null)
        {
            progressSlider.value = 0f;
            progressSlider.gameObject.SetActive(false);
        }

        if (reviveButtonObject != null)
        {
            reviveButtonObject.SetActive(false);
        }
    }

    private void EnsureUI()
    {
        if (promptText == null)
        {
            GameObject prompt = new GameObject("Revive Prompt", typeof(RectTransform), typeof(Text));
            RectTransform rect = prompt.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -130f);
            rect.sizeDelta = new Vector2(520f, 44f);

            promptText = prompt.GetComponent<Text>();
            promptText.alignment = TextAnchor.MiddleCenter;
            promptText.fontSize = 22;
            promptText.color = Color.white;
        }

        if (progressSlider == null)
        {
            GameObject progress = new GameObject("Revive Progress", typeof(RectTransform), typeof(Image), typeof(Slider));
            RectTransform rect = progress.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -170f);
            rect.sizeDelta = new Vector2(360f, 26f);

            Image bg = progress.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);

            GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
            RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
            fillAreaRect.SetParent(rect, false);
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.offsetMin = new Vector2(4f, 4f);
            fillAreaRect.offsetMax = new Vector2(-4f, -4f);

            GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            fillRect.SetParent(fillAreaRect, false);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            fill.GetComponent<Image>().color = new Color(0.35f, 1f, 0.45f, 1f);

            progressSlider = progress.GetComponent<Slider>();
            progressSlider.minValue = 0f;
            progressSlider.maxValue = 1f;
            progressSlider.value = 0f;
            progressSlider.fillRect = fillRect;
            progressSlider.direction = Slider.Direction.LeftToRight;
        }

        if (reviveButtonObject == null)
        {
            GameObject button = new GameObject("Revive Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(ReviveHoldButton));
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-56f, 132f);
            rect.sizeDelta = new Vector2(150f, 58f);
            button.GetComponent<Image>().color = new Color(0.1f, 0.55f, 0.22f, 0.88f);

            GameObject label = new GameObject("Label", typeof(RectTransform), typeof(Text));
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            Text labelText = label.GetComponent<Text>();
            labelText.text = "REVIVE";
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.fontSize = 20;
            labelText.color = Color.white;

            reviveButtonObject = button;
            reviveHoldButton = button.GetComponent<ReviveHoldButton>();
        }
    }
}
