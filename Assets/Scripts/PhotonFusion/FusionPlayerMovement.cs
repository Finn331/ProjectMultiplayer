using Fusion;
using UnityEngine;
using UnityEngine.UI;

public class FusionPlayerMovement : NetworkBehaviour
{
    private static readonly System.Collections.Generic.HashSet<int> MissingTransformSyncWarnings = new System.Collections.Generic.HashSet<int>();

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private CharacterController controller;
    [SerializeField] private FloatingJoystick moveJoystick;

    [Header("Gravity & Jump")]
    [SerializeField] private bool enableJump = true;
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float jumpForce = 1.6f;
    [SerializeField] private Button jumpButton;
    [SerializeField] private bool autoBindJumpButton = true;
    [SerializeField] private string jumpButtonNameContains = "jump";
    [SerializeField] private float jumpButtonRetryInterval = 1f;

    [Header("Look")]
    [SerializeField] private Transform cameraHolder;
    [SerializeField] private LookArea lookArea;
    [SerializeField] private float lookSensitivity = 0.2f;
    [SerializeField] private float maxLookAngle = 80f;

    private float verticalVelocity;
    private float xRotation;
    private float nextJumpButtonSearchTime;
    private bool jumpButtonBound;
    private bool warnedNonSharedMode;
    private Vector3 animatorPlanarVelocity;
    private Vector2 animatorMoveInput;
    private float animatorSpeed;

    public float MoveSpeed => moveSpeed;
    public Vector3 AnimatorPlanarVelocity => animatorPlanarVelocity;
    public Vector2 AnimatorMoveInput => animatorMoveInput;
    public float AnimatorSpeed => animatorSpeed;

    public override void Spawned()
    {
        if (controller == null)
        {
            controller = GetComponent<CharacterController>();
        }

        if (HasFusionInputAuthority())
        {
            WarnIfUnsupportedAuthorityModel();
            WarnIfMissingTransformSync();
            RefreshSceneBindings();
        }
        else
        {
            UnbindJumpButton();
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasFusionInputAuthority() || controller == null)
        {
            return;
        }

        if (moveJoystick == null || lookArea == null)
        {
            RefreshSceneBindings();
        }

        TryBindJumpButton();

        Move();
        Look();
        ApplyGravity();
    }

    private void OnDisable()
    {
        UnbindJumpButton();
    }

    private void OnDestroy()
    {
        UnbindJumpButton();
    }

    public void RefreshSceneBindings()
    {
        if (!HasFusionInputAuthority())
        {
            return;
        }

        if (controller == null)
        {
            controller = GetComponent<CharacterController>();
        }

        if (moveJoystick == null)
        {
            moveJoystick = FindObjectOfType<FloatingJoystick>(true);
        }

        if (lookArea == null)
        {
            lookArea = FindObjectOfType<LookArea>(true);
        }

        if (cameraHolder == null)
        {
            var fpsController = GetComponent<FPSControllerMobile>();
            if (fpsController != null)
            {
                cameraHolder = fpsController.cameraHolder;
            }
        }

        TryBindJumpButton();
    }

    public void Jump()
    {
        if (!HasFusionInputAuthority() || !enableJump || controller == null || !controller.isGrounded)
        {
            return;
        }

        verticalVelocity = Mathf.Sqrt(jumpForce * -2f * gravity);
    }

    private void Move()
    {
        if (moveJoystick == null)
        {
            ClearAnimatorMovementState();
            return;
        }

        Vector2 input = new Vector2(moveJoystick.Horizontal, moveJoystick.Vertical);
        float inputMagnitude = Mathf.Clamp01(input.magnitude);
        if (inputMagnitude <= 0.0001f)
        {
            ClearAnimatorMovementState();
            return;
        }

        Vector3 direction = transform.right * input.x + transform.forward * input.y;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            ClearAnimatorMovementState();
            return;
        }

        animatorMoveInput = Vector2.ClampMagnitude(input, 1f);
        animatorPlanarVelocity = direction.normalized * (moveSpeed * inputMagnitude);
        animatorSpeed = Mathf.Clamp01(animatorPlanarVelocity.magnitude / Mathf.Max(0.01f, moveSpeed));

        controller.Move(animatorPlanarVelocity * GetDeltaTime());
    }

    private void ClearAnimatorMovementState()
    {
        animatorPlanarVelocity = Vector3.zero;
        animatorMoveInput = Vector2.zero;
        animatorSpeed = 0f;
    }

    private Vector2 accumulatedLookDelta;

    private void Update()
    {
        if (Object == null || !Object.HasStateAuthority)
        {
            return;
        }

        if (lookArea != null)
        {
            accumulatedLookDelta += lookArea.LookDelta;
            lookArea.ResetDelta();
        }
    }

    private void Look()
    {
        if (lookArea == null || cameraHolder == null)
        {
            return;
        }

        Vector2 delta = accumulatedLookDelta;
        accumulatedLookDelta = Vector2.zero;

        if (delta.sqrMagnitude < 0.01f)
        {
            return;
        }

        float lookX = delta.x * lookSensitivity;
        float lookY = delta.y * lookSensitivity;
        xRotation = Mathf.Clamp(xRotation - lookY, -maxLookAngle, maxLookAngle);
        cameraHolder.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * lookX);
    }

    private void ApplyGravity()
    {
        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        float deltaTime = GetDeltaTime();
        verticalVelocity += gravity * deltaTime;
        controller.Move(Vector3.up * verticalVelocity * deltaTime);
    }

    private void TryBindJumpButton()
    {
        if (jumpButton == null && autoBindJumpButton)
        {
            if (Time.unscaledTime < nextJumpButtonSearchTime)
            {
                return;
            }

            nextJumpButtonSearchTime = Time.unscaledTime + Mathf.Max(0.1f, jumpButtonRetryInterval);
            jumpButton = FindJumpButtonInScene();
        }

        if (jumpButton == null || jumpButtonBound)
        {
            return;
        }

        jumpButton.onClick.AddListener(Jump);
        jumpButtonBound = true;
    }

    private void UnbindJumpButton()
    {
        if (jumpButton != null && jumpButtonBound)
        {
            jumpButton.onClick.RemoveListener(Jump);
        }

        jumpButtonBound = false;
    }

    private Button FindJumpButtonInScene()
    {
        Button[] buttons = FindObjectsOfType<Button>(true);
        string keyword = string.IsNullOrWhiteSpace(jumpButtonNameContains)
            ? "jump"
            : jumpButtonNameContains.Trim().ToLowerInvariant();

        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null || button.gameObject == null)
            {
                continue;
            }

            if (button.gameObject.name.ToLowerInvariant().Contains(keyword))
            {
                return button;
            }
        }

        return null;
    }

    private bool HasFusionInputAuthority()
    {
        return Object != null && Object.HasStateAuthority;
    }

    private void WarnIfUnsupportedAuthorityModel()
    {
        if (warnedNonSharedMode || Runner == null || IsRunnerSharedMode())
        {
            return;
        }

        warnedNonSharedMode = true;
        Debug.LogWarning(
            "FusionPlayerMovement is designed for Photon Fusion Shared Mode client-authoritative movement. In client-server modes, NetworkTransform alone does not make this owner-driven CharacterController authoritative; route movement through state authority instead.",
            this);
    }

    private bool IsRunnerSharedMode()
    {
        System.Reflection.PropertyInfo gameModeProperty = Runner.GetType().GetProperty("GameMode");
        object gameMode = gameModeProperty != null ? gameModeProperty.GetValue(Runner, null) : null;

        return gameMode == null || string.Equals(gameMode.ToString(), "Shared", System.StringComparison.OrdinalIgnoreCase);
    }

    private void WarnIfMissingTransformSync()
    {
        if (!HasFusionInputAuthority() || HasNetworkTransformComponent())
        {
            return;
        }

        int warningKey = GetTransformSyncWarningKey();
        if (!MissingTransformSyncWarnings.Add(warningKey))
        {
            return;
        }

        Debug.LogWarning(
            "FusionPlayerMovement is owner-driven for Photon Fusion Shared Mode and only moves the local input-authority CharacterController. Add a Fusion NetworkTransform-style sync component to publish Shared Mode owner movement to remote proxies.",
            this);
    }

    private int GetTransformSyncWarningKey()
    {
        NetworkObject networkObject = Object;
        return networkObject != null ? networkObject.GetInstanceID() : GetInstanceID();
    }

    private bool HasNetworkTransformComponent()
    {
        Component[] components = GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component != null && component.GetType().Name.Contains("NetworkTransform"))
            {
                return true;
            }
        }

        return false;
    }

    private float GetDeltaTime()
    {
        return Runner != null ? Runner.DeltaTime : Time.deltaTime;
    }
}
