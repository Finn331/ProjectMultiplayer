using UnityEngine;

public class PlayerAnimatorDriver : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private FPSControllerMobile movementController;

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

    private float lastRawGroundedTime;
    private float jumpLockUntilTime;
    private float smoothedVerticalVelocity;
    private float verticalVelocityDamp;
    private bool wasGrounded;

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

        lastRawGroundedTime = Time.time;
        jumpLockUntilTime = 0f;
        smoothedVerticalVelocity = 0f;
        verticalVelocityDamp = 0f;
        wasGrounded = characterController != null && characterController.isGrounded;
    }

    private void LateUpdate()
    {
        if (animator == null || characterController == null)
        {
            return;
        }

        Vector3 velocity = characterController.velocity;
        float inputMagnitude = this.GetInputMagnitude();
        float speedNormalized = inputMagnitude;
        Vector2 moveInput = this.GetDirectionInput(velocity);
        float verticalVelocity = velocity.y;
        bool rawGrounded = characterController.isGrounded;

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
            float horizontalSpeed = new Vector3(velocity.x, 0f, velocity.z).magnitude;
            speedNormalized = Mathf.Clamp01(horizontalSpeed / movementController.moveSpeed);
        }

        speedNormalized = speedNormalized <= moveInputDeadZone ? 0f : speedNormalized;

        bool bufferedGrounded = rawGrounded ||
            (Time.time - lastRawGroundedTime <= groundedBufferTime && verticalVelocity <= 0.1f);
        bool isGrounded = bufferedGrounded && Time.time >= jumpLockUntilTime;
        bool isRunning = speedNormalized > 0.15f && inputMagnitude >= runInputThreshold;
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
}
