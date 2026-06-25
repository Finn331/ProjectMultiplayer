# Local Player Body Visibility Implementation Plan

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Hide the local player's own head and upper torso while keeping hands, arms, legs, and hips visible, without affecting how other players see the model.

**Architecture:** Add one small Fusion `NetworkBehaviour` to `FusionPlayer.prefab` that applies renderer hiding only on the local authority instance. The component stores original renderer state, applies local-only hiding after spawn, and restores state on disable/despawn/authority change. Prefab wiring uses explicit renderer references for the known `Player Prototype` renderer children.

**Tech Stack:** Unity, C#, Photon Fusion, Unity MCP, Context7 Photon Fusion documentation.

---

### Task 1: Create `FusionLocalBodyVisibility` script

**Objective:** Add a focused component that can hide configured renderers for only the local player instance.

**Files:**
- Create: `Assets/Scripts/PhotonFusion/FusionLocalBodyVisibility.cs`

**Step 1: Create the script**

Use `apply_patch` or Unity MCP script creation to add this complete file:

```csharp
using Fusion;
using UnityEngine;
using UnityEngine.Rendering;

public class FusionLocalBodyVisibility : NetworkBehaviour
{
    [Header("Local Authority")]
    [SerializeField] private bool useStateAuthorityFallback = true;

    [Header("Local First Person Visibility")]
    [SerializeField] private Renderer[] hideForLocalPlayer;
    [SerializeField] private bool disableRenderer = true;
    [SerializeField] private bool forceRenderingOff = true;

    private bool[] originalEnabledStates;
    private bool[] originalForceRenderingOffStates;
    private ShadowCastingMode[] originalShadowCastingModes;
    private bool originalStatesCaptured;
    private bool isLocalHidden;

    private void Awake()
    {
        CaptureOriginalStates();
        ApplyVisibility(IsLocalPlayerInstance());
    }

    private void OnEnable()
    {
        ApplyVisibility(IsLocalPlayerInstance());
    }

    public override void Spawned()
    {
        CaptureOriginalStates();
        ApplyVisibility(IsLocalPlayerInstance());
    }

    public override void FixedUpdateNetwork()
    {
        bool shouldHide = IsLocalPlayerInstance();
        if (shouldHide != isLocalHidden)
        {
            ApplyVisibility(shouldHide);
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        RestoreOriginalStates();
    }

    private void OnDisable()
    {
        RestoreOriginalStates();
    }

    public void ApplyVisibilityForDiagnostics(bool shouldHide)
    {
        CaptureOriginalStates();
        ApplyVisibility(shouldHide);
    }

    public bool IsRendererHiddenForDiagnostics(Renderer renderer)
    {
        if (renderer == null)
        {
            return false;
        }

        return renderer.forceRenderingOff || !renderer.enabled;
    }

    private bool IsLocalPlayerInstance()
    {
        if (Object == null)
        {
            return false;
        }

        return Object.HasInputAuthority || (useStateAuthorityFallback && Object.HasStateAuthority);
    }

    private void ApplyVisibility(bool shouldHide)
    {
        CaptureOriginalStates();

        if (!shouldHide)
        {
            RestoreOriginalStates();
            return;
        }

        if (hideForLocalPlayer == null)
        {
            return;
        }

        for (int i = 0; i < hideForLocalPlayer.Length; i++)
        {
            Renderer renderer = hideForLocalPlayer[i];
            if (renderer == null)
            {
                continue;
            }

            if (forceRenderingOff)
            {
                renderer.forceRenderingOff = true;
            }

            if (disableRenderer)
            {
                renderer.enabled = false;
            }
        }

        isLocalHidden = true;
    }

    private void CaptureOriginalStates()
    {
        if (originalStatesCaptured)
        {
            return;
        }

        int count = hideForLocalPlayer != null ? hideForLocalPlayer.Length : 0;
        originalEnabledStates = new bool[count];
        originalForceRenderingOffStates = new bool[count];
        originalShadowCastingModes = new ShadowCastingMode[count];

        for (int i = 0; i < count; i++)
        {
            Renderer renderer = hideForLocalPlayer[i];
            if (renderer == null)
            {
                originalEnabledStates[i] = true;
                originalForceRenderingOffStates[i] = false;
                originalShadowCastingModes[i] = ShadowCastingMode.On;
                continue;
            }

            originalEnabledStates[i] = renderer.enabled;
            originalForceRenderingOffStates[i] = renderer.forceRenderingOff;
            originalShadowCastingModes[i] = renderer.shadowCastingMode;
        }

        originalStatesCaptured = true;
    }

    private void RestoreOriginalStates()
    {
        if (!originalStatesCaptured || hideForLocalPlayer == null)
        {
            isLocalHidden = false;
            return;
        }

        for (int i = 0; i < hideForLocalPlayer.Length; i++)
        {
            Renderer renderer = hideForLocalPlayer[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.enabled = i < originalEnabledStates.Length ? originalEnabledStates[i] : true;
            renderer.forceRenderingOff = i < originalForceRenderingOffStates.Length && originalForceRenderingOffStates[i];
            renderer.shadowCastingMode = i < originalShadowCastingModes.Length ? originalShadowCastingModes[i] : ShadowCastingMode.On;
        }

        isLocalHidden = false;
    }
}
```

**Step 2: Verify the script compiles structurally**

Run via Unity MCP:

- `validate_script` on `Assets/Scripts/PhotonFusion/FusionLocalBodyVisibility.cs` with `level=standard`.

Expected: no C# syntax errors.

**Step 3: Refresh Unity scripts**

Run via Unity MCP:

- `refresh_unity(mode="if_dirty", scope="scripts", compile="request", wait_for_ready=true)`.

Expected: Unity finishes compiling.

**Step 4: Check console**

Run via Unity MCP:

- `read_console(action="get", types=["error"], count="20")`.

Expected: no new compile errors from `FusionLocalBodyVisibility`.

**Step 5: Commit**

```bash
git add -- "Assets/Scripts/PhotonFusion/FusionLocalBodyVisibility.cs"
git commit -m "Add Fusion local body visibility component"
```

---

### Task 2: Add diagnostics coverage for renderer hide and restore

**Objective:** Add a minimal EditMode test or diagnostic validation path for the new component's non-network renderer state behavior.

**Files:**
- Create or modify: an existing EditMode test file if present under `Assets/**/Tests/**/*.cs`
- If no suitable test folder exists, skip test file creation and use the diagnostic methods from Task 1 in an editor execution snippet.

**Step 1: Search existing test structure**

Use `glob`:

- `Assets/**/*Tests*.cs`
- `Assets/**/Tests/**/*.cs`

Expected: determine whether a conventional Unity test location exists.

**Step 2: If tests exist, add a small EditMode test**

Test behavior:

- Create a temporary `GameObject`.
- Add a child `MeshRenderer` or `SkinnedMeshRenderer` where possible.
- Add `FusionLocalBodyVisibility`.
- Use reflection or serialized object to assign `hideForLocalPlayer` to the renderer.
- Call `ApplyVisibilityForDiagnostics(true)`.
- Assert renderer is hidden.
- Call `ApplyVisibilityForDiagnostics(false)` or disable component.
- Assert original renderer state is restored.

Expected: test verifies hide/restore without requiring a live Fusion runner.

**Step 3: If no tests exist, run an editor diagnostics snippet instead**

Use Unity MCP `execute_code` with a temporary object and reflection to validate the diagnostic methods.

Expected output should include:

- `PASS local visibility hides renderer`
- `PASS local visibility restores renderer`

**Step 4: Commit test if a test file was added**

```bash
git add -- "Assets"
git commit -m "Add local body visibility diagnostics"
```

If no test file is added, do not create a commit for this task.

---

### Task 3: Wire the component onto `FusionPlayer.prefab`

**Objective:** Attach `FusionLocalBodyVisibility` to the player prefab and configure explicit renderer references.

**Files:**
- Modify: `Assets/Assets/Prefabs/FusionPlayer.prefab`

**Step 1: Confirm script import completed**

Use Unity MCP:

- `refresh_unity(mode="if_dirty", scope="all", compile="request", wait_for_ready=true)`.
- `read_console(action="get", types=["error"], count="20")`.

Expected: no compile errors, script type available to Unity.

**Step 2: Add component with Unity MCP**

Use Unity MCP prefab editing or editor code, whichever is safer for serialized references:

- Open or load `Assets/Assets/Prefabs/FusionPlayer.prefab`.
- Add `FusionLocalBodyVisibility` to root `FusionPlayer` if not already present.

Expected: root component list includes `FusionLocalBodyVisibility`.

**Step 3: Configure hidden renderer references**

Set `hideForLocalPlayer` to these renderer objects:

- `FusionPlayer/Player Prototype/Ch28_Hair`
- `FusionPlayer/Player Prototype/Ch28_Eyelashes`
- `FusionPlayer/Player Prototype/Ch28_Hoody`

Do not include these renderers:

- `FusionPlayer/Player Prototype/Ch28_Pants`
- `FusionPlayer/Player Prototype/Ch28_Sneakers`
- `FusionPlayer/Player Prototype/Ch28_Body` unless a visual test proves it does not remove required hands/hips.

Set booleans:

- `useStateAuthorityFallback = true`
- `disableRenderer = true`
- `forceRenderingOff = true`

**Step 4: Save prefab**

Use Unity MCP prefab stage save/close or an editor code save path.

Expected: prefab asset is modified only with the new component and references.

**Step 5: Inspect prefab hierarchy**

Use Unity MCP `manage_prefabs(action="get_hierarchy", prefab_path="Assets/Assets/Prefabs/FusionPlayer.prefab")`.

Expected: root `FusionPlayer` component list includes `FusionLocalBodyVisibility`.

**Step 6: Check git diff carefully**

Run:

```bash
git diff -- "Assets/Assets/Prefabs/FusionPlayer.prefab"
```

Expected: diff contains only the new component addition and serialized fields/references. No unrelated component deletion, no unexpected prefab rewrites.

**Step 7: Commit**

```bash
git add -- "Assets/Assets/Prefabs/FusionPlayer.prefab"
git commit -m "Wire local body visibility on Fusion player"
```

---

### Task 4: Verify local/remote behavior in Unity

**Objective:** Confirm the component behaves correctly in editor diagnostics and does not introduce console errors.

**Files:**
- No intended file changes.

**Step 1: Refresh and compile**

Use Unity MCP:

- `refresh_unity(mode="if_dirty", scope="all", compile="request", wait_for_ready=true)`.

Expected: editor ready.

**Step 2: Read errors**

Use Unity MCP:

- `read_console(action="get", types=["error"], count="50", include_stacktrace=true)`.

Expected: zero new errors related to `FusionLocalBodyVisibility`, prefab loading, or missing scripts.

**Step 3: Run component diagnostics on prefab**

Use Unity MCP editor code to load prefab contents temporarily and verify:

- Component exists.
- `hideForLocalPlayer` has `Ch28_Hair`, `Ch28_Eyelashes`, and `Ch28_Hoody`.
- `Ch28_Pants` and `Ch28_Sneakers` are not in the hide list.
- `Ch28_Body` is not in the hide list for the first pass.

Expected: all checks pass.

**Step 4: Manual play-mode visual validation**

Use the game flow if feasible:

- Start host/local player.
- Confirm first-person view no longer sees hair/face/hoodie/upper obstruction.
- Confirm hands/arms, legs, and hips remain visible as much as the current model supports.
- Start a second client or use existing multiplayer test flow if available.
- Confirm remote players still show full body.
- Confirm another client sees the local player's full model.

Expected: local-only hide is isolated to the owning player view.

**Step 5: Document any mesh limitation**

If upper torso is still visible because `Ch28_Body` is a combined mesh, report that explicitly and do not hide `Ch28_Body` unless the user approves losing hands/hips or adding a partial mesh fallback.

Expected: no broad visual regression.

---

### Task 5: Final verification and status

**Objective:** Prove the implementation is complete enough to hand back.

**Files:**
- No intended file changes unless Task 4 uncovered a safe correction.

**Step 1: Check worktree**

Run:

```bash
git status --short
```

Expected: no unintended untracked or modified files, except accepted Unity-generated metadata if any.

**Step 2: Check recent commits**

Run:

```bash
git log --oneline -5
```

Expected: includes the script commit and prefab wiring commit.

**Step 3: Read Unity console one last time**

Use Unity MCP:

- `read_console(action="get", types=["error"], count="50", include_stacktrace=true)`.

Expected: no new errors from this feature.

**Step 4: Summarize outcome**

Final response should include:

- Script path.
- Prefab path.
- Renderers hidden locally.
- What remains visible.
- Any limitation around `Ch28_Body` if observed.
- Verification performed.
