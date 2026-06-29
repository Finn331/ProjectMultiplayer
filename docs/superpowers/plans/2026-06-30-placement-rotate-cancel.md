# Placement Rotate & Cancel Implementation Plan

> **For agentic workers:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Add rotate (45° per tap) and cancel buttons to placement mode, both auto-resolved from scene button names.

**Architecture:** Local-only additions to `PlaceableItemSystem.cs` — no Fusion, no prefab changes, no new files.

**Tech Stack:** Unity 2022.3, C#, UGUI, existing `PlaceableItemSystem`.

---

### Task 1: Add Rotate & Cancel Buttons and Logic

**Objective:** Add rotate and cancel to placement mode by modifying `PlaceableItemSystem.cs` only.

**Files:**
- Modify: `Assets/Scripts/Player/Survival/PlaceableItemSystem.cs`

**Step 1: Inspect current state**

Run:
```powershell
rg -n "placeButton|rotation|eulerAngles|TogglePlacementMode|RefreshButton|ResolveButtonReference|BindButton|UnbindButton" "Assets/Scripts/Player/Survival/PlaceableItemSystem.cs"
```

Expected: existing button bind logic present, rotation set in `UpdatePreview()`.

**Step 2: Apply the edit**

Insert new serialized fields after `placeButton` (line 21):

```csharp
    [SerializeField] private Button rotateButton;
    [SerializeField] private Button cancelButton;
```

Add tracking field after `currentPreviewBounds` (line 41):

```csharp
    private bool rotateButtonBound;
    private bool cancelButtonBound;
    private float previewYawOffset;
```

Modify `EnterPlacementMode()`:

```csharp
    private void EnterPlacementMode()
    {
        placementMode = true;
        previewYawOffset = 0f;
        EnsurePreviewObject();
        UpdatePreview();
    }
```

Modify `ExitPlacementMode()`:

```csharp
    private void ExitPlacementMode()
    {
        placementMode = false;
        currentPlacementValid = false;
        previewYawOffset = 0f;
        HideRotateCancelButtons();
        if (previewObject != null)
        {
            Destroy(previewObject);
            previewObject = null;
            previewRenderers = null;
        }
    }
```

Replace `UpdatePreview()` rotation line:

```csharp
        Quaternion targetRotation = Quaternion.Euler(0f, transform.eulerAngles.y + previewYawOffset, 0f);
```

Add show/hide methods after `ResolveButtonReference()`:

```csharp
    private void ShowRotateCancelButtons()
    {
        ResolveRotateCancelButtons();
        BindRotateCancelButtons();
        if (rotateButton != null)
        {
            rotateButton.gameObject.SetActive(true);
            rotateButton.interactable = true;
        }
        if (cancelButton != null)
        {
            cancelButton.gameObject.SetActive(true);
            cancelButton.interactable = true;
        }
    }

    private void HideRotateCancelButtons()
    {
        UnbindRotateCancelButtons();
        if (rotateButton != null) rotateButton.gameObject.SetActive(false);
        if (cancelButton != null) cancelButton.gameObject.SetActive(false);
    }

    private void ResolveRotateCancelButtons()
    {
        if (rotateButton == null) rotateButton = FindButtonByName("rotate");
        if (cancelButton == null) cancelButton = FindButtonByName("cancel");
    }

    private void BindRotateCancelButtons()
    {
        if (!rotateButtonBound && rotateButton != null)
        {
            rotateButton.onClick.RemoveListener(RotatePreview);
            rotateButton.onClick.AddListener(RotatePreview);
            rotateButtonBound = true;
        }
        if (!cancelButtonBound && cancelButton != null)
        {
            cancelButton.onClick.RemoveListener(CancelPlacement);
            cancelButton.onClick.AddListener(CancelPlacement);
            cancelButtonBound = true;
        }
    }

    private void UnbindRotateCancelButtons()
    {
        if (rotateButtonBound && rotateButton != null)
        {
            rotateButton.onClick.RemoveListener(RotatePreview);
            rotateButtonBound = false;
        }
        if (cancelButtonBound && cancelButton != null)
        {
            cancelButton.onClick.RemoveListener(CancelPlacement);
            cancelButtonBound = false;
        }
    }

    public void RotatePreview()
    {
        previewYawOffset = (previewYawOffset + 45f) % 360f;
    }

    public void CancelPlacement()
    {
        ExitPlacementMode();
    }
```

Modify `RefreshButton()` to show/hide rotate/cancel:

```csharp
    private void RefreshButton()
    {
        if (placeButton == null || !HasLocalAuthority())
        {
            return;
        }

        bool canPlace = CanPlaceSelectedItem();
        placeButton.gameObject.SetActive(canPlace);
        placeButton.interactable = canPlace;

        if (placementMode && canPlace)
        {
            ShowRotateCancelButtons();
        }
        else
        {
            HideRotateCancelButtons();
        }
    }
```

**Step 3: Compile check**

Unity MCP:
```text
refresh_unity(mode="if_dirty", scope="scripts", compile="request", wait_for_ready=true)
read_console(action="get", types=["error"], count="20")
```
Expected: 0 errors.

**Step 4: Runtime diagnostic**

Unity MCP `execute_code(action="execute", safety_checks=true)`:

```csharp
var go = new UnityEngine.GameObject("PlaceRotateCancelDiagnostic");
var system = go.AddComponent<PlaceableItemSystem>();
var method = typeof(PlaceableItemSystem).GetMethod("RotatePreview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
method.Invoke(system, null);
method.Invoke(system, null);
float yaw = (float)typeof(PlaceableItemSystem).GetField("previewYawOffset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(system);
UnityEngine.Object.DestroyImmediate(go);
return yaw == 90f ? "PASS: RotatePreview increments 45° per call." : "FAIL: yaw=" + yaw;
```

Expected: `PASS: RotatePreview increments 45° per call.`

**Step 5: Commit**

```powershell
git add "Assets/Scripts/Player/Survival/PlaceableItemSystem.cs"
git commit -m "Add placement rotate and cancel"
```

### Task 2: Final Validation

**Objective:** Verify no regressions.

**Files:** None, verification only.

**Step 1: Full refresh and console check**

Unity MCP:
```text
refresh_unity(mode="force", scope="all", compile="request", wait_for_ready=true)
read_console(action="get", types=["error"], count="30", include_stacktrace=true)
```

**Step 2: Git hygiene**

```powershell
git status --short; git log --oneline -6
```

Expected: clean vending-related state, only user's pre-existing unrelated changes remain.

No commit required for this task unless a regression fix is applied.
