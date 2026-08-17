# Forest Mobile Optimization Implementation Plan

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Apply a fixed mobile-safe optimization profile to the forest scene without adding a graphics settings panel.

**Architecture:** Use editor-side scene changes for Terrain/camera/static flags and add one editor self-test to prevent regressions. No runtime quality switching is introduced.

**Tech Stack:** Unity 2022.3, Terrain, Unity Occlusion Culling static flags, UnityMCP verification.

---

### Task 1: Add Forest Mobile Optimization Self-Test

**Objective:** Create a menu-driven editor self-test that validates mobile-safe forest settings.

**Files:**
- Create: `Assets/Editor/ForestMobileOptimizationSelfTest.cs`

**Steps:**
1. Add a static editor test with menu path `Project Multiplayer/Run Forest Mobile Optimization Self Test`.
2. Load/inspect `Assets/Scenes/Environment.unity`.
3. Fail if any Terrain has `treeDistance > 350`.
4. Fail if any active Camera has `useOcclusionCulling == false` or `farClipPlane > 600`.
5. Log `ForestMobileOptimizationSelfTest passed.` on success.

**Verification:**
- Run the menu item through UnityMCP.
- Expected first run before settings may fail if tree distance is still `5000`.

### Task 2: Apply Mobile-Safe Forest Scene Settings

**Objective:** Reduce forest rendering range and keep camera occlusion enabled.

**Files:**
- Modify: `Assets/Scenes/Environment.unity`

**Steps:**
1. Set every Terrain `treeDistance` to `250`.
2. Set every Terrain `detailObjectDistance` to `60`.
3. Set every Terrain `heightmapPixelError` to `8`.
4. Set active Camera `useOcclusionCulling = true`.
5. Set active Camera `farClipPlane = 500`.
6. Mark existing solid MeshRenderer objects as `Occludee Static`; mark large solid ones as `Occluder Static` only if bounds are meaningful.
7. Save `Environment.unity`.

**Verification:**
- Use UnityMCP execute code to print Terrain/camera values.
- Run the self-test.

### Task 3: Final Verification

**Objective:** Confirm compile, scene settings, and self-test pass.

**Files:**
- Read only after implementation.

**Steps:**
1. Refresh Unity and wait for readiness.
2. Check Unity console for errors.
3. Run `Project Multiplayer/Run Forest Mobile Optimization Self Test`.
4. Read console and confirm pass log.
5. Inspect `git status --short` and summarize intended/unrelated changes.
