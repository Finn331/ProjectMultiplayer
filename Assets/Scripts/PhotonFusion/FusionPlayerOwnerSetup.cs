using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityBehaviour = UnityEngine.Behaviour;

public class FusionPlayerOwnerSetup : NetworkBehaviour
{
    private static readonly UnityBehaviour[] EmptyBehaviours = new UnityBehaviour[0];
    private static readonly GameObject[] EmptyObjects = new GameObject[0];

    [SerializeField] private UnityBehaviour[] ownerOnlyBehaviours;
    [SerializeField] private Camera[] ownerOnlyCameras;
    [SerializeField] private AudioListener[] ownerOnlyAudioListeners;
    [SerializeField] private GameObject[] ownerOnlyObjects;

    private UnityBehaviour[] runtimeOwnerOnlyBehaviours;
    private Camera[] runtimeOwnerOnlyCameras;
    private AudioListener[] runtimeOwnerOnlyAudioListeners;
    private GameObject[] runtimeOwnerOnlyObjects;
    private readonly List<AudioListener> disabledExternalAudioListeners = new List<AudioListener>();

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
        EnsureOwnerOnlyReferences(true);
    }

    public void ApplyOwnerStateForDiagnostics(bool isOwner)
    {
        EnsureOwnerOnlyReferences();
        ApplyOwnerState(isOwner);
    }

    public int GetOwnerOnlyCameraCountForDiagnostics()
    {
        EnsureOwnerOnlyReferences();
        return runtimeOwnerOnlyCameras != null ? runtimeOwnerOnlyCameras.Length : 0;
    }

    public int GetOwnerOnlyAudioListenerCountForDiagnostics()
    {
        EnsureOwnerOnlyReferences();
        return runtimeOwnerOnlyAudioListeners != null ? runtimeOwnerOnlyAudioListeners.Length : 0;
    }

    public string[] GetOwnerOnlyBehaviourTypeNamesForDiagnostics()
    {
        EnsureOwnerOnlyReferences();
        if (runtimeOwnerOnlyBehaviours == null)
        {
            return new string[0];
        }

        List<string> names = new List<string>();
        for (int i = 0; i < runtimeOwnerOnlyBehaviours.Length; i++)
        {
            if (runtimeOwnerOnlyBehaviours[i] != null)
            {
                names.Add(runtimeOwnerOnlyBehaviours[i].GetType().Name);
            }
        }

        return names.ToArray();
    }

    private bool IsLocalOwner()
    {
        return Object != null && Object.HasStateAuthority;
    }

    private void EnsureOwnerOnlyReferences(bool forceRefresh = false)
    {
        if (forceRefresh || runtimeOwnerOnlyBehaviours == null)
        {
            runtimeOwnerOnlyBehaviours = HasEntries(ownerOnlyBehaviours) ? ownerOnlyBehaviours : DiscoverOwnerOnlyBehaviours();
        }

        if (forceRefresh || runtimeOwnerOnlyCameras == null)
        {
            runtimeOwnerOnlyCameras = HasEntries(ownerOnlyCameras) ? ownerOnlyCameras : GetComponentsInChildren<Camera>(true);
        }

        if (forceRefresh || runtimeOwnerOnlyAudioListeners == null)
        {
            runtimeOwnerOnlyAudioListeners = HasEntries(ownerOnlyAudioListeners) ? ownerOnlyAudioListeners : GetComponentsInChildren<AudioListener>(true);
        }

        if (forceRefresh || runtimeOwnerOnlyObjects == null)
        {
            runtimeOwnerOnlyObjects = ownerOnlyObjects != null ? ownerOnlyObjects : EmptyObjects;
        }
    }

    private UnityBehaviour[] DiscoverOwnerOnlyBehaviours()
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

        return behaviours.Count > 0 ? behaviours.ToArray() : EmptyBehaviours;
    }

    private static bool HasEntries<T>(T[] items)
    {
        return items != null && items.Length > 0;
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

        SetBehavioursEnabled(runtimeOwnerOnlyBehaviours, isOwner);
        SetCamerasEnabled(runtimeOwnerOnlyCameras, isOwner);
        SetAudioListenersEnabled(runtimeOwnerOnlyAudioListeners, isOwner);
        SetObjectsActive(runtimeOwnerOnlyObjects, isOwner);

        if (isOwner)
        {
            WarnIfMissingOwnerCamera();
            EnsureSingleActiveAudioListener();
        }
        else
        {
            RestoreExternalAudioListeners();
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
        if (warnedMissingOwnerCamera || runtimeOwnerOnlyCameras == null || runtimeOwnerOnlyCameras.Length > 0)
        {
            return;
        }

        warnedMissingOwnerCamera = true;
        Debug.LogWarning("Fusion local player has no owner-only camera configured or discoverable: " + GetHierarchyPath(transform), this);
    }

    private void EnsureSingleActiveAudioListener()
    {
        AudioListener primaryListener = null;
        if (runtimeOwnerOnlyAudioListeners != null)
        {
            for (int i = 0; i < runtimeOwnerOnlyAudioListeners.Length; i++)
            {
                AudioListener candidate = runtimeOwnerOnlyAudioListeners[i];
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
            if (!IsOwnerOnlyAudioListener(listener) && !disabledExternalAudioListeners.Contains(listener))
            {
                disabledExternalAudioListeners.Add(listener);
            }

            disabledCount++;
        }

        if (disabledCount > 0)
        {
            Debug.LogWarning("FusionPlayerOwnerSetup disabled " + disabledCount + " extra AudioListener component(s) to keep one local listener active.", this);
        }
    }

    private bool IsOwnerOnlyAudioListener(AudioListener listener)
    {
        if (runtimeOwnerOnlyAudioListeners == null)
        {
            return false;
        }

        for (int i = 0; i < runtimeOwnerOnlyAudioListeners.Length; i++)
        {
            if (runtimeOwnerOnlyAudioListeners[i] == listener)
            {
                return true;
            }
        }

        return false;
    }

    private void RestoreExternalAudioListeners()
    {
        if (disabledExternalAudioListeners.Count == 0)
        {
            return;
        }

        for (int i = 0; i < disabledExternalAudioListeners.Count; i++)
        {
            AudioListener listener = disabledExternalAudioListeners[i];
            if (listener != null)
            {
                listener.enabled = true;
            }
        }

        disabledExternalAudioListeners.Clear();
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
