using Fusion;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Footstep SFX untuk player. Non-networked: tiap client memutar audio
/// secara lokal berdasarkan state movement yang sudah tersinkron (CharacterController velocity).
///
/// - Walk / Run / Sprint pakai clip set berbeda (folder per surface).
/// - Jump up / jump down dipicu dari transisi grounded.
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

    [Header("Timing (detik antar langkah)")]
    [SerializeField] private float walkStepInterval = 0.52f;
    [SerializeField] private float runStepInterval = 0.36f;
    [SerializeField] private float sprintStepInterval = 0.28f;

    [Header("Volume")]
    [Range(0f, 1f)] public float localVolume = 0.35f;   // volume untuk pemain lokal sendiri
    [Range(0f, 1f)] public float remoteVolume = 0.9f;   // volume langkah pemain lain
    [SerializeField] private float jumpVolumeScale = 1.15f;

    [Header("Pitch Variation")]
    [SerializeField] private float pitchMin = 0.92f;
    [SerializeField] private float pitchMax = 1.08f;

    [Header("Detection")]
    [SerializeField] private float minSpeedToStep = 0.6f;   // kecepatan horizontal minimum dianggap berjalan
    [SerializeField] private float runSpeedThreshold = 3.4f; // >= ini = run
    [SerializeField] private float sprintSpeedThreshold = 5.5f; // >= ini = sprint

    [Header("AudioSource")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private float spatialMinDistance = 2.5f;
    [SerializeField] private float spatialMaxDistance = 22f;

    private const string ResourceRoot = "Audio/Footsteps/";
    private static readonly string[] SurfaceNames = { "wood", "snow", "ice" };

    private FusionPlayerMovement movement;
    private CharacterController controller;
    private bool wasGrounded = true;
    private float stepTimer;
    private readonly System.Random rng = new System.Random();

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
        if (movement.ControlsBlocked) { stepTimer = 0f; return; }
        // Death handling: biarkan FusionPlayerDeath mengurus mute jika perlu.

        Vector3 v = controller.velocity;
        float hSpeed = new Vector3(v.x, 0f, v.z).magnitude;
        bool grounded = controller.isGrounded;

        // Landing / takeoff
        if (wasGrounded && !grounded)
        {
            PlayRandom(jumpUpClips, BaseVolume());
        }
        else if (!wasGrounded && grounded && v.y < -0.1f)
        {
            PlayRandom(jumpDownClips, BaseVolume() * jumpVolumeScale);
            stepTimer = 0f; // langkah pertama setelah mendarat langsung
        }
        wasGrounded = grounded;

        if (!grounded || hSpeed < minSpeedToStep)
        {
            stepTimer = Mathf.Min(stepTimer, 0.05f);
            return;
        }

        // Pilih interval & set clip sesuai kecepatan
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

        // Interval juga disesuaikan kecepatan aktual (biar sync dengan animasi)
        interval *= Mathf.Clamp(moveSpeedRef() > 0.01f ? moveSpeedRef() / Mathf.Max(0.01f, hSpeed) : 1f, 0.75f, 1.25f);

        stepTimer += Time.deltaTime;
        if (stepTimer >= interval)
        {
            stepTimer = 0f;
            PlayRandom(set, BaseVolume());
        }
    }

    private float moveSpeedRef()
    {
        return movement != null ? movement.MoveSpeed : 5f;
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
