using Unity.Netcode;
using UnityEngine;

public class OwnerDrivenNetworkTransform : NetworkBehaviour
{
    [Header("Sync")]
    [SerializeField] private bool syncPosition = true;
    [SerializeField] private bool syncRotation = true;
    [SerializeField] private float sendInterval = 0.033f;
    [SerializeField] private float positionThreshold = 0.001f;
    [SerializeField] private float rotationThresholdDegrees = 0.5f;

    [Header("Smoothing")]
    [SerializeField] private float lerpPositionSpeed = 18f;
    [SerializeField] private float lerpRotationSpeed = 18f;
    [SerializeField] private float teleportDistance = 3.5f;

    [Header("Server Validation")]
    [SerializeField] private bool enableServerMovementValidation = true;
    [SerializeField] private float serverMaxMoveSpeed = 14f;
    [SerializeField] private float serverMaxRotationDegreesPerSecond = 1080f;
    [SerializeField] private float serverPositionTolerance = 0.5f;
    [SerializeField] private float serverHardTeleportDistance = 12f;
    [SerializeField] private bool logRejectedServerMovement;

    [Header("Owner Correction")]
    [SerializeField] private bool ownerApplyServerCorrection = true;
    [SerializeField] private float ownerCorrectionThreshold = 0.45f;
    [SerializeField] private float ownerCorrectionLerpSpeed = 16f;
    [SerializeField] private float ownerCorrectionSnapDistance = 3.5f;

    private readonly NetworkVariable<Vector3> syncedPosition =
        new NetworkVariable<Vector3>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<Quaternion> syncedRotation =
        new NetworkVariable<Quaternion>(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private float nextSendTime;
    private Vector3 lastSentPosition;
    private Quaternion lastSentRotation;
    private float lastServerSampleTime;
    private Vector3 lastServerAcceptedPosition;
    private Quaternion lastServerAcceptedRotation;
    private bool hasServerSample;
    private bool hasSyncedServerTransform;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        syncedPosition.OnValueChanged += this.OnSyncedPositionChanged;
        syncedRotation.OnValueChanged += this.OnSyncedRotationChanged;

        sendInterval = Mathf.Max(0.05f, sendInterval);
        positionThreshold = Mathf.Max(0.003f, positionThreshold);
        rotationThresholdDegrees = Mathf.Max(1f, rotationThresholdDegrees);
        lastSentPosition = transform.position;
        lastSentRotation = transform.rotation;
        nextSendTime = Time.time;

        if (IsServer)
        {
            hasServerSample = true;
            lastServerSampleTime = Time.unscaledTime;
            lastServerAcceptedPosition = transform.position;
            lastServerAcceptedRotation = transform.rotation;
            this.PushCurrentTransformToNetwork();
        }
    }

    public override void OnNetworkDespawn()
    {
        syncedPosition.OnValueChanged -= this.OnSyncedPositionChanged;
        syncedRotation.OnValueChanged -= this.OnSyncedRotationChanged;
        hasSyncedServerTransform = false;
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!IsSpawned)
        {
            return;
        }

        if (IsOwner)
        {
            this.TickOwnerCorrectionFromServer();
            this.TickOwnerTransformSend();
            return;
        }

        this.TickRemoteTransformInterpolation();
    }

    private void TickOwnerTransformSend()
    {
        if (Time.time < nextSendTime)
        {
            return;
        }

        nextSendTime = Time.time + Mathf.Max(0.01f, sendInterval);

        Vector3 currentPosition = transform.position;
        Quaternion currentRotation = transform.rotation;
        bool shouldSendPosition = syncPosition &&
            Vector3.SqrMagnitude(currentPosition - lastSentPosition) >= (positionThreshold * positionThreshold);
        bool shouldSendRotation = syncRotation &&
            Quaternion.Angle(currentRotation, lastSentRotation) >= rotationThresholdDegrees;

        if (!shouldSendPosition && !shouldSendRotation)
        {
            return;
        }

        lastSentPosition = currentPosition;
        lastSentRotation = currentRotation;

        if (IsServer)
        {
            hasServerSample = true;
            lastServerSampleTime = Time.unscaledTime;
            lastServerAcceptedPosition = currentPosition;
            lastServerAcceptedRotation = currentRotation;
            this.PushCurrentTransformToNetwork();
            return;
        }

        this.SubmitTransformServerRpc(currentPosition, currentRotation);
    }

    private void TickRemoteTransformInterpolation()
    {
        if (syncPosition)
        {
            Vector3 targetPosition = syncedPosition.Value;
            float distance = Vector3.Distance(transform.position, targetPosition);
            if (distance >= teleportDistance)
            {
                transform.position = targetPosition;
            }
            else
            {
                float t = 1f - Mathf.Exp(-lerpPositionSpeed * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, targetPosition, t);
            }
        }

        if (syncRotation)
        {
            Quaternion targetRotation = syncedRotation.Value;
            float t = 1f - Mathf.Exp(-lerpRotationSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, t);
        }
    }

    private void PushCurrentTransformToNetwork()
    {
        hasSyncedServerTransform = true;

        if (syncPosition)
        {
            syncedPosition.Value = transform.position;
        }

        if (syncRotation)
        {
            syncedRotation.Value = transform.rotation;
        }
    }

    private void OnSyncedPositionChanged(Vector3 previousValue, Vector3 newValue)
    {
        hasSyncedServerTransform = true;
    }

    private void OnSyncedRotationChanged(Quaternion previousValue, Quaternion newValue)
    {
        hasSyncedServerTransform = true;
    }

    public void ServerTeleport(Vector3 position, Quaternion rotation)
    {
        if (!IsServer)
        {
            return;
        }

        transform.SetPositionAndRotation(position, rotation);
        hasServerSample = true;
        lastServerSampleTime = Time.unscaledTime;
        lastServerAcceptedPosition = position;
        lastServerAcceptedRotation = rotation;
        lastSentPosition = position;
        lastSentRotation = rotation;
        this.PushCurrentTransformToNetwork();
    }

    [ServerRpc]
    private void SubmitTransformServerRpc(Vector3 position, Quaternion rotation, ServerRpcParams serverRpcParams = default)
    {
        if (serverRpcParams.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        if (!this.ValidateSubmittedTransform(position, rotation))
        {
            if (syncPosition)
            {
                syncedPosition.Value = hasServerSample ? lastServerAcceptedPosition : transform.position;
            }

            if (syncRotation)
            {
                syncedRotation.Value = hasServerSample ? lastServerAcceptedRotation : transform.rotation;
            }

            return;
        }

        if (syncPosition)
        {
            syncedPosition.Value = position;
        }

        if (syncRotation)
        {
            syncedRotation.Value = rotation;
        }

        hasServerSample = true;
        lastServerSampleTime = Time.unscaledTime;
        lastServerAcceptedPosition = position;
        lastServerAcceptedRotation = rotation;
    }

    private bool ValidateSubmittedTransform(Vector3 submittedPosition, Quaternion submittedRotation)
    {
        if (!enableServerMovementValidation)
        {
            return true;
        }

        float now = Time.unscaledTime;
        if (!hasServerSample)
        {
            hasServerSample = true;
            lastServerSampleTime = now;
            lastServerAcceptedPosition = transform.position;
            lastServerAcceptedRotation = transform.rotation;
            return true;
        }

        float elapsed = Mathf.Max(0.001f, now - lastServerSampleTime);
        float positionDelta = Vector3.Distance(lastServerAcceptedPosition, submittedPosition);
        float rotationDelta = Quaternion.Angle(lastServerAcceptedRotation, submittedRotation);

        float maxDistanceBySpeed = Mathf.Max(0.5f, serverMaxMoveSpeed) * elapsed + Mathf.Max(0f, serverPositionTolerance);
        float maxRotationBySpeed = Mathf.Max(90f, serverMaxRotationDegreesPerSecond) * elapsed + 5f;

        bool rejected =
            positionDelta > Mathf.Max(0.5f, serverHardTeleportDistance) ||
            positionDelta > maxDistanceBySpeed ||
            rotationDelta > maxRotationBySpeed;

        if (rejected && logRejectedServerMovement)
        {
            Debug.LogWarning(
                $"OwnerDrivenNetworkTransform rejected movement. " +
                $"deltaPos={positionDelta:0.###}, allowedPos={maxDistanceBySpeed:0.###}, " +
                $"deltaRot={rotationDelta:0.###}, allowedRot={maxRotationBySpeed:0.###}, elapsed={elapsed:0.###}");
        }

        return !rejected;
    }

    private void TickOwnerCorrectionFromServer()
    {
        if (!ownerApplyServerCorrection || !IsSpawned || !hasSyncedServerTransform || NetworkManager == null || !NetworkManager.IsListening)
        {
            return;
        }

        if (syncPosition)
        {
            Vector3 targetPosition = syncedPosition.Value;
            float distance = Vector3.Distance(transform.position, targetPosition);
            if (distance >= Mathf.Max(0.1f, ownerCorrectionThreshold))
            {
                if (distance >= Mathf.Max(0.5f, ownerCorrectionSnapDistance))
                {
                    transform.position = targetPosition;
                }
                else
                {
                    float t = 1f - Mathf.Exp(-Mathf.Max(1f, ownerCorrectionLerpSpeed) * Time.deltaTime);
                    transform.position = Vector3.Lerp(transform.position, targetPosition, t);
                }
            }
        }

        if (syncRotation)
        {
            Quaternion targetRotation = syncedRotation.Value;
            float rotationDelta = Quaternion.Angle(transform.rotation, targetRotation);
            if (rotationDelta >= 1f)
            {
                float t = 1f - Mathf.Exp(-Mathf.Max(1f, ownerCorrectionLerpSpeed) * Time.deltaTime);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, t);
            }
        }
    }
}
