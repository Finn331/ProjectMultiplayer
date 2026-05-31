using Fusion;
using UnityEngine;

public class FusionPlayerOwnerSetup : NetworkBehaviour
{
    [SerializeField] private UnityEngine.Behaviour[] ownerOnlyBehaviours;
    [SerializeField] private Camera[] ownerOnlyCameras;
    [SerializeField] private AudioListener[] ownerOnlyAudioListeners;
    [SerializeField] private GameObject[] ownerOnlyObjects;

    public override void Spawned()
    {
        ApplyOwnerState(Object != null && Object.HasStateAuthority);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        ApplyOwnerState(false);
    }

    private void OnDisable()
    {
        ApplyOwnerState(false);
    }

    private void ApplyOwnerState(bool isOwner)
    {
        if (ownerOnlyBehaviours != null)
        {
            for (int i = 0; i < ownerOnlyBehaviours.Length; i++)
            {
                if (ownerOnlyBehaviours[i] != null)
                {
                    ownerOnlyBehaviours[i].enabled = isOwner;
                }
            }
        }

        if (ownerOnlyCameras != null)
        {
            for (int i = 0; i < ownerOnlyCameras.Length; i++)
            {
                if (ownerOnlyCameras[i] != null)
                {
                    ownerOnlyCameras[i].enabled = isOwner;
                }
            }
        }

        if (ownerOnlyAudioListeners != null)
        {
            for (int i = 0; i < ownerOnlyAudioListeners.Length; i++)
            {
                if (ownerOnlyAudioListeners[i] != null)
                {
                    ownerOnlyAudioListeners[i].enabled = isOwner;
                }
            }
        }

        if (ownerOnlyObjects != null)
        {
            for (int i = 0; i < ownerOnlyObjects.Length; i++)
            {
                if (ownerOnlyObjects[i] != null)
                {
                    ownerOnlyObjects[i].SetActive(isOwner);
                }
            }
        }
    }
}
