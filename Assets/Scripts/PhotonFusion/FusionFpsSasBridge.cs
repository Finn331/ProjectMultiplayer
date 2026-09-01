using Fusion;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The SINGLE Fusion Shared-Mode movement/look driver for the KINEMATION/SAS first-person player.
///
/// Extends FusionPlayerMovement so every existing system that does GetComponent&lt;FusionPlayerMovement&gt;()
/// (PlayerFootstepAudio, PlayerSurfaceEffects, FusionPlayerDeath, FusionPlayerDownedState, spawner)
/// keeps working, while the ACTUAL CharacterController movement + camera look are delegated to the SAS
/// stack (FPSMovement + FPSController + FPSCameraController) — i.e. control behaves EXACTLY like FPSGenericPlayer.
///
/// Mobile input (FloatingJoystick + LookArea) is bridged because the game is touch-based.
/// This class does NOT call base.Spawned/FixedUpdateNetwork (avoids duplicate CharacterController/look
/// processing from the private base.Update). It owns everything itself.
/// </summary>
public class FusionFpsSasBridge : FusionPlayerMovement
{
    [Header("SAS")]
    [SerializeField] private Demo.Scripts.Runtime.Character.FPSMovement sasMovement;
    [SerializeField] private Demo.Scripts.Runtime.Character.FPSController sasController;
    [SerializeField] private Demo.Scripts.Runtime.Character.FPSMovementSettings sasMovementSettings;
    [SerializeField] private FloatingJoystick mobileJoystick;
    [SerializeField] private LookArea mobileLookArea;
    [SerializeField] private float sasLookSensitivity = 0.2f;

    private float nextJumpSearchTime;
    private bool sasJumpButtonBound;
    private System.Reflection.FieldInfo lookDeltaField;
    // Yaw accumulated from the mobile LookArea drag, applied to the ROOT rotation during the Fusion
    // network tick (FixedUpdateNetwork). Applying yaw in Update() (render frame) is overwritten by
    // Fusion's NetworkTransform, which in Shared mode always syncs position + rotation and writes the
    // rotation captured at the NETWORK TICK back to the transform — so a render-frame yaw always
    // snaps to 0. Pitch is unaffected because it lives on the camera (a child) which NetworkTransform
    // does not touch. Applying yaw in the tick (exactly like the SAS CharacterController.Move that
    // already works for position) lets NetworkTransform capture the value and keep it.
    private float pendingYaw;

    public override void Spawned()
    {
        if (controller == null) controller = GetComponent<CharacterController>();
        if (sasMovement == null) sasMovement = GetComponent<Demo.Scripts.Runtime.Character.FPSMovement>();
        if (sasController == null) sasController = GetComponent<Demo.Scripts.Runtime.Character.FPSController>();

        if (sasMovement != null)
        {
            // SAS is ticked externally (from FixedUpdateNetwork) rather than by its own Update.
            sasMovement.SetExternalTick(true);
            // CRITICAL: bind the MovementSettings asset so SAS gait velocities (walk=3, sprint=6.5)
            // are non-zero. Without it _desiredGait.velocity stays 0 -> player barely moves / "slow motion".
            // The bridge serialized field is the source of truth; if it was not bound, fall back to the
            // settings already serialized on the FPSMovement component (read via reflection - the field is
            // a private [SerializeField]).
            if (sasMovementSettings == null)
            {
                var msField = sasMovement.GetType().GetField("movementSettings",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (msField != null) sasMovementSettings = msField.GetValue(sasMovement) as Demo.Scripts.Runtime.Character.FPSMovementSettings;
            }
            if (sasMovementSettings != null)
            {
                sasMovement.SetMovementSettings(sasMovementSettings);
            }
        }

        if (sasController != null)
        {
            lookDeltaField = sasController.GetType().GetField("_lookDeltaInput",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        }

        // 2P FIX: initialize the SAS playable graph on EVERY copy (local AND remote), not only the
        // authority copy. Previously the animator.Initialize() call lived in FixedUpdateNetwork behind
        // the HasFusionInputAuthority gate, so remote copies never built their playable graph and
        // FPSPlayablesController.Update() threw ArgumentNullException every frame on _masterMixer (x2
        // logged per 2P session). Initialize() is idempotent (guarded by _isInitialized); guard with
        // try/catch because the playable graph build needs the Animator already enabled on this copy.
        var fa = GetComponent<KINEMATION.FPSAnimationFramework.Runtime.Core.FPSAnimator>();
        if (fa != null)
        {
            try { fa.Initialize(); }
            catch (System.Exception e) { Debug.LogWarning("[SasBridge] remote FPSAnimator init deferred: " + e.Message); }
        }
        if (sasMovement != null)
        {
            try { sasMovement.EnsureInitialized(); }
            catch (System.Exception e) { Debug.LogWarning("[SasBridge] remote SAS init deferred: " + e.Message); }
        }

        if (HasFusionInputAuthority())
        {
            RefreshSceneBindings();
        }
    }
    public new void RefreshSceneBindings()
    {
        if (controller == null) controller = GetComponent<CharacterController>();
        if (mobileJoystick == null) mobileJoystick = FindObjectOfType<FloatingJoystick>(true);
        if (mobileLookArea == null) mobileLookArea = FindObjectOfType<LookArea>(true);
        TryBindJumpButton();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasFusionInputAuthority() || sasMovement == null)
        {
            return;
        }

        if (mobileJoystick == null || mobileLookArea == null)
        {
            RefreshSceneBindings();
        }

        TryBindJumpButton();

        // Respect death/downed lock set by FusionPlayerDeath/FusionPlayerDownedState via ControlsBlocked.
        if (ControlsBlocked)
        {
            sasMovement.SetInputDirection(Vector2.zero);
            sasMovement.RequestSprint(false);
            return;
        }

        // --- Movement → SAS ---
        Vector2 input = mobileJoystick != null ? new Vector2(mobileJoystick.Horizontal, mobileJoystick.Vertical) : Vector2.zero;
        float inputMagnitude = Mathf.Clamp01(input.magnitude);
        // The joystick computes input = (pointer - center) / (radius * canvas.scaleFactor). So a FULL pull
        // (pointer at the edge) lands at magnitude ~1/scaleFactor, which is < 1 whenever scaleFactor > 1
        // (~0.81 at scaleFactor 1.236). That means the sprint gate below (>= 0.85) is NEVER reached and the
        // player is stuck in the walking gait (~3 m/s) — feels very slow. Normalize by the canvas scaleFactor
        // so a full pull = 1.0 and sprint is reachable on ANY device, regardless of resolution/design scale.
        if (mobileJoystick != null && inputMagnitude > 0.0001f)
        {
            Canvas joyCanvas = mobileJoystick.GetComponentInParent<Canvas>();
            if (joyCanvas != null && joyCanvas.scaleFactor > 0f)
                inputMagnitude = Mathf.Clamp01(inputMagnitude * joyCanvas.scaleFactor);
        }
        if (inputMagnitude > 0.0001f)
        {
            sasMovement.SetInputDirection(input);
            sasMovement.RequestSprint(inputMagnitude >= 0.85f && input.y > 0.05f);
        }
        else
        {
            sasMovement.SetInputDirection(Vector2.zero);
            sasMovement.RequestSprint(false);
        }

        // --- Authoritative SAS tick: SAS FPSMovement already applies gravity + planar CharacterController.Move
        // internally (UpdateGrounded/UpdateInAir/UpdateMovement). We do NOT add our own vertical move here
        // to avoid double gravity. Motion + gait state are fully "persis FPSGenericPlayer".
        //
        // Fusin's first FixedUpdateNetwork can run BEFORE FPSController.Start -> FPSAnimator.Initialize, so the
        // UserInputController property map and the playable graph are not built yet (NRE / ArgumentNullException).
        // Initialize() is idempotent (guarded by _isInitialized) so calling it here is safe.
        var animator = GetComponent<KINEMATION.FPSAnimationFramework.Runtime.Core.FPSAnimator>();
        if (animator != null) animator.Initialize();
        sasMovement.EnsureInitialized();
        // SLOW-MOTION FIX: SAS movement math now integrates with the FUSION tick delta (Runner.DeltaTime,
        // verified TickRate=32 => 0.03125s) instead of render-frame Time.deltaTime (~0.010s at editor fps),
        // which scaled every velocity integration and gravity to ~0.32x => walk 0.95 m/s instead of 3.0 and
        // floaty/jump "slow motion". Set before each tick; TickLateUpdate uses the same value this frame.
        sasMovement.TickDeltaTime = Runner.DeltaTime;
        sasMovement.TickMovement();

        // FPSGenericPlayer runs TickLateUpdate() from LateUpdate() every frame. With _externalTick=true the
        // LateUpdate() is disabled, so without this the player never leaves the InAir state after a jump
        // (state gets stuck InAir forever, and RequestJump is then rejected by its InAir guard). TickLateUpdate
        // resets InAir -> Idle once the player touches the ground again. Match the SAS demo exactly.
        sasMovement.TickLateUpdate();

        // Apply the yaw accumulated from the mobile LookArea drag to the ROOT rotation HERE, in the
        // network tick, so Fusion's NetworkTransform captures it (position already works the same way
        // via sasMovement.TickMovement -> controller.Move). A render-frame yaw is overwritten by
        // NetworkTransform's Shared-mode rotation sync and snaps back to 0.
        if (pendingYaw != 0f)
        {
            transform.rotation *= Quaternion.Euler(0f, pendingYaw, 0f);
            pendingYaw = 0f;
        }
    }

    private void Update()
    {
        if (Object == null || !Object.HasStateAuthority)
        {
            return;
        }

        if (mobileLookArea == null)
        {
            mobileLookArea = FindObjectOfType<LookArea>(true);
        }

        if (mobileLookArea != null && sasController != null)
        {
            Vector2 delta = mobileLookArea.LookDelta;
            mobileLookArea.ResetDelta();
            if (delta.sqrMagnitude < 0.0001f)
            {
                // No drag this frame: clear so SAS doesn't keep applying stale look.
                if (lookDeltaField != null) lookDeltaField.SetValue(sasController, Vector2.zero);
                return;
            }

            // Feed this frame's drag delta into SAS. SAS FPSController applies BOTH yaw (on root,
            // transform.rotation) and pitch (via camera/_playerInput.y) internally, exactly like FPSGenericPlayer.
            // We SET (not accumulate) because PlayerInput normally overwrites _lookDeltaInput every frame.
            Vector2 scaled = delta * sasLookSensitivity;
            if (lookDeltaField != null) lookDeltaField.SetValue(sasController, scaled);

            // Accumulate the horizontal component for the ROOT yaw. It is applied in FixedUpdateNetwork
            // (the network tick) so Fusion's NetworkTransform captures it — a render-frame root yaw is
            // overwritten by Shared-mode rotation sync and snaps back to 0. settings.sensitivity and the
            // SensitivityMultiplier scale are both 1 (verified), so scaled.x already equals the per-frame
            // yaw delta FPSController would apply.
            pendingYaw += scaled.x;
        }
    }

    /// <summary>Bind the mobile jump button (bound to this bridge's Jump).</summary>
    private void TryBindJumpButton()
    {
        if (!enableJump) return;
        if (Time.unscaledTime < nextJumpSearchTime) return;
        nextJumpSearchTime = Time.unscaledTime + 0.5f;

        Button[] buttons = FindObjectsOfType<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null && buttons[i].gameObject.name.ToLowerInvariant().Contains("jump"))
            {
                if (!sasJumpButtonBound)
                {
                    buttons[i].onClick.AddListener(Jump);
                    sasJumpButtonBound = true;
                }
                return;
            }
        }
    }

    private void OnDisable()
    {
        UnbindJumpButtonIfBound();
    }

    private void OnDestroy()
    {
        UnbindJumpButtonIfBound();
    }

    /// <summary>Jump button handler. Hides base Jump so we drive SAS RequestJump (SAS handles vertical).</summary>
    public new void Jump()
    {
        if (!HasFusionInputAuthority() || !enableJump || controller == null || !controller.isGrounded)
        {
            return;
        }
        if (sasMovement != null) sasMovement.RequestJump();
    }

    private void UnbindJumpButtonIfBound()
    {
        if (!sasJumpButtonBound) return;
        Button[] buttons = FindObjectsOfType<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null && buttons[i].gameObject.name.ToLowerInvariant().Contains("jump"))
            {
                buttons[i].onClick.RemoveListener(Jump);
            }
        }
        sasJumpButtonBound = false;
    }

    private bool HasFusionInputAuthority()
    {
        return Object != null && Object.HasStateAuthority;
    }
}
