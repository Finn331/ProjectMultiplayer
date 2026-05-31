using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class FusionAnimatorSync : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private CharacterController controller;

    [Header("Parameter Names")]
    [SerializeField] private string speedParam = "Speed";
    [SerializeField] private string verticalVelocityParam = "VerticalVelocity";
    [SerializeField] private string isGroundedParam = "IsGrounded";
    [SerializeField] private string moveXParam = "MoveX";
    [SerializeField] private string moveYParam = "MoveY";

    [Header("Sync Tuning")]
    [SerializeField] private float floatSyncThreshold = 0.01f;

    [Networked] private float Speed { get; set; }
    [Networked] private float VerticalVelocity { get; set; }
    [Networked] private NetworkBool IsGrounded { get; set; }
    [Networked] private float MoveX { get; set; }
    [Networked] private float MoveY { get; set; }

    private int speedHash;
    private int verticalVelocityHash;
    private int isGroundedHash;
    private int moveXHash;
    private int moveYHash;
    private bool hasSpeedParam;
    private bool hasVerticalVelocityParam;
    private bool hasIsGroundedParam;
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
                out float moveX,
                out float moveY);
                
            ApplyValuesToAnimator(speed, verticalVelocity, grounded, moveX, moveY);
            
            // Sync to network
            Speed = GetThresholdedValue(Speed, speed);
            VerticalVelocity = GetThresholdedValue(VerticalVelocity, verticalVelocity);
            IsGrounded = grounded;
            MoveX = GetThresholdedValue(MoveX, moveX);
            MoveY = GetThresholdedValue(MoveY, moveY);
            
            return;
        }

        // Remote proxy applies network state to Animator
        ApplyNetworkStateToAnimator();
    }

    // Removed PushNetworkStateFromController since it is handled in FixedUpdateNetwork

    private void CaptureLocalControllerState(
        out float speed,
        out float verticalVelocity,
        out bool grounded,
        out float moveX,
        out float moveY)
    {
        speed = 0f;
        verticalVelocity = 0f;
        grounded = true;
        moveX = 0f;
        moveY = 0f;

        if (controller == null)
        {
            return;
        }

        Vector3 localVelocity = transform.InverseTransformDirection(controller.velocity);
        Vector3 planarVelocity = new Vector3(controller.velocity.x, 0f, controller.velocity.z);
        speed = planarVelocity.magnitude;
        verticalVelocity = controller.velocity.y;
        grounded = controller.isGrounded;
        moveX = localVelocity.x;
        moveY = localVelocity.z;
    }

    // Removed TrySendAnimatorState and RPC_UpdateAnimatorState (not needed in Shared Mode)

    private void ApplyNetworkStateToAnimator()
    {
        if (animator == null)
        {
            return;
        }

        ApplyValuesToAnimator(Speed, VerticalVelocity, IsGrounded, MoveX, MoveY);
    }

    private void ApplyValuesToAnimator(float speed, float verticalVelocity, bool grounded, float moveX, float moveY)
    {
        if (animator == null)
        {
            return;
        }

        if (hasSpeedParam)
        {
            animator.SetFloat(speedHash, speed);
        }

        if (hasVerticalVelocityParam)
        {
            animator.SetFloat(verticalVelocityHash, verticalVelocity);
        }

        if (hasIsGroundedParam)
        {
            animator.SetBool(isGroundedHash, grounded);
        }

        if (hasMoveXParam)
        {
            animator.SetFloat(moveXHash, moveX);
        }

        if (hasMoveYParam)
        {
            animator.SetFloat(moveYHash, moveY);
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
    }

    private void CacheAnimatorParameters()
    {
        speedHash = Animator.StringToHash(speedParam);
        verticalVelocityHash = Animator.StringToHash(verticalVelocityParam);
        isGroundedHash = Animator.StringToHash(isGroundedParam);
        moveXHash = Animator.StringToHash(moveXParam);
        moveYHash = Animator.StringToHash(moveYParam);

        hasSpeedParam = false;
        hasVerticalVelocityParam = false;
        hasIsGroundedParam = false;
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

    private bool HasFusionInputAuthority()
    {
        return Object != null && Object.HasInputAuthority;
    }

    private bool HasFusionStateAuthority()
    {
        return Object != null && Object.HasStateAuthority;
    }
}
