using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityBehaviour = UnityEngine.Behaviour;

public class FusionPlayerOwnerSetup : NetworkBehaviour
{
    [SerializeField] private UnityBehaviour[] ownerOnlyBehaviours;
    [SerializeField] private Camera[] ownerOnlyCameras;
    [SerializeField] private AudioListener[] ownerOnlyAudioListeners;
    [SerializeField] private GameObject[] ownerOnlyObjects;

    private bool hasAppliedOwnerState;
    private bool lastOwnerState;
    private bool warnedMissingOwnerCamera;

    private void Awake()
    {
        EnsureOwnerOnlyReferences();
        ApplyOwnerState(false);
    }

    private void OnEnable()
    {
        if (Object != null)
        {
            ApplyOwnerState(IsLocalOwner());
        }
    }

    public override void Spawned()
    {
        EnsureOwnerOnlyReferences();
        ApplyOwnerState(IsLocalOwner());
    }

    public override void FixedUpdateNetwork()
    {
        bool isOwner = IsLocalOwner();
        if (!hasAppliedOwnerState || isOwner != lastOwnerState)
        {
            ApplyOwnerState(isOwner);
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        ApplyOwnerState(false);
    }

    private void OnDisable()
    {
        ApplyOwnerState(false);
    }

    public void RefreshOwnerOnlyReferencesForDiagnostics()
    {
        EnsureOwnerOnlyReferences();
    }

    public void ApplyOwnerStateForDiagnostics(bool isOwner)
    {
        EnsureOwnerOnlyReferences();
        ApplyOwnerState(isOwner);
    }

    public int GetOwnerOnlyCameraCountForDiagnostics()
    {
        EnsureOwnerOnlyReferences();
        return ownerOnlyCameras != null ? ownerOnlyCameras.Length : 0;
    }

    public int GetOwnerOnlyAudioListenerCountForDiagnostics()
    {
        EnsureOwnerOnlyReferences();
        return ownerOnlyAudioListeners != null ? ownerOnlyAudioListeners.Length : 0;
    }

    public string[] GetOwnerOnlyBehaviourTypeNamesForDiagnostics()
    {
        EnsureOwnerOnlyReferences();
        if (ownerOnlyBehaviours == null)
        {
            return new string[0];
        }

        List<string> names = new List<string>();
        for (int i = 0; i < ownerOnlyBehaviours.Length; i++)
        {
            if (ownerOnlyBehaviours[i] != null)
            {
                names.Add(ownerOnlyBehaviours[i].GetType().Name);
            }
        }

        return names.ToArray();
    }

    private bool IsLocalOwner()
    {
        return Object != null && Object.HasStateAuthority;
    }

    private void EnsureOwnerOnlyReferences()
    {
        if (ownerOnlyBehaviours == null || ownerOnlyBehaviours.Length == 0)
        {
            List<UnityBehaviour> behaviours = new List<UnityBehaviour>();
            AddComponentsInChildren<FPSControllerMobile>(behaviours);
            AddComponentsInChildren<PlayerInteractionSystem>(behaviours);
            AddComponentsInChildren<PlayerInventoryUI>(behaviours);
            AddComponentsInChildren<GridInventoryUI>(behaviours);
            AddComponentsInChildren<DraggableInventoryUI>(behaviours);
            AddComponentsInChildren<MobileHotbarUI>(behaviours);
            AddComponentsInChildren<HotbarConsumeUI>(behaviours);
            AddComponentsInChildren<PlayerAxeCombat>(behaviours);
            AddComponentsInChildren<PlayerProceduralAnimation>(behaviours);
            ownerOnlyBehaviours = behaviours.ToArray();
        }

        if (ownerOnlyCameras == null || ownerOnlyCameras.Length == 0)
        {
            ownerOnlyCameras = GetComponentsInChildren<Camera>(true);
        }

        if (ownerOnlyAudioListeners == null || ownerOnlyAudioListeners.Length == 0)
        {
            ownerOnlyAudioListeners = GetComponentsInChildren<AudioListener>(true);
        }

        if (ownerOnlyObjects == null)
        {
            ownerOnlyObjects = new GameObject[0];
        }
    }

    private static void AddIfPresent(List<UnityBehaviour> behaviours, UnityBehaviour behaviour)
    {
        if (behaviour != null && !behaviours.Contains(behaviour))
        {
            behaviours.Add(behaviour);
        }
    }

    private void AddComponentsInChildren<T>(List<UnityBehaviour> behaviours) where T : UnityBehaviour
    {
        T[] components = GetComponentsInChildren<T>(true);
        for (int i = 0; i < components.Length; i++)
        {
            AddIfPresent(behaviours, components[i]);
        }
    }

    private void ApplyOwnerState(bool isOwner)
    {
        EnsureOwnerOnlyReferences();
        hasAppliedOwnerState = true;
        lastOwnerState = isOwner;

        SetBehavioursEnabled(ownerOnlyBehaviours, isOwner);
        SetCamerasEnabled(ownerOnlyCameras, isOwner);
        SetAudioListenersEnabled(ownerOnlyAudioListeners, isOwner);
        SetObjectsActive(ownerOnlyObjects, isOwner);

        if (isOwner)
        {
            WarnIfMissingOwnerCamera();
            EnsureSingleActiveAudioListener();
        }
    }

    private static void SetBehavioursEnabled(UnityBehaviour[] behaviours, bool enabled)
    {
        if (behaviours == null)
        {
            return;
        }

        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] != null)
            {
                behaviours[i].enabled = enabled;
            }
        }
    }

    private static void SetCamerasEnabled(Camera[] cameras, bool enabled)
    {
        if (cameras == null)
        {
            return;
        }

        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null)
            {
                cameras[i].enabled = enabled;
            }
        }
    }

    private static void SetAudioListenersEnabled(AudioListener[] listeners, bool enabled)
    {
        if (listeners == null)
        {
            return;
        }

        for (int i = 0; i < listeners.Length; i++)
        {
            if (listeners[i] != null)
            {
                listeners[i].enabled = enabled;
            }
        }
    }

    private static void SetObjectsActive(GameObject[] objects, bool active)
    {
        if (objects == null)
        {
            return;
        }

        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] != null)
            {
                objects[i].SetActive(active);
            }
        }
    }

    private void WarnIfMissingOwnerCamera()
    {
        if (warnedMissingOwnerCamera || ownerOnlyCameras == null || ownerOnlyCameras.Length > 0)
        {
            return;
        }

        warnedMissingOwnerCamera = true;
        Debug.LogWarning("Fusion local player has no owner-only camera configured or discoverable: " + GetHierarchyPath(transform), this);
    }

    private void EnsureSingleActiveAudioListener()
    {
        AudioListener primaryListener = null;
        if (ownerOnlyAudioListeners != null)
        {
            for (int i = 0; i < ownerOnlyAudioListeners.Length; i++)
            {
                AudioListener candidate = ownerOnlyAudioListeners[i];
                if (candidate != null && candidate.enabled && candidate.gameObject.activeInHierarchy)
                {
                    primaryListener = candidate;
                    break;
                }
            }
        }

        if (primaryListener == null)
        {
            return;
        }

        AudioListener[] listeners = FindObjectsOfType<AudioListener>(true);
        int disabledCount = 0;
        for (int i = 0; i < listeners.Length; i++)
        {
            AudioListener listener = listeners[i];
            if (listener == null || listener == primaryListener || !listener.enabled)
            {
                continue;
            }

            listener.enabled = false;
            disabledCount++;
        }

        if (disabledCount > 0)
        {
            Debug.LogWarning("FusionPlayerOwnerSetup disabled " + disabledCount + " extra AudioListener component(s) to keep one local listener active.", this);
        }
    }

    private static string GetHierarchyPath(Transform target)
    {
        if (target == null)
        {
            return string.Empty;
        }

        string path = target.name;
        while (target.parent != null)
        {
            target = target.parent;
            path = target.name + "/" + path;
        }

        return path;
    }
}
