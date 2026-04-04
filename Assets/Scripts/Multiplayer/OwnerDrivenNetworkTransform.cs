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

    private readonly NetworkVariable<Vector3> syncedPosition =
        new NetworkVariable<Vector3>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<Quaternion> syncedRotation =
        new NetworkVariable<Quaternion>(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private float nextSendTime;
    private Vector3 lastSentPosition;
    private Quaternion lastSentRotation;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        sendInterval = Mathf.Max(0.05f, sendInterval);
        positionThreshold = Mathf.Max(0.003f, positionThreshold);
        rotationThresholdDegrees = Mathf.Max(1f, rotationThresholdDegrees);
        lastSentPosition = transform.position;
        lastSentRotation = transform.rotation;
        nextSendTime = Time.time;

        if (IsServer)
        {
            this.PushCurrentTransformToNetwork();
        }
    }

    private void Update()
    {
        if (!IsSpawned)
        {
            return;
        }

        if (IsOwner)
        {
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
        if (syncPosition)
        {
            syncedPosition.Value = transform.position;
        }

        if (syncRotation)
        {
            syncedRotation.Value = transform.rotation;
        }
    }

    [ServerRpc]
    private void SubmitTransformServerRpc(Vector3 position, Quaternion rotation, ServerRpcParams serverRpcParams = default)
    {
        if (serverRpcParams.Receive.SenderClientId != OwnerClientId)
        {
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
    }
}
