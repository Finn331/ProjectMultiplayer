# Chest Split Stack Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add long-press split-stack quantity transfers between player inventory/hotbar and storage chests in both directions.

**Architecture:** Keep UI as interaction orchestration only and keep item mutation inside `StorageChest` or `FusionStorageChest`. Add amount-aware transaction overloads that preserve existing full-stack drag calls by delegating old methods to the new amount-aware methods. Build the quantity dialog at runtime inside `StorageChestUI`, matching the current runtime-generated chest UI style.

**Tech Stack:** Unity C#, Unity UI/EventSystem, TextMeshPro, Photon Fusion `NetworkBehaviour`/RPC, existing `PlayerInventory` slot APIs.

---

## File Structure

- Modify `Assets/Scripts/Object/Storage/StorageChest.cs`: add amount-aware local/NGO store and take APIs, capacity helpers, and amount-aware server RPCs.
- Modify `Assets/Scripts/PhotonFusion/FusionStorageChest.cs`: add amount to pending transactions, public request methods, and RPCs while preserving Shared Mode authority handoff.
- Modify `Assets/Scripts/Player/Survival/MobileHotbarUI.cs`: expose a hotbar long-press notification for split drag while preserving existing drag events.
- Modify `Assets/Scripts/Player/Survival/HotbarSlotUI.cs`: detect a shorter split long-press and notify `MobileHotbarUI` without breaking hold-to-drop.
- Modify `Assets/Scripts/Object/Storage/StorageChestSlotUI.cs`: forward pointer down/up events to `StorageChestUI` so chest/inventory panel slots can enter split mode.
- Modify `Assets/Scripts/Object/Storage/StorageChestUI.cs`: add split source state, quantity dialog, capacity computation, and amount-aware transfer calls.
- Do not modify item definitions, max stack definitions, scene layout, or inventory/hotbar internal move behavior.

## Task 1: Add Amount-Aware Local Chest Transactions

**Files:**
- Modify: `Assets/Scripts/Object/Storage/StorageChest.cs`

- [ ] **Step 1: Add public amount-aware overloads and capacity helpers**

Insert these methods after the existing `TryRequestTake(PlayerInventory playerInventory, int chestSlotIndex, int preferredPlayerSlotIndex)` method. Keep the existing full-stack methods, but change their bodies as shown here.

```csharp
public bool TryRequestStore(PlayerInventory playerInventory, int playerSlotIndex, int chestSlotIndex)
{
    return this.TryRequestStore(playerInventory, playerSlotIndex, chestSlotIndex, int.MaxValue);
}

public bool TryRequestStore(PlayerInventory playerInventory, int playerSlotIndex, int chestSlotIndex, int requestedAmount)
{
    this.EnsureSlotSetup();
    if (playerInventory == null || !this.IsValidSlot(chestSlotIndex) || requestedAmount <= 0)
    {
        return false;
    }

    if (this.IsNetworkSessionActiveButChestNotSpawned())
    {
        this.ShowChestSyncWarning();
        return false;
    }

    if (!this.UseNetworkedChest())
    {
        return this.StoreFromPlayer(playerInventory, playerSlotIndex, chestSlotIndex, requestedAmount);
    }

    if (!this.HasLocalAuthority())
    {
        return false;
    }

    this.RequestStoreServerRpc(playerSlotIndex, chestSlotIndex, requestedAmount);
    return true;
}

public bool TryRequestTake(PlayerInventory playerInventory, int chestSlotIndex, int preferredPlayerSlotIndex)
{
    return this.TryRequestTake(playerInventory, chestSlotIndex, preferredPlayerSlotIndex, int.MaxValue);
}

public bool TryRequestTake(PlayerInventory playerInventory, int chestSlotIndex, int preferredPlayerSlotIndex, int requestedAmount)
{
    this.EnsureSlotSetup();
    if (playerInventory == null || !this.IsValidSlot(chestSlotIndex) || requestedAmount <= 0)
    {
        return false;
    }

    if (this.IsNetworkSessionActiveButChestNotSpawned())
    {
        this.ShowChestSyncWarning();
        return false;
    }

    if (!this.UseNetworkedChest())
    {
        return this.TakeToPlayer(playerInventory, chestSlotIndex, preferredPlayerSlotIndex, requestedAmount);
    }

    if (!this.HasLocalAuthority())
    {
        return false;
    }

    this.RequestTakeServerRpc(chestSlotIndex, preferredPlayerSlotIndex, requestedAmount);
    return true;
}

public int GetStoreCapacity(PlayerInventory playerInventory, int playerSlotIndex, int chestSlotIndex)
{
    this.EnsureSlotSetup();
    if (playerInventory == null || !this.IsValidSlot(chestSlotIndex))
    {
        return 0;
    }

    ItemType? sourceItemType = playerInventory.GetSlotItemType(playerSlotIndex);
    int sourceAmount = playerInventory.GetSlotAmount(playerSlotIndex);
    if (sourceItemType == null || sourceAmount <= 0)
    {
        return 0;
    }

    PlayerInventory.InventoryEntry targetEntry = slotEntries[chestSlotIndex];
    if (targetEntry != null && !targetEntry.IsEmpty && targetEntry.itemType != sourceItemType.Value)
    {
        return 0;
    }

    int currentChestAmount = targetEntry == null || targetEntry.IsEmpty ? 0 : targetEntry.amount;
    return Mathf.Clamp(MaxStackPerSlot - currentChestAmount, 0, sourceAmount);
}

public int GetTakeCapacity(PlayerInventory playerInventory, int chestSlotIndex, int preferredPlayerSlotIndex)
{
    this.EnsureSlotSetup();
    if (playerInventory == null || !this.IsValidSlot(chestSlotIndex))
    {
        return 0;
    }

    ItemType? itemType = this.GetSlotItemType(chestSlotIndex);
    int chestAmount = this.GetSlotAmount(chestSlotIndex);
    if (itemType == null || chestAmount <= 0)
    {
        return 0;
    }

    bool includeHotbar = playerInventory.IsHotbarSlot(preferredPlayerSlotIndex);
    int targetPlayerSlot = playerInventory.FindPreferredInventorySlot(itemType.Value, preferredPlayerSlotIndex, includeHotbar);
    if (targetPlayerSlot < 0)
    {
        return 0;
    }

    ItemType? targetItemType = playerInventory.GetSlotItemType(targetPlayerSlot);
    int targetAmount = playerInventory.GetSlotAmount(targetPlayerSlot);
    if (targetItemType != null && targetItemType.Value != itemType.Value)
    {
        return 0;
    }

    int targetCapacity = playerInventory.MaxStackPerSlot - targetAmount;
    return Mathf.Clamp(targetCapacity, 0, chestAmount);
}
```

- [ ] **Step 2: Replace private full-stack mutation methods with amount-aware versions**

Replace `StoreFromPlayer(PlayerInventory playerInventory, int playerSlotIndex, int chestSlotIndex)` and `TakeToPlayer(PlayerInventory playerInventory, int chestSlotIndex, int preferredPlayerSlotIndex)` with this code.

```csharp
private bool StoreFromPlayer(PlayerInventory playerInventory, int playerSlotIndex, int chestSlotIndex, int requestedAmount)
{
    ItemType? itemType = playerInventory.GetSlotItemType(playerSlotIndex);
    if (itemType == null || requestedAmount <= 0)
    {
        return false;
    }

    PlayerInventory.InventoryEntry targetEntry = slotEntries[chestSlotIndex];
    if (targetEntry == null)
    {
        targetEntry = new PlayerInventory.InventoryEntry();
        slotEntries[chestSlotIndex] = targetEntry;
    }

    if (!targetEntry.IsEmpty && targetEntry.itemType != itemType.Value)
    {
        return false;
    }

    int playerAmount = playerInventory.GetSlotAmount(playerSlotIndex);
    int currentChestAmount = targetEntry.IsEmpty ? 0 : targetEntry.amount;
    int transferable = Mathf.Min(playerAmount, requestedAmount, MaxStackPerSlot - currentChestAmount);
    if (transferable <= 0)
    {
        return false;
    }

    if (!playerInventory.RemoveItemFromSlot(playerSlotIndex, transferable, out ItemType removedType))
    {
        return false;
    }

    if (removedType != itemType.Value)
    {
        playerInventory.AddItemToSlot(removedType, transferable, playerSlotIndex);
        return false;
    }

    targetEntry.itemType = removedType;
    targetEntry.amount = currentChestAmount + transferable;
    this.MarkChanged();
    return true;
}

private bool TakeToPlayer(PlayerInventory playerInventory, int chestSlotIndex, int preferredPlayerSlotIndex, int requestedAmount)
{
    ItemType? itemType = this.GetSlotItemType(chestSlotIndex);
    int chestAmount = this.GetSlotAmount(chestSlotIndex);
    if (itemType == null || chestAmount <= 0 || requestedAmount <= 0)
    {
        return false;
    }

    bool includeHotbar = playerInventory.IsHotbarSlot(preferredPlayerSlotIndex);
    int targetPlayerSlot = playerInventory.FindPreferredInventorySlot(itemType.Value, preferredPlayerSlotIndex, includeHotbar);
    if (targetPlayerSlot < 0)
    {
        return false;
    }

    int amountToMove = Mathf.Min(chestAmount, requestedAmount);
    int acceptedAmount = playerInventory.AddItemToSlot(itemType.Value, amountToMove, targetPlayerSlot);
    if (acceptedAmount <= 0)
    {
        return false;
    }

    PlayerInventory.InventoryEntry chestEntry = slotEntries[chestSlotIndex];
    chestEntry.amount -= acceptedAmount;
    if (chestEntry.amount <= 0)
    {
        chestEntry.amount = 0;
        chestEntry.itemType = default;
    }

    this.MarkChanged();
    return true;
}
```

- [ ] **Step 3: Update NGO server RPC signatures**

Replace the two server RPCs at the bottom of `StorageChest.cs` with this code.

```csharp
[ServerRpc(RequireOwnership = false)]
private void RequestStoreServerRpc(int playerSlotIndex, int chestSlotIndex, int requestedAmount, ServerRpcParams serverRpcParams = default)
{
    if (!this.TryGetPlayerInventoryForClient(serverRpcParams.Receive.SenderClientId, out PlayerInventory playerInventory))
    {
        return;
    }

    if (Vector3.Distance(playerInventory.transform.position, transform.position) > interactDistance)
    {
        return;
    }

    this.StoreFromPlayer(playerInventory, playerSlotIndex, chestSlotIndex, requestedAmount);
}

[ServerRpc(RequireOwnership = false)]
private void RequestTakeServerRpc(int chestSlotIndex, int preferredPlayerSlotIndex, int requestedAmount, ServerRpcParams serverRpcParams = default)
{
    if (!this.TryGetPlayerInventoryForClient(serverRpcParams.Receive.SenderClientId, out PlayerInventory playerInventory))
    {
        return;
    }

    if (Vector3.Distance(playerInventory.transform.position, transform.position) > interactDistance)
    {
        return;
    }

    this.TakeToPlayer(playerInventory, chestSlotIndex, preferredPlayerSlotIndex, requestedAmount);
}
```

- [ ] **Step 4: Validate `StorageChest.cs`**

Run with Unity MCP:

```text
unityMCP_validate_script(uri: "Assets/Scripts/Object/Storage/StorageChest.cs", level: "standard", include_diagnostics: true)
```

Expected: `0 errors`. Warnings are acceptable only if unrelated to the edited methods.

## Task 2: Add Amount-Aware Fusion Chest Transactions

**Files:**
- Modify: `Assets/Scripts/PhotonFusion/FusionStorageChest.cs`

- [ ] **Step 1: Add `Amount` to pending transactions**

Update the `PendingTransaction` struct.

```csharp
private struct PendingTransaction
{
    public PendingTransactionType Type;
    public NetworkObject PlayerObject;
    public int PlayerSlot;
    public int ChestSlot;
    public int PreferredPlayerSlot;
    public int Amount;
}
```

- [ ] **Step 2: Replace public request methods with overloads**

Replace `RequestTakeFromChest(...)` and `RequestDepositToChest(...)` with this code.

```csharp
public bool RequestTakeFromChest(NetworkObject playerObject, int chestSlot, int preferredPlayerSlot)
{
    return RequestTakeFromChest(playerObject, chestSlot, preferredPlayerSlot, int.MaxValue);
}

public bool RequestTakeFromChest(NetworkObject playerObject, int chestSlot, int preferredPlayerSlot, int amount)
{
    if (!CanSendTransaction(playerObject) || amount <= 0)
    {
        return false;
    }

    if (!HasFusionStateAuthority())
    {
        RequestStateAuthorityForTransaction(new PendingTransaction
        {
            Type = PendingTransactionType.Take,
            PlayerObject = playerObject,
            ChestSlot = chestSlot,
            PreferredPlayerSlot = preferredPlayerSlot,
            Amount = amount
        });
        return true;
    }

    RPC_TakeFromChest(playerObject, chestSlot, preferredPlayerSlot, amount);
    return true;
}

public bool RequestDepositToChest(NetworkObject playerObject, int playerSlot, int chestSlot)
{
    return RequestDepositToChest(playerObject, playerSlot, chestSlot, int.MaxValue);
}

public bool RequestDepositToChest(NetworkObject playerObject, int playerSlot, int chestSlot, int amount)
{
    if (!CanSendTransaction(playerObject) || amount <= 0)
    {
        return false;
    }

    if (!HasFusionStateAuthority())
    {
        RequestStateAuthorityForTransaction(new PendingTransaction
        {
            Type = PendingTransactionType.Deposit,
            PlayerObject = playerObject,
            PlayerSlot = playerSlot,
            ChestSlot = chestSlot,
            Amount = amount
        });
        return true;
    }

    RPC_DepositToChest(playerObject, playerSlot, chestSlot, amount);
    return true;
}
```

- [ ] **Step 3: Update pending transaction replay**

Replace `ExecutePendingTransaction()` with this code.

```csharp
private void ExecutePendingTransaction()
{
    PendingTransaction transaction = pendingTransaction;
    pendingTransaction = default;

    if (transaction.Type == PendingTransactionType.Take)
    {
        RPC_TakeFromChest(transaction.PlayerObject, transaction.ChestSlot, transaction.PreferredPlayerSlot, transaction.Amount);
        return;
    }

    if (transaction.Type == PendingTransactionType.Deposit)
    {
        RPC_DepositToChest(transaction.PlayerObject, transaction.PlayerSlot, transaction.ChestSlot, transaction.Amount);
    }
}
```

- [ ] **Step 4: Replace Fusion RPCs with amount-aware versions**

Replace `RPC_TakeFromChest(...)` and `RPC_DepositToChest(...)` with this code.

```csharp
[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
public void RPC_TakeFromChest(NetworkObject playerObject, int chestSlot, int preferredPlayerSlot, int requestedAmount, RpcInfo info = default)
{
    if (requestedAmount <= 0 ||
        !IsAuthorizedForPlayer(playerObject, info) ||
        !TryGetPlayerInventory(playerObject, out PlayerInventory playerInventory) ||
        !TryReadSlot(chestSlot, out ItemType itemType, out int chestAmount))
    {
        return;
    }

    if (!IsPlayerInRange(playerObject))
    {
        return;
    }

    bool includeHotbar = playerInventory.IsHotbarSlot(preferredPlayerSlot);
    int targetPlayerSlot = playerInventory.FindPreferredInventorySlot(itemType, preferredPlayerSlot, includeHotbar);
    if (targetPlayerSlot < 0)
    {
        return;
    }

    int amountToMove = Mathf.Min(chestAmount, requestedAmount);
    int acceptedAmount = playerInventory.AddItemToSlot(itemType, amountToMove, targetPlayerSlot);
    if (acceptedAmount <= 0)
    {
        return;
    }

    int remainingAmount = chestAmount - acceptedAmount;
    Amounts.Set(chestSlot, Mathf.Max(0, remainingAmount));
    if (remainingAmount <= 0)
    {
        ClearSlot(chestSlot);
    }

    ChestChanged?.Invoke();
}

[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
public void RPC_DepositToChest(NetworkObject playerObject, int playerSlot, int chestSlot, int requestedAmount, RpcInfo info = default)
{
    if (requestedAmount <= 0 || !IsAuthorizedForPlayer(playerObject, info) || !TryGetPlayerInventory(playerObject, out PlayerInventory playerInventory) || !IsValidSlot(chestSlot))
    {
        return;
    }

    if (!IsPlayerInRange(playerObject))
    {
        return;
    }

    ItemType? sourceItemType = playerInventory.GetSlotItemType(playerSlot);
    if (sourceItemType == null)
    {
        return;
    }

    int currentChestAmount = Amounts[chestSlot];
    if (currentChestAmount > 0 && (!IsValidItemType(ItemTypes[chestSlot]) || (ItemType)ItemTypes[chestSlot] != sourceItemType.Value))
    {
        return;
    }

    int transferableAmount = Mathf.Min(playerInventory.GetSlotAmount(playerSlot), requestedAmount, MaxStackPerSlot - currentChestAmount);
    if (transferableAmount <= 0)
    {
        return;
    }

    if (!playerInventory.RemoveItemFromSlot(playerSlot, transferableAmount, out ItemType removedItemType))
    {
        return;
    }

    if (removedItemType != sourceItemType.Value)
    {
        RestoreRemovedItems(playerInventory, playerSlot, removedItemType, transferableAmount);
        return;
    }

    currentChestAmount = Amounts[chestSlot];
    if (currentChestAmount > 0 && (!IsValidItemType(ItemTypes[chestSlot]) || (ItemType)ItemTypes[chestSlot] != removedItemType))
    {
        RestoreRemovedItems(playerInventory, playerSlot, removedItemType, transferableAmount);
        return;
    }

    int acceptedAmount = Mathf.Min(transferableAmount, MaxStackPerSlot - currentChestAmount);
    if (acceptedAmount <= 0)
    {
        RestoreRemovedItems(playerInventory, playerSlot, removedItemType, transferableAmount);
        return;
    }

    ItemTypes.Set(chestSlot, (int)removedItemType);
    Amounts.Set(chestSlot, currentChestAmount + acceptedAmount);

    if (acceptedAmount < transferableAmount)
    {
        RestoreRemovedItems(playerInventory, playerSlot, removedItemType, transferableAmount - acceptedAmount);
    }

    ChestChanged?.Invoke();
}
```

- [ ] **Step 5: Validate `FusionStorageChest.cs`**

Run with Unity MCP:

```text
unityMCP_validate_script(uri: "Assets/Scripts/PhotonFusion/FusionStorageChest.cs", level: "standard", include_diagnostics: true)
```

Expected: `0 errors`. If Fusion weaver warnings appear, refresh scripts and check Unity console before continuing.

## Task 3: Expose Hotbar Long-Press Split Signal

**Files:**
- Modify: `Assets/Scripts/Player/Survival/MobileHotbarUI.cs`
- Modify: `Assets/Scripts/Player/Survival/HotbarSlotUI.cs`

- [ ] **Step 1: Add split long-press callback to `MobileHotbarUI`**

Add this field near the existing drag events.

```csharp
[HideInInspector] public Func<int, ItemType, bool> OnSlotLongPressForSplit;
```

Add this method after `GetSlotAmount(int hotbarSlotIndex)`.

```csharp
public bool NotifySlotLongPressForSplit(int hotbarSlotIndex)
{
    ItemType? slotItem = this.GetSlotItem(hotbarSlotIndex);
    if (slotItem == null || this.GetSlotAmount(hotbarSlotIndex) <= 1)
    {
        return false;
    }

    return OnSlotLongPressForSplit != null && OnSlotLongPressForSplit.Invoke(hotbarSlotIndex, slotItem.Value);
}
```

- [ ] **Step 2: Detect split long-press in `HotbarSlotUI`**

Add this field near `holdTime`.

```csharp
private const float splitHoldTime = 0.45f;
private bool splitHoldNotified;
```

Replace the `Update()` method with this code.

```csharp
void Update()
{
    ResolveHotbar();

    if (!isHolding)
    {
        return;
    }

    timer += Time.deltaTime;

    if (!splitHoldNotified && timer >= splitHoldTime)
    {
        splitHoldNotified = hotbar != null && hotbar.NotifySlotLongPressForSplit(slotIndex);
    }

    if (!splitHoldNotified && enableHoldToDrop && timer >= holdTime)
    {
        isHolding = false;
        if (hotbar != null)
        {
            hotbar.DropFromSlot(slotIndex);
        }
    }
}
```

Update `OnPointerDown` and `OnPointerUp` to reset and respect the split signal.

```csharp
public void OnPointerDown(PointerEventData eventData)
{
    ResolveHotbar();
    isHolding = true;
    splitHoldNotified = false;
    timer = 0f;
}

public void OnPointerUp(PointerEventData eventData)
{
    ResolveHotbar();
    isHolding = false;

    if (!splitHoldNotified && timer < holdTime)
    {
        if (hotbar != null)
        {
            hotbar.SelectSlot(slotIndex);
        }
    }
}
```

- [ ] **Step 3: Validate hotbar scripts**

Run with Unity MCP:

```text
unityMCP_validate_script(uri: "Assets/Scripts/Player/Survival/MobileHotbarUI.cs", level: "standard", include_diagnostics: true)
unityMCP_validate_script(uri: "Assets/Scripts/Player/Survival/HotbarSlotUI.cs", level: "standard", include_diagnostics: true)
```

Expected: `0 errors` for both scripts.

## Task 4: Add Slot Pointer Hooks for Chest UI Slots

**Files:**
- Modify: `Assets/Scripts/Object/Storage/StorageChestSlotUI.cs`

- [ ] **Step 1: Add pointer interfaces and forwarding methods**

Replace the class declaration with this code.

```csharp
public class StorageChestSlotUI : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
```

Add these methods before `OnBeginDrag`.

```csharp
public void OnPointerDown(PointerEventData eventData)
{
    owner?.HandleSlotPointerDown(this);
}

public void OnPointerUp(PointerEventData eventData)
{
    owner?.HandleSlotPointerUp(this);
}
```

- [ ] **Step 2: Validate `StorageChestSlotUI.cs` after owner methods exist**

Do not run validation until Task 5 adds `HandleSlotPointerDown` and `HandleSlotPointerUp` to `StorageChestUI`; otherwise this task will fail as expected.

## Task 5: Implement Split State and Quantity Dialog in `StorageChestUI`

**Files:**
- Modify: `Assets/Scripts/Object/Storage/StorageChestUI.cs`

- [ ] **Step 1: Add split configuration and fields**

Add these fields after the existing `Behavior` header.

```csharp
[SerializeField] private float splitLongPressSeconds = 0.45f;
[SerializeField] private Color splitHighlightColor = new Color(0.25f, 0.7f, 1f, 1f);
```

Add these fields after `private bool initialized;`.

```csharp
private StorageChestSlotUI pointerDownSlot;
private float pointerDownStartedAt;
private bool splitDragMode;
private bool hotbarSplitArmed;
private int hotbarSplitSlot = -1;
private RectTransform splitDialogRoot;
private TextMeshProUGUI splitDialogTitle;
private TMP_InputField splitQuantityInput;
private Button splitMinusButton;
private Button splitPlusButton;
private Button splitHalfButton;
private Button splitMaxButton;
private Button splitCancelButton;
private Button splitConfirmButton;
private SplitTransfer pendingSplitTransfer;

private enum SplitTransferDirection
{
    None,
    PlayerToChest,
    ChestToPlayer
}

private struct SplitTransfer
{
    public SplitTransferDirection Direction;
    public int PlayerSlot;
    public int ChestSlot;
    public int MaxAmount;
}
```

- [ ] **Step 2: Update `Update()` for long-press detection**

Replace `Update()` with this code.

```csharp
private void Update()
{
    if (!ShouldKeepChestOpen())
    {
        CloseChest();
        return;
    }

    UpdateSplitLongPress();
}
```

Add these methods after `Update()`.

```csharp
public void HandleSlotPointerDown(StorageChestSlotUI slot)
{
    if (slot == null || !SlotHasItem(slot) || GetSlotAmount(slot) <= 1)
    {
        return;
    }

    pointerDownSlot = slot;
    pointerDownStartedAt = Time.unscaledTime;
}

public void HandleSlotPointerUp(StorageChestSlotUI slot)
{
    if (pointerDownSlot == slot)
    {
        pointerDownSlot = null;
    }
}

private void UpdateSplitLongPress()
{
    if (pointerDownSlot == null || splitDragMode)
    {
        return;
    }

    if (Time.unscaledTime - pointerDownStartedAt < splitLongPressSeconds)
    {
        return;
    }

    if (!SlotHasItem(pointerDownSlot) || GetSlotAmount(pointerDownSlot) <= 1)
    {
        pointerDownSlot = null;
        return;
    }

    splitDragMode = true;
    pointerDownSlot.SetHighlight(true, slotColor, splitHighlightColor);
}
```

- [ ] **Step 3: Subscribe and unsubscribe hotbar split callback**

In `BindHotbarDrag()`, after subscribing `OnSlotDragEnd`, add this code.

```csharp
subscribedHotbar.OnSlotLongPressForSplit = OnHotbarLongPressForSplit;
```

In `UnbindHotbarDrag()`, before `subscribedHotbar = null;`, add this code.

```csharp
if (subscribedHotbar.OnSlotLongPressForSplit == OnHotbarLongPressForSplit)
{
    subscribedHotbar.OnSlotLongPressForSplit = null;
}
```

Add this method near the other hotbar drag methods.

```csharp
private bool OnHotbarLongPressForSplit(int hotbarSlotIndex, ItemType itemType)
{
    if (!IsChestVisible() || hotbarUI == null || playerInventory == null)
    {
        return false;
    }

    int globalSlotIndex = hotbarUI.GetHotbarGlobalSlotIndex(hotbarSlotIndex);
    if (globalSlotIndex < 0 || playerInventory.GetSlotAmount(globalSlotIndex) <= 1)
    {
        return false;
    }

    hotbarSplitArmed = true;
    hotbarSplitSlot = hotbarSlotIndex;
    return true;
}
```

- [ ] **Step 4: Update drag/drop routing to open split dialog when armed**

In `BeginSlotDrag`, after `dragSourceSlot = slot;`, add this code.

```csharp
if (pointerDownSlot != slot)
{
    splitDragMode = false;
}
```

In `HandleSlotDrop`, replace the two transfer branches with this code.

```csharp
if (dragSourceSlot.Kind == StorageChestSlotKind.PlayerInventory && targetSlot.Kind == StorageChestSlotKind.Chest)
{
    dragDropHandled = true;
    if (splitDragMode && TryOpenSplitDialog(SplitTransferDirection.PlayerToChest, dragSourceSlot.SlotIndex, targetSlot.SlotIndex))
    {
        return;
    }

    DepositPlayerSlotToChest(dragSourceSlot.SlotIndex, targetSlot.SlotIndex);
    return;
}

if (dragSourceSlot.Kind == StorageChestSlotKind.Chest && targetSlot.Kind == StorageChestSlotKind.PlayerInventory)
{
    dragDropHandled = true;
    if (splitDragMode && TryOpenSplitDialog(SplitTransferDirection.ChestToPlayer, targetSlot.SlotIndex, dragSourceSlot.SlotIndex))
    {
        return;
    }

    TakeChestSlotToPlayer(dragSourceSlot.SlotIndex, targetSlot.SlotIndex);
}
```

In `EndSlotDrag`, replace the chest-to-hotbar transfer line with this code.

```csharp
if (splitDragMode && TryOpenSplitDialog(SplitTransferDirection.ChestToPlayer, hotbarUI.GetHotbarGlobalSlotIndex(targetHotbarSlot), dragSourceSlot.SlotIndex))
{
    dragDropHandled = true;
}
else
{
    TakeChestSlotToHotbar(dragSourceSlot.SlotIndex, targetHotbarSlot);
    dragDropHandled = true;
}
```

At the end of `EndSlotDrag`, before `Refresh();`, add this cleanup.

```csharp
pointerDownSlot = null;
splitDragMode = false;
```

In `OnHotbarDragEnd`, replace the chest target branch with this code.

```csharp
if (targetSlot != null && targetSlot.Kind == StorageChestSlotKind.Chest)
{
    bool shouldSplit = hotbarSplitArmed && hotbarSplitSlot == sourceHotbarSlot;
    if (shouldSplit && TryOpenSplitDialog(SplitTransferDirection.PlayerToChest, hotbarDragSourceGlobalSlot, targetSlot.SlotIndex))
    {
        ClearHotbarDragState();
        return;
    }

    DepositPlayerSlotToChest(hotbarDragSourceGlobalSlot, targetSlot.SlotIndex);
    Refresh();
}
```

In `ClearHotbarDragState()`, add this cleanup.

```csharp
hotbarSplitArmed = false;
hotbarSplitSlot = -1;
```

- [ ] **Step 5: Add amount-aware transfer wrappers and capacity helpers**

Add these methods after `DepositPlayerSlotToChest(int playerSlot, int chestSlot)`.

```csharp
private void DepositPlayerSlotToChest(int playerSlot, int chestSlot, int amount)
{
    if (playerInventory == null || amount <= 0)
    {
        return;
    }

    if (activeFusionChest != null)
    {
        Fusion.NetworkObject playerObject = playerInventory.GetComponent<Fusion.NetworkObject>();
        activeFusionChest.RequestDepositToChest(playerObject, playerSlot, chestSlot, amount);
        return;
    }

    activeChest?.TryRequestStore(playerInventory, playerSlot, chestSlot, amount);
}
```

Add this overload after `TakeChestSlotToPlayer(int chestSlot, int playerSlot)`.

```csharp
private void TakeChestSlotToPlayer(int chestSlot, int playerSlot, int amount)
{
    if (playerInventory == null || amount <= 0)
    {
        return;
    }

    if (activeFusionChest != null)
    {
        Fusion.NetworkObject playerObject = playerInventory.GetComponent<Fusion.NetworkObject>();
        activeFusionChest.RequestTakeFromChest(playerObject, chestSlot, playerSlot, amount);
        return;
    }

    activeChest?.TryRequestTake(playerInventory, chestSlot, playerSlot, amount);
}
```

Add these helper methods near `GetActiveSlotAmount`.

```csharp
private int GetSlotAmount(StorageChestSlotUI slot)
{
    if (slot == null)
    {
        return 0;
    }

    return slot.Kind == StorageChestSlotKind.PlayerInventory
        ? playerInventory != null ? playerInventory.GetSlotAmount(slot.SlotIndex) : 0
        : GetActiveSlotAmount(slot.SlotIndex);
}

private int GetActiveMaxStackPerSlot()
{
    if (activeFusionChest != null)
    {
        return activeFusionChest.MaxStackPerSlot;
    }

    return activeChest != null ? activeChest.MaxStackPerSlot : 1;
}

private int GetSplitMaxAmount(SplitTransferDirection direction, int playerSlot, int chestSlot)
{
    if (playerInventory == null)
    {
        return 0;
    }

    if (direction == SplitTransferDirection.PlayerToChest)
    {
        if (activeChest != null)
        {
            return activeChest.GetStoreCapacity(playerInventory, playerSlot, chestSlot);
        }

        ItemType? sourceItemType = playerInventory.GetSlotItemType(playerSlot);
        int sourceAmount = playerInventory.GetSlotAmount(playerSlot);
        if (sourceItemType == null || sourceAmount <= 0)
        {
            return 0;
        }

        ItemType? chestItemType = GetActiveSlotItemType(chestSlot);
        int chestAmount = GetActiveSlotAmount(chestSlot);
        if (chestItemType != null && chestItemType.Value != sourceItemType.Value)
        {
            return 0;
        }

        return Mathf.Clamp(GetActiveMaxStackPerSlot() - chestAmount, 0, sourceAmount);
    }

    if (direction == SplitTransferDirection.ChestToPlayer)
    {
        if (activeChest != null)
        {
            return activeChest.GetTakeCapacity(playerInventory, chestSlot, playerSlot);
        }

        ItemType? chestItemType = GetActiveSlotItemType(chestSlot);
        int chestAmount = GetActiveSlotAmount(chestSlot);
        if (chestItemType == null || chestAmount <= 0)
        {
            return 0;
        }

        bool includeHotbar = playerInventory.IsHotbarSlot(playerSlot);
        int targetPlayerSlot = playerInventory.FindPreferredInventorySlot(chestItemType.Value, playerSlot, includeHotbar);
        if (targetPlayerSlot < 0)
        {
            return 0;
        }

        ItemType? targetItemType = playerInventory.GetSlotItemType(targetPlayerSlot);
        int targetAmount = playerInventory.GetSlotAmount(targetPlayerSlot);
        if (targetItemType != null && targetItemType.Value != chestItemType.Value)
        {
            return 0;
        }

        return Mathf.Clamp(playerInventory.MaxStackPerSlot - targetAmount, 0, chestAmount);
    }

    return 0;
}
```

- [ ] **Step 6: Build the quantity dialog**

Add these methods before `SetVisible(bool visible)`.

```csharp
private bool TryOpenSplitDialog(SplitTransferDirection direction, int playerSlot, int chestSlot)
{
    int maxAmount = GetSplitMaxAmount(direction, playerSlot, chestSlot);
    if (maxAmount <= 1)
    {
        return false;
    }

    pendingSplitTransfer = new SplitTransfer
    {
        Direction = direction,
        PlayerSlot = playerSlot,
        ChestSlot = chestSlot,
        MaxAmount = maxAmount
    };

    EnsureSplitDialog();
    int defaultAmount = Mathf.Clamp(Mathf.CeilToInt(maxAmount * 0.5f), 1, maxAmount);
    splitDialogTitle.text = direction == SplitTransferDirection.PlayerToChest ? "Move to Chest" : "Take from Chest";
    SetSplitQuantity(defaultAmount);
    splitDialogRoot.gameObject.SetActive(true);
    splitDialogRoot.SetAsLastSibling();
    return true;
}

private void EnsureSplitDialog()
{
    if (splitDialogRoot != null || targetCanvas == null)
    {
        return;
    }

    GameObject dialogObject = new GameObject("Split Quantity Dialog", typeof(RectTransform), typeof(Image));
    splitDialogRoot = dialogObject.GetComponent<RectTransform>();
    splitDialogRoot.SetParent(targetCanvas.transform, false);
    splitDialogRoot.anchorMin = new Vector2(0.5f, 0.5f);
    splitDialogRoot.anchorMax = new Vector2(0.5f, 0.5f);
    splitDialogRoot.pivot = new Vector2(0.5f, 0.5f);
    splitDialogRoot.sizeDelta = new Vector2(360f, 230f);
    splitDialogRoot.anchoredPosition = Vector2.zero;
    dialogObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.88f);

    splitDialogTitle = CreateLabel("Split Title", splitDialogRoot, 22f, FontStyles.Bold, new Color(1f, 0.85f, 0.4f, 1f), TextAlignmentOptions.Center);
    splitDialogTitle.rectTransform.anchorMin = new Vector2(0f, 1f);
    splitDialogTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
    splitDialogTitle.rectTransform.pivot = new Vector2(0.5f, 1f);
    splitDialogTitle.rectTransform.anchoredPosition = new Vector2(0f, -14f);
    splitDialogTitle.rectTransform.sizeDelta = new Vector2(-24f, 30f);

    GameObject inputObject = new GameObject("Quantity Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
    RectTransform inputRect = inputObject.GetComponent<RectTransform>();
    inputRect.SetParent(splitDialogRoot, false);
    inputRect.anchorMin = new Vector2(0.5f, 1f);
    inputRect.anchorMax = new Vector2(0.5f, 1f);
    inputRect.pivot = new Vector2(0.5f, 1f);
    inputRect.anchoredPosition = new Vector2(0f, -58f);
    inputRect.sizeDelta = new Vector2(140f, 42f);
    inputObject.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.12f, 1f);
    splitQuantityInput = inputObject.GetComponent<TMP_InputField>();
    splitQuantityInput.contentType = TMP_InputField.ContentType.IntegerNumber;

    TextMeshProUGUI inputText = CreateLabel("Text", inputRect, 22f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
    inputText.rectTransform.anchorMin = Vector2.zero;
    inputText.rectTransform.anchorMax = Vector2.one;
    inputText.rectTransform.offsetMin = new Vector2(8f, 2f);
    inputText.rectTransform.offsetMax = new Vector2(-8f, -2f);
    splitQuantityInput.textComponent = inputText;

    splitMinusButton = CreateButton("Minus", splitDialogRoot, new Vector2(-120f, -112f), "-", () => AdjustSplitQuantity(-1));
    splitPlusButton = CreateButton("Plus", splitDialogRoot, new Vector2(120f, -112f), "+", () => AdjustSplitQuantity(1));
    splitHalfButton = CreateButton("Half", splitDialogRoot, new Vector2(-56f, -112f), "Half", () => SetSplitQuantity(Mathf.CeilToInt(pendingSplitTransfer.MaxAmount * 0.5f)));
    splitMaxButton = CreateButton("Max", splitDialogRoot, new Vector2(56f, -112f), "Max", () => SetSplitQuantity(pendingSplitTransfer.MaxAmount));
    splitCancelButton = CreateButton("Cancel", splitDialogRoot, new Vector2(-78f, -170f), "Cancel", CancelSplitDialog);
    splitConfirmButton = CreateButton("Confirm", splitDialogRoot, new Vector2(78f, -170f), "Confirm", ConfirmSplitDialog);

    ResizeDialogButton(splitMinusButton, 48f, 36f);
    ResizeDialogButton(splitPlusButton, 48f, 36f);
    ResizeDialogButton(splitHalfButton, 86f, 36f);
    ResizeDialogButton(splitMaxButton, 86f, 36f);
    ResizeDialogButton(splitCancelButton, 120f, 38f);
    ResizeDialogButton(splitConfirmButton, 120f, 38f);
    splitDialogRoot.gameObject.SetActive(false);
}

private void ResizeDialogButton(Button button, float width, float height)
{
    if (button == null)
    {
        return;
    }

    RectTransform rect = button.GetComponent<RectTransform>();
    rect.anchorMin = new Vector2(0.5f, 1f);
    rect.anchorMax = new Vector2(0.5f, 1f);
    rect.pivot = new Vector2(0.5f, 1f);
    rect.sizeDelta = new Vector2(width, height);
}

private void AdjustSplitQuantity(int delta)
{
    SetSplitQuantity(GetSplitQuantityInputValue() + delta);
}

private void SetSplitQuantity(int value)
{
    if (splitQuantityInput == null)
    {
        return;
    }

    int clamped = Mathf.Clamp(value, 1, Mathf.Max(1, pendingSplitTransfer.MaxAmount));
    splitQuantityInput.SetTextWithoutNotify(clamped.ToString());
}

private int GetSplitQuantityInputValue()
{
    if (splitQuantityInput == null || !int.TryParse(splitQuantityInput.text, out int value))
    {
        return 1;
    }

    return value;
}

private void CancelSplitDialog()
{
    HideSplitDialog();
}

private void ConfirmSplitDialog()
{
    int maxAmount = GetSplitMaxAmount(pendingSplitTransfer.Direction, pendingSplitTransfer.PlayerSlot, pendingSplitTransfer.ChestSlot);
    int amount = Mathf.Clamp(GetSplitQuantityInputValue(), 1, maxAmount);
    if (amount <= 0)
    {
        HideSplitDialog();
        return;
    }

    if (pendingSplitTransfer.Direction == SplitTransferDirection.PlayerToChest)
    {
        DepositPlayerSlotToChest(pendingSplitTransfer.PlayerSlot, pendingSplitTransfer.ChestSlot, amount);
    }
    else if (pendingSplitTransfer.Direction == SplitTransferDirection.ChestToPlayer)
    {
        TakeChestSlotToPlayer(pendingSplitTransfer.ChestSlot, pendingSplitTransfer.PlayerSlot, amount);
    }

    HideSplitDialog();
    Refresh();
}

private void HideSplitDialog()
{
    if (splitDialogRoot != null)
    {
        splitDialogRoot.gameObject.SetActive(false);
    }

    pendingSplitTransfer = default;
}
```

- [ ] **Step 7: Clean up dialog on close/disable**

In `CloseChest()`, call `HideSplitDialog();` before `DestroyDragIcon();`.

In `CleanupRuntimeUI()`, after `DestroyDragIcon();`, add this code.

```csharp
if (splitDialogRoot != null)
{
    Destroy(splitDialogRoot.gameObject);
    splitDialogRoot = null;
}

pointerDownSlot = null;
splitDragMode = false;
hotbarSplitArmed = false;
hotbarSplitSlot = -1;
pendingSplitTransfer = default;
```

- [ ] **Step 8: Validate chest UI scripts**

Run with Unity MCP:

```text
unityMCP_validate_script(uri: "Assets/Scripts/Object/Storage/StorageChestUI.cs", level: "standard", include_diagnostics: true)
unityMCP_validate_script(uri: "Assets/Scripts/Object/Storage/StorageChestSlotUI.cs", level: "standard", include_diagnostics: true)
```

Expected: `0 errors` for both scripts.

## Task 6: Run Unity Diagnostics for Transfer Behavior

**Files:**
- No source files created. Use Unity MCP `execute_code` diagnostics.

- [ ] **Step 1: Verify local amount-aware chest deposit and take**

Run this Unity MCP execute code in Edit Mode.

```csharp
var go = new GameObject("SplitStackDiagnostic_Player");
var chestGo = new GameObject("SplitStackDiagnostic_Chest");
try
{
    var inventory = go.AddComponent<PlayerInventory>();
    var chest = chestGo.AddComponent<StorageChest>();

    var addAccepted = inventory.AddItemToSlot(ItemType.Stone, 10, 0);
    if (addAccepted != 10) return "FAIL: could not seed player inventory";

    if (!chest.TryRequestStore(inventory, 0, 0, 4)) return "FAIL: split deposit returned false";
    if (inventory.GetSlotAmount(0) != 6) return "FAIL: player slot expected 6 after deposit, got " + inventory.GetSlotAmount(0);
    if (chest.GetSlotAmount(0) != 4) return "FAIL: chest slot expected 4 after deposit, got " + chest.GetSlotAmount(0);

    if (!chest.TryRequestTake(inventory, 0, 1, 2)) return "FAIL: split take returned false";
    if (inventory.GetSlotAmount(1) != 2) return "FAIL: player target expected 2 after take, got " + inventory.GetSlotAmount(1);
    if (chest.GetSlotAmount(0) != 2) return "FAIL: chest slot expected 2 after take, got " + chest.GetSlotAmount(0);

    return "PASS: local split deposit and take move only requested amounts";
}
finally
{
    UnityEngine.Object.DestroyImmediate(go);
    UnityEngine.Object.DestroyImmediate(chestGo);
}
```

Expected: `PASS: local split deposit and take move only requested amounts`.

- [ ] **Step 2: Verify capacity rejection and clamp behavior**

Run this Unity MCP execute code in Edit Mode.

```csharp
var go = new GameObject("SplitStackDiagnostic_Player_Capacity");
var chestGo = new GameObject("SplitStackDiagnostic_Chest_Capacity");
try
{
    var inventory = go.AddComponent<PlayerInventory>();
    var chest = chestGo.AddComponent<StorageChest>();

    inventory.AddItemToSlot(ItemType.Stone, 16, 0);
    inventory.AddItemToSlot(ItemType.Wood, 1, 1);
    if (!chest.TryRequestStore(inventory, 0, 0, 15)) return "FAIL: initial deposit failed";
    if (chest.GetStoreCapacity(inventory, 0, 0) != 1) return "FAIL: expected remaining chest capacity 1";
    if (!chest.TryRequestStore(inventory, 0, 0, 99)) return "FAIL: clamped deposit should accept final 1";
    if (chest.GetSlotAmount(0) != 16) return "FAIL: chest should be full at 16";
    if (chest.TryRequestStore(inventory, 1, 0, 1)) return "FAIL: different item should not deposit into stone stack";

    return "PASS: local split capacity and mismatch rules are enforced";
}
finally
{
    UnityEngine.Object.DestroyImmediate(go);
    UnityEngine.Object.DestroyImmediate(chestGo);
}
```

Expected: `PASS: local split capacity and mismatch rules are enforced`.

- [ ] **Step 3: Verify Fusion API signatures and pending amount storage compile**

Run this Unity MCP execute code after script refresh.

```csharp
var type = typeof(FusionStorageChest);
var deposit = type.GetMethod("RequestDepositToChest", new System.Type[] { typeof(Fusion.NetworkObject), typeof(int), typeof(int), typeof(int) });
var take = type.GetMethod("RequestTakeFromChest", new System.Type[] { typeof(Fusion.NetworkObject), typeof(int), typeof(int), typeof(int) });
var rpcDeposit = type.GetMethod("RPC_DepositToChest");
var rpcTake = type.GetMethod("RPC_TakeFromChest");

if (deposit == null) return "FAIL: amount-aware RequestDepositToChest missing";
if (take == null) return "FAIL: amount-aware RequestTakeFromChest missing";
if (rpcDeposit == null) return "FAIL: RPC_DepositToChest missing";
if (rpcTake == null) return "FAIL: RPC_TakeFromChest missing";
return "PASS: FusionStorageChest exposes amount-aware split transfer API";
```

Expected: `PASS: FusionStorageChest exposes amount-aware split transfer API`.

- [ ] **Step 4: Check Unity console**

Run with Unity MCP:

```text
unityMCP_read_console(action: "get", types: ["error"], count: "20", format: "plain", include_stacktrace: true)
```

Expected: no errors related to `StorageChest`, `FusionStorageChest`, `StorageChestUI`, `StorageChestSlotUI`, `MobileHotbarUI`, or `HotbarSlotUI`.

## Task 7: Manual Playtest Checklist

**Files:**
- No source files modified.

- [ ] **Step 1: Test normal drag remains full-stack**

In Play Mode, drag a stack normally from inventory to chest. Expected: full stack moves as before.

- [ ] **Step 2: Test inventory to chest split**

Long-press an inventory stack greater than `1`, drag to an empty chest slot, choose a quantity lower than the stack amount, and confirm. Expected: only that quantity moves to chest.

- [ ] **Step 3: Test hotbar to chest split**

Long-press a hotbar stack greater than `1`, drag to an empty chest slot, choose a quantity lower than the stack amount, and confirm. Expected: only that quantity moves to chest.

- [ ] **Step 4: Test chest to inventory split**

Long-press a chest stack greater than `1`, drag to an inventory slot, choose a quantity lower than the stack amount, and confirm. Expected: only that quantity moves to inventory.

- [ ] **Step 5: Test chest to hotbar split**

Long-press a chest stack greater than `1`, drag to a hotbar slot, choose a quantity lower than the stack amount, and confirm. Expected: only that quantity moves to hotbar.

- [ ] **Step 6: Test cancel and invalid target behavior**

Open the split dialog and press `Cancel`. Expected: no counts change. Try dropping onto a different-item full target. Expected: no transfer and no item loss.

- [ ] **Step 7: Test Fusion multiplayer stale state**

With two clients, open the same chest. Client A opens a quantity dialog from chest to inventory. Client B changes the source slot before Client A confirms. Client A confirms. Expected: transaction clamps or rejects safely with no item loss.

## Task 8: Final Verification and Optional Commit

**Files:**
- Modified source files from Tasks 1-5.

- [ ] **Step 1: Refresh Unity scripts**

Run with Unity MCP:

```text
unityMCP_refresh_unity(mode: "force", scope: "scripts", compile: "request", wait_for_ready: true)
```

Expected: editor reports ready for tools.

- [ ] **Step 2: Validate all edited scripts**

Run with Unity MCP:

```text
unityMCP_validate_script(uri: "Assets/Scripts/Object/Storage/StorageChest.cs", level: "standard", include_diagnostics: true)
unityMCP_validate_script(uri: "Assets/Scripts/PhotonFusion/FusionStorageChest.cs", level: "standard", include_diagnostics: true)
unityMCP_validate_script(uri: "Assets/Scripts/Object/Storage/StorageChestUI.cs", level: "standard", include_diagnostics: true)
unityMCP_validate_script(uri: "Assets/Scripts/Object/Storage/StorageChestSlotUI.cs", level: "standard", include_diagnostics: true)
unityMCP_validate_script(uri: "Assets/Scripts/Player/Survival/MobileHotbarUI.cs", level: "standard", include_diagnostics: true)
unityMCP_validate_script(uri: "Assets/Scripts/Player/Survival/HotbarSlotUI.cs", level: "standard", include_diagnostics: true)
```

Expected: `0 errors` for each script.

- [ ] **Step 3: Run diagnostics from Task 6 again**

Expected: all diagnostics return `PASS`.

- [ ] **Step 4: Inspect git diff**

Run:

```powershell
git diff -- Assets/Scripts/Object/Storage/StorageChest.cs Assets/Scripts/PhotonFusion/FusionStorageChest.cs Assets/Scripts/Object/Storage/StorageChestUI.cs Assets/Scripts/Object/Storage/StorageChestSlotUI.cs Assets/Scripts/Player/Survival/MobileHotbarUI.cs Assets/Scripts/Player/Survival/HotbarSlotUI.cs docs/superpowers/specs/2026-06-15-chest-split-stack-design.md docs/superpowers/plans/2026-06-15-chest-split-stack.md
```

Expected: only split-stack related changes appear.

- [ ] **Step 5: Commit only if the user explicitly requests it**

Do not commit automatically. If the user asks for a commit, run:

```powershell
git status --short
git add Assets/Scripts/Object/Storage/StorageChest.cs Assets/Scripts/PhotonFusion/FusionStorageChest.cs Assets/Scripts/Object/Storage/StorageChestUI.cs Assets/Scripts/Object/Storage/StorageChestSlotUI.cs Assets/Scripts/Player/Survival/MobileHotbarUI.cs Assets/Scripts/Player/Survival/HotbarSlotUI.cs docs/superpowers/specs/2026-06-15-chest-split-stack-design.md docs/superpowers/plans/2026-06-15-chest-split-stack.md
git commit -m "Add chest split-stack transfers"
```

Expected: one commit containing only split-stack source, spec, and plan changes.
