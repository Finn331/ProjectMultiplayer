using UnityEngine;
using UnityEngine.UI;

public class PlayerSurvivalUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerSurvivalSystem survivalSystem;
    [SerializeField] private RectTransform sliderContainer;
    [SerializeField] private Slider healthSlider;
    [SerializeField] private Slider thirstSlider;
    [SerializeField] private Slider hungerSlider;

    [Header("Layout")]
    [SerializeField] private float containerWidth = 460f;
    [SerializeField] private float containerHeight = 132f;
    [SerializeField] private float containerOffsetX = 0f;
    [SerializeField] private float containerOffsetY = -22f;
    [SerializeField] private float sliderWidth = 420f;
    [SerializeField] private float sliderHeight = 24f;
    [SerializeField] private float sliderSpacing = 34f;
    [SerializeField] private bool autoCreateMissingSliders = true;

    [Header("Colors")]
    [SerializeField] private Color healthColor = Color.red;
    [SerializeField] private Color thirstColor = Color.cyan;
    [SerializeField] private Color hungerColor = Color.green;

    private void Awake()
    {
        if (survivalSystem == null)
        {
            survivalSystem = FindObjectOfType<PlayerSurvivalSystem>();
        }

        this.EnsureContainer();

        if (autoCreateMissingSliders)
        {
            this.EnsureSliders();
        }

        this.ConfigureSlider(healthSlider);
        this.ConfigureSlider(thirstSlider);
        this.ConfigureSlider(hungerSlider);

        this.RefreshBars();
    }

    private void OnEnable()
    {
        if (survivalSystem != null)
        {
            survivalSystem.StatsChanged += this.HandleStatsChanged;
        }

        this.RefreshBars();
    }

    private void OnDisable()
    {
        if (survivalSystem != null)
        {
            survivalSystem.StatsChanged -= this.HandleStatsChanged;
        }
    }

    private void HandleStatsChanged(float healthValue, float hungerValue, float thirstValue)
    {
        this.RefreshBars();
    }

    private Vector2 BuildVector2(float xValue, float yValue)
    {
        Vector2 value = Vector2.zero;
        value.x = xValue;
        value.y = yValue;
        return value;
    }

    private void EnsureContainer()
    {
        if (sliderContainer != null)
        {
            return;
        }

        GameObject container = new GameObject("Survival HUD", typeof(RectTransform));
        RectTransform containerRect = container.GetComponent<RectTransform>();
        containerRect.SetParent(transform, false);

        Vector2 topCenter = this.BuildVector2(0.5f, 1f);
        containerRect.anchorMin = topCenter;
        containerRect.anchorMax = topCenter;
        containerRect.pivot = topCenter;
        containerRect.anchoredPosition = this.BuildVector2(containerOffsetX, containerOffsetY);
        containerRect.sizeDelta = this.BuildVector2(containerWidth, containerHeight);

        sliderContainer = containerRect;
    }

    private void EnsureSliders()
    {
        if (healthSlider == null)
        {
            healthSlider = this.CreateSliderBar("Health Slider", 0f, healthColor);
        }

        if (thirstSlider == null)
        {
            thirstSlider = this.CreateSliderBar("Thirst Slider", -sliderSpacing, thirstColor);
        }

        if (hungerSlider == null)
        {
            hungerSlider = this.CreateSliderBar("Hunger Slider", -sliderSpacing * 2f, hungerColor);
        }
    }

    private Slider CreateSliderBar(string objectName, float yOffset, Color fillColor)
    {
        GameObject root = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Slider));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.SetParent(sliderContainer, false);

        Vector2 topCenter = this.BuildVector2(0.5f, 1f);
        rootRect.anchorMin = topCenter;
        rootRect.anchorMax = topCenter;
        rootRect.pivot = topCenter;
        rootRect.anchoredPosition = this.BuildVector2(0f, yOffset);
        rootRect.sizeDelta = this.BuildVector2(sliderWidth, sliderHeight);

        Image backgroundImage = root.GetComponent<Image>();
        Color backgroundColor = Color.black;
        backgroundColor.a = 0.42f;
        backgroundImage.color = backgroundColor;

        Slider slider = root.GetComponent<Slider>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 1f;
        slider.wholeNumbers = false;

        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.SetParent(rootRect, false);
        fillAreaRect.anchorMin = Vector2.zero;
        fillAreaRect.anchorMax = Vector2.one;
        fillAreaRect.offsetMin = this.BuildVector2(8f, 6f);
        fillAreaRect.offsetMax = this.BuildVector2(-8f, -6f);

        GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.SetParent(fillAreaRect, false);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        Image fillImage = fill.GetComponent<Image>();
        fillImage.color = fillColor;

        GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
        handleAreaRect.SetParent(rootRect, false);
        handleAreaRect.anchorMin = Vector2.zero;
        handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.offsetMin = this.BuildVector2(8f, 0f);
        handleAreaRect.offsetMax = this.BuildVector2(-8f, 0f);

        GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        handleRect.SetParent(handleAreaRect, false);
        handleRect.sizeDelta = this.BuildVector2(2f, sliderHeight - 4f);

        Image handleImage = handle.GetComponent<Image>();
        Color handleColor = Color.white;
        handleColor.a = 0.2f;
        handleImage.color = handleColor;

        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.targetGraphic = handleImage;

        return slider;
    }

    private void ConfigureSlider(Slider slider)
    {
        if (slider == null)
        {
            return;
        }

        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
    }

    private void RefreshBars()
    {
        if (survivalSystem == null)
        {
            this.SetSliderValue(healthSlider, 0f);
            this.SetSliderValue(thirstSlider, 0f);
            this.SetSliderValue(hungerSlider, 0f);
            return;
        }

        this.SetSliderValue(healthSlider, survivalSystem.HealthNormalized);
        this.SetSliderValue(thirstSlider, survivalSystem.ThirstNormalized);
        this.SetSliderValue(hungerSlider, survivalSystem.HungerNormalized);
    }

    private void SetSliderValue(Slider slider, float normalizedValue)
    {
        if (slider == null)
        {
            return;
        }

        slider.value = Mathf.Clamp01(normalizedValue);
    }
}
