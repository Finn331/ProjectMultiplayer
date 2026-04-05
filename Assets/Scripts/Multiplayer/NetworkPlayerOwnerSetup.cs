using Unity.Netcode;
using UnityEngine;

public class NetworkPlayerOwnerSetup : NetworkBehaviour
{
    [Header("Owner-Only Components")]
    [SerializeField] private FPSControllerMobile movementController;
    [SerializeField] private PlayerInteractionSystem interactionSystem;
    [SerializeField] private PlayerInventoryUI inventoryUI;
    [SerializeField] private PlayerAxeCombat axeCombat;
    [SerializeField] private LookArea lookArea;
    [SerializeField] private HeadLookRigAutoSetup headLookRigSetup;

    [Header("Visual Components")]
    [SerializeField] private Camera[] localOnlyCameras;
    [SerializeField] private AudioListener[] localOnlyAudioListeners;

    private void Awake()
    {
        if (movementController == null)
        {
            movementController = GetComponent<FPSControllerMobile>();
        }

        if (interactionSystem == null)
        {
            interactionSystem = GetComponent<PlayerInteractionSystem>();
        }

        if (inventoryUI == null)
        {
            inventoryUI = GetComponent<PlayerInventoryUI>();
        }

        if (axeCombat == null)
        {
            axeCombat = GetComponent<PlayerAxeCombat>();
        }

        if (axeCombat == null)
        {
            axeCombat = gameObject.AddComponent<PlayerAxeCombat>();
        }

        if (headLookRigSetup == null)
        {
            headLookRigSetup = GetComponent<HeadLookRigAutoSetup>();
        }

        if (localOnlyCameras == null || localOnlyCameras.Length == 0)
        {
            localOnlyCameras = GetComponentsInChildren<Camera>(true);
        }

        if (localOnlyAudioListeners == null || localOnlyAudioListeners.Length == 0)
        {
            localOnlyAudioListeners = GetComponentsInChildren<AudioListener>(true);
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        this.ApplyOwnerState(IsOwner);
    }

    private void ApplyOwnerState(bool isLocalOwner)
    {
        this.SetBehaviourState(movementController, isLocalOwner);
        this.SetBehaviourState(interactionSystem, isLocalOwner);
        this.SetBehaviourState(inventoryUI, isLocalOwner);
        // Keep axe combat behaviour enabled on all clients so remote players can
        // still render replicated swing visuals via RPC, while input is gated inside the component.
        this.SetBehaviourState(axeCombat, true);
        this.SetBehaviourState(lookArea, isLocalOwner);
        this.SetBehaviourState(headLookRigSetup, isLocalOwner);

        for (int i = 0; i < localOnlyCameras.Length; i++)
        {
            if (localOnlyCameras[i] != null)
            {
                localOnlyCameras[i].enabled = isLocalOwner;
            }
        }

        for (int i = 0; i < localOnlyAudioListeners.Length; i++)
        {
            if (localOnlyAudioListeners[i] != null)
            {
                localOnlyAudioListeners[i].enabled = isLocalOwner;
            }
        }
    }

    private void SetBehaviourState(Behaviour behaviour, bool state)
    {
        if (behaviour == null)
        {
            return;
        }

        behaviour.enabled = state;
    }
}
