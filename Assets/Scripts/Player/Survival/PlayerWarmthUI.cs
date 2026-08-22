using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bar UI untuk warmth (kehangatan). Muncul otomatis di bawah bar survival lain,
/// pakai pattern CreateSliderBar yang sama dengan PlayerSurvivalUI.
/// Warna berubah: oranye normal → biru muda saat freezing.
/// </summary>
public class PlayerWarmthUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerWarmthSystem warmthSystem;
    [SerializeField] private RectTransform sliderContainer;
    [SerializeField] private float sliderWidth = 420f;
    [SerializeField] private float sliderHeight = 24f;
    [SerializeField] private float yOffset = -102f; // di bawah hunger (0, -34, -68)

    [Header("Colors")]
    [SerializeField] private Color warmColor = new Color(1f, 0.55f, 0.15f);   // oranye hangat
    [SerializeField] private Color coldColor = new Color(0.55f, 0.8f, 1f);    // biru dingin

    [Header("Layout")]
    [SerializeField] private bool autoCreate = true;

    private Slider warmthSlider;
    private Image fillImage;

    private void Awake()
    {
        if (warmthSystem == null)
        {
            warmthSystem = GetComponentInParent<PlayerWarmthSystem>();
            if (warmthSystem == null)
            {
                var player = GameObject.Find("FusionPlayer(Clone)");
                if (player != null) warmthSystem = player.GetComponent<PlayerWarmthSystem>();
            }
        }
    }

    private void OnEnable()
    {
        PlayerWarmthSystem.WarmthChanged += HandleWarmthChanged;
        EnsureBar();
        Refresh();
    }

    private void OnDisable()
    {
        PlayerWarmthSystem.WarmthChanged -= HandleWarmthChanged;
    }

    private void Update()
    {
        // Fallback refresh kalau event tidak sampai (misal UI dibuat belakangan)
        if (warmthSlider != null && !IsInvoking(nameof(Refresh)))
        {
            Refresh();
        }
    }

    private void HandleWarmthChanged(PlayerWarmthSystem source)
    {
        Refresh();
    }

    private void EnsureBar()
    {
        if (!autoCreate || warmthSlider != null) return;

        if (sliderContainer == null)
        {
            // Cari container milik PlayerSurvivalUI
            var survivalUi = GetComponentInParent<PlayerSurvivalUI>();
            if (survivalUi != null)
            {
                var field = typeof(PlayerSurvivalUI).GetField("sliderContainer",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                sliderContainer = field != null ? (RectTransform)field.GetValue(survivalUi) : null;
            }
        }

        if (sliderContainer == null) return;

        GameObject root = new GameObject("Warmth Slider", typeof(RectTransform), typeof(Image), typeof(Slider));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.SetParent(sliderContainer, false);

        Vector2 topCenter = new Vector2(0.5f, 1f);
        rootRect.anchorMin = topCenter;
        rootRect.anchorMax = topCenter;
        rootRect.pivot = topCenter;
        rootRect.anchoredPosition = new Vector2(0f, yOffset);
        rootRect.sizeDelta = new Vector2(sliderWidth, sliderHeight);

        var bg = root.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.45f);

        var handleArea = new GameObject("Fill Area", typeof(RectTransform)).GetComponent<RectTransform>();
        handleArea.SetParent(rootRect, false);
        handleArea.anchorMin = new Vector2(0f, 0f);
        handleArea.anchorMax = new Vector2(1f, 1f);
        handleArea.offsetMin = new Vector2(4f, 4f);
        handleArea.offsetMax = new Vector2(-4f, -4f);

        var fillRect = new GameObject("Fill", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        fillRect.SetParent(handleArea, false);
        fillImage = fillRect.GetComponent<Image>();
        fillImage.color = warmColor;
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(1f, 1f);

        var slider = root.GetComponent<Slider>();
        slider.fillRect = fillRect;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 1f;
        slider.interactable = false;
        slider.transition = Selectable.Transition.None;

        warmthSlider = slider;
    }

    private void Refresh()
    {
        if (warmthSystem == null || warmthSlider == null) return;

        float normalized = warmthSystem.WarmthNormalized;
        warmthSlider.value = normalized;
        if (fillImage != null)
        {
            fillImage.color = warmthSystem.IsFreezing ? coldColor : warmColor;
        }
    }
}
