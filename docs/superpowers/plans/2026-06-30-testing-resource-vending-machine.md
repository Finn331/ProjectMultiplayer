# Testing Resource Vending Machine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a scene testing helper named `vending_food` that opens a mobile-friendly panel and dispenses Wood, Fiber, Stone, or Cloth x5 into the interacting player's inventory.

**Architecture:** Add one focused runtime script that integrates with the existing `Interactable` and `PlayerInteractionSystem` flow. The script builds a local-only UI panel, tracks the current interactor, and calls `PlayerInventory.AddItem(ItemType, 5)` without network spawning or Fusion prefab registration.

**Tech Stack:** Unity 2022.3, C#, UGUI, existing `PlayerInventory`, existing `Interactable`, Unity MCP validation.

---

## File Structure

- Create: `Assets/Scripts/Testing/TestingResourceVendingMachine.cs`
  - Responsibility: open/close vending UI and dispense configured resource buttons into the current player's inventory.
- Modify: `Assets/Scenes/Gameplay.unity`
  - Responsibility: add or update exactly one scene GameObject named `vending_food` with a simple visual, collider, `Interactable`, and `TestingResourceVendingMachine`.
- Do not modify: Fusion prefab tables.
  - Reason: this helper adds directly to local player inventory and does not spawn networked world objects.

## Task 1: Add TestingResourceVendingMachine Script

**Files:**
- Create: `Assets/Scripts/Testing/TestingResourceVendingMachine.cs`
- Create by Unity: `Assets/Scripts/Testing/TestingResourceVendingMachine.cs.meta`

- [ ] **Step 1: Confirm target APIs**

Run:

```powershell
rg -n "public int AddItem\(ItemType itemType, int amount\)|public void ShowInfo|public class Interactable" "Assets/Scripts"
```

Expected:

```text
Assets/Scripts/Player/Survival/PlayerInventory.cs:92:    public int AddItem(ItemType itemType, int amount)
Assets/Scripts/Object/Manager/PickupUIManager.cs:31:    public void ShowInfo(string message)
Assets/Scripts/Interactable.cs:6:public class Interactable : MonoBehaviour
```

- [ ] **Step 2: Create script folder**

Use a filesystem or Unity asset operation to ensure this folder exists:

```text
Assets/Scripts/Testing
```

- [ ] **Step 3: Write the script**

Create `Assets/Scripts/Testing/TestingResourceVendingMachine.cs` with this complete content:

```csharp
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class TestingResourceVendingMachine : MonoBehaviour
{
    private const int DispenseAmount = 5;

    [SerializeField] private string panelTitle = "Testing Resources";
    [SerializeField] private Vector2 panelSize = new Vector2(360f, 420f);

    private PlayerInventory currentInventory;
    private Canvas vendingCanvas;
    private GameObject panelObject;

    public void OpenForInteractor(PlayerInteractionSystem interactor)
    {
        if (interactor == null)
        {
            ShowInfo("No player selected");
            return;
        }

        currentInventory = interactor.GetComponent<PlayerInventory>();
        if (currentInventory == null)
        {
            ShowInfo("No player inventory found");
            return;
        }

        EnsureUI();
        panelObject.SetActive(true);
    }

    public void Close()
    {
        if (panelObject != null)
        {
            panelObject.SetActive(false);
        }
    }

    private void DispenseWood()
    {
        Dispense(ItemType.Wood, "Wood");
    }

    private void DispenseFiber()
    {
        Dispense(ItemType.Fiber, "Fiber");
    }

    private void DispenseStone()
    {
        Dispense(ItemType.Stone, "Stone");
    }

    private void DispenseCloth()
    {
        Dispense(ItemType.Cloth, "Cloth");
    }

    private void Dispense(ItemType itemType, string label)
    {
        if (currentInventory == null)
        {
            ShowInfo("Open vending first");
            return;
        }

        int accepted = currentInventory.AddItem(itemType, DispenseAmount);
        if (accepted > 0)
        {
            ShowInfo(label + " +" + accepted);
        }

        if (accepted < DispenseAmount)
        {
            ShowInfo("Inventory Full");
        }
    }

    private void EnsureUI()
    {
        if (panelObject != null)
        {
            return;
        }

        vendingCanvas = FindFirstObjectByType<Canvas>();
        if (vendingCanvas == null)
        {
            GameObject canvasObject = new GameObject("Testing Resource Vending Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            vendingCanvas = canvasObject.GetComponent<Canvas>();
            vendingCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
        }

        panelObject = new GameObject("Testing Resource Vending Panel", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(vendingCanvas.transform, false);

        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = panelSize;
        panelRect.anchoredPosition = Vector2.zero;

        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = new Color(0.08f, 0.08f, 0.08f, 0.92f);

        CreateText(panelObject.transform, panelTitle, new Vector2(0f, 160f), 30, TextAnchor.MiddleCenter);
        CreateButton(panelObject.transform, "WOOD x5", new Vector2(0f, 90f), DispenseWood);
        CreateButton(panelObject.transform, "FIBER x5", new Vector2(0f, 30f), DispenseFiber);
        CreateButton(panelObject.transform, "STONE x5", new Vector2(0f, -30f), DispenseStone);
        CreateButton(panelObject.transform, "CLOTH x5", new Vector2(0f, -90f), DispenseCloth);
        CreateButton(panelObject.transform, "CLOSE", new Vector2(0f, -160f), Close);

        panelObject.SetActive(false);
    }

    private static void CreateButton(Transform parent, string label, Vector2 anchoredPosition, UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = new GameObject(label + " Button", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(260f, 48f);
        rect.anchoredPosition = anchoredPosition;

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.22f, 0.24f, 0.26f, 0.96f);

        Button button = buttonObject.GetComponent<Button>();
        button.onClick.AddListener(action);

        CreateText(buttonObject.transform, label, Vector2.zero, 22, TextAnchor.MiddleCenter);
    }

    private static Text CreateText(Transform parent, string text, Vector2 anchoredPosition, int fontSize, TextAnchor alignment)
    {
        GameObject textObject = new GameObject(text + " Text", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(320f, 44f);
        rect.anchoredPosition = anchoredPosition;

        Text uiText = textObject.GetComponent<Text>();
        uiText.text = text;
        uiText.alignment = alignment;
        uiText.fontSize = fontSize;
        uiText.color = Color.white;
        uiText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return uiText;
    }

    private static void ShowInfo(string message)
    {
        if (PickupUIManager.instance != null)
        {
            PickupUIManager.instance.ShowInfo(message);
        }
        else
        {
            Debug.Log(message);
        }
    }
}
```

- [ ] **Step 4: Compile-check script**

Use Unity MCP:

```text
refresh_unity(mode="if_dirty", scope="scripts", compile="request", wait_for_ready=true)
read_console(action="get", types=["error"], count="20", include_stacktrace=true)
```

Expected: no new compiler errors mentioning `TestingResourceVendingMachine`.

- [ ] **Step 5: Runtime diagnostic for dispense logic**

Use Unity MCP `execute_code(action="execute", safety_checks=true)` with this code:

```csharp
var player = new GameObject("VendingDiagnosticPlayer");
var inventory = player.AddComponent<PlayerInventory>();
var interactor = player.AddComponent<PlayerInteractionSystem>();
var vending = new GameObject("VendingDiagnosticMachine").AddComponent<TestingResourceVendingMachine>();

vending.OpenForInteractor(interactor);

var method = typeof(TestingResourceVendingMachine).GetMethod("DispenseWood", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
method.Invoke(vending, null);

int woodAmount = inventory.GetAmount(ItemType.Wood);
Object.DestroyImmediate(vending.gameObject);
Object.DestroyImmediate(player);

return woodAmount == 5 ? "PASS: Vending dispensed Wood x5." : "FAIL: Wood amount was " + woodAmount;
```

Expected:

```text
PASS: Vending dispensed Wood x5.
```

- [ ] **Step 6: Commit**

Run:

```powershell
git status --short
git add "Assets/Scripts/Testing/TestingResourceVendingMachine.cs" "Assets/Scripts/Testing/TestingResourceVendingMachine.cs.meta"
git commit -m "Add testing resource vending machine script"
```

Expected: commit includes only the new script and meta file.

## Task 2: Add vending_food To Gameplay Scene

**Files:**
- Modify: `Assets/Scenes/Gameplay.unity`

- [ ] **Step 1: Inspect current scene for duplicates**

Run:

```powershell
rg -n "m_Name: vending_food|vending_food" "Assets/Scenes/Gameplay.unity"
```

Expected before this task: no matches, or one existing `vending_food` that will be updated instead of duplicated.

- [ ] **Step 2: Create or update scene GameObject with Unity MCP**

Use Unity MCP `execute_code(action="execute", safety_checks=true)` with this code:

```csharp
using UnityEditor;
using UnityEngine;

GameObject vending = GameObject.Find("vending_food");
if (vending == null)
{
    vending = GameObject.CreatePrimitive(PrimitiveType.Cube);
    vending.name = "vending_food";
}

vending.transform.position = new Vector3(2f, 1f, 2f);
vending.transform.rotation = Quaternion.identity;
vending.transform.localScale = new Vector3(1.1f, 2f, 0.55f);

int interactableLayer = LayerMask.NameToLayer("Interactable");
if (interactableLayer < 0)
{
    interactableLayer = LayerMask.NameToLayer("Item");
}

if (interactableLayer >= 0)
{
    vending.layer = interactableLayer;
}

BoxCollider collider = vending.GetComponent<BoxCollider>();
if (collider == null)
{
    collider = vending.AddComponent<BoxCollider>();
}
collider.isTrigger = false;
collider.center = Vector3.zero;
collider.size = Vector3.one;

var interactable = vending.GetComponent<Interactable>();
if (interactable == null)
{
    interactable = vending.AddComponent<Interactable>();
}

var vendingScript = vending.GetComponent<TestingResourceVendingMachine>();
if (vendingScript == null)
{
    vendingScript = vending.AddComponent<TestingResourceVendingMachine>();
}

interactable.onInteraction.RemoveAllListeners();
interactable.onInteraction.AddListener(() =>
{
    PlayerInteractionSystem[] interactors = Object.FindObjectsByType<PlayerInteractionSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
    PlayerInteractionSystem nearest = null;
    float nearestSqr = 9f;
    for (int i = 0; i < interactors.Length; i++)
    {
        PlayerInteractionSystem candidate = interactors[i];
        if (candidate == null)
        {
            continue;
        }

        float distanceSqr = (candidate.transform.position - vending.transform.position).sqrMagnitude;
        if (distanceSqr <= nearestSqr)
        {
            nearestSqr = distanceSqr;
            nearest = candidate;
        }
    }

    vendingScript.OpenForInteractor(nearest);
});

Renderer renderer = vending.GetComponent<Renderer>();
if (renderer != null)
{
    renderer.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
}

EditorUtility.SetDirty(vending);
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(vending.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(vending.scene);

return "PASS: vending_food scene object created or updated.";
```

Expected:

```text
PASS: vending_food scene object created or updated.
```

- [ ] **Step 3: Verify scene object components**

Use Unity MCP `execute_code(action="execute", safety_checks=true)` with this code:

```csharp
GameObject vending = GameObject.Find("vending_food");
if (vending == null) return "FAIL: vending_food missing.";

bool hasCollider = vending.GetComponent<BoxCollider>() != null;
bool hasInteractable = vending.GetComponent<Interactable>() != null;
bool hasVending = vending.GetComponent<TestingResourceVendingMachine>() != null;

return hasCollider && hasInteractable && hasVending
    ? "PASS: vending_food has BoxCollider, Interactable, and TestingResourceVendingMachine."
    : $"FAIL: collider={hasCollider}, interactable={hasInteractable}, vending={hasVending}";
```

Expected:

```text
PASS: vending_food has BoxCollider, Interactable, and TestingResourceVendingMachine.
```

- [ ] **Step 4: Compile and console check**

Use Unity MCP:

```text
refresh_unity(mode="if_dirty", scope="all", compile="request", wait_for_ready=true)
read_console(action="get", types=["error"], count="30", include_stacktrace=true)
```

Expected: no new Unity console errors caused by `vending_food` or `TestingResourceVendingMachine`.

- [ ] **Step 5: Commit scene change only**

Run:

```powershell
git status --short
git add "Assets/Scenes/Gameplay.unity"
git commit -m "Add testing resource vending machine to gameplay scene"
```

Expected: commit includes only `Assets/Scenes/Gameplay.unity`. Do not stage unrelated `MainMenu.unity` or new asset folders.

## Task 3: Final Validation

**Files:**
- No intended source edits.

- [ ] **Step 1: Clear console and refresh all assets**

Use Unity MCP:

```text
read_console(action="clear")
refresh_unity(mode="force", scope="all", compile="request", wait_for_ready=true)
read_console(action="get", types=["error"], count="50", include_stacktrace=true)
```

Expected: no new errors.

- [ ] **Step 2: Verify exactly one vending_food object**

Use Unity MCP `execute_code(action="execute", safety_checks=true)` with this code:

```csharp
GameObject[] all = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
int count = 0;
GameObject vending = null;
for (int i = 0; i < all.Length; i++)
{
    if (all[i] != null && all[i].name == "vending_food")
    {
        count++;
        vending = all[i];
    }
}

if (count != 1) return "FAIL: vending_food count is " + count;

bool hasCollider = vending.GetComponent<BoxCollider>() != null;
bool hasInteractable = vending.GetComponent<Interactable>() != null;
bool hasVending = vending.GetComponent<TestingResourceVendingMachine>() != null;
return hasCollider && hasInteractable && hasVending
    ? "PASS: exactly one vending_food with required components."
    : $"FAIL: collider={hasCollider}, interactable={hasInteractable}, vending={hasVending}";
```

Expected:

```text
PASS: exactly one vending_food with required components.
```

- [ ] **Step 3: Verify all four dispense actions**

Use Unity MCP `execute_code(action="execute", safety_checks=true)` with this code:

```csharp
var player = new GameObject("VendingFinalDiagnosticPlayer");
var inventory = player.AddComponent<PlayerInventory>();
var interactor = player.AddComponent<PlayerInteractionSystem>();
var vending = new GameObject("VendingFinalDiagnosticMachine").AddComponent<TestingResourceVendingMachine>();
vending.OpenForInteractor(interactor);

System.Type type = typeof(TestingResourceVendingMachine);
System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
type.GetMethod("DispenseWood", flags).Invoke(vending, null);
type.GetMethod("DispenseFiber", flags).Invoke(vending, null);
type.GetMethod("DispenseStone", flags).Invoke(vending, null);
type.GetMethod("DispenseCloth", flags).Invoke(vending, null);

bool ok = inventory.GetAmount(ItemType.Wood) == 5
    && inventory.GetAmount(ItemType.Fiber) == 5
    && inventory.GetAmount(ItemType.Stone) == 5
    && inventory.GetAmount(ItemType.Cloth) == 5;

string result = ok
    ? "PASS: Vending dispensed Wood/Fiber/Stone/Cloth x5."
    : $"FAIL: wood={inventory.GetAmount(ItemType.Wood)}, fiber={inventory.GetAmount(ItemType.Fiber)}, stone={inventory.GetAmount(ItemType.Stone)}, cloth={inventory.GetAmount(ItemType.Cloth)}";

Object.DestroyImmediate(vending.gameObject);
Object.DestroyImmediate(player);
return result;
```

Expected:

```text
PASS: Vending dispensed Wood/Fiber/Stone/Cloth x5.
```

- [ ] **Step 4: Git hygiene check**

Run:

```powershell
git status --short
git log --oneline -8
```

Expected: only unrelated pre-existing user changes remain unstaged, if any. Recent commits include vending script and gameplay scene commits.
