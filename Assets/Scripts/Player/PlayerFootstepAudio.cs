using Fusion;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Footstep SFX untuk player. Non-networked: tiap client memutar audio
/// secara lokal berdasarkan state animasi lokomosi player.
///
/// - SYNC ANIMASI (utama): langkah dipicu dari fase normalizedTime state
///   Walk/Run di Animator (2 titik kontak kaki per siklus), sehingga suara
///   selalu pas dengan kaki yang menginjak — jalan maupun lari, looping terus.
/// - Pemilihan clip set mengikuti STATE animasi: Walk -> walkClips,
///   Run -> runClips (run juga dipakai untuk sprint karena controller ini
///   hanya punya state Walk & Run).
/// - FALLBACK: jika Animator tidak ditemukan / tidak sedang di state lokomosi
///   yang dikenali, dipakai timer berbasis kecepatan aktual (dengan stabilizer
///   grounding anti-flicker) supaya footstep tetap bunyi.
/// - Jump up/down kini ikut state animasi (masuk JumpStart / masuk Land),
///   bukan lagi heuristik velocity.
/// - Random clip + pitch variation supaya tidak monoton.
/// - 3D spatial (rolloff) sehingga pemain lain mendengar langkah dari posisi aslinya.
/// </summary>
[RequireComponent(typeof(FusionPlayerMovement))]
public class PlayerFootstepAudio : MonoBehaviour
{
    private enum SurfaceType { Wood = 0, Snow = 1, Ice = 2 }

    [Header("Surface")]
    [SerializeField] private SurfaceType surface = SurfaceType.Wood;

    [Header("Clip Sets (opsional — auto-load dari Resources jika kosong)")]
    [SerializeField] private AudioClip[] walkClips;
    [SerializeField] private AudioClip[] runClips;
    [SerializeField] private AudioClip[] sprintClips;
    [SerializeField] private AudioClip[] jumpUpClips;
    [SerializeField] private AudioClip[] jumpDownClips;

    [Header("Timing Fallback (detik antar langkah, tanpa sync animasi)")]
    [SerializeField] private float walkStepInterval = 0.4f;   // = panjang siklus HumanM@Walk01 (0.8s) / 2
    [SerializeField] private float runStepInterval = 0.3f;    // = panjang siklus HumanM@Run01 (0.6s) / 2
    [SerializeField] private float sprintStepInterval = 0.28f;

    [Header("Volume")]
    [Range(0f, 1f)] public float localVolume = 0.7f;    // volume untuk pemain lokal sendiri
    [Range(0f, 1f)] public float remoteVolume = 0.9f;   // volume langkah pemain lain
    [SerializeField] private float jumpVolumeScale = 1.15f;

    [Header("Pitch Variation")]
    [SerializeField] private float pitchMin = 0.92f;
    [SerializeField] private float pitchMax = 1.08f;

    [Header("Anim Sync")]
    [SerializeField] private Animator animator; // otomatis GetComponentInChildren jika kosong
    [SerializeField] private string walkStateName = "Walk";
    [SerializeField] private string runStateName = "Run";
    [SerializeField] private string jumpStartStateName = "JumpStart";
    [SerializeField] private string landStateName = "Land";
    [Tooltip("Kecepatan horizontal minimum agar langkah tersync tetapi dibunyikan.")]
    [SerializeField] private float syncedMinSpeed = 0.3f;

    [Header("Detection (fallback timer)")]
    [SerializeField] private float minSpeedToStep = 0.6f;   // kecepatan horizontal minimum dianggap berjalan
    [SerializeField] private float runSpeedThreshold = 3.4f; // >= ini = run
    [SerializeField] private float sprintSpeedThreshold = 5.5f; // >= ini = sprint

    [Header("Grounding Stabilizer (fallback timer, anti flicker di terrain)")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundCheckExtra = 0.3f;
    [SerializeField] private float freeFallVelocity = -6f;

    [Header("AudioSource")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private float spatialMinDistance = 2.5f;
    [SerializeField] private float spatialMaxDistance = 22f;

    [Header("Debug")]
    [SerializeField] private bool debugLogSteps = false;

    private const string ResourceRoot = "Audio/Footsteps/";
    private static readonly string[] SurfaceNames = { "wood", "snow", "ice" };

    private FusionPlayerMovement movement;
    private CharacterController controller;
    private int walkStateHash;
    private int runStateHash;
    private int jumpStartStateHash;
    private int landStateHash;

    // Anim-sync tracking
    private int prevAnimStateHash;
    private bool hasPrevAnimState;
    private int prevFootfallIndex;   // indeks langkah = floor(normalizedTime * 2)
    private bool hasPrevFootfall;

    // Fallback timer tracking
    private bool wasStableGrounded = true;
    private float airTime;
    private float stepTimer;
    private Vector3 lastPosition;
    private bool hasLastPosition;
    private readonly System.Random rng = new System.Random();

    /// <summary>
    /// Dipicu tiap langkah (jalur sync animasi maupun timer fallback).
    /// Argumen: surface ID saat ini (0=wood,1=snow,2=ice) dan kecepatan horizontal (m/s).
    /// Dipakai PlayerSurfaceEffects untuk jejak kaki salju.
    /// </summary>
    public event System.Action<int, float> Footfall;

    private void Awake()
    {
        movement = GetComponent<FusionPlayerMovement>();
        controller = GetComponent<CharacterController>();

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
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

        walkStateHash = Animator.StringToHash(walkStateName);
        runStateHash = Animator.StringToHash(runStateName);
        jumpStartStateHash = Animator.StringToHash(jumpStartStateName);
        landStateHash = Animator.StringToHash(landStateName);

        LoadClipsFromResources();
    }

    /// <summary>
    /// Auto-load clip sets dari Resources sesuai surface yang dipilih,
    /// hanya untuk set yang belum diisi lewat Inspector.
    /// </summary>
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
        // Nama file tidak seragam ("fst_ice_light_jumpdown_001", "fst_light_ice_run_001"),
        // jadi muat semua sub-asset dalam folder via Resources.LoadAll.
        string path = ResourceRoot + SurfaceNames[(int)surface] + "/" + action;
        AudioClip[] clips = Resources.LoadAll<AudioClip>(path);
        return clips ?? new AudioClip[0];
    }

    /// <summary>Ganti permukaan saat runtime (misalnya nanti ada sistem material terrain).</summary>
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

        // Jalur utama: sinkron dengan fase animasi kaki. EKSKLUSIF — jika Animator
        // dengan controller ada, JANGAN campur jalur timer (hindari langkah ganda saat
        // state bertransisi; Idle/udara/memang bukan momen langkah).
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            TryUpdateFromAnimation();
            return;
        }

        // Fallback: hanya dipakai bila sama sekali tidak ada Animator/controller.
        UpdateLegacyTimer();
    }

    private void ResetTrackers()
    {
        stepTimer = 0f;
        airTime = 0f;
        wasStableGrounded = true;
        hasPrevFootfall = false;
        prevFootfallIndex = 0;
        hasPrevAnimState = false;
    }

    // ------------------------------------------------------------------
    // SYNC ANIMASI
    // ------------------------------------------------------------------

    private void TryUpdateFromAnimation()
    {
        AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
        int hash = st.shortNameHash;
        float normalizedTime = st.normalizedTime;

        bool isWalk = hash == walkStateHash;
        bool isRun = hash == runStateHash;

        if (!isWalk && !isRun && hash != jumpStartStateHash && hash != landStateHash)
        {
            // State lain (Idle, attack, dsb.) — bukan momen langkah berjalan.
            hasPrevAnimState = false;
            hasPrevFootfall = false;
            return;
        }

        // Deteksi transisi antar state untuk event sekali-jalan.
        if (hasPrevAnimState && hash != prevAnimStateHash)
        {
            if (hash == jumpStartStateHash)
            {
                PlayRandom(jumpUpClips, BaseVolume());
                if (debugLogSteps) Debug.Log("[Footstep] jump up (state)");
            }
            else if (hash == landStateHash)
            {
                PlayRandom(jumpDownClips, BaseVolume() * jumpVolumeScale);
                if (debugLogSteps) Debug.Log("[Footstep] landed (state)");
            }
        }
        hasPrevAnimState = true;
        prevAnimStateHash = hash;

        if (!isWalk && !isRun)
        {
            // Di udara / mendarat: tidak ada langkah berjalan.
            hasPrevFootfall = false;
            return;
        }

        // Kecepatan aktual (position delta) untuk filter slide.
        float hSpeed = MeasureHSpeed();

        // Indeks langkah: 2 langkah per siklus animasi. floor(normalizedTime*2)
        // naik monoton selama state berjalan — kebal frame rate rendah/hitch
        // (beda dengan cek penyeberangan titik fase yang bisa melompati kontak).
        int footfallIndex = Mathf.FloorToInt(normalizedTime * 2f);

        if (hasPrevFootfall && hSpeed >= syncedMinSpeed && footfallIndex != prevFootfallIndex)
        {
            PlayRandom(isRun ? runClips : walkClips, BaseVolume());
            Footfall?.Invoke((int)surface, hSpeed);
            if (debugLogSteps) Debug.Log("[Footstep] step state=" + (isRun ? runStateName : walkStateName)
                + " nT=" + normalizedTime.ToString("F2") + " hSpeed=" + hSpeed.ToString("F1"));
        }
        prevFootfallIndex = footfallIndex;
        hasPrevFootfall = true;
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
    // FALLBACK TIMER (dipakai jika Animator tidak ada / state tak dikenali)
    // ------------------------------------------------------------------

    private void UpdateLegacyTimer()
    {
        float hSpeed = MeasureHSpeed();

        // Grounding stabil: isGrounded sering flicker false beberapa frame di lereng/
        // bump terrain (Move() hanya dipanggil per Fusion tick). Anggap grounded selama
        // ada tanah dekat di bawah kaki dan kita tidak sedang naik tinggi / jatuh bebas.
        bool rawGrounded = controller.isGrounded;
        bool stableGrounded = rawGrounded
            || (controller.velocity.y < 1f && controller.velocity.y > freeFallVelocity && HasGroundBelow());

        float airBefore = airTime;
        if (stableGrounded)
        {
            airTime = 0f;
        }
        else
        {
            airTime += Time.deltaTime;
        }

        // Takeoff / landing versi timer (fallback saja; jalur utama pakai state animasi).
        if (wasStableGrounded && !stableGrounded && airTime >= 0.12f && controller.velocity.y > 0.5f)
        {
            PlayRandom(jumpUpClips, BaseVolume());
        }
        else if (!wasStableGrounded && stableGrounded && airBefore >= 0.12f && controller.velocity.y < -0.1f)
        {
            PlayRandom(jumpDownClips, BaseVolume() * jumpVolumeScale);
            stepTimer = 0f;
        }
        wasStableGrounded = stableGrounded;

        if (!stableGrounded || hSpeed < minSpeedToStep)
        {
            if (!stableGrounded)
            {
                stepTimer = Mathf.Min(stepTimer, 0.05f);
            }
            return;
        }

        AudioClip[] set;
        float interval;
        if (hSpeed >= sprintSpeedThreshold)
        {
            set = sprintClips; interval = sprintStepInterval;
        }
        else if (hSpeed >= runSpeedThreshold)
        {
            set = runClips; interval = runStepInterval;
        }
        else
        {
            set = walkClips; interval = walkStepInterval;
        }

        stepTimer += Time.deltaTime;
        if (stepTimer >= interval)
        {
            stepTimer = 0f;
            PlayRandom(set, BaseVolume());
            Footfall?.Invoke((int)surface, hSpeed);
            if (debugLogSteps) Debug.Log("[Footstep] step (timer) surface=" + SurfaceNames[(int)surface]
                + " hSpeed=" + hSpeed.ToString("F1"));
        }
    }

    /// <summary>
    /// Cek ada tanah dekat di bawah telapak kaki (fallback saat isGrounded flicker).
    /// Raycast ke bawah dari sekitar pinggang, abaikan collider milik player sendiri.
    /// </summary>
    private bool HasGroundBelow()
    {
        if (controller == null) return false;

        float probeTop = Mathf.Max(controller.height * 0.9f, 0.8f);
        Vector3 origin = transform.position + Vector3.up * probeTop;
        float maxDistance = probeTop + groundCheckExtra;

        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, maxDistance, groundMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null) continue;
            if (hitCollider.transform == transform || hitCollider.transform.IsChildOf(transform)) continue;
            // Tanah harus di bawah telapak (dekat ujung ray), bukan dinding di sebelah kaki.
            if (hits[i].distance >= probeTop - 0.15f)
            {
                return true;
            }
        }
        return false;
    }

    private float BaseVolume()
    {
        // Local player = punya State Authority atas object-nya sendiri (Shared Mode)
        NetworkObject nob = GetComponent<NetworkObject>();
        bool isLocal = nob != null && nob.HasStateAuthority;
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
