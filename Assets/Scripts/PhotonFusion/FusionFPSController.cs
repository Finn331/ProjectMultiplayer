using Demo.Scripts.Runtime.Character;
using Fusion;
using UnityEngine;

/// <summary>
/// Fusion-aware network wrapper around KINEMATION SAS (Scriptable Animation System).
///
/// Strategi:
/// - Mewarisi FusionPlayerMovement (NetworkBehaviour) sehingga 9 file dependent
///   yang masih GetComponent&lt;FusionPlayerMovement&gt;() TIDAK perlu diedit.
/// - Di-attach dengan komponen SAS: FPSMovement (locomotion) + FPSController (state).
///   Ticked manual di FixedUpdateNetwork (HANYA pada input authority) sehingga
///   movement tersinkron dengan Fusion tick; SAS Update() di-skip via SetExternalTick.
/// - Input (joystick + look + jump) datang dari FPSControllerMobile (Carlo) —
///   diteruskan ke FPSMovement.SetInputDirection() / RequestJump() / RequestSprint().
/// - Surface slowdown (PlayerSurfaceEffects.SetSurfaceSpeedMultiplier) di-forward
///   ke FPSMovement.SpeedMultiplier supaya gait velocity ikut melambat.
/// - onJump / onLanded delegate dari SAS di-bridge ke PlayerFootstepAudio agar
///   suara jump/land terpicu dari event kinematic (bukan raycast hand-rolled).
///
/// Catatan penting:
/// - Fusion Shared Mode (HasStateAuthority = HasInputAuthority untuk owner).
/// - Asset SAS (SAS FPSMovement/FPSController) ada di
///   Assets/KINEMATION/scriptable-animation-system-main/ (namespace Demo.Scripts.Runtime.Character).
/// </summary>
[RequireComponent(typeof(FusionPlayerMovement))]
public class FusionFPSController : FusionPlayerMovement
{
    [Header("KINEMATION SAS")]
    [SerializeField] private FPSMovement sasMovement;
    [SerializeField] private FPSMovementSettings sasMovementSettings;
    [SerializeField] private FPSController sasState;
    [Tooltip("Ambang joystick magnitude (0-1) untuk dianggap sprint.")]
    [Range(0.4f, 1f)] [SerializeField] private float sprintJoystickThreshold = 0.85f;

    private FPSControllerMobile mobileInput;
    private PlayerFootstepAudio footstepAudio;
    private bool initialized;
    private bool footstepHooksBound;

    public override void Spawned()
    {
        base.Spawned();

        if (sasMovement == null)
        {
            sasMovement = GetComponent<FPSMovement>();
        }
        if (sasMovement != null)
        {
            sasMovement.SetExternalTick(true);
            sasMovement.SetConsumeInput(false);
            if (sasMovementSettings != null)
            {
                sasMovement.SetMovementSettings(sasMovementSettings);
            }
            sasMovement.EnsureInitialized();
        }
        if (sasState == null)
        {
            sasState = GetComponent<FPSController>();
        }

        mobileInput = GetComponent<FPSControllerMobile>();
        footstepAudio = GetComponent<PlayerFootstepAudio>();

        BindFootstepHooks();
        initialized = true;
    }

    public override void FixedUpdateNetwork()
    {
        // NOTE: base.FixedUpdateNetwork() calls Move() (joystick-driven),
        // Look() (camera rotation), and ApplyGravity() (CharacterController).
        // Move() di-skip via override di bawah karena SAS handles displacement.
        // Look() + ApplyGravity() tetap dipakai untuk aiming + grounded keep.
        base.FixedUpdateNetwork();

        if (!initialized || sasMovement == null)
        {
            return;
        }

        // Hanya owner yang menggerakkan. Remote players mengikuti NetworkTransform.
        if (!HasInputAuthoritySafe())
        {
            return;
        }

        if (ControlsBlocked)
        {
            sasMovement.SetInputDirection(Vector2.zero);
            sasMovement.RequestSprint(false);
            return;
        }

        if (mobileInput == null)
        {
            mobileInput = GetComponent<FPSControllerMobile>();
        }

        // === Input bridge: joystick → SAS ===
        if (mobileInput != null && mobileInput.moveJoystick != null)
        {
            Vector2 input = new Vector2(mobileInput.moveJoystick.Horizontal, mobileInput.moveJoystick.Vertical);
            float magnitude = Mathf.Clamp01(input.magnitude);
            sasMovement.SetInputDirection(Vector2.ClampMagnitude(input, 1f));
            sasMovement.RequestSprint(magnitude >= sprintJoystickThreshold && input.y > 0.05f);
        }
        else
        {
            sasMovement.SetInputDirection(Vector2.zero);
            sasMovement.RequestSprint(false);
        }

        // === Surface multiplier (dari PlayerSurfaceEffects) ===
        sasMovement.SpeedMultiplier = GetSurfaceSpeedMultiplier();

        // === Tick SAS movement di Fusion FixedUpdateNetwork (bukan Unity Update) ===
        sasMovement.TickMovement();

        // === Tick LateUpdate-equivalent (InAir transitions) ===
        sasMovement.TickLateUpdate();

        // === Sinkronisasi animator state untuk dependent scripts ===
        SyncAnimatorState();
    }

    /// <summary>
    /// Override jump dipicu FPSControllerMobile (joystick button). Forward ke SAS
    /// supaya velocity.y + state InAir-nya dikontrol SAS (gravity, air friction).
    /// </summary>
    public new void Jump()
    {
        if (!HasInputAuthoritySafe() || !enableJump || ControlsBlocked)
        {
            return;
        }

        if (sasMovement == null)
        {
            base.Jump();
            return;
        }

        if (controller != null && !controller.isGrounded)
        {
            return;
        }

        sasMovement.RequestJump();
    }

    private bool HasInputAuthoritySafe()
    {
        if (Object == null)
        {
            return true;
        }
        try
        {
            return Object.HasStateAuthority;
        }
        catch
        {
            return true;
        }
    }

    private void SyncAnimatorState()
    {
        if (sasMovement == null)
        {
            return;
        }

        float planarSpeed = sasMovement.GetSpeed();
        animatorPlanarVelocity = sasMovement.MoveVector;
        animatorPlanarVelocity.y = 0f;
        Vector2 input = sasMovement.AnimatorVelocity;
        animatorMoveInput = input;
        float maxSprint = sasMovementSettings != null ? Mathf.Max(0.0001f, sasMovementSettings.sprinting.velocity) : 6.5f;
        animatorSpeed = input.magnitude > 0.0001f
            ? Mathf.Clamp01(input.magnitude)
            : Mathf.Clamp01(planarSpeed / maxSprint);
    }

    private void BindFootstepHooks()
    {
        if (footstepHooksBound || sasMovement == null || footstepAudio == null)
        {
            return;
        }

        sasMovement.onJump -= OnSasJump;
        sasMovement.onJump += OnSasJump;
        sasMovement.onLanded -= OnSasLanded;
        sasMovement.onLanded += OnSasLanded;
        footstepHooksBound = true;
    }

    /// <summary>
    /// Override Move dari FusionPlayerMovement agar SAS (sasMovement.TickMovement)
    /// menjadi satu-satunya sumber CharacterController.Move(). Jika tidak di-skip,
    /// base.Move() dan SAS TickMovement saling interfere dan player tidak bergerak
    /// (controller.Move() dipanggil dua kali per tick dengan delta berbeda).
    /// Look() dan ApplyGravity() tetap diwarisi dari base — aiming + gravity OK.
    /// </summary>
    protected override void Move()
    {
        // Intentionally empty. SAS owns displacement via controller.Move(MoveVector * dt).
        // Tetap reset animator fields supaya PlayerAnimatorDriver tidak baca stale state.
        animatorPlanarVelocity = Vector3.zero;
        animatorMoveInput = Vector2.zero;
        animatorSpeed = 0f;
    }

    private void OnSasJump()
    {
        if (footstepAudio != null)
        {
            footstepAudio.TriggerJump();
        }
    }

    private void OnSasLanded()
    {
        if (footstepAudio != null)
        {
            footstepAudio.TriggerLand();
        }
    }
}


