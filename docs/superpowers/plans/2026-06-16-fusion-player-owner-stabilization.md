# Fusion Player Owner Stabilization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stabilize Photon Fusion local/remote player ownership so only the local player has active camera, audio, and local input helpers.

**Architecture:** Keep `FusionPlayerOwnerSetup` as the single Fusion ownership gate. It auto-discovers local-only references when prefab arrays are empty, applies owner state during spawn/authority lifecycle, and exposes diagnostic methods used by Unity MCP checks. Network/visual Fusion components remain enabled so remote proxies continue to animate and replicate.

**Tech Stack:** Unity, C#, Photon Fusion Shared Mode, Unity MCP script validation and editor diagnostics.

---

## File Structure

- Modify: `Assets/Scripts/PhotonFusion/FusionPlayerOwnerSetup.cs`
  - Responsibility: own all Fusion player local-only activation logic for cameras, audio listeners, input/presentation behaviours, and explicit owner-only child objects.
- Verify only: `Assets/Assets/Prefabs/FusionPlayer.prefab`
  - Responsibility: prefab must contain `FusionPlayerOwnerSetup`, child camera/audio listener references, and remote-safe Fusion components. No serialized prefab edit is required unless diagnostics prove the child camera/listener is missing.
- Verify only: `Assets/Scripts/PhotonFusion/FusionPlayerMovement.cs`
  - Responsibility: remains enabled for all instances and continues to gate movement internally with `Object.HasStateAuthority`.
- Verify only: `Assets/Scripts/Player/Movement/FPSController.cs`
  - Responsibility: remains owner-only because it controls first-person rig camera/audio stabilization.

---

### Task 1: Add Red Diagnostic For Missing Owner Setup Surface

**Files:**
- Test/Diagnostic: Unity MCP `execute_code`
- Do not modify production files in this task.

- [ ] **Step 1: Run the diagnostic that should fail before implementation**

Run this Unity MCP `execute_code` snippet:

```csharp
var type = typeof(FusionPlayerOwnerSetup);
string[] requiredMethods =
{
    "RefreshOwnerOnlyReferencesForDiagnostics",
    "ApplyOwnerStateForDiagnostics",
    "GetOwnerOnlyCameraCountForDiagnostics",
    "GetOwnerOnlyAudioListenerCountForDiagnostics",
    "GetOwnerOnlyBehaviourTypeNamesForDiagnostics"
};

foreach (string methodName in requiredMethods)
{
    if (type.GetMethod(methodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public) == null)
    {
        return "FAIL: FusionPlayerOwnerSetup missing diagnostic method " + methodName;
    }
}

return "PASS: FusionPlayerOwnerSetup exposes diagnostic owner-only surface";
```

Expected before implementation: `FAIL: FusionPlayerOwnerSetup missing diagnostic method RefreshOwnerOnlyReferencesForDiagnostics`.

- [ ] **Step 2: Confirm no files changed**

Run: `git status --short`

Expected: no new production file changes from this diagnostic step.

---

### Task 2: Implement Self-Healing Fusion Owner Setup

**Files:**
- Modify: `Assets/Scripts/PhotonFusion/FusionPlayerOwnerSetup.cs`

- [ ] **Step 1: Replace `FusionPlayerOwnerSetup.cs` with the owner-gating implementation**

Replace the file with this complete code:

```csharp
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class FusionPlayerOwnerSetup : NetworkBehaviour
{
    [SerializeField] private Behaviour[] ownerOnlyBehaviours;
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
            List<Behaviour> behaviours = new List<Behaviour>();
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

    private static void AddIfPresent(List<Behaviour> behaviours, Behaviour behaviour)
    {
        if (behaviour != null && !behaviours.Contains(behaviour))
        {
            behaviours.Add(behaviour);
        }
    }

    private void AddComponentsInChildren<T>(List<Behaviour> behaviours) where T : Behaviour
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

    private static void SetBehavioursEnabled(Behaviour[] behaviours, bool enabled)
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
```

- [ ] **Step 2: Validate the script**

Run Unity MCP `validate_script`:

```text
uri: Assets/Scripts/PhotonFusion/FusionPlayerOwnerSetup.cs
level: standard
include_diagnostics: true
```

Expected: `0 errors`. Warnings must be reviewed; do not proceed if a warning indicates missing types or unreachable lifecycle code.

- [ ] **Step 3: Run the diagnostic from Task 1 again**

Run the same Unity MCP `execute_code` snippet from Task 1.

Expected after implementation: `PASS: FusionPlayerOwnerSetup exposes diagnostic owner-only surface`.

- [ ] **Step 4: Commit the script change**

Run:

```bash
git add -- Assets/Scripts/PhotonFusion/FusionPlayerOwnerSetup.cs
git commit -m "Stabilize Fusion player owner setup"
```

---

### Task 3: Verify Prefab Owner-Only Discovery And Remote-Safe Components

**Files:**
- Verify only: `Assets/Assets/Prefabs/FusionPlayer.prefab`
- Verify only: `Assets/Scripts/PhotonFusion/FusionPlayerOwnerSetup.cs`

- [ ] **Step 1: Run prefab discovery diagnostic**

Run this Unity MCP `execute_code` snippet:

```csharp
#if UNITY_EDITOR
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Assets/Prefabs/FusionPlayer.prefab");
if (prefab == null)
{
    return "FAIL: FusionPlayer prefab not found";
}

GameObject instance = null;
try
{
    instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
    if (instance == null)
    {
        return "FAIL: could not instantiate FusionPlayer prefab";
    }

    var setup = instance.GetComponent<FusionPlayerOwnerSetup>();
    if (setup == null)
    {
        return "FAIL: FusionPlayer prefab missing FusionPlayerOwnerSetup";
    }

    setup.RefreshOwnerOnlyReferencesForDiagnostics();
    if (setup.GetOwnerOnlyCameraCountForDiagnostics() <= 0)
    {
        return "FAIL: FusionPlayerOwnerSetup did not discover owner-only cameras";
    }

    if (setup.GetOwnerOnlyAudioListenerCountForDiagnostics() <= 0)
    {
        return "FAIL: FusionPlayerOwnerSetup did not discover owner-only audio listeners";
    }

    var behaviourNames = new System.Collections.Generic.HashSet<string>(setup.GetOwnerOnlyBehaviourTypeNamesForDiagnostics());
    if (!behaviourNames.Contains("FPSControllerMobile"))
    {
        return "FAIL: owner-only behaviours do not include FPSControllerMobile";
    }

    string[] remoteSafeTypes =
    {
        "FusionPlayerMovement",
        "FusionAnimatorSync",
        "PlayerAnimatorDriver",
        "FusionPlayerCombat",
        "FusionPlayerInventory",
        "FusionPlayerSurvival"
    };

    foreach (string typeName in remoteSafeTypes)
    {
        if (behaviourNames.Contains(typeName))
        {
            return "FAIL: remote-safe network component was marked owner-only: " + typeName;
        }
    }

    return "PASS: FusionPlayer owner setup discovers camera/audio/local helpers and leaves network visual components remote-safe";
}
finally
{
    if (instance != null)
    {
        UnityEngine.Object.DestroyImmediate(instance);
    }
}
#else
return "FAIL: diagnostic requires Unity Editor";
#endif
```

Expected: `PASS: FusionPlayer owner setup discovers camera/audio/local helpers and leaves network visual components remote-safe`.

- [ ] **Step 2: If the diagnostic fails because camera or audio listener is missing, inspect the prefab before changing anything**

Run Unity MCP `manage_asset`:

```text
action: get_info
path: Assets/Assets/Prefabs/FusionPlayer.prefab
```

Expected: asset info is returned. If the prefab truly lacks a child camera/audio listener, stop and ask the user whether to wire an existing child camera or create a new child camera; do not invent a camera hierarchy without approval.

- [ ] **Step 3: Confirm no prefab edit was needed**

Run: `git status --short`

Expected if Task 3 passed without prefab changes: no modified `FusionPlayer.prefab` from this task.

---

### Task 4: Verify Owner And Remote Runtime Toggle Behavior

**Files:**
- Verify only: `Assets/Assets/Prefabs/FusionPlayer.prefab`
- Verify only: `Assets/Scripts/PhotonFusion/FusionPlayerOwnerSetup.cs`

- [ ] **Step 1: Run local/remote toggle diagnostic on a temporary prefab instance**

Run this Unity MCP `execute_code` snippet:

```csharp
#if UNITY_EDITOR
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Assets/Prefabs/FusionPlayer.prefab");
if (prefab == null)
{
    return "FAIL: FusionPlayer prefab not found";
}

GameObject instance = null;
try
{
    instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
    if (instance == null)
    {
        return "FAIL: could not instantiate FusionPlayer prefab";
    }

    var setup = instance.GetComponent<FusionPlayerOwnerSetup>();
    if (setup == null)
    {
        return "FAIL: temp FusionPlayer missing FusionPlayerOwnerSetup";
    }

    setup.RefreshOwnerOnlyReferencesForDiagnostics();
    setup.ApplyOwnerStateForDiagnostics(false);

    Camera[] cameras = instance.GetComponentsInChildren<Camera>(true);
    for (int i = 0; i < cameras.Length; i++)
    {
        if (cameras[i] != null && cameras[i].enabled)
        {
            return "FAIL: remote owner state left a camera enabled: " + cameras[i].name;
        }
    }

    AudioListener[] listeners = instance.GetComponentsInChildren<AudioListener>(true);
    for (int i = 0; i < listeners.Length; i++)
    {
        if (listeners[i] != null && listeners[i].enabled)
        {
            return "FAIL: remote owner state left an AudioListener enabled: " + listeners[i].name;
        }
    }

    setup.ApplyOwnerStateForDiagnostics(true);

    int enabledCameraCount = 0;
    cameras = instance.GetComponentsInChildren<Camera>(true);
    for (int i = 0; i < cameras.Length; i++)
    {
        if (cameras[i] != null && cameras[i].enabled)
        {
            enabledCameraCount++;
        }
    }

    if (enabledCameraCount <= 0)
    {
        return "FAIL: local owner state did not enable any child camera";
    }

    int enabledListenerCount = 0;
    listeners = instance.GetComponentsInChildren<AudioListener>(true);
    for (int i = 0; i < listeners.Length; i++)
    {
        if (listeners[i] != null && listeners[i].enabled)
        {
            enabledListenerCount++;
        }
    }

    if (enabledListenerCount != 1)
    {
        return "FAIL: local owner state expected exactly one child AudioListener, found " + enabledListenerCount;
    }

    return "PASS: FusionPlayerOwnerSetup disables remote camera/audio and enables one local listener";
}
finally
{
    if (instance != null)
    {
        UnityEngine.Object.DestroyImmediate(instance);
    }
}
#else
return "FAIL: diagnostic requires Unity Editor";
#endif
```

Expected: `PASS: FusionPlayerOwnerSetup disables remote camera/audio and enables one local listener`.

- [ ] **Step 2: Check Unity console errors**

Run Unity MCP `read_console`:

```text
action: get
types: ["error"]
count: "20"
format: detailed
include_stacktrace: true
```

Expected: `0 log entries`.

- [ ] **Step 3: Confirm the temporary instance did not dirty the scene**

Run: `git status --short`

Expected: only intended script changes from earlier tasks, or clean if Task 2 was already committed.

---

### Task 5: Final Verification And Handoff

**Files:**
- Verify: `Assets/Scripts/PhotonFusion/FusionPlayerOwnerSetup.cs`
- Verify: `Assets/Assets/Prefabs/FusionPlayer.prefab`

- [ ] **Step 1: Refresh Unity scripts**

Run Unity MCP `refresh_unity`:

```text
mode: if_dirty
scope: scripts
compile: request
wait_for_ready: true
```

Expected: editor reports ready for tools.

- [ ] **Step 2: Validate touched script**

Run Unity MCP `validate_script`:

```text
uri: Assets/Scripts/PhotonFusion/FusionPlayerOwnerSetup.cs
level: standard
include_diagnostics: true
```

Expected: `0 errors`.

- [ ] **Step 3: Re-run all diagnostics**

Run the Unity MCP `execute_code` snippets from Task 1, Task 3, and Task 4.

Expected results:

```text
PASS: FusionPlayerOwnerSetup exposes diagnostic owner-only surface
PASS: FusionPlayer owner setup discovers camera/audio/local helpers and leaves network visual components remote-safe
PASS: FusionPlayerOwnerSetup disables remote camera/audio and enables one local listener
```

- [ ] **Step 4: Check Unity console errors**

Run Unity MCP `read_console`:

```text
action: get
types: ["error"]
count: "20"
format: detailed
include_stacktrace: true
```

Expected: `0 log entries`.

- [ ] **Step 5: Check git status**

Run: `git status --short`

Expected: clean, or only files intentionally changed after the Task 2 commit.

- [ ] **Step 6: If there are final uncommitted intentional changes, commit them**

Run:

```bash
git add -- Assets/Scripts/PhotonFusion/FusionPlayerOwnerSetup.cs Assets/Assets/Prefabs/FusionPlayer.prefab
git commit -m "Verify Fusion player owner-only wiring"
```

Expected: commit succeeds only if there were intentional changes. If `git status --short` was clean, skip this step.

- [ ] **Step 7: Report manual QA checklist**

Tell the user to test:

```text
1. Host creates room and starts Gameplay.
2. Friend joins using the room code.
3. Host can move/look/jump only host player.
4. Friend can move/look/jump only friend player.
5. Each machine sees exactly one active camera viewpoint.
6. Unity/player logs show no duplicate AudioListener warning after both players join.
7. Hotbar, inventory, interaction, chest, and attack still work for the local player.
8. Remote player animation and attack visuals remain visible.
```
