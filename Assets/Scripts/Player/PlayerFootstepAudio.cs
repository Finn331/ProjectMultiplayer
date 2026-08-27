using Fusion;
using UnityEngine;

/// <summary>
/// Footstep SFX untuk player. Non-networked: tiap client memutar audio
/// secara lokal berdasarkan state player.
///
/// CATATAN EVOLUSI:
/// - V1: timer + grounding raycast kompleks (sUMBER flicker).
/// - V2: tambah anim-sync (normalizedTime footfall) ... TAPI animator Carlo
///   ternyata hanya punya state Idle/Run/Fall (TIDAK ada Walk, JumpStart, Land),
///   sehingga jalur anim-sync HANYA aktif saat lari dan tidak pernah saat jalan
///   pelan. Jalan pelan selalu masuk fallback timer yang punya grounding flicker.
/// - V3: PURE timer-based (pola Cowsins) + grounding pakai parameter Animator
///   `IsGrounded` (lebih reliable dari raycast di terrain uneven) dengan fallback
///   ke `controller.isGrounded`. Tidak bergantung pada nama state animasi.
///   Berlaku konsisten untuk jalan pelan maupun lari.
/// - V4 (sekarang): HYBRID STRIDE TIMER + RAYCAST GATE.
///   Primary = distance-based stride timer (`stepLength / speed`) — natural cadence
///   manusia (langkah konstan per meter, bukan per detik). Raycast foot-down jadi
///   GATE opsional (bunyi hanya saat kaki di tanah, TIDAK reset timer → anti-ghost).
///   Strategi ini menghindari "terlalu cepat" yang terjadi saat raycast dijadikan
///   trigger utama (kaki menyentuh tanah tiap frame, 2x per cycle).
///   Default stride: walk 1.4m, run 2.2m, sprint 2.8m → ~2.0-2.7 langkah/detik (natural).
///
/// Desain:
/// - Stride timer: `stepTimer -= dt`; saat <= 0 → trigger + `stepTimer = stepLength/speed`
///   (clamp [minStepInterval, maxStepInterval]).
/// - Raycast gate: `CheckFootDown()` untuk memblokir trigger saat kaki di udara.
/// - Min-speed threshold 0.8 m/s supaya idle micro-move tidak bunyi.
/// - Landing dideteksi dari perubahan sign `VerticalVelocity` (negatif) + ground check.
/// - Jump up dideteksi dari `VerticalVelocity` tiba-tiba positif saat leave-ground.
/// - Random clip + pitch variation (0.7-1.3) supaya tidak monoton.
/// - 3D spatial (rolloff) sehingga pemain lain mendengar dari posisi asli.
/// - Dedicated AudioSource kedua (index 1) agar tidak interferensi dengan audio senjata.
/// - PlayerSurfaceEffects hook tetap dipanggil via event Footfall untuk jejak salju.
/// </summary>
[RequireComponent(typeof(FusionPlayerMovement))]
public class PlayerFootstepAudio : MonoBehaviour
{
    private enum SurfaceType { Wood = 0, Snow = 1, Ice = 2, Grass = 3, Metal = 4, Mud = 5 }

    [Header("Surface")]
    [SerializeField] private SurfaceType surface = SurfaceType.Wood;

    [Header("Clip Sets (opsional — auto-load dari Resources jika kosong)")]
    [SerializeField] private AudioClip[] walkClips;
    [SerializeField] private AudioClip[] runClips;
    [SerializeField] private AudioClip[] sprintClips;
    [SerializeField] private AudioClip[] jumpUpClips;
    [SerializeField] private AudioClip[] jumpDownClips;

    [Header("Timing")]
    [Tooltip("Panjang satu langkah dalam meter saat jalan pelan. Standar manusia langkah pendek: 0.7-0.8m. Nilai ini akan diinterpretasikan sebagai stride (2 langkah = 1 stride cycle).")]
    [SerializeField, Range(0.3f, 3f)] private float walkStepLength = 1.4f;
    [Tooltip("Panjang satu langkah (stride) dalam meter saat lari. Standar pelari: 1.8-2.2m.")]
    [SerializeField, Range(0.5f, 4f)] private float runStepLength = 2.2f;
    [Tooltip("Panjang satu langkah (stride) dalam meter saat sprint. Standar sprinter: 2.5-3.0m.")]
    [SerializeField, Range(0.6f, 4f)] private float sprintStepLength = 2.8f;
    [Tooltip("Interval minimum antar langkah (anti double-play saat frame hitch).")]
    [SerializeField, Range(0.05f, 0.4f)] private float minStepInterval = 0.30f;
    [Tooltip("Interval maksimum antar langkah (anti jeda terlalu panjang saat jalan super pelan).")]
    [SerializeField, Range(0.4f, 1.5f)] private float maxStepInterval = 0.7f;

    [Header("Volume")]
    [Range(0f, 1f)] public float localVolume = 0.7f;
    [Range(0f, 1f)] public float remoteVolume = 0.9f;
    [SerializeField] private float jumpVolumeScale = 1.15f;

    [Header("Pitch Variation")]
    [SerializeField] private float pitchMin = 0.7f;
    [SerializeField] private float pitchMax = 1.3f;

    [Header("Detection")]
    [Tooltip("Kecepatan horizontal minimum untuk bunyi langkah (m/s). 0.8 = jalan pelan di atas ini baru bunyi (skip idle micro-move).")]
    [SerializeField] private float minSpeedToStep = 0.8f;
    [Tooltip(">= ini = run clip.")]
    [SerializeField] private float runSpeedThreshold = 3.4f;
    [Tooltip(">= ini = sprint clip.")]
    [SerializeField] private float sprintSpeedThreshold = 5.5f;
    [Tooltip("Vertical velocity minimum (positif) untuk trigger jump up.")]
    [SerializeField] private float jumpUpVelocity = 1.2f;
    [Tooltip("Vertical velocity minimum (negatif) untuk trigger landing.")]
    [SerializeField] private float landingVelocity = -1.5f;
    [Tooltip("Waktu minimum di udara sebelum landing bisa trigger (anti false-positive).")]
    [SerializeField] private float minAirTimeForLand = 0.08f;
    [Tooltip("Jika true: raycast foot-down jadi GATE tambahan supaya bunyi hanya saat kaki di tanah (tidak reset timer).")]
    [SerializeField] private bool gateStepOnRaycast = true;
    [Tooltip("Jarak raycast gate (meter). 0.3m = ketat (hanya saat kaki di tanah).")]
    [SerializeField] private float groundCheckDist = 0.3f;

    [Header("Animator (opsional, untuk ground & velocity parameter)")]
    [SerializeField] private Animator animator;
    [SerializeField] private string isGroundedParam = "IsGrounded";
    [SerializeField] private string verticalVelocityParam = "VerticalVelocity";
    [SerializeField] private string speedParam = "Speed";

    [Header("AudioSource")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private float spatialMinDistance = 2.5f;
    [SerializeField] private float spatialMaxDistance = 22f;

    [Header("Debug")]
    [SerializeField] private bool debugLogSteps = false;

    private const string ResourceRoot = "Audio/Footsteps/";
    private static readonly string[] SurfaceNames = { "wood", "snow", "ice", "grass", "metal", "mud" };

    private FusionPlayerMovement movement;
    private CharacterController controller;
    private int isGroundedHash;
    private int verticalVelocityHash;
    private int speedHash;
    private bool hasIsGroundedParam;
    private bool hasVerticalVelocityParam;
    private bool hasSpeedParam;

    // Timer & state
    private float stepTimer;
    private float airTime;
    private bool wasGrounded = true;
    private float prevVerticalVelocity;
    private Vector3 lastPosition;
    private bool hasLastPosition;
    private readonly System.Random rng = new System.Random();

    /// <summary>
    /// Dipicu tiap langkah. Argumen: surface ID (0..5) dan kecepatan horizontal (m/s).
    /// Dipakai PlayerSurfaceEffects untuk jejak kaki salju.
    /// </summary>
    public event System.Action<int, float> Footfall;
    private void Awake()
    {
        movement = GetComponent<FusionPlayerMovement>();
        controller = GetComponent<CharacterController>();

        if (audioSource == null)
        {
            // Pola Cowsins: dedicated AudioSource kedua untuk footstep agar tidak
            // mengganggu audio senjata (yang biasanya pakai AudioSource pertama).
            var existingSources = GetComponents<AudioSource>();
            if (existingSources.Length > 1)
            {
                audioSource = existingSources[1];
            }
            else
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                var mainSource = existingSources.Length > 0 ? existingSources[0] : null;
                if (mainSource != null)
                {
                    audioSource.spatialBlend = mainSource.spatialBlend;
                    audioSource.outputAudioMixerGroup = mainSource.outputAudioMixerGroup;
                }
            }
        }

        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;               // fully 3D
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.minDistance = spatialMinDistance;
        audioSource.maxDistance = spatialMaxDistance;
        audioSource.dopplerLevel = 0f;

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        CacheAnimatorParams();

        // stepTimer mulai pada interval menengah supaya langkah pertama tidak terlalu cepat/lambat.
        stepTimer = 0.4f;

        LoadClipsFromResources();
    }

    private void CacheAnimatorParams()
    {
        if (animator == null)
        {
            hasIsGroundedParam = false;
            hasVerticalVelocityParam = false;
            hasSpeedParam = false;
            return;
        }

        isGroundedHash = Animator.StringToHash(isGroundedParam);
        verticalVelocityHash = Animator.StringToHash(verticalVelocityParam);
        speedHash = Animator.StringToHash(speedParam);

        // Cache parameter existence — hindari GetComponent warning tiap frame.
        var parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name == isGroundedParam) hasIsGroundedParam = true;
            else if (parameters[i].name == verticalVelocityParam) hasVerticalVelocityParam = true;
            else if (parameters[i].name == speedParam) hasSpeedParam = true;
        }
    }

    /// <summary>Auto-load clip sets dari Resources sesuai surface.</summary>
    public void LoadClipsFromResources()
    {
        if (walkClips == null || walkClips.Length == 0) walkClips = LoadSet("walk");
        if (runClips == null || runClips.Length == 0) runClips = LoadSet("run");
        if (sprintClips == null || sprintClips.Length == 0) sprintClips = LoadSet("sprint");
        if (jumpUpClips == null || jumpUpClips.Length == 0) jumpUpClips = LoadSet("jumpup");
        if (jumpDownClips == null || jumpDownClips.Length == 0) jumpDownClips = LoadSet("jumpdown");
    }

    private AudioClip[] LoadSet(string action)
    {
        string path = ResourceRoot + SurfaceNames[(int)surface] + "/" + action;
        AudioClip[] clips = Resources.LoadAll<AudioClip>(path);
        return clips ?? new AudioClip[0];
    }

    /// <summary>Ganti permukaan saat runtime (untuk sistem material terrain nanti).</summary>
    public void SetSurface(int newSurface)
    {
        if (surface == (SurfaceType)newSurface) return;
        surface = (SurfaceType)newSurface;
        walkClips = null; runClips = null; sprintClips = null;
        jumpUpClips = null; jumpDownClips = null;
        LoadClipsFromResources();
    }

    private void Update()
    {
        if (movement == null || controller == null || audioSource == null) return;

        if (movement.ControlsBlocked)
        {
            ResetTrackers();
            return;
        }

        // V3: PURE timer-based. Konsisten untuk semua kondisi (jalan/lari/lompat)
        // karena TIDAK bergantung pada nama state Animator (Carlo hanya punya Idle/Run/Fall).
        UpdateFootstepTimer();
    }

    private void ResetTrackers()
    {
        stepTimer = 0.4f;
        airTime = 0f;
        wasGrounded = true;
        prevVerticalVelocity = 0f;
        hasLastPosition = false;
    }

    // ------------------------------------------------------------------
    // GROUND & VELOCITY (multi-source, fallback chain)
    // ------------------------------------------------------------------

    /// <summary>True bila player sedang di tanah. Prioritas: controller.isGrounded (fisik real-time)
    /// > Animator param (bisa lag karena diisi network/Fusion).</summary>
    private bool IsGrounded()
    {
        if (controller != null)
        {
            return controller.isGrounded;
        }
        if (hasIsGroundedParam && animator != null)
        {
            return animator.GetBool(isGroundedHash);
        }
        return false;
    }

    /// <summary>Vertical velocity (m/s). Prioritas: controller.velocity.y (fisik real-time)
    /// > Animator param (bisa lag karena diisi network/Fusion).</summary>
    private float GetVerticalVelocity()
    {
        if (controller != null)
        {
            return controller.velocity.y;
        }
        if (hasVerticalVelocityParam && animator != null)
        {
            return animator.GetFloat(verticalVelocityHash);
        }
        return 0f;
    }

    /// <summary>Speed (m/s). Prioritas: controller.velocity aktual > Animator param > position delta.
    /// controller.velocity dipakai sebagai primary karena merefleksikan gerakan fisik real-time
    /// (tanpa lag animator param yang diisi network/Fusion).</summary>
    private float GetSpeed()
    {
        if (controller != null)
        {
            Vector3 v = controller.velocity;
            v.y = 0f;
            float mag = v.magnitude;
            if (mag > 0.001f) return mag;
        }
        if (hasSpeedParam && animator != null)
        {
            return animator.GetFloat(speedHash);
        }
        return MeasureHSpeed();
    }

    /// <summary>Kecepatan horizontal aktual dari position delta (dengan hitch guard).</summary>
    private float MeasureHSpeed()
    {
        Vector3 position = transform.position;
        float hSpeed;
        if (!hasLastPosition)
        {
            hSpeed = 0f;
            hasLastPosition = true;
        }
        else
        {
            Vector3 delta = position - lastPosition;
            delta.y = 0f;
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            if (dt > 0.2f)
            {
                // Frame putus/stutter: jangan hitung kecepatan dari frame ini.
                hSpeed = 0f;
            }
            else
            {
                hSpeed = delta.magnitude / dt;
            }
        }
        lastPosition = position;
        return hSpeed;
    }

    // ------------------------------------------------------------------
    // FOOTSTEP TIMER (pola Cowsins, robust untuk semua kondisi)
    // ------------------------------------------------------------------

    private void UpdateFootstepTimer()
    {
        bool grounded = IsGrounded();
        float verticalVelocity = GetVerticalVelocity();
        float hSpeed = GetSpeed();
        // === Jump / Landing detection (parameter-based, tidak bergantung state name) ===
        if (grounded)
        {
            // Landing: baru mendarat setelah sebelumnya di udara cukup lama & velocity turun.
            if (!wasGrounded && airTime >= minAirTimeForLand && prevVerticalVelocity <= landingVelocity)
            {
                PlayRandom(jumpDownClips, BaseVolume() * jumpVolumeScale);
                if (debugLogSteps) Debug.Log($"[Footstep] LAND (vY={prevVerticalVelocity:F2} airTime={airTime:F2})");
                // Reset separuh interval agar langkah pertama setelah landing tidak kelamaan.
                stepTimer = 0.4f;
            }
            airTime = 0f;
        }
        else
        {
            airTime += Time.deltaTime;

            // Jump up: tinggalkan tanah dengan velocity positif.
            if (wasGrounded && airTime < 0.05f && verticalVelocity >= jumpUpVelocity)
            {
                PlayRandom(jumpUpClips, BaseVolume());
                if (debugLogSteps) Debug.Log($"[Footstep] JUMP UP (vY={verticalVelocity:F2})");
            }
        }

        wasGrounded = grounded;
        prevVerticalVelocity = verticalVelocity;

        // === Footstep detection ===
        if (!grounded || hSpeed < minSpeedToStep)
        {
            // Di udara atau diam: reset timer (anti false-trigger saat mendarat).
            stepTimer = minStepInterval;
            return;
        }

        // Pilih clip set by speed bracket.
        AudioClip[] set;
        string setName;
        if (hSpeed >= sprintSpeedThreshold) { set = sprintClips; setName = "sprint"; }
        else if (hSpeed >= runSpeedThreshold) { set = runClips; setName = "run"; }
        else { set = walkClips; setName = "walk"; }

        // === STRIDE TIMER (primary): bunyi tiap stepLength meter berjalan ===
        // Ini natural karena langkah manusia konstan per meter, bukan per detik.
        float stepLength = (hSpeed >= sprintSpeedThreshold) ? sprintStepLength
                          : (hSpeed >= runSpeedThreshold) ? runStepLength : walkStepLength;
        float interval = Mathf.Clamp(stepLength / hSpeed, minStepInterval, maxStepInterval);
        stepTimer -= Time.deltaTime;
        bool timerReady = (stepTimer <= 0f);

        // === RAYCAST GATE (optional): bunyi hanya saat kaki benar-benar di tanah ===
        // TIDAK reset timer — hanya memblokir trigger saat kaki di udara (anti ghost step).
        bool raycastOk = gateStepOnRaycast ? CheckFootDown() : true;

        if (timerReady && raycastOk)
        {
            stepTimer = interval;
            PlayRandom(set, BaseVolume());
            Footfall?.Invoke((int)surface, hSpeed);
            if (debugLogSteps) Debug.Log($"[Footstep] STEP {setName} surface={SurfaceNames[(int)surface]} hSpeed={hSpeed:F1} interval={interval:F2}s");
        }
    }

    /// <summary>Raycast dari bawah CharacterController ke tanah. True bila kaki menyentuh ground.</summary>
    private bool CheckFootDown()
    {
        if (controller == null) return false;
        // Origin: titik di antara 2 kaki (bawah controller, offset sedikit ke dalam radius).
        Vector3 origin = transform.position + Vector3.down * (controller.height * 0.5f - controller.radius * 0.5f);
        float dist = groundCheckDist + controller.skinWidth;
        RaycastHit hit;
        if (Physics.Raycast(origin, Vector3.down, out hit, dist, ~0, QueryTriggerInteraction.Ignore))
        {
            // Hanya trigger jika benar-benar tanah (bukan langit/plafon).
            return hit.distance <= groundCheckDist;
        }
        return false;
    }

    private float BaseVolume()
    {
        // Local player = punya State Authority atas object-nya sendiri (Shared Mode).
        // Guard: NetworkObject bisa belum Spawned (mis. di Editor test tanpa Fusion runner)
        // → anggap local player (pakai localVolume) supaya tidak throw.
        NetworkObject nob = GetComponent<NetworkObject>();
        bool isLocal = true;
        if (nob != null)
        {
            try { isLocal = nob.HasStateAuthority; }
            catch (System.Exception) { isLocal = true; }
        }
        return isLocal ? localVolume : remoteVolume;
    }

    private void PlayRandom(AudioClip[] clips, float volume)
    {
        if (clips == null || clips.Length == 0 || audioSource == null) return;
        AudioClip clip = clips[rng.Next(clips.Length)];
        if (clip == null) return;

        audioSource.pitch = (float)(pitchMin + rng.NextDouble() * (pitchMax - pitchMin));
        audioSource.PlayOneShot(clip, Mathf.Clamp01(volume));
        audioSource.pitch = 1f; // reset agar tidak memengaruhi sumber lain
    }
}
