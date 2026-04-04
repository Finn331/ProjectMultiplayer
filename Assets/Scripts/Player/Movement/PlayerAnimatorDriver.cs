using UnityEngine;
using Unity.Netcode;

public class PlayerAnimatorDriver : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private FPSControllerMobile movementController;
    [SerializeField] private LowHealthInjuredAnimationController injuredAnimationController;

    [Header("Parameter Names")]
    [SerializeField] private string speedParam = "Speed";
    [SerializeField] private string verticalVelocityParam = "VerticalVelocity";
    [SerializeField] private string isGroundedParam = "IsGrounded";
    [SerializeField] private string isRunningParam = "IsRunning";
    [SerializeField] private string moveXParam = "MoveX";
    [SerializeField] private string moveYParam = "MoveY";

    [Header("Tuning")]
    [SerializeField] private float runInputThreshold = 0.75f;
    [SerializeField] private float speedSmoothTime = 0.1f;
    [SerializeField] private float moveInputDeadZone = 0.08f;
    [SerializeField] private float directionSmoothTime = 0.08f;
    [SerializeField] private float groundedBufferTime = 0.08f;
    [SerializeField] private float minAirTimeAfterJump = 0.12f;
    [SerializeField] private float jumpStartVerticalThreshold = 0.2f;
    [SerializeField] private float verticalVelocitySmoothTime = 0.05f;
    [SerializeField] private bool useGroundProbeFallback = true;
    [SerializeField] private float groundProbeDistance = 0.25f;
    [SerializeField] private LayerMask groundProbeMask = ~0;
    [SerializeField, Range(0.1f, 1f)] private float injuredMaxSpeedNormalized = 0.2f;
    [SerializeField] private bool disableRunningWhenInjured = true;
    [SerializeField, Range(0.01f, 0.5f)] private float injuredInputThresholdForFullAnimSpeed = 0.08f;

    private float lastRawGroundedTime;
    private float jumpLockUntilTime;
    private float smoothedVerticalVelocity;
    private float verticalVelocityDamp;
    private bool wasGrounded;
    private Vector3 lastFramePosition;
    private bool hasLastFramePosition;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }

        if (movementController == null)
        {
            movementController = GetComponent<FPSControllerMobile>();
        }

        if (injuredAnimationController == null)
        {
            injuredAnimationController = GetComponent<LowHealthInjuredAnimationController>();
        }

        lastRawGroundedTime = Time.time;
        jumpLockUntilTime = 0f;
        smoothedVerticalVelocity = 0f;
        verticalVelocityDamp = 0f;
        wasGrounded = characterController != null && characterController.isGrounded;
        lastFramePosition = transform.position;
        hasLastFramePosition = true;
    }

    private void LateUpdate()
    {
        if (animator == null || characterController == null)
        {
            return;
        }

        if (!this.ShouldProcessAnimator())
        {
            return;
        }

        Vector3 velocity = characterController.velocity;
        Vector3 syntheticVelocity = Vector3.zero;
        if (hasLastFramePosition && Time.deltaTime > 0.0001f)
        {
            syntheticVelocity = (transform.position - lastFramePosition) / Time.deltaTime;
        }

        Vector3 referenceVelocity = velocity.sqrMagnitude > 0.0001f ? velocity : syntheticVelocity;
        float inputMagnitude = this.GetInputMagnitude();
        float speedNormalized = inputMagnitude;
        Vector2 moveInput = this.GetDirectionInput(referenceVelocity);
        float verticalVelocity = referenceVelocity.y;
        bool rawGrounded = characterController.isGrounded;
        bool jumpPressedRecently = movementController != null && movementController.WasJumpPressedRecently(minAirTimeAfterJump + 0.08f);
        if (!rawGrounded && useGroundProbeFallback)
        {
            rawGrounded = this.IsGroundedByProbe();
            if (rawGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = 0f;
            }
        }

        if (jumpPressedRecently)
        {
            rawGrounded = false;
            verticalVelocity = Mathf.Max(verticalVelocity, jumpStartVerticalThreshold + 0.05f);
            jumpLockUntilTime = Mathf.Max(jumpLockUntilTime, Time.time + minAirTimeAfterJump);
        }

        if (rawGrounded)
        {
            lastRawGroundedTime = Time.time;
        }

        if (!rawGrounded && wasGrounded && verticalVelocity > jumpStartVerticalThreshold)
        {
            jumpLockUntilTime = Time.time + minAirTimeAfterJump;
        }

        // Fallback if joystick input is unavailable, e.g. during non-mobile testing.
        if (speedNormalized <= moveInputDeadZone && movementController != null && movementController.moveSpeed > 0.01f)
        {
            float horizontalSpeed = new Vector3(referenceVelocity.x, 0f, referenceVelocity.z).magnitude;
            speedNormalized = Mathf.Clamp01(horizontalSpeed / movementController.moveSpeed);
        }

        speedNormalized = speedNormalized <= moveInputDeadZone ? 0f : speedNormalized;
        bool injuredActive = injuredAnimationController != null && injuredAnimationController.IsInjuredActive;
        float injuredSpeedCap = injuredMaxSpeedNormalized;

        if (injuredAnimationController != null)
        {
            injuredSpeedCap = Mathf.Clamp01(injuredAnimationController.InjuredMovementSpeedMultiplier);
        }

        if (injuredActive)
        {
            bool hasInjuredMoveInput =
                inputMagnitude >= injuredInputThresholdForFullAnimSpeed ||
                speedNormalized > moveInputDeadZone;
            speedNormalized = hasInjuredMoveInput ? injuredSpeedCap : 0f;
        }

        bool bufferedGrounded = rawGrounded ||
            (Time.time - lastRawGroundedTime <= groundedBufferTime && verticalVelocity <= 0.1f);
        bool isGrounded = bufferedGrounded && Time.time >= jumpLockUntilTime;
        bool isRunning =
            (!disableRunningWhenInjured || !injuredActive) &&
            speedNormalized > 0.15f &&
            inputMagnitude >= runInputThreshold;
        smoothedVerticalVelocity = Mathf.SmoothDamp(
            smoothedVerticalVelocity,
            verticalVelocity,
            ref verticalVelocityDamp,
            verticalVelocitySmoothTime);

        if (isGrounded && smoothedVerticalVelocity < 0f)
        {
            smoothedVerticalVelocity = 0f;
        }

        animator.SetFloat(speedParam, speedNormalized, speedSmoothTime, Time.deltaTime);
        animator.SetFloat(verticalVelocityParam, smoothedVerticalVelocity);
        animator.SetBool(isGroundedParam, isGrounded);
        animator.SetBool(isRunningParam, isRunning);
        animator.SetFloat(moveXParam, moveInput.x, directionSmoothTime, Time.deltaTime);
        animator.SetFloat(moveYParam, moveInput.y, directionSmoothTime, Time.deltaTime);

        wasGrounded = isGrounded;
        lastFramePosition = transform.position;
        hasLastFramePosition = true;
    }

    private float GetInputMagnitude()
    {
        if (movementController == null || movementController.moveJoystick == null)
        {
            return 0f;
        }

        float horizontal = movementController.moveJoystick.Horizontal;
        float vertical = movementController.moveJoystick.Vertical;
        return Mathf.Clamp01(new Vector2(horizontal, vertical).magnitude);
    }

    private Vector2 GetInputVector()
    {
        if (movementController == null || movementController.moveJoystick == null)
        {
            return Vector2.zero;
        }

        float horizontal = movementController.moveJoystick.Horizontal;
        float vertical = movementController.moveJoystick.Vertical;
        Vector2 input = new Vector2(horizontal, vertical);

        if (input.magnitude <= moveInputDeadZone)
        {
            return Vector2.zero;
        }

        return Vector2.ClampMagnitude(input, 1f);
    }

    private Vector2 GetDirectionInput(Vector3 velocity)
    {
        Vector3 horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
        float maxSpeed = movementController != null ? Mathf.Max(0.01f, movementController.moveSpeed) : 5f;

        if (horizontalVelocity.sqrMagnitude > 0.0001f)
        {
            Vector3 localVelocity = transform.InverseTransformDirection(horizontalVelocity);
            Vector2 normalizedVelocity = new Vector2(localVelocity.x, localVelocity.z) / maxSpeed;
            Vector2 clamped = Vector2.ClampMagnitude(normalizedVelocity, 1f);

            if (clamped.magnitude > moveInputDeadZone)
            {
                return clamped;
            }
        }

        return this.GetInputVector();
    }

    private bool IsGroundedByProbe()
    {
        if (characterController == null)
        {
            return false;
        }

        Bounds bounds = characterController.bounds;
        float probeRadius = Mathf.Max(0.05f, characterController.radius * 0.9f);
        float castDistance = Mathf.Max(0.1f, groundProbeDistance);

        Vector3 footCenter = bounds.center;
        footCenter.y = bounds.min.y + probeRadius + 0.02f;

        if (Physics.CheckSphere(footCenter, probeRadius, groundProbeMask, QueryTriggerInteraction.Ignore))
        {
            return true;
        }

        return Physics.SphereCast(
            footCenter + (Vector3.up * 0.05f),
            probeRadius,
            Vector3.down,
            out _,
            castDistance,
            groundProbeMask,
            QueryTriggerInteraction.Ignore);
    }

    private bool ShouldProcessAnimator()
    {
        NetworkObject networkObject = GetComponent<NetworkObject>();
        if (networkObject == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            return true;
        }

        if (!networkObject.IsSpawned)
        {
            return false;
        }

        // In multiplayer, only owner computes animator parameters from local input/physics.
        // Remote players receive synced parameters via network bridge to avoid false "falling" state.
        return networkObject.IsOwner;
    }
}
