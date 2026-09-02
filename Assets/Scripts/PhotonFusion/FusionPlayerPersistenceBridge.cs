using Fusion;
using UnityEngine;

/// <summary>
/// Lives on the Fusion player prefab. When the player object is destroyed because the
/// scene unloads (Fusion scene transition), it captures the local player's inventory and
/// survival state into <see cref="FusionPlayerPersistence"/> so the freshly spawned player
/// in the new scene can be restored. Only the local (state-authority) instance captures.
/// </summary>
[DisallowMultipleComponent]
public class FusionPlayerPersistenceBridge : NetworkBehaviour
{
    private bool isLocalPlayer;
    private string capturedRoomCode;
    private int capturedRunnerInstanceId;

    public override void Spawned()
    {
        Fusion.NetworkObject networkObject = Object;
        isLocalPlayer = networkObject != null && networkObject.HasStateAuthority;
        capturedRoomCode = PhotonFusionSessionState.HasSession
            ? PhotonFusionSessionState.Active.RoomCode
            : string.Empty;
#if UNITY_6000_5_OR_NEWER
        capturedRunnerInstanceId = Runner != null ? (int)UnityEngine.EntityId.ToULong(Runner.GetEntityId()) : 0;
#else
        capturedRunnerInstanceId = Runner != null ? Runner.GetInstanceID() : 0;
#endif
    }

    private void OnDestroy()
    {
        if (!isLocalPlayer || !Application.isPlaying || capturedRunnerInstanceId == 0)
        {
            return;
        }

        PlayerInventory inventory = GetComponent<PlayerInventory>();
        PlayerSurvivalSystem survival = GetComponent<PlayerSurvivalSystem>();
        FusionPlayerPersistence.Capture(capturedRoomCode, capturedRunnerInstanceId, inventory, survival);
    }
}