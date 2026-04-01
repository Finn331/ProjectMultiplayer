using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Animations.Rigging;

[ExecuteAlways]
public class HeadLookRigAutoSetup : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private FPSControllerMobile movementController;
    [SerializeField] private Transform cameraHolder;
    [SerializeField] private Transform headBone;
    [SerializeField] private Transform lookTarget;

    [Header("Rigging")]
    [SerializeField] private RigBuilder rigBuilder;
    [SerializeField] private Rig headRig;
    [SerializeField] private MultiAimConstraint headAimConstraint;

    [Header("Settings")]
    [SerializeField] private bool setupOnEnable = true;
    [SerializeField] private bool disableLegacyProceduralHeadTracking = true;
    [SerializeField] private float lookTargetDistance = 9f;
    [SerializeField, Range(0f, 1f)] private float constraintWeight = 0.7f;
    [SerializeField] private float minPitchLimit = -60f;
    [SerializeField] private float maxPitchLimit = 60f;

    private static readonly BindingFlags NonPublicInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private bool runtimeRigBuilt;

    private void OnEnable()
    {
        runtimeRigBuilt = false;

        if (setupOnEnable)
        {
            this.Setup();
        }
    }

    private void Start()
    {
        if (Application.isPlaying)
        {
            this.Setup();
            this.TryBuildRigAtRuntime();
        }
    }

    private void LateUpdate()
    {
        if (Application.isPlaying)
        {
            this.TryBuildRigAtRuntime();
        }
    }

    [ContextMenu("Setup Head Look Rig")]
    public void Setup()
    {
        if (!this.ResolveReferences())
        {
            return;
        }

        this.EnsureLookTarget();
        this.EnsureRigObjects();
        this.EnsureMultiAimConstraint();
        this.ConfigureRigBuilder();
        this.ConfigureMultiAimConstraint();
        this.DisableLegacyHeadTrackingIfNeeded();

        if (Application.isPlaying && rigBuilder != null)
        {
            rigBuilder.Build();
        }
    }

    private bool ResolveReferences()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (movementController == null)
        {
            movementController = GetComponent<FPSControllerMobile>();
        }

        if (cameraHolder == null && movementController != null)
        {
            cameraHolder = movementController.cameraHolder;
        }

        if (cameraHolder == null)
        {
            Camera childCamera = GetComponentInChildren<Camera>(true);
            if (childCamera != null)
            {
                cameraHolder = childCamera.transform.parent != null ? childCamera.transform.parent : childCamera.transform;
            }
        }

        if (headBone == null && animator != null && animator.isHuman)
        {
            headBone = animator.GetBoneTransform(HumanBodyBones.Head);
        }

        if (headBone == null)
        {
            Debug.LogWarning("HeadLookRigAutoSetup: Head bone not found.");
            return false;
        }

        if (cameraHolder == null)
        {
            Debug.LogWarning("HeadLookRigAutoSetup: Camera holder not found.");
            return false;
        }

        return true;
    }

    private void EnsureLookTarget()
    {
        if (lookTarget == null)
        {
            Transform existing = cameraHolder.Find("HeadLookTarget");
            if (existing != null)
            {
                lookTarget = existing;
            }
            else
            {
                GameObject targetObject = new GameObject("HeadLookTarget");
                lookTarget = targetObject.transform;
                lookTarget.SetParent(cameraHolder, false);
            }
        }

        lookTarget.localPosition = new Vector3(0f, 0f, Mathf.Max(1f, lookTargetDistance));
        lookTarget.localRotation = Quaternion.identity;
    }

    private void EnsureRigObjects()
    {
        Transform animatedRoot = this.GetAnimatedHierarchyRoot();

        if (rigBuilder == null)
        {
            rigBuilder = GetComponent<RigBuilder>();
            if (rigBuilder == null)
            {
                rigBuilder = gameObject.AddComponent<RigBuilder>();
            }
        }

        if (headRig == null)
        {
            Transform rigRoot = transform.Find("HeadAimRig");
            if (rigRoot == null)
            {
                GameObject rigObject = new GameObject("HeadAimRig");
                rigRoot = rigObject.transform;
                rigRoot.SetParent(animatedRoot, false);
            }
            else if (rigRoot.parent != animatedRoot)
            {
                rigRoot.SetParent(animatedRoot, false);
            }

            headRig = rigRoot.GetComponent<Rig>();
            if (headRig == null)
            {
                headRig = rigRoot.gameObject.AddComponent<Rig>();
            }
        }

        if (headRig != null && headRig.transform.parent != animatedRoot)
        {
            headRig.transform.SetParent(animatedRoot, false);
        }
    }

    private Transform GetAnimatedHierarchyRoot()
    {
        if (headBone == null)
        {
            return transform;
        }

        Transform current = headBone;
        while (current.parent != null && current.parent != transform)
        {
            current = current.parent;
        }

        return current != null ? current : transform;
    }

    private void EnsureMultiAimConstraint()
    {
        if (headRig == null)
        {
            return;
        }

        Transform constraintHost = headRig.transform.Find("HeadAimConstraint");
        if (constraintHost == null)
        {
            GameObject hostObject = new GameObject("HeadAimConstraint");
            constraintHost = hostObject.transform;
            constraintHost.SetParent(headRig.transform, false);
        }

        if (headAimConstraint == null || headAimConstraint.transform != constraintHost)
        {
            headAimConstraint = constraintHost.GetComponent<MultiAimConstraint>();
            if (headAimConstraint == null)
            {
                headAimConstraint = constraintHost.gameObject.AddComponent<MultiAimConstraint>();
            }
        }

        MultiAimConstraint legacyOnHead = headBone != null ? headBone.GetComponent<MultiAimConstraint>() : null;
        if (legacyOnHead != null && legacyOnHead != headAimConstraint)
        {
            legacyOnHead.weight = 0f;
            legacyOnHead.enabled = false;
        }
    }

    private void ConfigureRigBuilder()
    {
        if (rigBuilder == null || headRig == null)
        {
            return;
        }

        List<RigLayer> layers = rigBuilder.layers ?? new List<RigLayer>();
        bool exists = false;

        for (int i = 0; i < layers.Count; i++)
        {
            if (layers[i].rig == headRig)
            {
                RigLayer activeLayer = layers[i];
                activeLayer.active = true;
                layers[i] = activeLayer;
                exists = true;
                break;
            }
        }

        if (!exists)
        {
            RigLayer layer = new RigLayer(headRig);
            layer.active = true;
            layers.Add(layer);
        }

        rigBuilder.layers = layers;
    }

    private void ConfigureMultiAimConstraint()
    {
        if (headAimConstraint == null || lookTarget == null || headBone == null)
        {
            return;
        }

        MultiAimConstraintData data = this.GetConstraintData(headAimConstraint);
        data.constrainedObject = headBone;

        WeightedTransformArray sources = data.sourceObjects;
        sources.Clear();
        sources.Add(new WeightedTransform(lookTarget, 1f));
        data.sourceObjects = sources;

        data.maintainOffset = false;
        data.offset = Vector3.zero;
        data.limits = new Vector2(minPitchLimit, maxPitchLimit);
        data.aimAxis = MultiAimConstraintData.Axis.Z;
        data.upAxis = MultiAimConstraintData.Axis.Y;
        data.worldUpType = MultiAimConstraintData.WorldUpType.SceneUp;
        data.worldUpAxis = MultiAimConstraintData.Axis.Y;
        data.worldUpObject = null;
        data.constrainedXAxis = true;
        data.constrainedYAxis = true;
        data.constrainedZAxis = false;

        this.SetConstraintData(headAimConstraint, data);
        headAimConstraint.weight = constraintWeight;

        if (headRig != null)
        {
            headRig.weight = 1f;
        }
    }

    private void DisableLegacyHeadTrackingIfNeeded()
    {
        if (!disableLegacyProceduralHeadTracking)
        {
            return;
        }

        PlayerProceduralAnimation procedural = GetComponent<PlayerProceduralAnimation>();
        if (procedural == null)
        {
            return;
        }

        FieldInfo legacyIkField = typeof(PlayerProceduralAnimation).GetField("useAnimatorIKHeadTracking", NonPublicInstance);
        if (legacyIkField != null)
        {
            legacyIkField.SetValue(procedural, false);
        }

        FieldInfo legacyHeadField = typeof(PlayerProceduralAnimation).GetField("enableLegacyHeadTracking", NonPublicInstance);
        if (legacyHeadField != null)
        {
            legacyHeadField.SetValue(procedural, false);
        }
    }

    private MultiAimConstraintData GetConstraintData(MultiAimConstraint constraint)
    {
        FieldInfo dataField = typeof(MultiAimConstraint).GetField("m_Data", NonPublicInstance);
        if (dataField == null)
        {
            throw new MissingFieldException("MultiAimConstraint.m_Data field not found.");
        }

        return (MultiAimConstraintData)dataField.GetValue(constraint);
    }

    private void SetConstraintData(MultiAimConstraint constraint, MultiAimConstraintData data)
    {
        FieldInfo dataField = typeof(MultiAimConstraint).GetField("m_Data", NonPublicInstance);
        if (dataField == null)
        {
            throw new MissingFieldException("MultiAimConstraint.m_Data field not found.");
        }

        dataField.SetValue(constraint, data);
    }

    private void TryBuildRigAtRuntime()
    {
        if (runtimeRigBuilt || rigBuilder == null || !rigBuilder.isActiveAndEnabled)
        {
            return;
        }

        rigBuilder.Build();
        runtimeRigBuilt = true;
    }
}
