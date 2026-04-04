using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class FPSControllerMobile : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public FloatingJoystick moveJoystick;
    public CharacterController controller;
    public LowHealthInjuredAnimationController injuredAnimationController;
    [Range(0.01f, 0.5f)] public float injuredInputThresholdForFullSpeed = 0.08f;

    [Header("Gravity & Jump")]
    public bool enableJump = true;
    public float gravity = -9.81f;
    public float jumpForce = 1.6f;

    [Header("Input Buttons")]
    [SerializeField] private Button jumpButton;
    [SerializeField] private bool autoBindJumpButton = true;
    [SerializeField] private string jumpButtonNameContains = "jump";

    [Header("Look Settings")]
    public Transform cameraHolder;
    public LookArea lookArea;

    [Range(0.05f, 1f)]
    public float mobileLookSensitivity = 0.2f;

    [Range(30f, 89f)]
    public float maxLookAngle = 80f;

    [Header("First-Person Camera Fix")]
    public Camera mainCamera;
    public bool stabilizeCameraLocalTransform = true;
    public Vector3 cameraLocalPositionOffset = new Vector3(0f, 0.525f, 0.12f);
    public bool anchorCameraToHead = true;
    public Transform headBone;
    public Vector3 headAnchorOffset = new Vector3(0f, 0.06f, 0.14f);
    public bool followHeadPosition = false;
    public bool followHeadRotation = true;
    [Range(1f, 40f)] public float headAnchorFollowSpeed = 20f;
    public bool disableHeadLookRigForFirstPerson = false;
    public bool enforceNearClipPlaneForFirstPerson = true;
    [Range(0.01f, 0.3f)] public float firstPersonNearClipPlane = 0.12f;
    public bool hideHeadAccessoriesForFirstPerson = true;
    public bool hideFullCharacterMeshForFirstPerson = false;

    [Header("Camera Collision")]
    public bool preventCameraClipping = true;
    [Range(0.02f, 0.35f)] public float cameraCollisionRadius = 0.08f;
    [Range(0.01f, 0.3f)] public float cameraCollisionPadding = 0.04f;
    [Range(0.01f, 0.25f)] public float cameraCollisionMinDistance = 0.02f;
    public LayerMask cameraCollisionLayers = ~0;

    float xRotation = 0f;
    float verticalVelocity;
    float lastJumpPressedTime = -999f;
    private bool jumpButtonBound;
    readonly List<Renderer> firstPersonHiddenRenderers = new List<Renderer>();
    readonly List<bool> firstPersonHiddenRendererOriginalStates = new List<bool>();
    readonly List<ShadowCastingMode> firstPersonHiddenRendererOriginalShadowModes = new List<ShadowCastingMode>();
    readonly List<bool> firstPersonHiddenRendererOriginalForceRenderingOff = new List<bool>();

    void Start()
    {
        if (!this.HasInputAuthority())
        {
            this.SetLocalFirstPersonRigActive(false);
            enabled = false;
            return;
        }

        if (!controller)
            controller = GetComponent<CharacterController>();

        if (moveJoystick == null)
        {
            moveJoystick = FindObjectOfType<FloatingJoystick>();
        }

        if (lookArea == null)
        {
            lookArea = FindObjectOfType<LookArea>();
        }

        if (!injuredAnimationController)
            injuredAnimationController = GetComponent<LowHealthInjuredAnimationController>();

        if (!mainCamera)
        {
            if (cameraHolder)
            {
                mainCamera = cameraHolder.GetComponentInChildren<Camera>(true);
            }

            if (!mainCamera)
            {
                mainCamera = Camera.main;
            }
        }

        if (!headBone)
        {
            Animator animator = GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            }
        }

        if (disableHeadLookRigForFirstPerson)
        {
            HeadLookRigAutoSetup headLookSetup = GetComponent<HeadLookRigAutoSetup>();
            if (headLookSetup != null)
            {
                headLookSetup.enabled = false;
            }

            Behaviour rigBuilder = GetComponent("RigBuilder") as Behaviour;
            if (rigBuilder != null)
            {
                rigBuilder.enabled = false;
            }
        }

        this.ApplyFirstPersonRendererVisibility();
        this.ApplyFirstPersonCameraStabilization();
        this.TryBindJumpButton();
        this.SetLocalFirstPersonRigActive(true);
    }

    void Update()
    {
        if (!this.HasInputAuthority())
        {
            return;
        }

        MobileMovement();
        MobileLook();
        ApplyGravity();
    }

    void LateUpdate()
    {
        if (!this.HasInputAuthority())
        {
            return;
        }

        this.ApplyFirstPersonRendererVisibility();
        this.ApplyFirstPersonCameraStabilization();
    }

    // ================= MOVEMENT =================
    void MobileMovement()
    {
        if (!controller || moveJoystick == null)
        {
            return;
        }

        Vector2 joystickInput = new Vector2(moveJoystick.Horizontal, moveJoystick.Vertical);
        float inputMagnitude = Mathf.Clamp01(joystickInput.magnitude);

        if (inputMagnitude <= 0.0001f)
        {
            return;
        }

        bool injuredActive = injuredAnimationController != null && injuredAnimationController.IsInjuredActive;
        float effectiveInputMagnitude = inputMagnitude;

        if (injuredActive)
        {
            effectiveInputMagnitude = inputMagnitude >= injuredInputThresholdForFullSpeed ? 1f : 0f;
            if (effectiveInputMagnitude <= 0f)
            {
                return;
            }
        }

        Vector3 inputDirection =
            transform.right * joystickInput.x +
            transform.forward * joystickInput.y;

        Vector3 moveDirection = inputDirection.sqrMagnitude > 0.0001f
            ? inputDirection.normalized
            : Vector3.zero;
        float targetSpeed = moveSpeed * effectiveInputMagnitude;
        controller.Move(moveDirection * targetSpeed * Time.deltaTime);
    }

    // ================= LOOK =================
    void MobileLook()
    {
        if (!lookArea || !cameraHolder) return;

        Vector2 delta = lookArea.LookDelta;
        if (delta.sqrMagnitude < 0.01f) return;

        float lookX = delta.x * mobileLookSensitivity;
        float lookY = delta.y * mobileLookSensitivity;

        xRotation -= lookY;
        xRotation = Mathf.Clamp(xRotation, -maxLookAngle, maxLookAngle);

        cameraHolder.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * lookX);

        lookArea.ResetDelta();
    }

    void ApplyFirstPersonCameraStabilization()
    {
        if (!stabilizeCameraLocalTransform || !cameraHolder || !mainCamera)
        {
            return;
        }

        if (cameraHolder.localPosition != Vector3.zero)
        {
            cameraHolder.localPosition = Vector3.zero;
        }

        Transform cameraTransform = mainCamera.transform;
        if (cameraTransform.parent != cameraHolder)
        {
            return;
        }

        float lerpFactor = 1f - Mathf.Exp(-headAnchorFollowSpeed * Time.deltaTime);
        Vector3 desiredWorldPosition;
        Quaternion desiredLocalRotation;

        if (anchorCameraToHead && headBone != null)
        {
            desiredWorldPosition = followHeadPosition
                ? headBone.TransformPoint(headAnchorOffset)
                : cameraHolder.position + (headBone.rotation * headAnchorOffset);
            desiredLocalRotation = followHeadRotation
                ? Quaternion.Inverse(cameraHolder.rotation) * headBone.rotation
                : Quaternion.identity;
        }
        else
        {
            desiredWorldPosition = cameraHolder.TransformPoint(cameraLocalPositionOffset);
            desiredLocalRotation = Quaternion.identity;
        }

        if (preventCameraClipping)
        {
            desiredWorldPosition = this.ResolveCameraCollision(cameraHolder.position, desiredWorldPosition);
        }

        Vector3 targetLocalPosition = cameraHolder.InverseTransformPoint(desiredWorldPosition);
        cameraTransform.localPosition = Vector3.Lerp(cameraTransform.localPosition, targetLocalPosition, lerpFactor);
        cameraTransform.localRotation = Quaternion.Slerp(cameraTransform.localRotation, desiredLocalRotation, lerpFactor);

        if (enforceNearClipPlaneForFirstPerson && Mathf.Abs(mainCamera.nearClipPlane - firstPersonNearClipPlane) > 0.0001f)
        {
            mainCamera.nearClipPlane = firstPersonNearClipPlane;
        }
    }

    Vector3 ResolveCameraCollision(Vector3 pivotWorldPosition, Vector3 desiredWorldPosition)
    {
        Vector3 direction = desiredWorldPosition - pivotWorldPosition;
        float distance = direction.magnitude;
        if (distance <= 0.0001f)
        {
            return desiredWorldPosition;
        }

        Vector3 directionNormalized = direction / distance;
        RaycastHit[] hits = Physics.SphereCastAll(
            pivotWorldPosition,
            cameraCollisionRadius,
            directionNormalized,
            distance,
            cameraCollisionLayers,
            QueryTriggerInteraction.Ignore);

        float closestDistance = float.MaxValue;
        bool hasBlockingHit = false;
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null)
            {
                continue;
            }

            Transform hitTransform = hit.collider.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
            {
                continue;
            }

            if (hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                hasBlockingHit = true;
            }
        }

        if (!hasBlockingHit)
        {
            return desiredWorldPosition;
        }

        float safeDistance = Mathf.Max(cameraCollisionMinDistance, closestDistance - cameraCollisionPadding);
        return pivotWorldPosition + (directionNormalized * safeDistance);
    }

    void ApplyFirstPersonRendererVisibility()
    {
        if (!hideHeadAccessoriesForFirstPerson)
        {
            this.RestoreFirstPersonRendererVisibility();
            return;
        }

        if (firstPersonHiddenRenderers.Count > 0)
        {
            for (int i = 0; i < firstPersonHiddenRenderers.Count; i++)
            {
                Renderer r = firstPersonHiddenRenderers[i];
                if (r != null)
                {
                    if (hideFullCharacterMeshForFirstPerson)
                    {
                        r.forceRenderingOff = true;
                    }
                    else
                    {
                        r.forceRenderingOff = false;
                        r.enabled = false;
                    }
                }
            }
            return;
        }

        SkinnedMeshRenderer[] allSkinned = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < allSkinned.Length; i++)
        {
            SkinnedMeshRenderer renderer = allSkinned[i];
            if (renderer == null)
            {
                continue;
            }

            string lowerName = renderer.gameObject.name.ToLowerInvariant();
            bool isHeadAccessory =
                lowerName.Contains("hair") ||
                lowerName.Contains("eyelash") ||
                lowerName.Contains("beard");

            if (!isHeadAccessory)
            {
                continue;
            }

            firstPersonHiddenRenderers.Add(renderer);
            firstPersonHiddenRendererOriginalStates.Add(renderer.enabled);
            firstPersonHiddenRendererOriginalShadowModes.Add(renderer.shadowCastingMode);
            firstPersonHiddenRendererOriginalForceRenderingOff.Add(renderer.forceRenderingOff);

            if (hideFullCharacterMeshForFirstPerson)
            {
                renderer.forceRenderingOff = true;
            }
            else
            {
                renderer.forceRenderingOff = false;
                renderer.enabled = false;
            }
        }

        if (hideFullCharacterMeshForFirstPerson)
        {
            for (int i = 0; i < allSkinned.Length; i++)
            {
                SkinnedMeshRenderer renderer = allSkinned[i];
                if (renderer == null || firstPersonHiddenRenderers.Contains(renderer))
                {
                    continue;
                }

                firstPersonHiddenRenderers.Add(renderer);
                firstPersonHiddenRendererOriginalStates.Add(renderer.enabled);
                firstPersonHiddenRendererOriginalShadowModes.Add(renderer.shadowCastingMode);
                firstPersonHiddenRendererOriginalForceRenderingOff.Add(renderer.forceRenderingOff);
                renderer.forceRenderingOff = true;
            }
        }
    }

    void RestoreFirstPersonRendererVisibility()
    {
        for (int i = 0; i < firstPersonHiddenRenderers.Count; i++)
        {
            Renderer r = firstPersonHiddenRenderers[i];
            if (r != null)
            {
                bool original = i < firstPersonHiddenRendererOriginalStates.Count ? firstPersonHiddenRendererOriginalStates[i] : true;
                ShadowCastingMode originalShadow = i < firstPersonHiddenRendererOriginalShadowModes.Count
                    ? firstPersonHiddenRendererOriginalShadowModes[i]
                    : ShadowCastingMode.On;
                bool originalForceRenderingOff = i < firstPersonHiddenRendererOriginalForceRenderingOff.Count
                    ? firstPersonHiddenRendererOriginalForceRenderingOff[i]
                    : false;
                r.enabled = original;
                r.shadowCastingMode = originalShadow;
                r.forceRenderingOff = originalForceRenderingOff;
            }
        }
    }

    void OnDisable()
    {
        this.RestoreFirstPersonRendererVisibility();
    }

    // ================= GRAVITY & JUMP =================
    void ApplyGravity()
    {
        if (controller.isGrounded)
        {
            if (verticalVelocity < 0)
                verticalVelocity = -2f;

            // if (enableJump && Input.GetKeyDown(KeyCode.Space))
            // {
            //     verticalVelocity = Mathf.Sqrt(jumpForce * -2f * gravity);
            // }
        }

        verticalVelocity += gravity * Time.deltaTime;
        controller.Move(Vector3.up * verticalVelocity * Time.deltaTime);
    }

    public void Jump()
    {
        if (!this.HasInputAuthority())
        {
            return;
        }

        if (enableJump && controller != null && controller.isGrounded)
        {
            verticalVelocity = Mathf.Sqrt(jumpForce * -2f * gravity);
            lastJumpPressedTime = Time.time;
        }
    }

    public bool WasJumpPressedRecently(float windowSeconds)
    {
        float window = Mathf.Max(0.01f, windowSeconds);
        return (Time.time - lastJumpPressedTime) <= window;
    }

    private void TryBindJumpButton()
    {
        if (jumpButton == null && autoBindJumpButton)
        {
            jumpButton = this.FindJumpButtonInScene();
        }

        if (jumpButton == null || jumpButtonBound)
        {
            return;
        }

        jumpButton.onClick.AddListener(this.Jump);
        jumpButtonBound = true;
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

            string buttonName = button.gameObject.name.ToLowerInvariant();
            if (buttonName.Contains(keyword))
            {
                return button;
            }
        }

        return null;
    }

    private bool HasInputAuthority()
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

        return networkObject.IsOwner;
    }

    private void SetLocalFirstPersonRigActive(bool active)
    {
        if (mainCamera != null)
        {
            mainCamera.enabled = active;
        }

        AudioListener listener = mainCamera != null ? mainCamera.GetComponent<AudioListener>() : null;
        if (listener != null)
        {
            listener.enabled = active;
        }
    }

    private void OnDestroy()
    {
        if (jumpButton != null && jumpButtonBound)
        {
            jumpButton.onClick.RemoveListener(this.Jump);
        }

        jumpButtonBound = false;
    }

}
