using System;
using UnityEngine;

/// <summary>
/// Procedural Animation System for Player Character.
/// Handles head tracking, body lean, arm/leg swing, head bob, and breathing.
/// </summary>
public class PlayerProceduralAnimation : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform headBone;
    [SerializeField] private Transform spineBone;
    [SerializeField] private Transform leftArmBone;
    [SerializeField] private Transform rightArmBone;
    [SerializeField] private Transform leftLegBone;
    [SerializeField] private Transform rightLegBone;
    [SerializeField] private Camera playerCamera;

    [Header("Head Tracking")]
    [SerializeField] private float headRotationSpeed = 5f;
    [SerializeField] private float maxHeadPitch = 60f;
    [SerializeField] private float maxHeadYaw = 80f;
    [SerializeField, Range(0f, 1f)] private float headTrackingWeight = 0.9f;
    [SerializeField] private bool enableLegacyHeadTracking = true;
    [SerializeField] private bool useAnimatorIKHeadTracking = true;
    [SerializeField] private float ikLookDistance = 8f;
    [SerializeField, Range(0f, 1f)] private float ikBodyWeight = 0.15f;
    [SerializeField, Range(0f, 1f)] private float ikHeadWeight = 0.9f;
    [SerializeField, Range(0f, 1f)] private float ikEyesWeight = 0f;
    [SerializeField, Range(0f, 1f)] private float ikClampWeight = 0.35f;

    [Header("Body Lean")]
    [SerializeField] private float leanAngle = 15f;
    [SerializeField] private float leanSpeed = 5f;

    [Header("Arm Swing")]
    [SerializeField] private float armSwingAmount = 30f;
    [SerializeField] private float armSwingSpeed = 8f;

    [Header("Leg Swing")]
    [SerializeField] private float legSwingAmount = 22f;
    [SerializeField] private float legSwingSpeed = 8f;

    [Header("Breathing")]
    [SerializeField] private float breathingAmount = 0.5f;
    [SerializeField] private float breathingSpeed = 1f;

    [Header("Step Bob")]
    [SerializeField] private float bobAmountY = 0.05f;
    [SerializeField] private float bobAmountX = 0.03f;
    [SerializeField] private float bobSpeed = 10f;

    [Header("Animator Compatibility")]
    [SerializeField] private bool allowFullBodyProceduralWithAnimator = false;

    private CharacterController cachedCharacterController;
    private Animator cachedAnimator;

    private Vector3 originalHeadLocalEuler;
    private Vector3 originalSpineLocalEuler;
    private Vector3 originalLeftArmLocalEuler;
    private Vector3 originalRightArmLocalEuler;
    private Vector3 originalLeftLegLocalEuler;
    private Vector3 originalRightLegLocalEuler;

    private Vector3 originalHeadLocalPosition;
    private Vector3 originalSpineLocalScale;

    private float walkCycle;
    private float breathingCycle;
    private float smoothedHeadPitch;
    private float smoothedHeadYaw;
    private float headPitchVelocity;
    private float headYawVelocity;

    private void Start()
    {
        this.AutoFindBones();

        cachedCharacterController = GetComponent<CharacterController>();
        cachedAnimator = GetComponent<Animator>();

        if (headBone != null)
        {
            originalHeadLocalEuler = headBone.localEulerAngles;
            originalHeadLocalPosition = headBone.localPosition;
        }

        if (spineBone != null)
        {
            originalSpineLocalEuler = spineBone.localEulerAngles;
            originalSpineLocalScale = spineBone.localScale;
        }

        if (leftArmBone != null)
        {
            originalLeftArmLocalEuler = leftArmBone.localEulerAngles;
        }

        if (rightArmBone != null)
        {
            originalRightArmLocalEuler = rightArmBone.localEulerAngles;
        }

        if (leftLegBone != null)
        {
            originalLeftLegLocalEuler = leftLegBone.localEulerAngles;
        }

        if (rightLegBone != null)
        {
            originalRightLegLocalEuler = rightLegBone.localEulerAngles;
        }

        if (playerCamera == null)
        {
            Camera childCamera = GetComponentInChildren<Camera>(true);
            playerCamera = childCamera != null ? childCamera : Camera.main;
        }
    }

    private void LateUpdate()
    {
        Vector3 velocity = cachedCharacterController != null ? cachedCharacterController.velocity : Vector3.zero;
        float horizontalSpeed = new Vector3(velocity.x, 0f, velocity.z).magnitude;
        bool isMoving = horizontalSpeed > 0.1f;
        bool isGrounded = cachedCharacterController == null || cachedCharacterController.isGrounded;

        if (isMoving && isGrounded)
        {
            walkCycle += Time.deltaTime * horizontalSpeed * bobSpeed;
        }

        breathingCycle += Time.deltaTime * breathingSpeed;

        bool animatorHasController =
            cachedAnimator != null &&
            cachedAnimator.enabled &&
            cachedAnimator.runtimeAnimatorController != null;
        bool shouldUseIkHeadTracking = animatorHasController && useAnimatorIKHeadTracking;

        bool canApplyFullBodyProcedural =
            !animatorHasController || allowFullBodyProceduralWithAnimator;

        if (isGrounded && canApplyFullBodyProcedural)
        {
            if (!shouldUseIkHeadTracking && enableLegacyHeadTracking)
            {
                this.ApplyHeadTracking(animatorHasController);
            }

            this.ApplyBodyLean(velocity);
            this.ApplyArmSwing(walkCycle, isMoving, horizontalSpeed);
            this.ApplyLegSwing(walkCycle, isMoving, horizontalSpeed);
            this.ApplyStepBob(walkCycle, isMoving);
            this.ApplyBreathing(breathingCycle);
        }
        else if (animatorHasController && !shouldUseIkHeadTracking && enableLegacyHeadTracking)
        {
            // Keep look responsiveness, but avoid overriding full-body Animator clip pose.
            this.ApplyHeadTracking(true);
        }
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (!useAnimatorIKHeadTracking || cachedAnimator == null || !cachedAnimator.enabled || playerCamera == null)
        {
            return;
        }

        Quaternion lookOffset = this.CalculateLookOffset();
        Vector3 lookDirection = transform.TransformDirection(lookOffset * Vector3.forward);
        float targetDistance = Mathf.Max(2f, ikLookDistance);
        Vector3 lookOrigin = headBone != null ? headBone.position : transform.position + Vector3.up * 1.6f;
        Vector3 lookTarget = lookOrigin + lookDirection.normalized * targetDistance;

        cachedAnimator.SetLookAtWeight(headTrackingWeight, ikBodyWeight, ikHeadWeight, ikEyesWeight, ikClampWeight);
        cachedAnimator.SetLookAtPosition(lookTarget);
    }

    private void ApplyHeadTracking(bool useAnimatorPoseAsBase)
    {
        if (headBone == null || playerCamera == null)
        {
            return;
        }

        Quaternion lookOffset = this.CalculateLookOffset();
        float blend = Mathf.Clamp01(Time.deltaTime * headRotationSpeed);

        if (useAnimatorPoseAsBase)
        {
            // Additive offset over current animated head pose to avoid fighting idle/walk/run clips.
            Quaternion animatedHeadPose = headBone.localRotation;
            Quaternion targetRotation = animatedHeadPose * lookOffset;
            headBone.localRotation = Quaternion.Slerp(animatedHeadPose, targetRotation, blend);
            return;
        }

        Quaternion baseHeadRotation = Quaternion.Euler(originalHeadLocalEuler);
        Quaternion fallbackTarget = baseHeadRotation * lookOffset;
        headBone.localRotation = Quaternion.Slerp(headBone.localRotation, fallbackTarget, blend);
    }

    private Quaternion CalculateLookOffset()
    {
        if (playerCamera == null)
        {
            return Quaternion.identity;
        }

        Vector3 camForward = playerCamera.transform.forward;
        Vector3 localForward = transform.InverseTransformDirection(camForward);

        float pitch = Mathf.Asin(Mathf.Clamp(localForward.y, -1f, 1f)) * Mathf.Rad2Deg;
        float yaw = Mathf.Atan2(localForward.x, localForward.z) * Mathf.Rad2Deg;

        float targetPitch = Mathf.Clamp(-pitch, -maxHeadPitch, maxHeadPitch) * headTrackingWeight;
        float targetYaw = Mathf.Clamp(yaw, -maxHeadYaw, maxHeadYaw) * headTrackingWeight;
        float smoothTime = Mathf.Max(0.01f, 1f / Mathf.Max(0.01f, headRotationSpeed));

        smoothedHeadPitch = Mathf.SmoothDamp(smoothedHeadPitch, targetPitch, ref headPitchVelocity, smoothTime);
        smoothedHeadYaw = Mathf.SmoothDamp(smoothedHeadYaw, targetYaw, ref headYawVelocity, smoothTime);

        return Quaternion.Euler(smoothedHeadPitch, smoothedHeadYaw, 0f);
    }

    private void ApplyBodyLean(Vector3 velocity)
    {
        if (spineBone == null)
        {
            return;
        }

        Vector3 horizontalVel = new Vector3(velocity.x, 0f, velocity.z);
        Vector3 targetLean = Vector3.zero;

        if (horizontalVel.sqrMagnitude > 0.01f)
        {
            Vector3 localVel = transform.InverseTransformDirection(horizontalVel.normalized);
            float targetLeanZ = -localVel.x * leanAngle;
            float targetLeanX = localVel.z * leanAngle * 0.5f;
            targetLean = new Vector3(targetLeanX, 0f, targetLeanZ);
        }

        Quaternion targetRotation = Quaternion.Euler(originalSpineLocalEuler + targetLean);
        spineBone.localRotation = Quaternion.Slerp(spineBone.localRotation, targetRotation, Time.deltaTime * leanSpeed);
    }

    private void ApplyArmSwing(float cycle, bool isMoving, float speed)
    {
        if (leftArmBone == null || rightArmBone == null)
        {
            return;
        }

        Quaternion leftTarget;
        Quaternion rightTarget;

        if (isMoving)
        {
            float swingAngle = Mathf.Sin(cycle) * armSwingAmount * Mathf.Clamp01(speed / 5f);
            leftTarget = Quaternion.Euler(originalLeftArmLocalEuler + new Vector3(swingAngle, 0f, 0f));
            rightTarget = Quaternion.Euler(originalRightArmLocalEuler + new Vector3(-swingAngle, 0f, 0f));
        }
        else
        {
            leftTarget = Quaternion.Euler(originalLeftArmLocalEuler);
            rightTarget = Quaternion.Euler(originalRightArmLocalEuler);
        }

        leftArmBone.localRotation = Quaternion.Slerp(leftArmBone.localRotation, leftTarget, Time.deltaTime * armSwingSpeed);
        rightArmBone.localRotation = Quaternion.Slerp(rightArmBone.localRotation, rightTarget, Time.deltaTime * armSwingSpeed);
    }

    private void ApplyLegSwing(float cycle, bool isMoving, float speed)
    {
        if (leftLegBone == null || rightLegBone == null)
        {
            return;
        }

        Quaternion leftTarget;
        Quaternion rightTarget;

        if (isMoving)
        {
            float legAngle = Mathf.Sin(cycle) * legSwingAmount * Mathf.Clamp01(speed / 5f);
            leftTarget = Quaternion.Euler(originalLeftLegLocalEuler + new Vector3(-legAngle, 0f, 0f));
            rightTarget = Quaternion.Euler(originalRightLegLocalEuler + new Vector3(legAngle, 0f, 0f));
        }
        else
        {
            leftTarget = Quaternion.Euler(originalLeftLegLocalEuler);
            rightTarget = Quaternion.Euler(originalRightLegLocalEuler);
        }

        leftLegBone.localRotation = Quaternion.Slerp(leftLegBone.localRotation, leftTarget, Time.deltaTime * legSwingSpeed);
        rightLegBone.localRotation = Quaternion.Slerp(rightLegBone.localRotation, rightTarget, Time.deltaTime * legSwingSpeed);
    }

    private void ApplyStepBob(float cycle, bool isMoving)
    {
        if (headBone == null)
        {
            return;
        }

        Vector3 targetPosition = originalHeadLocalPosition;

        if (isMoving)
        {
            float bobY = Mathf.Sin(cycle * 2f) * bobAmountY;
            float bobX = Mathf.Cos(cycle) * bobAmountX;
            targetPosition += new Vector3(bobX, bobY, 0f);
        }

        headBone.localPosition = Vector3.Lerp(headBone.localPosition, targetPosition, Time.deltaTime * bobSpeed);
    }

    private void ApplyBreathing(float cycle)
    {
        if (spineBone == null)
        {
            return;
        }

        float breathOffset = Mathf.Sin(cycle) * breathingAmount * 0.01f;
        Vector3 targetScale = originalSpineLocalScale + new Vector3(0f, breathOffset, 0f);
        spineBone.localScale = Vector3.Lerp(spineBone.localScale, targetScale, Time.deltaTime * breathingSpeed);
    }

    [ContextMenu("AutoFindBones")]
    public void AutoFindBones()
    {
        Transform[] localTransforms = GetComponentsInChildren<Transform>(true);

        foreach (Transform t in localTransforms)
        {
            string boneName = t.name;

            if (headBone == null && this.NameContains(boneName, "Head") && !this.NameContains(boneName, "HeadTop"))
            {
                headBone = t;
                continue;
            }

            if (spineBone == null && this.NameContains(boneName, "Spine") &&
                !this.NameContains(boneName, "Spine1") && !this.NameContains(boneName, "Spine2"))
            {
                spineBone = t;
                continue;
            }

            if (leftArmBone == null && this.NameContains(boneName, "LeftArm"))
            {
                leftArmBone = t;
                continue;
            }

            if (rightArmBone == null && this.NameContains(boneName, "RightArm"))
            {
                rightArmBone = t;
                continue;
            }

            if (leftLegBone == null && this.NameContains(boneName, "LeftUpLeg"))
            {
                leftLegBone = t;
                continue;
            }

            if (rightLegBone == null && this.NameContains(boneName, "RightUpLeg"))
            {
                rightLegBone = t;
            }
        }
    }

    private bool NameContains(string source, string keyword)
    {
        return source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
