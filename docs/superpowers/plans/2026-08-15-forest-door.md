# Forest Door Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a host-triggered in-world door in `Gameplay` that transitions the whole Fusion room into `Environment`.

**Architecture:** A new `ForestDoor` MonoBehaviour calls the existing `PhotonFusionSceneLoader.LoadForest()` (which already does the room-wide `runner.LoadScene` transition with host-only guard). `PlayerInteractionSystem` gains a branch to route the existing interact action to the door.

**Tech Stack:** Unity 2022.3, Photon Fusion 2 (Shared mode), existing `Interactable`/`PlayerInteractionSystem` interaction flow.

---

### Task 1: Add `ForestDoor` component

**Files:**
- Create: `Assets/Scripts/PhotonFusion/ForestDoor.cs`
- Create: `Assets/Editor/ForestDoorSelfTest.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Editor/ForestDoorSelfTest.cs`:

```csharp
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class ForestDoorSelfTest
{
    [MenuItem("Project Multiplayer/Run Forest Door Self Test")]
    public static void Run()
    {
        GameObject host = new GameObject("ForestDoorTestHost");
        PhotonFusionSceneLoader loader = host.AddComponent<PhotonFusionSceneLoader>();
        GameObject doorGo = new GameObject("ForestDoorTestDoor");
        ForestDoor door = doorGo.AddComponent<ForestDoor>();

        FieldInfo loaderField = typeof(ForestDoor).GetField("sceneLoader", BindingFlags.NonPublic | BindingFlags.Instance);
        loaderField.SetValue(door, loader);

        bool interacted = door.TryInteract();
        if (!interacted)
        {
            throw new System.InvalidOperationException("ForestDoor.TryInteract should return true when a scene loader exists.");
        }

        UnityEngine.Object.DestroyImmediate(doorGo);
        UnityEngine.Object.DestroyImmediate(host);
        Debug.Log("ForestDoorSelfTest passed.");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run menu `Project Multiplayer/Run Forest Door Self Test` via unityMCP `execute_menu_item`.
Expected: FAIL (compile error, `ForestDoor` not defined).

- [ ] **Step 3: Write minimal implementation**

Create `Assets/Scripts/PhotonFusion/ForestDoor.cs`:

```csharp
using UnityEngine;

public class ForestDoor : MonoBehaviour
{
    private PhotonFusionSceneLoader sceneLoader;

    public bool TryInteract()
    {
        ResolveSceneLoader();
        if (sceneLoader == null)
        {
            return false;
        }

        sceneLoader.LoadForest();
        return true;
    }

    private void ResolveSceneLoader()
    {
        if (sceneLoader == null)
        {
            sceneLoader = FindObjectOfType<PhotonFusionSceneLoader>(true);
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run menu `Project Multiplayer/Run Forest Door Self Test`.
Expected: PASS (`ForestDoorSelfTest passed.`), console errors `0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/PhotonFusion/ForestDoor.cs Assets/Scripts/PhotonFusion/ForestDoor.cs.meta Assets/Editor/ForestDoorSelfTest.cs Assets/Editor/ForestDoorSelfTest.cs.meta
git commit -m "feat: add ForestDoor component"
```

---

### Task 2: Route interaction to ForestDoor

**Files:**
- Modify: `Assets/Scripts/Player/Survival/PlayerInteractionSystem.cs` (inside `TryInteract()`, before the `currentTarget.Interact()` fallback)

- [ ] **Step 1: Write the failing test**

Extend `Assets/Editor/ForestDoorSelfTest.cs` with a second menu item:

```csharp
[MenuItem("Project Multiplayer/Run Forest Door Interaction Routing Self Test")]
public static void RunRoutingTest()
{
    const string playerInteractionSystemPath = "Assets/Scripts/Player/Survival/PlayerInteractionSystem.cs";
    string contents = System.IO.File.ReadAllText(playerInteractionSystemPath);
    if (!contents.Contains("GetComponent<ForestDoor>()"))
    {
        throw new System.InvalidOperationException("PlayerInteractionSystem.TryInteract does not route to ForestDoor.");
    }

    Debug.Log("ForestDoorInteractionRoutingSelfTest passed.");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run menu `Project Multiplayer/Run Forest Door Interaction Routing Self Test`.
Expected: FAIL (throws "does not route to ForestDoor").

- [ ] **Step 3: Write minimal implementation**

In `Assets/Scripts/Player/Survival/PlayerInteractionSystem.cs`, add a branch in `TryInteract()` right before the `currentTarget.Interact();` fallback (after the `FusionFurnace` block):

```csharp
        ForestDoor forestDoor = currentTarget.GetComponent<ForestDoor>();
        if (forestDoor != null)
        {
            if (forestDoor.TryInteract() && pickButton != null)
            {
                pickButton.SetActive(false);
            }

            return;
        }

        currentTarget.Interact();
```

- [ ] **Step 4: Run test to verify it passes**

Run menu `Project Multiplayer/Run Forest Door Interaction Routing Self Test`.
Expected: PASS. Then run `Project Multiplayer/Run Forest Door Self Test` — still PASS. Console errors `0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Player/Survival/PlayerInteractionSystem.cs Assets/Editor/ForestDoorSelfTest.cs
git commit -m "feat: route interaction to ForestDoor"
```

---

### Task 3: Add ForestDoor object to Gameplay scene

**Files:**
- Modify: `Assets/Scenes/Gameplay.unity` (add `ForestDoor` GameObject)

- [ ] **Step 1: Create the door GameObject**

Via unityMCP `manage_gameobject`:
- Create primitive `Cube`, name `ForestDoor`.
- Add components: `Interactable`, `ForestDoor`, `BoxCollider` (already present on Cube).
- Set layer to `3` (`Item`).
- Place near a plausible wall/edge, e.g. position `[-8, 1, 2]`, scale `[1.5, 2.5, 0.3]`.

- [ ] **Step 2: Save the scene**

Via unityMCP `manage_scene` action `save` with path `Assets/Scenes/Gameplay.unity`.

- [ ] **Step 3: Verify wiring**

- Console errors `0`.
- `find_gameobjects` by component `ForestDoor` returns the object.
- Object layer is `3`, has `Interactable`, `ForestDoor`, `BoxCollider`.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scenes/Gameplay.unity
git commit -m "feat: add ForestDoor to Gameplay scene"
```

---

### Task 4: Final verification

- [ ] **Step 1:** Run both self-test menus via `execute_menu_item`; both PASS, console errors `0`.
- [ ] **Step 2:** Verify script validation (`validate_script`) for `ForestDoor.cs` and `PlayerInteractionSystem.cs` returns `0` errors.
- [ ] **Step 3:** Manual multiplayer QA (user): host creates room -> in Gameplay, host looks at door -> press E -> all players transition to Environment and spawn. Non-host press E -> no-op (warning log).
