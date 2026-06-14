using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class FusionAnimatorSync : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private CharacterController controller;
    [SerializeField] private FusionPlayerMovement movementController;

    [Header("Parameter Names")]
    [SerializeField] private string speedParam = "Speed";
    [SerializeField] private string verticalVelocityParam = "VerticalVelocity";
    [SerializeField] private string isGroundedParam = "IsGrounded";
    [SerializeField] private string isRunningParam = "IsRunning";
    [SerializeField] private string moveXParam = "MoveX";
    [SerializeField] private string moveYParam = "MoveY";

    [Header("Sync Tuning")]
    [SerializeField] private float floatSyncThreshold = 0.01f;
    [SerializeField] private float fallbackMaxMoveSpeed = 5f;
    [SerializeField, Range(0.1f, 1f)] private float runInputThreshold = 0.75f;

    [Networked] private float Speed { get; set; }
    [Networked] private float VerticalVelocity { get; set; }
    [Networked] private NetworkBool IsGrounded { get; set; }
    [Networked] private NetworkBool IsRunning { get; set; }
    [Networked] private float MoveX { get; set; }
    [Networked] private float MoveY { get; set; }

    private int speedHash;
    private int verticalVelocityHash;
    private int isGroundedHash;
    private int isRunningHash;
    private int moveXHash;
    private int moveYHash;
    private bool hasSpeedParam;
    private bool hasVerticalVelocityParam;
    private bool hasIsGroundedParam;
    private bool hasIsRunningParam;
    private bool hasMoveXParam;
    private bool hasMoveYParam;
    private float lastSentSpeed;
    private float lastSentVerticalVelocity;
    private float lastSentMoveX;
    private float lastSentMoveY;
    private bool lastSentGrounded;
    private bool hasSentAnimatorState;

    public override void Spawned()
    {
        ResolveReferences();
        CacheAnimatorParameters();
    }

    private void Awake()
    {
        ResolveReferences();
        CacheAnimatorParameters();
    }

    public override void FixedUpdateNetwork()
    {
        if (HasFusionStateAuthority())
        {
            // Owner captures local state, applies to Animator, and syncs to Networked variables
            CaptureLocalControllerState(
                out float speed,
                out float verticalVelocity,
                out bool grounded,
                out bool running,
                out float moveX,
                out float moveY);

            ApplyValuesToAnimator(speed, verticalVelocity, grounded, running, moveX, moveY);
            
            // Sync to network
            Speed = GetThresholdedValue(Speed, speed);
            VerticalVelocity = GetThresholdedValue(VerticalVelocity, verticalVelocity);
            IsGrounded = grounded;
            IsRunning = running;
            MoveX = GetThresholdedValue(MoveX, moveX);
            MoveY = GetThresholdedValue(MoveY, moveY);
            RPC_UpdateAnimatorState(speed, verticalVelocity, grounded, running, moveX, moveY);
            
            return;
        }

        // Remote proxy applies network state to Animator
        ApplyNetworkStateToAnimator();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_UpdateAnimatorState(float speed, float verticalVelocity, bool grounded, bool running, float moveX, float moveY, RpcInfo info = default)
    {
        if (HasFusionStateAuthority())
        {
            Speed = GetThresholdedValue(Speed, speed);
            VerticalVelocity = GetThresholdedValue(VerticalVelocity, verticalVelocity);
            IsGrounded = grounded;
            IsRunning = running;
            MoveX = GetThresholdedValue(MoveX, moveX);
            MoveY = GetThresholdedValue(MoveY, moveY);
        }

        if (!HasFusionStateAuthority())
        {
            ApplyValuesToAnimator(speed, verticalVelocity, grounded, running, moveX, moveY);
        }
    }

    private void CaptureLocalControllerState(
        out float speed,
        out float verticalVelocity,
        out bool grounded,
        out bool running,
        out float moveX,
        out float moveY)
    {
        speed = 0f;
        verticalVelocity = 0f;
        grounded = true;
        running = false;
        moveX = 0f;
        moveY = 0f;

        if (controller == null)
        {
            return;
        }

        float maxMoveSpeed = GetMaxMoveSpeed();
        verticalVelocity = controller.velocity.y;
        grounded = controller.isGrounded;

        if (movementController != null)
        {
            Vector2 input = movementController.AnimatorMoveInput;
            speed = movementController.AnimatorSpeed;
            running = speed >= runInputThreshold;
            moveX = Mathf.Clamp(input.x, -1f, 1f);
            moveY = Mathf.Clamp(input.y, -1f, 1f);
            return;
        }

        Vector3 localVelocity = transform.InverseTransformDirection(controller.velocity);
        Vector3 planarVelocity = new Vector3(controller.velocity.x, 0f, controller.velocity.z);
        speed = Mathf.Clamp01(planarVelocity.magnitude / maxMoveSpeed);
        running = speed >= runInputThreshold;
        moveX = Mathf.Clamp(localVelocity.x / maxMoveSpeed, -1f, 1f);
        moveY = Mathf.Clamp(localVelocity.z / maxMoveSpeed, -1f, 1f);
    }

    // Removed TrySendAnimatorState and RPC_UpdateAnimatorState (not needed in Shared Mode)

    private void ApplyNetworkStateToAnimator()
    {
        if (animator == null)
        {
            return;
        }

        ApplyValuesToAnimator(Speed, VerticalVelocity, IsGrounded, IsRunning, MoveX, MoveY);
    }

    private void ApplyValuesToAnimator(float speed, float verticalVelocity, bool grounded, bool running, float moveX, float moveY)
    {
        if (animator == null)
        {
            return;
        }

        if (hasSpeedParam)
        {
            animator.SetFloat(speedHash, Mathf.Clamp01(speed));
        }

        if (hasVerticalVelocityParam)
        {
            animator.SetFloat(verticalVelocityHash, verticalVelocity);
        }

        if (hasIsGroundedParam)
        {
            animator.SetBool(isGroundedHash, grounded);
        }

        if (hasIsRunningParam)
        {
            animator.SetBool(isRunningHash, running);
        }

        if (hasMoveXParam)
        {
            animator.SetFloat(moveXHash, Mathf.Clamp(moveX, -1f, 1f));
        }

        if (hasMoveYParam)
        {
            animator.SetFloat(moveYHash, Mathf.Clamp(moveY, -1f, 1f));
        }
    }

    private float GetThresholdedValue(float current, float value)
    {
        if (Mathf.Abs(current - value) > floatSyncThreshold)
        {
            return value;
        }

        return current;
    }

    // Removed ShouldSendAnimatorState

    private void ResolveReferences()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (controller == null)
        {
            controller = GetComponent<CharacterController>();
        }

        if (movementController == null)
        {
            movementController = GetComponent<FusionPlayerMovement>();
        }
    }

    private void CacheAnimatorParameters()
    {
        speedHash = Animator.StringToHash(speedParam);
        verticalVelocityHash = Animator.StringToHash(verticalVelocityParam);
        isGroundedHash = Animator.StringToHash(isGroundedParam);
        isRunningHash = Animator.StringToHash(isRunningParam);
        moveXHash = Animator.StringToHash(moveXParam);
        moveYHash = Animator.StringToHash(moveYParam);

        hasSpeedParam = false;
        hasVerticalVelocityParam = false;
        hasIsGroundedParam = false;
        hasIsRunningParam = false;
        hasMoveXParam = false;
        hasMoveYParam = false;

        if (animator == null)
        {
            return;
        }

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type == AnimatorControllerParameterType.Float && parameter.nameHash == speedHash)
            {
                hasSpeedParam = true;
            }
            else if (parameter.type == AnimatorControllerParameterType.Float && parameter.nameHash == verticalVelocityHash)
            {
                hasVerticalVelocityParam = true;
            }
            else if (parameter.type == AnimatorControllerParameterType.Bool && parameter.nameHash == isGroundedHash)
            {
                hasIsGroundedParam = true;
            }
            else if (parameter.type == AnimatorControllerParameterType.Bool && parameter.nameHash == isRunningHash)
            {
                hasIsRunningParam = true;
            }
            else if (parameter.type == AnimatorControllerParameterType.Float && parameter.nameHash == moveXHash)
            {
                hasMoveXParam = true;
            }
            else if (parameter.type == AnimatorControllerParameterType.Float && parameter.nameHash == moveYHash)
            {
                hasMoveYParam = true;
            }
        }
    }

    private float GetMaxMoveSpeed()
    {
        if (movementController != null)
        {
            return Mathf.Max(0.01f, movementController.MoveSpeed);
        }

        return Mathf.Max(0.01f, fallbackMaxMoveSpeed);
    }

    private bool HasFusionInputAuthority()
    {
        return Object != null && Object.HasStateAuthority;
    }

    private bool HasFusionStateAuthority()
    {
        return Object != null && Object.HasStateAuthority;
    }
}
