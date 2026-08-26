using UnityEngine;

public class DayNightCycle : MonoBehaviour
{
    [Header("Cycle Duration")]
    [SerializeField] private float dayDurationSeconds = 14f;
    [SerializeField] private float nightDurationSeconds = 6f;
    [SerializeField] private float dawnDurationSeconds = 2f;
    [SerializeField] private float duskDurationSeconds = 2f;

    [Header("Sun")]
    [SerializeField] private Light sunLight;

    [Header("Sun Light Colors")]
    [SerializeField] private Gradient sunColorGradient;
    [SerializeField] private AnimationCurve sunIntensityCurve = AnimationCurve.EaseInOut(0f, 0.4f, 1f, 1.6f);

    [Header("Ambient")]
    [SerializeField] private Gradient ambientSkyGradient = DefaultGradient(new Color(0.65f, 0.72f, 0.85f));
    [SerializeField] private Gradient ambientEquatorGradient = DefaultGradient(new Color(0.5f, 0.55f, 0.6f));
    [SerializeField] private Gradient ambientGroundGradient = DefaultGradient(new Color(0.25f, 0.22f, 0.18f));

    [Header("Fog")]
    [SerializeField] private bool enableFog = false;
    [SerializeField] private Gradient fogColorGradient;
    [SerializeField] private AnimationCurve fogDensityCurve = AnimationCurve.EaseInOut(0f, 0.03f, 1f, 0.01f);

    [Header("Skybox")]
    [SerializeField] private Material skyboxMaterial;
    [SerializeField] private string skyboxTintProperty = "_Tint";
    [SerializeField] private Gradient skyboxTintGradient;
    [SerializeField] private string skyboxExposureProperty = "_Exposure";
    [SerializeField] private AnimationCurve skyboxExposureCurve = AnimationCurve.EaseInOut(0f, 0.3f, 1f, 1f);

    public enum TimeOfDay { Night, Dawn, Day, Dusk }
    public TimeOfDay CurrentPhase { get; private set; }

    private float totalCycleTime;
    private float dawnStartTime;
    private float dayStartTime;
    private float duskStartTime;
    private float nightStartTime;
    private static double startTime;

    private void Awake()
    {
        if (sunLight == null) sunLight = GetComponent<Light>();
        if (startTime == 0d) startTime = Time.timeAsDouble - (nightDurationSeconds + dawnDurationSeconds + dayDurationSeconds * 0.5); // mulai di fase siang
        // Pastikan ambient pakai mode Trilight (Gradient) — tidak butuh ambient probe bake, langsung terang
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientIntensity = 1.2f;
        CalculatePhaseTimes();
    }

    private static Gradient DefaultGradient(Color color)
    {
        var g = new Gradient();
        g.colorKeys = new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) };
        g.alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };
        return g;
    }

    private void OnValidate()
    {
        CalculatePhaseTimes();
    }

    private void CalculatePhaseTimes()
    {
        float halfDawn = dawnDurationSeconds / 2f;
        float halfDusk = duskDurationSeconds / 2f;

        nightStartTime = 0f;
        dawnStartTime = nightDurationSeconds - halfDawn;
        dayStartTime = nightDurationSeconds + halfDawn;
        duskStartTime = nightDurationSeconds + dayDurationSeconds - halfDusk;
        totalCycleTime = nightDurationSeconds + dawnDurationSeconds + dayDurationSeconds + duskDurationSeconds;
    }

    private void Update()
    {
        if (sunLight == null) return;

        float elapsed = (float)(Time.timeAsDouble - startTime);
        float cycleTime = elapsed % totalCycleTime;

        float phaseNormalized;
        TimeOfDay phase;
        DeterminePhase(cycleTime, out phase, out phaseNormalized);
        CurrentPhase = phase;

        // Pakai normalized sepanjang full cycle (0=malam -> 1=siang) agar gradient/curve
        // dievaluasi KONTINU dan tidak loncat di batas fase (hindari choppy).
        float fullT = GetFullCycleNormalized(phaseNormalized);

        ApplySun(fullT);
        ApplyAmbient(fullT);
        ApplyFog(fullT);
        ApplySkybox(fullT);
    }

    private void DeterminePhase(float t, out TimeOfDay phase, out float normalized)
    {
        if (t >= nightStartTime && t < dawnStartTime)
        {
            phase = TimeOfDay.Night;
            normalized = Mathf.InverseLerp(nightStartTime, dawnStartTime, t);
        }
        else if (t >= dawnStartTime && t < dayStartTime)
        {
            phase = TimeOfDay.Dawn;
            normalized = Mathf.InverseLerp(dawnStartTime, dayStartTime, t);
        }
        else if (t >= dayStartTime && t < duskStartTime)
        {
            phase = TimeOfDay.Day;
            normalized = Mathf.InverseLerp(dayStartTime, duskStartTime, t);
        }
        else if (t >= duskStartTime && t < totalCycleTime)
        {
            phase = TimeOfDay.Dusk;
            normalized = Mathf.InverseLerp(duskStartTime, totalCycleTime, t);
        }
        else
        {
            phase = TimeOfDay.Night;
            normalized = Mathf.InverseLerp(totalCycleTime, totalCycleTime + dawnStartTime, t);
        }
    }

    private float GetFullCycleNormalized(float phaseNormalized)
    {
        // Pemetaan KONTINU sepanjang cycle dengan malam di kedua ujung (t=0 dan t=1),
        // puncak siang di tengah (t=0.5). Agar saat cycle wrap (dusk -> night) tidak ada
        // lonjakan: t=1 (akhir dusk) = malam = sama dengan t=0 (awal night).
        float nightEnd = nightStartTime / totalCycleTime;
        float dawnEnd = dawnStartTime / totalCycleTime;
        float dayEnd = dayStartTime / totalCycleTime;
        float duskEnd = duskStartTime / totalCycleTime;
        switch (CurrentPhase)
        {
            case TimeOfDay.Night:
                // malam flat di t=0
                return 0f;
            case TimeOfDay.Dawn:
                // naik dari malam (0) ke puncak siang (0.5)
                return Mathf.Lerp(0f, 0.5f, phaseNormalized);
            case TimeOfDay.Day:
                // siang penuh di sekitar tengah: map ke [0.5, 0.5] (flat) atau sedikit variasi
                return Mathf.Lerp(0.5f, 0.5f, phaseNormalized);
            case TimeOfDay.Dusk:
                // turun dari puncak siang (0.5) ke malam (1.0) -> wrap ke t=0 = kontinu
                return Mathf.Lerp(0.5f, 1f, phaseNormalized);
            default:
                return 0f;
        }
    }

    private void ApplySun(float t)
    {
        float sunAngle = Mathf.Lerp(-90f, 270f, t);
        sunLight.transform.rotation = Quaternion.Euler(sunAngle, -30f, 0f);

        if (sunColorGradient != null)
            sunLight.color = sunColorGradient.Evaluate(t);

        sunLight.intensity = sunIntensityCurve.Evaluate(t);
    }

    private void ApplyAmbient(float t)
    {
        // Gunakan mode Trilight (Gradient Sky/Equator/Ground) — terang tanpa perlu ambient probe bake
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;

        if (ambientSkyGradient != null)
            RenderSettings.ambientSkyColor = ambientSkyGradient.Evaluate(t);

        if (ambientEquatorGradient != null)
            RenderSettings.ambientEquatorColor = ambientEquatorGradient.Evaluate(t);

        if (ambientGroundGradient != null)
            RenderSettings.ambientGroundColor = ambientGroundGradient.Evaluate(t);
    }

    private void ApplyFog(float t)
    {
        RenderSettings.fog = enableFog;

        if (!enableFog) return;

        if (fogColorGradient != null)
            RenderSettings.fogColor = fogColorGradient.Evaluate(t);

        RenderSettings.fogDensity = fogDensityCurve.Evaluate(t);
    }

    private void ApplySkybox(float t)
    {
        if (skyboxMaterial == null) return;

        if (skyboxTintGradient != null && skyboxMaterial.HasProperty(skyboxTintProperty))
            skyboxMaterial.SetColor(skyboxTintProperty, skyboxTintGradient.Evaluate(t));

        if (skyboxMaterial.HasProperty(skyboxExposureProperty))
            skyboxMaterial.SetFloat(skyboxExposureProperty, skyboxExposureCurve.Evaluate(t));
    }

    public float GetCurrentCycleTime()
    {
        float elapsed = (float)(Time.timeAsDouble - startTime);
        return elapsed % totalCycleTime;
    }
}
