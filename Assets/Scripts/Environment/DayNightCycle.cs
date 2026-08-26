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
    [SerializeField] private Gradient ambientSkyGradient;
    [SerializeField] private Gradient ambientEquatorGradient;
    [SerializeField] private Gradient ambientGroundGradient;

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
        CalculatePhaseTimes();
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

        ApplySun(phaseNormalized);
        ApplyAmbient(phaseNormalized);
        ApplyFog(phaseNormalized);
        ApplySkybox(phaseNormalized);
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
        switch (CurrentPhase)
        {
            case TimeOfDay.Night:
                return Mathf.Lerp(0f, dawnStartTime / totalCycleTime, phaseNormalized);
            case TimeOfDay.Dawn:
                return Mathf.Lerp(dawnStartTime / totalCycleTime, dayStartTime / totalCycleTime, phaseNormalized);
            case TimeOfDay.Day:
                return Mathf.Lerp(dayStartTime / totalCycleTime, duskStartTime / totalCycleTime, phaseNormalized);
            case TimeOfDay.Dusk:
                return Mathf.Lerp(duskStartTime / totalCycleTime, 1f, phaseNormalized);
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
