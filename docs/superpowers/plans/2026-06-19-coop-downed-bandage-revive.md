# Co-op Downed Bandage Revive Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a multiplayer downed-and-revive loop where zero health downs a player, a teammate revives them by holding input for five seconds, and revive consumes one crafted or picked-up bandage.

**Architecture:** Extend the existing survival/inventory/Fusion path instead of adding a parallel health system. `FusionPlayerSurvival` owns synced health/downed state, local helper components gate input while downed, and a focused revive interactor handles nearby target detection, hold progress, bandage consumption, and final revive requests.

**Tech Stack:** Unity C#, Photon Fusion Shared Mode, Unity UI, existing `PlayerSurvivalSystem`, `PlayerInventory`, `FusionPlayerSurvival`, `FusionPlayerInventory`, `FusionPlayerOwnerSetup`, `PickableItem`, and Unity MCP diagnostics.

---

## File Structure

- Modify: `Assets/Scripts/Object/Item/PickableItem.cs`
  - Add `Fiber`, `Cloth`, and `Bandage` item types.
- Modify: `Assets/Scripts/Player/Survival/PlayerSurvivalSystem.cs`
  - Expose `MaxHealth`, a `HasReachedZeroHealth`-style check through existing state, and a safe revive path already backed by `Revive(float healthPercent)`.
  - Keep zero-health handling centralized.
- Modify: `Assets/Scripts/PhotonFusion/FusionPlayerSurvival.cs`
  - Add synced `IsDowned` state.
  - Subscribe to local death/downed events on state authority.
  - Add a revive request RPC for Shared Mode.
- Modify: `Assets/Scripts/PhotonFusion/FusionPlayerMovement.cs`
  - Add a small local `ControlsBlocked` gate used by downed state.
- Modify: `Assets/Scripts/PhotonFusion/FusionPlayerCombat.cs`
  - Deny swing, tree hit, and player damage requests while downed.
- Modify: `Assets/Scripts/Player/Survival/PlayerInteractionSystem.cs`
  - Deny normal interact while downed.
- Modify: `Assets/Scripts/Player/Survival/HotbarConsumeUI.cs`
  - Deny consume while downed.
- Create: `Assets/Scripts/PhotonFusion/FusionPlayerDownedState.cs`
  - Local presentation/control gate for downed player state.
- Create: `Assets/Scripts/PhotonFusion/FusionPlayerReviveInteractor.cs`
  - Detect downed teammate, manage hold progress, consume bandage, and request revive.
- Create: `Assets/Scripts/Player/Survival/BandageCraftingSystem.cs`
  - Craft `2 Fiber + 1 Cloth -> 1 Bandage`.
- Create: `Assets/Scripts/UI/ReviveHoldButton.cs`
  - Mobile hold state using pointer down/up events.
- Create: `Assets/Scripts/UI/GameplayReviveHUD.cs`
  - Auto-created prompt, progress bar, and mobile revive hold button.
- Modify: `Assets/Assets/Prefabs/FusionPlayer.prefab`
  - Add `FusionPlayerDownedState`, `FusionPlayerReviveInteractor`, and `BandageCraftingSystem`.
- Modify: `Assets/Scenes/Gameplay.unity`
  - Add `GameplayReviveHUD` to `====Canvas====`.
  - Add simple scene pickups for `Fiber`, `Cloth`, and `Bandage`.

## Task 1: Item Types And Bandage Crafting Backend

**Files:**
- Modify: `Assets/Scripts/Object/Item/PickableItem.cs`
- Create: `Assets/Scripts/Player/Survival/BandageCraftingSystem.cs`
- Test/diagnostic: Unity MCP `execute_code` inventory crafting check.

- [ ] **Step 1: Update item enum**

Replace the enum in `Assets/Scripts/Object/Item/PickableItem.cs` with this complete enum:

```csharp
public enum ItemType
{
    Wood,
    Stone,
    Food,
    Axe,
    HealthConsumable,
    HungerConsumable,
    ThirstConsumable,
    Fiber,
    Cloth,
    Bandage
}
```

- [ ] **Step 2: Create `BandageCraftingSystem`**

Create `Assets/Scripts/Player/Survival/BandageCraftingSystem.cs`:

```csharp
using UnityEngine;

[DisallowMultipleComponent]
public class BandageCraftingSystem : MonoBehaviour
{
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private int fiberCost = 2;
    [SerializeField] private int clothCost = 1;
    [SerializeField] private int bandageOutput = 1;

    public int FiberCost => Mathf.Max(1, fiberCost);
    public int ClothCost => Mathf.Max(1, clothCost);
    public int BandageOutput => Mathf.Max(1, bandageOutput);

    private void Awake()
    {
        ResolveReferences();
    }

    public bool CanCraftBandage()
    {
        ResolveReferences();
        return inventory != null &&
            inventory.HasItem(ItemType.Fiber, FiberCost) &&
            inventory.HasItem(ItemType.Cloth, ClothCost);
    }

    public bool TryCraftBandage()
    {
        ResolveReferences();
        if (!CanCraftBandage())
        {
            ShowInfo("Need 2 Fiber + 1 Cloth");
            return false;
        }

        if (!inventory.RemoveItem(ItemType.Fiber, FiberCost))
        {
            ShowInfo("Need 2 Fiber");
            return false;
        }

        if (!inventory.RemoveItem(ItemType.Cloth, ClothCost))
        {
            inventory.AddItem(ItemType.Fiber, FiberCost);
            ShowInfo("Need 1 Cloth");
            return false;
        }

        int added = inventory.AddItem(ItemType.Bandage, BandageOutput);
        if (added < BandageOutput)
        {
            if (added > 0)
            {
                inventory.RemoveItem(ItemType.Bandage, added);
            }

            inventory.AddItem(ItemType.Fiber, FiberCost);
            inventory.AddItem(ItemType.Cloth, ClothCost);
            ShowInfo("Inventory Full");
            return false;
        }

        ShowInfo("Crafted Bandage");
        return true;
    }

    private void ResolveReferences()
    {
        if (inventory == null)
        {
            inventory = GetComponent<PlayerInventory>();
        }
    }

    private static void ShowInfo(string message)
    {
        if (PickupUIManager.instance != null)
        {
            PickupUIManager.instance.ShowInfo(message);
        }
    }
}
```

- [ ] **Step 3: Run Unity refresh and script validation**

Run via Unity MCP:

```text
refresh_unity(mode="if_dirty", scope="scripts", compile="request", wait_for_ready=true)
validate_script(uri="Assets/Scripts/Player/Survival/BandageCraftingSystem.cs", level="standard", include_diagnostics=true)
```

Expected: validation reports 0 errors.

- [ ] **Step 4: Run crafting diagnostic**

Run via Unity MCP `execute_code`:

```csharp
var go = new UnityEngine.GameObject("BandageCraftingDiagnostic");
var inventory = go.AddComponent<PlayerInventory>();
var crafting = go.AddComponent<BandageCraftingSystem>();

inventory.AddItem(ItemType.Fiber, 2);
inventory.AddItem(ItemType.Cloth, 1);

bool crafted = crafting.TryCraftBandage();
int fiber = inventory.GetAmount(ItemType.Fiber);
int cloth = inventory.GetAmount(ItemType.Cloth);
int bandage = inventory.GetAmount(ItemType.Bandage);
UnityEngine.Object.DestroyImmediate(go);

if (!crafted || fiber != 0 || cloth != 0 || bandage != 1)
{
    return $"FAIL crafted={crafted} fiber={fiber} cloth={cloth} bandage={bandage}";
}

return "PASS bandage crafting consumes 2 Fiber + 1 Cloth and creates 1 Bandage";
```

Expected: `PASS bandage crafting consumes 2 Fiber + 1 Cloth and creates 1 Bandage`.

- [ ] **Step 5: Commit**

```bash
git add -- Assets/Scripts/Object/Item/PickableItem.cs Assets/Scripts/Player/Survival/BandageCraftingSystem.cs
git commit -m "Add bandage crafting backend"
```

## Task 2: Survival Downed State Sync

**Files:**
- Modify: `Assets/Scripts/Player/Survival/PlayerSurvivalSystem.cs`
- Modify: `Assets/Scripts/PhotonFusion/FusionPlayerSurvival.cs`
- Test/diagnostic: Unity MCP lifecycle check.

- [ ] **Step 1: Add explicit health helpers to `PlayerSurvivalSystem`**

In `PlayerSurvivalSystem`, add this property near the existing `CurrentHealth` property:

```csharp
public float MaxHealth => maxHealth;
```

Keep the existing `IsDead`, `Died`, and `Revive(float healthPercent)` behavior unchanged.

- [ ] **Step 2: Extend `FusionPlayerSurvival` network state**

In `FusionPlayerSurvival`, add this networked property after `Injured`:

```csharp
[Networked] public NetworkBool IsDowned { get; private set; }
```

Add these fields near the existing private fields:

```csharp
private bool subscribedToDeath;
```

- [ ] **Step 3: Subscribe and unsubscribe local death event**

In `Spawned()`, after `ResolveReferences();`, add:

```csharp
SubscribeDeathEvent();
```

In `Despawned(...)`, before returning, add:

```csharp
UnsubscribeDeathEvent();
```

Add these methods to `FusionPlayerSurvival`:

```csharp
private void SubscribeDeathEvent()
{
    if (subscribedToDeath || survivalSystem == null)
    {
        return;
    }

    survivalSystem.Died += OnStateAuthoritySurvivalDied;
    subscribedToDeath = true;
}

private void UnsubscribeDeathEvent()
{
    if (!subscribedToDeath || survivalSystem == null)
    {
        subscribedToDeath = false;
        return;
    }

    survivalSystem.Died -= OnStateAuthoritySurvivalDied;
    subscribedToDeath = false;
}

private void OnStateAuthoritySurvivalDied()
{
    if (!HasFusionStateAuthority())
    {
        return;
    }

    IsDowned = true;
    QueueSnapshot(0f, survivalSystem.CurrentHunger, survivalSystem.CurrentThirst);
    TryFlushSnapshot(true);
}
```

- [ ] **Step 4: Update snapshot flush to write downed state**

In `TryFlushSnapshot(bool force)`, after `Injured = Health <= 35f;`, add:

```csharp
if (Health <= 0f)
{
    IsDowned = true;
}
```

- [ ] **Step 5: Add revive API to `FusionPlayerSurvival`**

Add this public method and RPC to `FusionPlayerSurvival`:

```csharp
public bool RequestReviveFrom(Vector3 reviverPosition, float reviveRange, float reviveHealthPercent)
{
    if (Runner == null || Object == null || !Object.IsValid)
    {
        return false;
    }

    RPC_RequestRevive(reviverPosition, reviveRange, reviveHealthPercent);
    return true;
}

[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
private void RPC_RequestRevive(Vector3 reviverPosition, float reviveRange, float reviveHealthPercent, RpcInfo info = default)
{
    ResolveReferences();
    if (survivalSystem == null || !IsDowned)
    {
        return;
    }

    float allowedRange = Mathf.Max(0.5f, reviveRange) + 0.5f;
    if ((transform.position - reviverPosition).sqrMagnitude > allowedRange * allowedRange)
    {
        return;
    }

    survivalSystem.Revive(reviveHealthPercent);
    IsDowned = false;
    QueueSnapshot(survivalSystem.CurrentHealth, survivalSystem.CurrentHunger, survivalSystem.CurrentThirst);
    TryFlushSnapshot(true);
}
```

This Shared Mode MVP trusts the reviver-side inventory consume and validates target state and revive distance on target state authority.

- [ ] **Step 6: Run Unity refresh and validation**

Run via Unity MCP:

```text
refresh_unity(mode="if_dirty", scope="scripts", compile="request", wait_for_ready=true)
validate_script(uri="Assets/Scripts/PhotonFusion/FusionPlayerSurvival.cs", level="standard", include_diagnostics=true)
validate_script(uri="Assets/Scripts/Player/Survival/PlayerSurvivalSystem.cs", level="standard", include_diagnostics=true)
```

Expected: 0 errors.

- [ ] **Step 7: Commit**

```bash
git add -- Assets/Scripts/Player/Survival/PlayerSurvivalSystem.cs Assets/Scripts/PhotonFusion/FusionPlayerSurvival.cs
git commit -m "Add Fusion downed state to survival sync"
```

## Task 3: Block Player Controls While Downed

**Files:**
- Modify: `Assets/Scripts/PhotonFusion/FusionPlayerMovement.cs`
- Modify: `Assets/Scripts/PhotonFusion/FusionPlayerCombat.cs`
- Modify: `Assets/Scripts/Player/Survival/PlayerInteractionSystem.cs`
- Modify: `Assets/Scripts/Player/Survival/HotbarConsumeUI.cs`
- Create: `Assets/Scripts/PhotonFusion/FusionPlayerDownedState.cs`

- [ ] **Step 1: Add movement block gate**

In `FusionPlayerMovement`, add this property near the public animator getters:

```csharp
public bool ControlsBlocked { get; set; }
```

At the start of `FixedUpdateNetwork()`, after the authority/controller null guard, add:

```csharp
if (ControlsBlocked)
{
    ClearAnimatorMovementState();
    return;
}
```

At the start of `Jump()`, add `ControlsBlocked` to the guard:

```csharp
if (ControlsBlocked || !HasFusionInputAuthority() || !enableJump || controller == null || !controller.isGrounded)
{
    return;
}
```

At the start of `Update()`, add:

```csharp
if (ControlsBlocked)
{
    accumulatedLookDelta = Vector2.zero;
    return;
}
```

- [ ] **Step 2: Gate combat requests**

In `FusionPlayerCombat`, add:

```csharp
private bool IsDowned()
{
    FusionPlayerSurvival survival = GetComponent<FusionPlayerSurvival>();
    return survival != null && survival.IsDowned;
}
```

Update `RequestSwing`, `RequestSceneTreeHit`, and `RequestPlayerDamage` guards to include `IsDowned()`:

```csharp
if (IsDowned() || !IsNetworkReady() || !HasFusionInputAuthority())
{
    return false;
}
```

For methods with damage arguments, keep the existing `damage <= 0f` check in the same guard.

- [ ] **Step 3: Gate interaction**

In `PlayerInteractionSystem`, add:

```csharp
private bool IsDowned()
{
    FusionPlayerSurvival survival = GetComponent<FusionPlayerSurvival>();
    return survival != null && survival.IsDowned;
}
```

At the top of `Update()`, after the authority guard, add:

```csharp
if (IsDowned())
{
    currentTarget = null;
    if (pickButton != null)
    {
        pickButton.SetActive(false);
    }
    return;
}
```

At the top of `TryInteract()`, add:

```csharp
if (IsDowned())
{
    return;
}
```

- [ ] **Step 4: Gate consumables**

In `HotbarConsumeUI`, add:

```csharp
private bool IsDowned()
{
    FusionPlayerSurvival survival = GetComponent<FusionPlayerSurvival>();
    return survival != null && survival.IsDowned;
}
```

At the top of `TryConsumeSelectedHotbarItem()`, after debounce is checked but before consuming, add:

```csharp
if (IsDowned())
{
    if (PickupUIManager.instance != null)
    {
        PickupUIManager.instance.ShowInfo("Cannot consume while downed");
    }
    return;
}
```

At the start of `CanConsumeSelectedItem()`, add:

```csharp
if (IsDowned())
{
    return false;
}
```

- [ ] **Step 5: Create local downed state component**

Create `Assets/Scripts/PhotonFusion/FusionPlayerDownedState.cs`:

```csharp
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class FusionPlayerDownedState : NetworkBehaviour
{
    [SerializeField] private FusionPlayerSurvival survival;
    [SerializeField] private FusionPlayerMovement movement;
    [SerializeField] private PlayerInteractionSystem interaction;
    [SerializeField] private HotbarConsumeUI consumeUI;
    [SerializeField] private PlayerAxeCombat axeCombat;

    private bool lastAppliedDowned;
    private bool hasApplied;

    public override void Spawned()
    {
        ResolveReferences();
        ApplyDownedState(IsDowned());
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        bool downed = IsDowned();
        if (!hasApplied || downed != lastAppliedDowned)
        {
            ApplyDownedState(downed);
        }
    }

    private bool IsDowned()
    {
        return survival != null && survival.IsDowned;
    }

    private void ApplyDownedState(bool downed)
    {
        hasApplied = true;
        lastAppliedDowned = downed;

        if (movement != null)
        {
            movement.ControlsBlocked = downed;
        }

        if (Object != null && Object.HasStateAuthority)
        {
            if (interaction != null)
            {
                interaction.enabled = !downed;
            }

            if (consumeUI != null)
            {
                consumeUI.enabled = !downed;
            }

            if (axeCombat != null)
            {
                axeCombat.enabled = !downed;
            }
        }
    }

    private void ResolveReferences()
    {
        if (survival == null)
        {
            survival = GetComponent<FusionPlayerSurvival>();
        }

        if (movement == null)
        {
            movement = GetComponent<FusionPlayerMovement>();
        }

        if (interaction == null)
        {
            interaction = GetComponent<PlayerInteractionSystem>();
        }

        if (consumeUI == null)
        {
            consumeUI = GetComponent<HotbarConsumeUI>();
        }

        if (axeCombat == null)
        {
            axeCombat = GetComponent<PlayerAxeCombat>();
        }
    }
}
```

- [ ] **Step 6: Validate scripts**

Run via Unity MCP:

```text
refresh_unity(mode="if_dirty", scope="scripts", compile="request", wait_for_ready=true)
validate_script(uri="Assets/Scripts/PhotonFusion/FusionPlayerDownedState.cs", level="standard", include_diagnostics=true)
validate_script(uri="Assets/Scripts/PhotonFusion/FusionPlayerMovement.cs", level="standard", include_diagnostics=true)
validate_script(uri="Assets/Scripts/PhotonFusion/FusionPlayerCombat.cs", level="standard", include_diagnostics=true)
validate_script(uri="Assets/Scripts/Player/Survival/PlayerInteractionSystem.cs", level="standard", include_diagnostics=true)
validate_script(uri="Assets/Scripts/Player/Survival/HotbarConsumeUI.cs", level="standard", include_diagnostics=true)
```

Expected: 0 errors.

- [ ] **Step 7: Commit**

```bash
git add -- Assets/Scripts/PhotonFusion/FusionPlayerDownedState.cs Assets/Scripts/PhotonFusion/FusionPlayerMovement.cs Assets/Scripts/PhotonFusion/FusionPlayerCombat.cs Assets/Scripts/Player/Survival/PlayerInteractionSystem.cs Assets/Scripts/Player/Survival/HotbarConsumeUI.cs
git commit -m "Block local player actions while downed"
```

## Task 4: Revive HUD And Hold Button

**Files:**
- Create: `Assets/Scripts/UI/ReviveHoldButton.cs`
- Create: `Assets/Scripts/UI/GameplayReviveHUD.cs`
- Modify: `Assets/Scenes/Gameplay.unity`

- [ ] **Step 1: Create mobile hold button state**

Create `Assets/Scripts/UI/ReviveHoldButton.cs`:

```csharp
using UnityEngine;
using UnityEngine.EventSystems;

public class ReviveHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public bool IsHeld { get; private set; }

    public void OnPointerDown(PointerEventData eventData)
    {
        IsHeld = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        IsHeld = false;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        IsHeld = false;
    }

    private void OnDisable()
    {
        IsHeld = false;
    }
}
```

- [ ] **Step 2: Create revive HUD**

Create `Assets/Scripts/UI/GameplayReviveHUD.cs`:

```csharp
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class GameplayReviveHUD : MonoBehaviour
{
    public static GameplayReviveHUD Instance { get; private set; }

    [SerializeField] private Text promptText;
    [SerializeField] private Slider progressSlider;
    [SerializeField] private GameObject reviveButtonObject;
    [SerializeField] private ReviveHoldButton reviveHoldButton;

    public bool IsMobileReviveHeld => reviveHoldButton != null && reviveHoldButton.IsHeld;

    private void Awake()
    {
        Instance = this;
        EnsureUI();
        Clear();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void ShowPrompt(string message, bool showButton)
    {
        EnsureUI();
        if (promptText != null)
        {
            promptText.gameObject.SetActive(!string.IsNullOrWhiteSpace(message));
            promptText.text = message;
        }

        if (reviveButtonObject != null)
        {
            reviveButtonObject.SetActive(showButton);
        }
    }

    public void SetProgress(float normalizedProgress)
    {
        EnsureUI();
        if (progressSlider != null)
        {
            float value = Mathf.Clamp01(normalizedProgress);
            progressSlider.gameObject.SetActive(value > 0f && value < 1f);
            progressSlider.value = value;
        }
    }

    public void Clear()
    {
        if (promptText != null)
        {
            promptText.text = string.Empty;
            promptText.gameObject.SetActive(false);
        }

        if (progressSlider != null)
        {
            progressSlider.value = 0f;
            progressSlider.gameObject.SetActive(false);
        }

        if (reviveButtonObject != null)
        {
            reviveButtonObject.SetActive(false);
        }
    }

    private void EnsureUI()
    {
        if (promptText == null)
        {
            GameObject prompt = new GameObject("Revive Prompt", typeof(RectTransform), typeof(Text));
            RectTransform rect = prompt.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -130f);
            rect.sizeDelta = new Vector2(520f, 44f);

            promptText = prompt.GetComponent<Text>();
            promptText.alignment = TextAnchor.MiddleCenter;
            promptText.fontSize = 22;
            promptText.color = Color.white;
        }

        if (progressSlider == null)
        {
            GameObject progress = new GameObject("Revive Progress", typeof(RectTransform), typeof(Image), typeof(Slider));
            RectTransform rect = progress.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -170f);
            rect.sizeDelta = new Vector2(360f, 26f);

            Image bg = progress.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);

            GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
            RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
            fillAreaRect.SetParent(rect, false);
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.offsetMin = new Vector2(4f, 4f);
            fillAreaRect.offsetMax = new Vector2(-4f, -4f);

            GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            fillRect.SetParent(fillAreaRect, false);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            fill.GetComponent<Image>().color = new Color(0.35f, 1f, 0.45f, 1f);

            progressSlider = progress.GetComponent<Slider>();
            progressSlider.minValue = 0f;
            progressSlider.maxValue = 1f;
            progressSlider.value = 0f;
            progressSlider.fillRect = fillRect;
            progressSlider.direction = Slider.Direction.LeftToRight;
        }

        if (reviveButtonObject == null)
        {
            GameObject button = new GameObject("Revive Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(ReviveHoldButton));
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-56f, 132f);
            rect.sizeDelta = new Vector2(150f, 58f);
            button.GetComponent<Image>().color = new Color(0.1f, 0.55f, 0.22f, 0.88f);

            GameObject label = new GameObject("Label", typeof(RectTransform), typeof(Text));
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            Text labelText = label.GetComponent<Text>();
            labelText.text = "REVIVE";
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.fontSize = 20;
            labelText.color = Color.white;

            reviveButtonObject = button;
            reviveHoldButton = button.GetComponent<ReviveHoldButton>();
        }
    }
}
```

- [ ] **Step 3: Attach HUD to gameplay canvas**

Run via Unity MCP:

```text
manage_components(action="add", target="====Canvas====", search_method="by_name", component_type="GameplayReviveHUD")
manage_scene(action="save", scene_name="Gameplay")
```

Expected: `====Canvas====` has `GameplayReviveHUD`.

- [ ] **Step 4: Validate scripts and scene**

Run via Unity MCP:

```text
refresh_unity(mode="if_dirty", scope="scripts", compile="request", wait_for_ready=true)
validate_script(uri="Assets/Scripts/UI/ReviveHoldButton.cs", level="standard", include_diagnostics=true)
validate_script(uri="Assets/Scripts/UI/GameplayReviveHUD.cs", level="standard", include_diagnostics=true)
read_console(action="get", types=["error"], count=20)
```

Expected: script validation 0 errors. Console may show stale editor inspector serialization exceptions; no compile errors should appear.

- [ ] **Step 5: Commit**

```bash
git add -- Assets/Scripts/UI/ReviveHoldButton.cs Assets/Scripts/UI/GameplayReviveHUD.cs Assets/Scenes/Gameplay.unity
git commit -m "Add revive gameplay HUD"
```

## Task 5: Revive Interactor

**Files:**
- Create: `Assets/Scripts/PhotonFusion/FusionPlayerReviveInteractor.cs`
- Modify: `Assets/Assets/Prefabs/FusionPlayer.prefab`

- [ ] **Step 1: Create revive interactor**

Create `Assets/Scripts/PhotonFusion/FusionPlayerReviveInteractor.cs`:

```csharp
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class FusionPlayerReviveInteractor : NetworkBehaviour
{
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private FusionPlayerSurvival selfSurvival;
    [SerializeField] private float reviveRange = 2.2f;
    [SerializeField] private float reviveDurationSeconds = 5f;
    [SerializeField, Range(0.01f, 1f)] private float reviveHealthPercent = 0.25f;
    [SerializeField] private KeyCode keyboardReviveKey = KeyCode.E;

    private FusionPlayerSurvival currentTarget;
    private float reviveProgressSeconds;

    public override void Spawned()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        if (!HasLocalAuthority())
        {
            return;
        }

        ResolveReferences();
        currentTarget = FindBestDownedTarget();
        if (currentTarget == null)
        {
            ResetReviveUI();
            return;
        }

        bool hasBandage = inventory != null && inventory.HasItem(ItemType.Bandage, 1);
        GameplayReviveHUD hud = GameplayReviveHUD.Instance;
        if (hud != null)
        {
            hud.ShowPrompt(hasBandage ? "Hold Interact to Revive (Bandage x1)" : "Need Bandage to Revive", hasBandage);
        }

        bool holding = hasBandage && (Input.GetKey(keyboardReviveKey) || (hud != null && hud.IsMobileReviveHeld));
        if (!holding)
        {
            reviveProgressSeconds = 0f;
            if (hud != null)
            {
                hud.SetProgress(0f);
            }
            return;
        }

        reviveProgressSeconds += Time.deltaTime;
        if (hud != null)
        {
            hud.SetProgress(reviveProgressSeconds / Mathf.Max(0.1f, reviveDurationSeconds));
        }

        if (reviveProgressSeconds >= reviveDurationSeconds)
        {
            CompleteRevive();
        }
    }

    private void CompleteRevive()
    {
        if (currentTarget == null || inventory == null || selfSurvival == null || selfSurvival.IsDowned)
        {
            ResetReviveUI();
            return;
        }

        if ((currentTarget.transform.position - transform.position).sqrMagnitude > reviveRange * reviveRange)
        {
            ResetReviveUI();
            return;
        }

        if (!inventory.RemoveItem(ItemType.Bandage, 1))
        {
            ResetReviveUI();
            return;
        }

        bool requested = currentTarget.RequestReviveFrom(transform.position, reviveRange, reviveHealthPercent);
        if (!requested)
        {
            inventory.AddItem(ItemType.Bandage, 1);
        }

        ResetReviveUI();
    }

    private FusionPlayerSurvival FindBestDownedTarget()
    {
        FusionPlayerSurvival[] survivals = FindObjectsOfType<FusionPlayerSurvival>(true);
        if (survivals == null || survivals.Length == 0)
        {
            return null;
        }

        FusionPlayerSurvival best = null;
        float bestSqr = reviveRange * reviveRange;
        for (int i = 0; i < survivals.Length; i++)
        {
            FusionPlayerSurvival candidate = survivals[i];
            if (candidate == null || candidate == selfSurvival || !candidate.IsDowned)
            {
                continue;
            }

            float sqr = (candidate.transform.position - transform.position).sqrMagnitude;
            if (sqr <= bestSqr)
            {
                bestSqr = sqr;
                best = candidate;
            }
        }

        return best;
    }

    private void ResetReviveUI()
    {
        reviveProgressSeconds = 0f;
        currentTarget = null;
        GameplayReviveHUD hud = GameplayReviveHUD.Instance;
        if (hud != null)
        {
            hud.Clear();
        }
    }

    private void ResolveReferences()
    {
        if (inventory == null)
        {
            inventory = GetComponent<PlayerInventory>();
        }

        if (selfSurvival == null)
        {
            selfSurvival = GetComponent<FusionPlayerSurvival>();
        }
    }

    private bool HasLocalAuthority()
    {
        return Object != null && Object.HasStateAuthority;
    }
}
```

- [ ] **Step 2: Validate revive interactor**

Run via Unity MCP:

```text
refresh_unity(mode="if_dirty", scope="scripts", compile="request", wait_for_ready=true)
validate_script(uri="Assets/Scripts/PhotonFusion/FusionPlayerReviveInteractor.cs", level="standard", include_diagnostics=true)
```

Expected: 0 errors.

- [ ] **Step 3: Add components to player prefab**

Run via Unity MCP:

```text
manage_prefabs(action="open_prefab_stage", prefab_path="Assets/Assets/Prefabs/FusionPlayer.prefab")
find_gameobjects(search_method="by_name", search_term="FusionPlayer", include_inactive=true)
manage_components(action="add", target=<FusionPlayer instance id>, search_method="by_id", component_type="FusionPlayerDownedState")
manage_components(action="add", target=<FusionPlayer instance id>, search_method="by_id", component_type="FusionPlayerReviveInteractor")
manage_components(action="add", target=<FusionPlayer instance id>, search_method="by_id", component_type="BandageCraftingSystem")
manage_prefabs(action="save_prefab_stage")
manage_prefabs(action="close_prefab_stage")
```

Expected: prefab root has `FusionPlayerDownedState`, `FusionPlayerReviveInteractor`, and `BandageCraftingSystem`.

- [ ] **Step 4: Run prefab diagnostic**

Run via Unity MCP `execute_code`:

```csharp
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Assets/Prefabs/FusionPlayer.prefab");
if (prefab == null) return "FAIL missing FusionPlayer prefab";
if (prefab.GetComponent<FusionPlayerDownedState>() == null) return "FAIL missing FusionPlayerDownedState";
if (prefab.GetComponent<FusionPlayerReviveInteractor>() == null) return "FAIL missing FusionPlayerReviveInteractor";
if (prefab.GetComponent<BandageCraftingSystem>() == null) return "FAIL missing BandageCraftingSystem";
return "PASS FusionPlayer revive components present";
```

Expected: `PASS FusionPlayer revive components present`.

- [ ] **Step 5: Commit**

```bash
git add -- Assets/Scripts/PhotonFusion/FusionPlayerReviveInteractor.cs Assets/Assets/Prefabs/FusionPlayer.prefab
git commit -m "Add Fusion revive interactor to player prefab"
```

## Task 6: Scene Pickups For Fiber, Cloth, And Bandage

**Files:**
- Modify: `Assets/Scenes/Gameplay.unity`

- [ ] **Step 1: Create simple MVP pickups**

Use Unity MCP to create three simple pickup objects near the existing spawn area. Each object must have `MeshRenderer`, `MeshFilter`, `BoxCollider`, `Rigidbody`, `PickableItem`, and `Interactable` so the existing pickup flow can detect them.

Run via Unity MCP batch operations:

```text
manage_gameobject(action="create", name="Fiber Pickup", primitive_type="Cube", position=[-2.6,0.35,-5.8], scale=[0.35,0.12,0.35], components_to_add=["BoxCollider","Rigidbody","PickableItem","Interactable"])
manage_components(action="set_property", target="Fiber Pickup", search_method="by_name", component_type="PickableItem", properties={"itemType":7,"itemName":"Fiber","amount":4})
manage_gameobject(action="create", name="Cloth Pickup", primitive_type="Cube", position=[-2.1,0.35,-5.8], scale=[0.35,0.12,0.35], components_to_add=["BoxCollider","Rigidbody","PickableItem","Interactable"])
manage_components(action="set_property", target="Cloth Pickup", search_method="by_name", component_type="PickableItem", properties={"itemType":8,"itemName":"Cloth","amount":2})
manage_gameobject(action="create", name="Bandage Pickup", primitive_type="Cube", position=[-1.6,0.35,-5.8], scale=[0.35,0.12,0.35], components_to_add=["BoxCollider","Rigidbody","PickableItem","Interactable"])
manage_components(action="set_property", target="Bandage Pickup", search_method="by_name", component_type="PickableItem", properties={"itemType":9,"itemName":"Bandage","amount":1})
manage_scene(action="save", scene_name="Gameplay")
```

The enum indices are based on `Fiber=7`, `Cloth=8`, `Bandage=9` after Task 1.

- [ ] **Step 2: Verify pickups exist**

Run via Unity MCP:

```text
find_gameobjects(search_method="by_name", search_term="Fiber Pickup", include_inactive=true)
find_gameobjects(search_method="by_name", search_term="Cloth Pickup", include_inactive=true)
find_gameobjects(search_method="by_name", search_term="Bandage Pickup", include_inactive=true)
```

Expected: one object found for each pickup.

- [ ] **Step 3: Commit**

```bash
git add -- Assets/Scenes/Gameplay.unity
git commit -m "Add bandage resource pickups to gameplay scene"
```

## Task 7: Integration Verification

**Files:**
- No intended source changes unless verification finds a defect.

- [ ] **Step 1: Run script compile and console check**

Run via Unity MCP:

```text
refresh_unity(mode="if_dirty", scope="scripts", compile="request", wait_for_ready=true)
read_console(action="get", types=["error"], count=30, include_stacktrace=true, format="detailed")
```

Expected: no C# compile errors. If stale Unity inspector `SerializedObjectNotCreatableException` entries appear without script compile errors, note them separately and continue.

- [ ] **Step 2: Run prefab and scene diagnostics**

Run via Unity MCP `execute_code`:

```csharp
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Assets/Prefabs/FusionPlayer.prefab");
if (prefab == null) return "FAIL missing FusionPlayer prefab";
if (prefab.GetComponent<FusionPlayerSurvival>() == null) return "FAIL missing FusionPlayerSurvival";
if (prefab.GetComponent<FusionPlayerDownedState>() == null) return "FAIL missing FusionPlayerDownedState";
if (prefab.GetComponent<FusionPlayerReviveInteractor>() == null) return "FAIL missing FusionPlayerReviveInteractor";
if (prefab.GetComponent<BandageCraftingSystem>() == null) return "FAIL missing BandageCraftingSystem";

var canvas = UnityEngine.GameObject.Find("====Canvas====");
if (canvas == null) return "FAIL missing gameplay canvas";
if (canvas.GetComponent<GameplayReviveHUD>() == null) return "FAIL missing GameplayReviveHUD";

string[] pickupNames = { "Fiber Pickup", "Cloth Pickup", "Bandage Pickup" };
foreach (string pickupName in pickupNames)
{
    var pickup = UnityEngine.GameObject.Find(pickupName);
    if (pickup == null) return "FAIL missing " + pickupName;
    if (pickup.GetComponent<PickableItem>() == null) return "FAIL missing PickableItem on " + pickupName;
    if (pickup.GetComponent<Interactable>() == null) return "FAIL missing Interactable on " + pickupName;
}

return "PASS revive prefab, HUD, and pickups are wired";
```

Expected: `PASS revive prefab, HUD, and pickups are wired`.

- [ ] **Step 3: Manual multiplayer QA**

Run the game with two clients:

1. Host creates room.
2. Client joins room.
3. Confirm each player spawns at correct spawn point and ping still shows non-zero ms after network settles.
4. Pick up Fiber and Cloth, craft Bandage using `BandageCraftingSystem.TryCraftBandage()` from a temporary UI hook or inspector/debug call.
5. Damage one player until health reaches zero.
6. Confirm downed player cannot move, interact, attack, consume, jump, or revive.
7. Move teammate into range with at least one Bandage.
8. Confirm prompt shows `Hold Interact to Revive (Bandage x1)`.
9. Hold `E` for five seconds.
10. Confirm revive consumes exactly one Bandage and target returns to 25% health.
11. Repeat with no Bandage and confirm `Need Bandage to Revive` appears and revive does not complete.

- [ ] **Step 4: Commit verification fixes or add final marker commit**

If verification required code fixes, commit those files with a targeted message. If no files changed, do not create an empty commit.

Suggested command for fixes:

```bash
git status --short
git add -- <changed-files>
git commit -m "Fix revive integration issues"
```

## Self-Review

- Spec coverage: The plan covers downed state, no bleed-out timer, bandage requirement, five-second hold, 25% revive health, pickup/crafting sources, prompt/button/progress UI, Fusion state sync, and validation/QA.
- Placeholder scan: No placeholder markers or unspecified implementation steps remain.
- Type consistency: New item types are `Fiber`, `Cloth`, `Bandage`; synced state is `FusionPlayerSurvival.IsDowned`; revive interaction uses `FusionPlayerReviveInteractor`; UI entry is `GameplayReviveHUD`; mobile hold state is `ReviveHoldButton`.
