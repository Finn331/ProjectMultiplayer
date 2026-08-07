# Networked Building Pieces Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Wall, Floor, Roof, and Door building pieces spawn as Photon Fusion network objects with session-only persistence, synchronized health, and state-authoritative demolish/despawn.

**Architecture:** Keep the mobile/local preview in `PlaceableItemSystem`, but move building placement validation and spawning into `FusionPlayerInventory` using one generic `NetworkBuildingPiece` prefab. `BuildingPiece` becomes a `NetworkBehaviour` with networked type, grid, rotation, health, and placer state; every client procedurally builds the matching primitive visual from that state.

**Tech Stack:** Unity 2022+/C#, Photon Fusion (`NetworkBehaviour`, `[Networked]`, `Runner.Spawn`, `Runner.Despawn`, RPCs), Unity Editor validation menus, Unity MCP for editor verification. Context7 MCP was requested, but the `context7` tool/resource server is not exposed in this session; use the local Fusion package source under `Assets/Photon/Fusion` and Unity compile checks for API verification.

---

## File Structure

- Create `Assets/Scripts/Building/BuildingPlacementRules.cs`: single source of truth for building item detection, item-to-piece mapping, bounds, contact skin, snap, rotation normalization, and rotation index conversion.
- Create `Assets/Editor/BuildingPlacementRulesSelfTest.cs`: editor validation menu for building bounds, contact skin, item mapping, snap, and rotation normalization.
- Modify `Assets/Scripts/PhotonFusion/BuildingPiece.cs`: convert from local `MonoBehaviour` to networked state-authoritative `NetworkBehaviour`, keep procedural visuals, add damage/demolish requests, and despawn through Fusion when spawned.
- Modify `Assets/Scripts/PhotonFusion/FusionPlayerInventory.cs`: route building items to one generic networked building prefab and remove the multiplayer `new GameObject` fallback.
- Modify `Assets/Scripts/Player/Survival/PlaceableItemSystem.cs`: use `BuildingPlacementRules` for preview and offline building placement, preserving the current snap behavior.
- Modify `Assets/Scripts/Player/Survival/PlayerInteractionSystem.cs`: pass the local player `NetworkObject` to building demolish requests instead of directly destroying buildings.
- Create `Assets/Editor/NetworkBuildingPiecePrefabBuilder.cs`: editor-only menu utility that creates/refreshes `Assets/Assets/Prefabs/NetworkBuildingPiece.prefab`, labels it `FusionPrefab`, and rebuilds the Fusion prefab table.
- Add generated prefab/meta assets under `Assets/Assets/Prefabs/NetworkBuildingPiece.prefab` and `.meta`.

---

### Task 1: Add Building Placement Rules With EditMode Tests

**Files:**
- Create: `Assets/Scripts/Building/BuildingPlacementRules.cs`
- Create: `Assets/Editor/BuildingPlacementRulesSelfTest.cs`

- [ ] **Step 1: Write the editor self-test before the helper exists**

Create `Assets/Editor/BuildingPlacementRulesSelfTest.cs`:

```csharp
using System;
using UnityEditor;
using UnityEngine;

public static class BuildingPlacementRulesSelfTest
{
    [MenuItem("Project Multiplayer/Run Building Placement Rules Self Test")]
    public static void Run()
    {
        Expect(BuildingPlacementRules.IsBuildingItem(ItemType.WallItem), "WallItem should be a building item.");
        Expect(BuildingPlacementRules.IsBuildingItem(ItemType.FloorItem), "FloorItem should be a building item.");
        Expect(BuildingPlacementRules.IsBuildingItem(ItemType.RoofItem), "RoofItem should be a building item.");
        Expect(BuildingPlacementRules.IsBuildingItem(ItemType.DoorItem), "DoorItem should be a building item.");
        Expect(!BuildingPlacementRules.IsBuildingItem(ItemType.Wood), "Wood should not be a building item.");
        Expect(!BuildingPlacementRules.IsBuildingItem(ItemType.Campfire), "Campfire should not be a building item.");

        Expect(BuildingPlacementRules.TryGetPieceType(ItemType.WallItem, out BuildingPieceType wall) && wall == BuildingPieceType.Wall, "WallItem should map to Wall.");
        Expect(BuildingPlacementRules.TryGetPieceType(ItemType.FloorItem, out BuildingPieceType floor) && floor == BuildingPieceType.Floor, "FloorItem should map to Floor.");
        Expect(BuildingPlacementRules.TryGetPieceType(ItemType.RoofItem, out BuildingPieceType roof) && roof == BuildingPieceType.Roof, "RoofItem should map to Roof.");
        Expect(BuildingPlacementRules.TryGetPieceType(ItemType.DoorItem, out BuildingPieceType door) && door == BuildingPieceType.Door, "DoorItem should map to Door.");
        Expect(!BuildingPlacementRules.TryGetPieceType(ItemType.Stone, out _), "Stone should not map to a building piece.");

        ExpectEqual(new Vector3(1f, 2f, 0.2f), BuildingPlacementRules.GetBounds(ItemType.WallItem), "Wall bounds");
        ExpectEqual(new Vector3(1f, 0.1f, 1f), BuildingPlacementRules.GetBounds(ItemType.FloorItem), "Floor bounds");
        ExpectEqual(new Vector3(1f, 0.1f, 1.5f), BuildingPlacementRules.GetBounds(ItemType.RoofItem), "Roof bounds");
        ExpectEqual(new Vector3(0.8f, 2f, 0.1f), BuildingPlacementRules.GetBounds(ItemType.DoorItem), "Door bounds");
        ExpectEqual(Vector3.one, BuildingPlacementRules.GetBounds(ItemType.Wood), "Fallback bounds");

        Vector3 wall = BuildingPlacementRules.GetPlacementCheckBounds(new Vector3(1f, 2f, 0.2f));
        ExpectEqual(new Vector3(0.94f, 1.94f, 0.14f), wall, "Wall check bounds");

        Vector3 tiny = BuildingPlacementRules.GetPlacementCheckBounds(new Vector3(0.01f, 0.01f, 0.01f));
        ExpectEqual(Vector3.one * 0.05f, tiny, "Minimum check bounds");

        ExpectEqual(new Vector3(1f, 0f, -3f), BuildingPlacementRules.SnapToGrid(new Vector3(1.2f, 0.49f, -2.6f)), "Positive snap");
        ExpectEqual(new Vector3(-2f, 1f, 4f), BuildingPlacementRules.SnapToGrid(new Vector3(-1.6f, 1.49f, 3.5f)), "Negative snap");

        ExpectYaw(0f, BuildingPlacementRules.NormalizeBuildingRotation(Quaternion.Euler(0f, 12f, 0f)), "Yaw 12");
        ExpectYaw(90f, BuildingPlacementRules.NormalizeBuildingRotation(Quaternion.Euler(0f, 47f, 0f)), "Yaw 47");
        ExpectYaw(180f, BuildingPlacementRules.NormalizeBuildingRotation(Quaternion.Euler(0f, 181f, 0f)), "Yaw 181");
        ExpectYaw(270f, BuildingPlacementRules.NormalizeBuildingRotation(Quaternion.Euler(0f, 314f, 0f)), "Yaw 314");

        Expect(BuildingPlacementRules.GetRotationIndex(Quaternion.Euler(0f, 0f, 0f)) == 0, "0 degrees index");
        Expect(BuildingPlacementRules.GetRotationIndex(Quaternion.Euler(0f, 90f, 0f)) == 1, "90 degrees index");
        Expect(BuildingPlacementRules.GetRotationIndex(Quaternion.Euler(0f, 180f, 0f)) == 2, "180 degrees index");
        Expect(BuildingPlacementRules.GetRotationIndex(Quaternion.Euler(0f, 270f, 0f)) == 3, "270 degrees index");
        Expect(BuildingPlacementRules.GetRotationIndex(Quaternion.Euler(0f, 360f, 0f)) == 0, "360 degrees index");

        Debug.Log("BuildingPlacementRulesSelfTest passed.");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static void ExpectEqual(Vector3 expected, Vector3 actual, string message)
    {
        if ((expected - actual).sqrMagnitude > 0.0001f)
        {
            throw new Exception(message + " expected " + expected + " actual " + actual);
        }
    }

    private static void ExpectYaw(float expected, Quaternion actual, string message)
    {
        if (Mathf.Abs(Mathf.DeltaAngle(expected, actual.eulerAngles.y)) > 0.001f)
        {
            throw new Exception(message + " expected yaw " + expected + " actual yaw " + actual.eulerAngles.y);
        }
    }
}
```

- [ ] **Step 2: Run compile to verify the self-test fails before implementation**

Use Unity MCP editor state and console checks.

```text
read_console(types=["error"])
```

Expected result: compile error that `BuildingPlacementRules` does not exist.

- [ ] **Step 3: Implement `BuildingPlacementRules`**

Create `Assets/Scripts/Building/BuildingPlacementRules.cs`:

```csharp
using UnityEngine;

public static class BuildingPlacementRules
{
    public const float PlacementContactSkin = 0.06f;

    public static bool IsBuildingItem(ItemType itemType)
    {
        return itemType == ItemType.WallItem
            || itemType == ItemType.FloorItem
            || itemType == ItemType.RoofItem
            || itemType == ItemType.DoorItem;
    }

    public static bool TryGetPieceType(ItemType itemType, out BuildingPieceType pieceType)
    {
        switch (itemType)
        {
            case ItemType.WallItem:
                pieceType = BuildingPieceType.Wall;
                return true;
            case ItemType.FloorItem:
                pieceType = BuildingPieceType.Floor;
                return true;
            case ItemType.RoofItem:
                pieceType = BuildingPieceType.Roof;
                return true;
            case ItemType.DoorItem:
                pieceType = BuildingPieceType.Door;
                return true;
            default:
                pieceType = default;
                return false;
        }
    }

    public static Vector3 GetBounds(ItemType itemType)
    {
        return itemType switch
        {
            ItemType.WallItem => new Vector3(1f, 2f, 0.2f),
            ItemType.FloorItem => new Vector3(1f, 0.1f, 1f),
            ItemType.RoofItem => new Vector3(1f, 0.1f, 1.5f),
            ItemType.DoorItem => new Vector3(0.8f, 2f, 0.1f),
            _ => Vector3.one
        };
    }

    public static Vector3 GetBounds(BuildingPieceType pieceType)
    {
        return pieceType switch
        {
            BuildingPieceType.Wall => new Vector3(1f, 2f, 0.2f),
            BuildingPieceType.Floor => new Vector3(1f, 0.1f, 1f),
            BuildingPieceType.Roof => new Vector3(1f, 0.1f, 1.5f),
            BuildingPieceType.Door => new Vector3(0.8f, 2f, 0.1f),
            _ => Vector3.one
        };
    }

    public static Vector3 GetPlacementCheckBounds(Vector3 bounds)
    {
        return Vector3.Max(bounds - Vector3.one * PlacementContactSkin, Vector3.one * 0.05f);
    }

    public static Vector3 SnapToGrid(Vector3 worldPosition)
    {
        const float gridSize = 1f;
        float snappedX = Mathf.Round(worldPosition.x / gridSize) * gridSize;
        float snappedY = Mathf.Round(worldPosition.y / gridSize) * gridSize;
        float snappedZ = Mathf.Round(worldPosition.z / gridSize) * gridSize;
        return new Vector3(snappedX, snappedY, snappedZ);
    }

    public static Quaternion NormalizeBuildingRotation(Quaternion rotation)
    {
        float yaw = Mathf.Round(rotation.eulerAngles.y / 90f) * 90f;
        return Quaternion.Euler(0f, yaw, 0f);
    }

    public static int GetRotationIndex(Quaternion rotation)
    {
        float normalizedYaw = NormalizeBuildingRotation(rotation).eulerAngles.y;
        int index = Mathf.RoundToInt(normalizedYaw / 90f) % 4;
        return index < 0 ? index + 4 : index;
    }
}
```

- [ ] **Step 4: Run the editor self-test to verify it passes**

Use Unity MCP to wait for compilation, then execute:

```text
execute_menu_item(menu_path="Project Multiplayer/Run Building Placement Rules Self Test")
```

Expected result: Unity console logs `BuildingPlacementRulesSelfTest passed.` and has zero compile errors.

- [ ] **Step 5: Check Unity console and commit**

Read Unity console errors through MCP. Expected result: zero compile errors.

Commit:

```bash
git add "Assets/Scripts/Building/BuildingPlacementRules.cs" "Assets/Editor/BuildingPlacementRulesSelfTest.cs"
git commit -m "test: add building placement rules self-test"
```

---

### Task 2: Use Shared Building Rules In Local Preview

**Files:**
- Modify: `Assets/Scripts/Player/Survival/PlaceableItemSystem.cs`

- [ ] **Step 1: Replace local building constants/helpers with shared rules**

In `PlaceableItemSystem.cs`, remove the class constant:

```csharp
private const float PlacementContactSkin = 0.06f;
```

Replace the body of local `IsBuildingItem` with:

```csharp
private static bool IsBuildingItem(ItemType itemType)
{
    return BuildingPlacementRules.IsBuildingItem(itemType);
}
```

Replace the body of `SnapToGrid` with:

```csharp
private static Vector3 SnapToGrid(Vector3 worldPosition)
{
    return BuildingPlacementRules.SnapToGrid(worldPosition);
}
```

Replace the body of `GetBuildingPreviewBounds` with:

```csharp
private static Vector3 GetBuildingPreviewBounds(ItemType itemType)
{
    return BuildingPlacementRules.GetBounds(itemType);
}
```

Replace the body of `GetPlacementCheckBounds` with:

```csharp
private static Vector3 GetPlacementCheckBounds(Vector3 bounds)
{
    return BuildingPlacementRules.GetPlacementCheckBounds(bounds);
}
```

- [ ] **Step 2: Reuse shared rotation normalization**

In `UpdatePreview()`, replace:

```csharp
float yaw = Mathf.Round(targetRotation.eulerAngles.y / 90f) * 90f;
targetRotation = Quaternion.Euler(0f, yaw, 0f);
```

with:

```csharp
targetRotation = BuildingPlacementRules.NormalizeBuildingRotation(targetRotation);
```

- [ ] **Step 3: Use shared item-to-piece mapping in offline placement**

In `SpawnOfflinePlaceable`, replace the building `switch` block with:

```csharp
if (!BuildingPlacementRules.TryGetPieceType(itemType, out BuildingPieceType pieceType))
{
    return false;
}

GameObject placed = new GameObject("Building_" + pieceType);
placed.transform.SetPositionAndRotation(position, BuildingPlacementRules.NormalizeBuildingRotation(rotation));
BuildingPiece piece = placed.AddComponent<BuildingPiece>();
piece.Initialize(pieceType, Vector3Int.RoundToInt(BuildingPlacementRules.SnapToGrid(position)), BuildingPlacementRules.GetRotationIndex(rotation));
return true;
```

- [ ] **Step 4: Run self-test and check console**

Run through Unity MCP:

```text
execute_menu_item(menu_path="Project Multiplayer/Run Building Placement Rules Self Test")
```

Expected result: Unity console logs `BuildingPlacementRulesSelfTest passed.` and has zero compile errors.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Player/Survival/PlaceableItemSystem.cs"
git commit -m "refactor: share building placement rules with preview"
```

---

### Task 3: Convert BuildingPiece To A NetworkBehaviour

**Files:**
- Modify: `Assets/Scripts/PhotonFusion/BuildingPiece.cs`

- [ ] **Step 1: Replace the class declaration and state fields**

At the top of `BuildingPiece.cs`, add Fusion:

```csharp
using Fusion;
using UnityEngine;
```

Change the class declaration and state fields to:

```csharp
public class BuildingPiece : NetworkBehaviour
{
    public const float DefaultMaxHealth = 100f;
    private const float InteractDistance = 3f;

    private float offlineHealth = DefaultMaxHealth;

    [Networked] public float Health { get; private set; }
    [Networked] public int PieceTypeValue { get; private set; }
    [Networked] public int GridX { get; private set; }
    [Networked] public int GridY { get; private set; }
    [Networked] public int GridZ { get; private set; }
    [Networked] public int RotationIndex { get; private set; }
    [Networked] public PlayerRef Placer { get; private set; }

    public BuildingPieceType PieceType => (BuildingPieceType)PieceTypeValue;
    public Vector3Int GridPosition => new Vector3Int(GridX, GridY, GridZ);
    public float HealthValue => IsNetworkedRuntime ? Health : offlineHealth;
    public float MaxHealthValue => DefaultMaxHealth;
    public float HealthRatio => Mathf.Clamp01(HealthValue / DefaultMaxHealth);
    public bool IsDestroyed => HealthValue <= 0f;

    private bool IsNetworkedRuntime => Object != null && Object.IsValid;
```

Keep these existing local fields after the new fields:

```csharp
private MeshRenderer meshRenderer;
private Material instanceMaterial;
private GameObject generatedModel;
private BoxCollider rootCollider;
private int builtPieceTypeValue = int.MinValue;
private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
```

- [ ] **Step 2: Add lifecycle methods that build visuals idempotently**

Replace `Start()` with:

```csharp
private void Start()
{
    EnsureVisualBuilt();
}

public override void Spawned()
{
    EnsureVisualBuilt();
}

public override void Render()
{
    EnsureVisualBuilt();
    UpdateDamageTint();
}

private void Update()
{
    if (!IsNetworkedRuntime)
    {
        EnsureVisualBuilt();
        UpdateDamageTint();
    }
}

private void EnsureVisualBuilt()
{
    if (!System.Enum.IsDefined(typeof(BuildingPieceType), PieceTypeValue))
    {
        return;
    }

    if (generatedModel != null && builtPieceTypeValue == PieceTypeValue && rootCollider != null)
    {
        return;
    }

    ClearGeneratedModel();
    CreateModel(PieceType);
    builtPieceTypeValue = PieceTypeValue;
    meshRenderer = GetComponentInChildren<MeshRenderer>();
    instanceMaterial = meshRenderer != null ? meshRenderer.material : null;
}

private void ClearGeneratedModel()
{
    for (int i = transform.childCount - 1; i >= 0; i--)
    {
        Transform child = transform.GetChild(i);
        if (Application.isPlaying)
        {
            Destroy(child.gameObject);
        }
        else
        {
            DestroyImmediate(child.gameObject);
        }
    }

    BoxCollider[] colliders = GetComponents<BoxCollider>();
    for (int i = colliders.Length - 1; i >= 0; i--)
    {
        if (Application.isPlaying)
        {
            Destroy(colliders[i]);
        }
        else
        {
            DestroyImmediate(colliders[i]);
        }
    }

    generatedModel = null;
    rootCollider = null;
    meshRenderer = null;
    instanceMaterial = null;
}
```

Replace the color work inside `Update()` with a helper:

```csharp
private void UpdateDamageTint()
{
    if (instanceMaterial == null) return;

    float ratio = HealthRatio;
    Color color;
    if (ratio > 0.66f) color = Color.Lerp(Color.yellow, Color.green, (ratio - 0.66f) / 0.34f);
    else if (ratio > 0.33f) color = Color.Lerp(Color.red, Color.yellow, (ratio - 0.33f) / 0.33f);
    else color = Color.red;
    instanceMaterial.SetColor(ColorPropertyId, color);
}
```

- [ ] **Step 3: Replace initialization with network-aware overloads**

Replace `Initialize(BuildingPieceType pieceType, Vector3Int gridPos, int rotIndex)` with:

```csharp
public void Initialize(BuildingPieceType pieceType, Vector3Int gridPos, int rotIndex)
{
    Initialize(pieceType, gridPos, rotIndex, PlayerRef.None);
}

public bool Initialize(BuildingPieceType pieceType, Vector3Int gridPos, int rotIndex, PlayerRef placer)
{
    if (IsNetworkedRuntime && !HasStateAuthority)
    {
        return false;
    }

    PieceTypeValue = (int)pieceType;
    GridX = gridPos.x;
    GridY = gridPos.y;
    GridZ = gridPos.z;
    RotationIndex = Mathf.Clamp(rotIndex, 0, 3);
    Placer = placer;

    if (IsNetworkedRuntime)
    {
        Health = DefaultMaxHealth;
    }
    else
    {
        offlineHealth = DefaultMaxHealth;
    }

    transform.rotation = Quaternion.Euler(0f, RotationIndex * 90f, 0f);
    EnsureVisualBuilt();
    return true;
}
```

- [ ] **Step 4: Replace damage and demolish methods with request/authority flow**

Replace `TakeDamage` and `Demolish` with:

```csharp
public void TakeDamage(float amount)
{
    RequestDamage(null, amount);
}

public void RequestDamage(NetworkObject requester, float amount)
{
    if (amount <= 0f)
    {
        return;
    }

    if (!IsNetworkedRuntime)
    {
        ApplyOfflineDamage(amount);
        return;
    }

    if (HasStateAuthority)
    {
        ApplyNetworkDamage(requester, amount);
        return;
    }

    RPC_RequestDamage(requester, amount);
}

public void Demolish()
{
    RequestDemolish(null);
}

public void RequestDemolish(NetworkObject requester)
{
    if (!IsNetworkedRuntime)
    {
        DropDemolishResources();
        Destroy(gameObject);
        return;
    }

    if (HasStateAuthority)
    {
        ApplyNetworkDemolish(requester);
        return;
    }

    RPC_RequestDemolish(requester);
}

[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
private void RPC_RequestDamage(NetworkObject requester, float amount, RpcInfo info = default)
{
    if (!IsAuthorizedRequester(requester, info))
    {
        return;
    }

    ApplyNetworkDamage(requester, amount);
}

[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
private void RPC_RequestDemolish(NetworkObject requester, RpcInfo info = default)
{
    if (!IsAuthorizedRequester(requester, info))
    {
        return;
    }

    ApplyNetworkDemolish(requester);
}
```

Add these helper methods:

```csharp
private void ApplyOfflineDamage(float amount)
{
    offlineHealth = Mathf.Max(0f, offlineHealth - amount);
    if (offlineHealth <= 0f)
    {
        DropDemolishResources();
        Destroy(gameObject);
    }
}

private void ApplyNetworkDamage(NetworkObject requester, float amount)
{
    if (!HasStateAuthority || amount <= 0f || !IsRequesterInRange(requester))
    {
        return;
    }

    Health = Mathf.Max(0f, Health - amount);
    if (Health <= 0f)
    {
        DropDemolishResources();
        Runner.Despawn(Object);
    }
}

private void ApplyNetworkDemolish(NetworkObject requester)
{
    if (!HasStateAuthority || !IsRequesterInRange(requester))
    {
        return;
    }

    DropDemolishResources();
    Runner.Despawn(Object);
}

private bool IsAuthorizedRequester(NetworkObject requester, RpcInfo info)
{
    if (requester == null)
    {
        return info.Source.IsNone;
    }

    if (requester.InputAuthority == info.Source)
    {
        return true;
    }

    return requester.HasStateAuthority && info.Source.IsNone;
}

private bool IsRequesterInRange(NetworkObject requester)
{
    return requester == null || Vector3.Distance(requester.transform.position, transform.position) <= InteractDistance;
}
```

- [ ] **Step 5: Update refund spawning to run once from state authority**

At the top of `DropDemolishResources`, use:

```csharp
if (IsNetworkedRuntime && !HasStateAuthority) return;
```

Replace `SpawnResourceDrop` with:

```csharp
private void SpawnResourceDrop(ItemType itemType, int amount, Vector3 position)
{
    FusionPlayerInventory[] handlers = FindObjectsOfType<FusionPlayerInventory>();
    for (int i = 0; i < handlers.Length; i++)
    {
        FusionPlayerInventory handler = handlers[i];
        if (handler != null && handler.SpawnTreeDropsFromData(position, position, Vector3.forward, itemType, 1, amount, 0.2f))
        {
            return;
        }
    }

    if (IsNetworkedRuntime)
    {
        Debug.LogWarning($"Building refund drop failed for {itemType} x{amount}; no local authoritative FusionPlayerInventory could spawn it.", this);
        return;
    }

    GameObject drop = GameObject.CreatePrimitive(PrimitiveType.Cube);
    drop.transform.position = position;
    drop.transform.localScale = new Vector3(0.25f, 0.15f, 0.25f);
    PickableItem pickableItem = drop.AddComponent<PickableItem>();
    pickableItem.itemType = itemType;
    pickableItem.amount = amount;
    pickableItem.itemName = itemType.ToString();
}
```

- [ ] **Step 6: Update `CreateModel` to use shared bounds and track generated objects**

Inside `CreateModel`, after each primitive is created, assign it to `generatedModel`:

```csharp
generatedModel = model;
```

Replace `BoxCollider collider = gameObject.AddComponent<BoxCollider>();` with:

```csharp
rootCollider = gameObject.AddComponent<BoxCollider>();
BoxCollider collider = rootCollider;
```

Keep the existing collider centers because they match current behavior:

```csharp
case BuildingPieceType.Wall:
    collider.size = BuildingPlacementRules.GetBounds(pieceType);
    collider.center = new Vector3(0f, 1f, 0f);
    break;
case BuildingPieceType.Floor:
    collider.size = BuildingPlacementRules.GetBounds(pieceType);
    collider.center = Vector3.zero;
    break;
case BuildingPieceType.Roof:
    collider.size = BuildingPlacementRules.GetBounds(pieceType);
    collider.center = new Vector3(0f, 0.05f, 0f);
    break;
case BuildingPieceType.Door:
    collider.size = BuildingPlacementRules.GetBounds(pieceType);
    collider.center = new Vector3(0f, 1f, 0f);
    break;
```

- [ ] **Step 7: Run Unity compile and commit**

Use Unity MCP editor state and console checks. Expected result: zero compile errors.

Commit:

```bash
git add "Assets/Scripts/PhotonFusion/BuildingPiece.cs"
git commit -m "feat: make building pieces network-authoritative"
```

---

### Task 4: Spawn Building Pieces Through FusionPlayerInventory

**Files:**
- Modify: `Assets/Scripts/PhotonFusion/FusionPlayerInventory.cs`

- [ ] **Step 1: Add generic building prefab bindings**

Under `[Header("Placeables")]`, add:

```csharp
[SerializeField] private NetworkPrefabRef buildingPiecePrefab;
[SerializeField] private GameObject buildingPiecePrefabObject;
```

- [ ] **Step 2: Route building items before normal placeable prefab lookup**

In `RPC_RequestPlace`, after `ItemType itemType = (ItemType)expectedItemTypeValue;` and the existing item validation, add:

```csharp
if (BuildingPlacementRules.IsBuildingItem(itemType))
{
    TryPlaceBuildingPiece(slotIndex, itemType, position, rotation, info.Source);
    return;
}
```

Then remove the old fallback block:

```csharp
if (IsBuildingItem(itemType))
{
    PlaceBuildingPiece(itemType, position, rotation);
    return;
}
```

- [ ] **Step 3: Add networked building placement implementation**

Add this method near the existing placement helpers:

```csharp
private void TryPlaceBuildingPiece(int slotIndex, ItemType itemType, Vector3 requestedPosition, Quaternion requestedRotation, PlayerRef placer)
{
    if (!BuildingPlacementRules.TryGetPieceType(itemType, out BuildingPieceType pieceType))
    {
        return;
    }

    if (!TryGetBuildingPrefab(out NetworkPrefabRef prefab, out GameObject prefabObject))
    {
        Debug.LogWarning("Cannot place building piece because NetworkBuildingPiece prefab is not assigned.", this);
        return;
    }

    Vector3 snappedPosition = BuildingPlacementRules.SnapToGrid(requestedPosition);
    Quaternion snappedRotation = BuildingPlacementRules.NormalizeBuildingRotation(requestedRotation);
    Vector3 bounds = BuildingPlacementRules.GetBounds(itemType);

    float maxDistance = Mathf.Max(0.5f, maxPlacementDistance);
    if ((snappedPosition - transform.position).sqrMagnitude > maxDistance * maxDistance)
    {
        return;
    }

    if (!TryGetValidPlacementGround(snappedPosition, out Collider groundCollider))
    {
        return;
    }

    if (IsPlacementBlocked(snappedPosition, snappedRotation, bounds, groundCollider))
    {
        return;
    }

    if (!inventory.RemoveItemFromSlot(slotIndex, 1, out ItemType removedItemType))
    {
        return;
    }

    if (removedItemType != itemType)
    {
        inventory.AddItemToSlot(removedItemType, 1, slotIndex);
        return;
    }

    NetworkObject placedObject = prefabObject != null
        ? Runner.Spawn(prefabObject, snappedPosition, snappedRotation, placer)
        : Runner.Spawn(prefab, snappedPosition, snappedRotation, placer);
    if (placedObject == null)
    {
        inventory.AddItemToSlot(itemType, 1, slotIndex);
        return;
    }

    BuildingPiece buildingPiece = placedObject.GetComponent<BuildingPiece>();
    if (buildingPiece == null || !buildingPiece.Initialize(pieceType, Vector3Int.RoundToInt(snappedPosition), BuildingPlacementRules.GetRotationIndex(snappedRotation), placer))
    {
        Runner.Despawn(placedObject);
        inventory.AddItemToSlot(itemType, 1, slotIndex);
    }
}
```

- [ ] **Step 4: Add generic building prefab lookup**

Add this method near `TryGetPlaceablePrefab`:

```csharp
private bool TryGetBuildingPrefab(out NetworkPrefabRef prefab, out GameObject prefabObject)
{
    prefabObject = null;
    if (buildingPiecePrefabObject != null && buildingPiecePrefabObject.GetComponent<NetworkObject>() != null)
    {
        prefabObject = buildingPiecePrefabObject;
        prefab = default;
        return true;
    }

    if (buildingPiecePrefab.IsValid)
    {
        prefab = buildingPiecePrefab;
        return true;
    }

    prefab = default;
    return false;
}
```

- [ ] **Step 5: Use contact-skin bounds for server collision validation**

In `IsPlacementBlocked`, replace:

```csharp
Vector3 halfExtents = Vector3.Max(bounds, Vector3.one * 0.1f) * 0.5f;
```

with:

```csharp
Vector3 checkBounds = BuildingPlacementRules.GetPlacementCheckBounds(Vector3.Max(bounds, Vector3.one * 0.1f));
Vector3 halfExtents = checkBounds * 0.5f;
```

- [ ] **Step 6: Delete unused local building fallback methods**

Remove these methods from `FusionPlayerInventory.cs` after confirming no remaining references in that file:

```csharp
private static bool IsBuildingItem(ItemType itemType)
private void PlaceBuildingPiece(ItemType itemType, Vector3 position, Quaternion rotation)
```

- [ ] **Step 7: Run self-test and compile check**

Run:

```text
execute_menu_item(menu_path="Project Multiplayer/Run Building Placement Rules Self Test")
```

Expected result: Unity console logs `BuildingPlacementRulesSelfTest passed.` and has zero compile errors.

- [ ] **Step 8: Commit**

```bash
git add "Assets/Scripts/PhotonFusion/FusionPlayerInventory.cs"
git commit -m "feat: spawn building pieces through Fusion"
```

---

### Task 5: Request Networked Building Demolish From PlayerInteractionSystem

**Files:**
- Modify: `Assets/Scripts/Player/Survival/PlayerInteractionSystem.cs`

- [ ] **Step 1: Add Fusion using**

At the top of `PlayerInteractionSystem.cs`, add:

```csharp
using Fusion;
```

- [ ] **Step 2: Replace direct demolish with request demolish**

In `TryInteract`, replace:

```csharp
currentBuildingTarget.Demolish();
```

with:

```csharp
NetworkObject playerObject = GetComponent<NetworkObject>();
currentBuildingTarget.RequestDemolish(playerObject);
```

- [ ] **Step 3: Hide HP bar when a target is destroyed**

Immediately after the request demolish call, keep the existing reset and add `HideHpBar()`:

```csharp
demolishHoldTimer = 0f;
currentBuildingTarget = null;
HideHpBar();
```

- [ ] **Step 4: Run compile check and commit**

Use Unity MCP editor state and console checks. Expected result: zero compile errors.

Commit:

```bash
git add "Assets/Scripts/Player/Survival/PlayerInteractionSystem.cs"
git commit -m "feat: request networked building demolish"
```

---

### Task 6: Create And Register NetworkBuildingPiece Prefab

**Files:**
- Create: `Assets/Editor/NetworkBuildingPiecePrefabBuilder.cs`
- Create/update through Unity Editor: `Assets/Assets/Prefabs/NetworkBuildingPiece.prefab`
- Modify through Unity Editor if needed: `Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion`
- Modify through Unity Editor if needed: scene/prefab containing `FusionPlayerInventory` serialized fields

- [ ] **Step 1: Create the editor prefab builder**

Create `Assets/Editor/NetworkBuildingPiecePrefabBuilder.cs`:

```csharp
#if UNITY_EDITOR
using Fusion;
using UnityEditor;
using UnityEngine;

public static class NetworkBuildingPiecePrefabBuilder
{
    private const string PrefabPath = "Assets/Assets/Prefabs/NetworkBuildingPiece.prefab";
    private const string FusionPrefabLabel = "FusionPrefab";

    [MenuItem("Project Multiplayer/Build Network Building Piece Prefab")]
    public static void BuildPrefab()
    {
        string folder = System.IO.Path.GetDirectoryName(PrefabPath)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
        {
            EnsureFolder(folder);
        }

        GameObject root = new GameObject("NetworkBuildingPiece");
        root.AddComponent<NetworkObject>();
        root.AddComponent<BuildingPiece>();
        BoxCollider collider = root.AddComponent<BoxCollider>();
        collider.size = Vector3.one;
        collider.center = Vector3.zero;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SetLabels(prefab, new[] { FusionPrefabLabel });
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorApplication.ExecuteMenuItem("Tools/Fusion/Rebuild Prefab Table");
        Debug.Log("NetworkBuildingPiece prefab created and Fusion prefab table rebuild requested.");
    }

    private static void EnsureFolder(string folder)
    {
        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }
}
#endif
```

- [ ] **Step 2: Compile and run the builder through Unity MCP**

Use Unity MCP to wait for compilation and read console. Expected result: zero compile errors.

Then execute the menu item through Unity MCP:

```text
execute_menu_item(menu_path="Project Multiplayer/Build Network Building Piece Prefab")
```

Expected result:

- `Assets/Assets/Prefabs/NetworkBuildingPiece.prefab` exists.
- The prefab has `NetworkObject`, `BuildingPiece`, and `BoxCollider`.
- Unity console contains `NetworkBuildingPiece prefab created and Fusion prefab table rebuild requested.`
- Unity console contains Fusion rebuild success or no Fusion rebuild error.

- [ ] **Step 3: Assign the prefab to FusionPlayerInventory serialized fields**

Use Unity MCP to locate player prefab(s) or scene player objects containing `FusionPlayerInventory`.

Set `buildingPiecePrefabObject` to `Assets/Assets/Prefabs/NetworkBuildingPiece.prefab` on every authoritative runtime player prefab/object that already has `FusionPlayerInventory` configured.

Do not hand-edit Unity YAML for this step. Use Unity Editor/MCP component/property operations so Unity serializes references correctly.

- [ ] **Step 4: Validate prefab table registration**

Use Unity MCP or editor script execution to confirm the prefab carries the `FusionPrefab` label and that the Fusion prefab table was rebuilt.

Expected result:

- `AssetDatabase.GetLabels(prefab)` includes `FusionPrefab`.
- `NetworkProjectConfigImporter` discovers the prefab during config import because it searches `l:FusionPrefab`.

- [ ] **Step 5: Check Unity console and commit**

Expected result: zero compile errors and no prefab registration errors.

Commit:

```bash
git add "Assets/Editor/NetworkBuildingPiecePrefabBuilder.cs" "Assets/Assets/Prefabs/NetworkBuildingPiece.prefab" "Assets/Assets/Prefabs/NetworkBuildingPiece.prefab.meta" "Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion" "Assets/Scenes/Gameplay.unity"
git commit -m "feat: add generic network building prefab"
```

If `Assets/Scenes/Gameplay.unity` was not changed because the reference lives on a player prefab, replace it with the changed player prefab path in the `git add` command.

---

### Task 7: Unity Verification And Multiplayer QA

**Files:**
- No source file changes expected unless verification exposes a defect.

- [ ] **Step 1: Run the building rules self-test**

Run through Unity MCP:

```text
execute_menu_item(menu_path="Project Multiplayer/Run Building Placement Rules Self Test")
```

Expected result: Unity console logs `BuildingPlacementRulesSelfTest passed.`.

- [ ] **Step 2: Run Unity compile and console verification**

Use Unity MCP editor state and console checks.

Expected result:

- `is_compiling == false`
- no `error` console entries from new code
- no missing script references on `NetworkBuildingPiece.prefab`

- [ ] **Step 3: Run editor probes for prefab and static placement data**

Use Unity MCP `execute_code` to run a probe equivalent to:

```csharp
using UnityEditor;
using UnityEngine;
using Fusion;

GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Assets/Prefabs/NetworkBuildingPiece.prefab");
Debug.Log("networkBuildingPrefab=" + (prefab != null));
Debug.Log("hasNetworkObject=" + (prefab != null && prefab.GetComponent<NetworkObject>() != null));
Debug.Log("hasBuildingPiece=" + (prefab != null && prefab.GetComponent<BuildingPiece>() != null));
Debug.Log("hasNetworkTransform=" + (prefab != null && prefab.GetComponent("NetworkTransform") != null));
Debug.Log("wallBounds=" + BuildingPlacementRules.GetBounds(ItemType.WallItem));
Debug.Log("wallCheckBounds=" + BuildingPlacementRules.GetPlacementCheckBounds(BuildingPlacementRules.GetBounds(ItemType.WallItem)));
```

Expected log facts:

- `networkBuildingPrefab=True`
- `hasNetworkObject=True`
- `hasBuildingPiece=True`
- `hasNetworkTransform=False`
- `wallBounds=(1.00, 2.00, 0.20)`
- `wallCheckBounds=(0.94, 1.94, 0.14)`

- [ ] **Step 4: Manual multiplayer QA**

Run a two-player Photon room and verify:

- Host/client can place Wall, Floor, Roof, and Door from the hotbar.
- Both players see each piece at the same position and rotation.
- Adjacent walls remain flush with no visible gap.
- Adjacent floor/roof placement is not falsely blocked.
- Looking far away still places on regular grid instead of over-snapping to existing pieces.
- A player who joins the still-running room sees previously placed pieces.
- Demolishing a piece on one player despawns it for both players.
- Refund drops appear once, not once per client.
- Dead/downed players cannot place or demolish pieces.

- [ ] **Step 5: Commit verification fixes or make final verification commit**

If verification required source fixes, commit those exact changed files:

```bash
git status --short
git add "Assets/Scripts/Building/BuildingPlacementRules.cs" "Assets/Editor/BuildingPlacementRulesSelfTest.cs" "Assets/Scripts/PhotonFusion/BuildingPiece.cs" "Assets/Scripts/PhotonFusion/FusionPlayerInventory.cs" "Assets/Scripts/Player/Survival/PlaceableItemSystem.cs" "Assets/Scripts/Player/Survival/PlayerInteractionSystem.cs" "Assets/Editor/NetworkBuildingPiecePrefabBuilder.cs" "Assets/Assets/Prefabs/NetworkBuildingPiece.prefab" "Assets/Assets/Prefabs/NetworkBuildingPiece.prefab.meta" "Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion" "Assets/Scenes/Gameplay.unity"
git commit -m "fix: verify networked building piece flow"
```

If no files changed, do not create an empty commit.

---

## Self-Review Notes

- Spec coverage: plan covers generic network prefab, networked building state, state-authoritative placement validation, damage/demolish requests, session-only persistence, prefab registration, networked refund drops, and required Unity/multiplayer validation.
- Context7 coverage: Context7 was explicitly requested, but the server is unavailable in this session. The plan uses local Photon Fusion source evidence: `NetworkProjectConfigImporter` discovers prefabs with the `FusionPrefab` label, and `NetworkProjectConfigUtilities.RebuildPrefabTable` labels prefabs with `NetworkObject` and rebuilds the table.
- Scope control: plan does not add cloud/local save persistence, ownership permissions, repair, upgrades, or new art.
