using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class NetworkAnimatorStateSync : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;

    [Header("Parameter Names")]
    [SerializeField] private string speedParam = "Speed";
    [SerializeField] private string verticalVelocityParam = "VerticalVelocity";
    [SerializeField] private string isGroundedParam = "IsGrounded";
    [SerializeField] private string isRunningParam = "IsRunning";
    [SerializeField] private string moveXParam = "MoveX";
    [SerializeField] private string moveYParam = "MoveY";

    [Header("Sync Tuning")]
    [SerializeField] private float floatSyncThreshold = 0.01f;

    private readonly NetworkVariable<float> speedValue =
        new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<float> verticalVelocityValue =
        new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<bool> isGroundedValue =
        new NetworkVariable<bool>(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<bool> isRunningValue =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<float> moveXValue =
        new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<float> moveYValue =
        new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private int speedHash;
    private int verticalVelocityHash;
    private int isGroundedHash;
    private int isRunningHash;
    private int moveXHash;
    private int moveYHash;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        speedHash = Animator.StringToHash(speedParam);
        verticalVelocityHash = Animator.StringToHash(verticalVelocityParam);
        isGroundedHash = Animator.StringToHash(isGroundedParam);
        isRunningHash = Animator.StringToHash(isRunningParam);
        moveXHash = Animator.StringToHash(moveXParam);
        moveYHash = Animator.StringToHash(moveYParam);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        speedValue.OnValueChanged += this.OnAnyValueChanged;
        verticalVelocityValue.OnValueChanged += this.OnAnyValueChanged;
        isGroundedValue.OnValueChanged += this.OnAnyValueChanged;
        isRunningValue.OnValueChanged += this.OnAnyValueChanged;
        moveXValue.OnValueChanged += this.OnAnyValueChanged;
        moveYValue.OnValueChanged += this.OnAnyValueChanged;

        if (IsOwner)
        {
            this.PushFromAnimator(true);
        }
        else
        {
            this.ApplyToAnimator();
        }
    }

    public override void OnNetworkDespawn()
    {
        speedValue.OnValueChanged -= this.OnAnyValueChanged;
        verticalVelocityValue.OnValueChanged -= this.OnAnyValueChanged;
        isGroundedValue.OnValueChanged -= this.OnAnyValueChanged;
        isRunningValue.OnValueChanged -= this.OnAnyValueChanged;
        moveXValue.OnValueChanged -= this.OnAnyValueChanged;
        moveYValue.OnValueChanged -= this.OnAnyValueChanged;
        base.OnNetworkDespawn();
    }

    private void LateUpdate()
    {
        if (!IsSpawned || animator == null)
        {
            return;
        }

        if (IsOwner)
        {
            this.PushFromAnimator(false);
        }
    }

    private void OnAnyValueChanged<T>(T previousValue, T newValue)
    {
        if (!IsSpawned || IsOwner)
        {
            return;
        }

        this.ApplyToAnimator();
    }

    private void PushFromAnimator(bool force)
    {
        if (animator == null)
        {
            return;
        }

        float speed = animator.GetFloat(speedHash);
        float verticalVelocity = animator.GetFloat(verticalVelocityHash);
        bool isGrounded = animator.GetBool(isGroundedHash);
        bool isRunning = animator.GetBool(isRunningHash);
        float moveX = animator.GetFloat(moveXHash);
        float moveY = animator.GetFloat(moveYHash);

        if (force || Mathf.Abs(speedValue.Value - speed) > floatSyncThreshold)
        {
            speedValue.Value = speed;
        }

        if (force || Mathf.Abs(verticalVelocityValue.Value - verticalVelocity) > floatSyncThreshold)
        {
            verticalVelocityValue.Value = verticalVelocity;
        }

        if (force || isGroundedValue.Value != isGrounded)
        {
            isGroundedValue.Value = isGrounded;
        }

        if (force || isRunningValue.Value != isRunning)
        {
            isRunningValue.Value = isRunning;
        }

        if (force || Mathf.Abs(moveXValue.Value - moveX) > floatSyncThreshold)
        {
            moveXValue.Value = moveX;
        }

        if (force || Mathf.Abs(moveYValue.Value - moveY) > floatSyncThreshold)
        {
            moveYValue.Value = moveY;
        }
    }

    private void ApplyToAnimator()
    {
        if (animator == null)
        {
            return;
        }

        animator.SetFloat(speedHash, speedValue.Value);
        animator.SetFloat(verticalVelocityHash, verticalVelocityValue.Value);
        animator.SetBool(isGroundedHash, isGroundedValue.Value);
        animator.SetBool(isRunningHash, isRunningValue.Value);
        animator.SetFloat(moveXHash, moveXValue.Value);
        animator.SetFloat(moveYHash, moveYValue.Value);
    }
}
