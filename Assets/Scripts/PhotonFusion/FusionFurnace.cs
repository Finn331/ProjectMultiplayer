using Fusion;
using UnityEngine;

public class FusionFurnace : NetworkBehaviour
{
    private const int SlotCount = 4;
    private const int MaxStack = 16;
    public const float SmeltTimeSeconds = 10f;
    private const float FoodCookTimeSeconds = 8f;
    private const float WoodToAshTimeSeconds = 8f;
    private const float FuelBurnTimePerWood = 30f;

    [Networked] private float BurnTimer { get; set; }
    [Networked] private NetworkBool IsLit { get; set; }
    [Networked] private int FuelType { get; set; }
    [Networked] private int FuelAmount { get; set; }

    [Networked, Capacity(SlotCount)] private NetworkArray<int> InputTypes { get; }
    [Networked, Capacity(SlotCount)] private NetworkArray<int> InputAmounts { get; }
    [Networked, Capacity(SlotCount)] private NetworkArray<float> CookTimers { get; }
    [Networked, Capacity(SlotCount)] private NetworkArray<int> OutputTypes { get; }
    [Networked, Capacity(SlotCount)] private NetworkArray<int> OutputAmounts { get; }

    private readonly Vector3[] slotPositions = new Vector3[]
    {
        new Vector3(0.15f, 0.55f, 0.15f),
        new Vector3(-0.15f, 0.55f, 0.15f),
        new Vector3(0.15f, 0.55f, -0.15f),
        new Vector3(-0.15f, 0.55f, -0.15f)
    };

    private readonly GameObject[] slotVisuals = new GameObject[SlotCount];

    public bool HasFuel => BurnTimer > 0f || FuelAmount > 0;
    public float FuelTimerValue => BurnTimer;
    public int FuelStackAmount => FuelAmount;
    public bool IsLitValue => IsLit;

    public bool HasOutput(int slot) => slot >= 0 && slot < SlotCount && OutputAmounts.Get(slot) > 0;
    public int GetOutputCount(int slot) => slot >= 0 && slot < SlotCount ? OutputAmounts.Get(slot) : 0;
    public int GetOutputType(int slot) => slot >= 0 && slot < SlotCount ? OutputTypes.Get(slot) : -1;

    public float GetSlotTimer(int slot) => slot >= 0 && slot < SlotCount ? CookTimers.Get(slot) : -1f;
    public int GetSlotInputType(int slot) => slot >= 0 && slot < SlotCount ? InputTypes.Get(slot) : -1;
    public int GetSlotQuantity(int slot) => slot >= 0 && slot < SlotCount ? InputAmounts.Get(slot) : 0;

    public void ToggleLit()
    {
        if (HasStateAuthority) ToggleLitInternal();
        else RPC_ToggleLit();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_ToggleLit()
    {
        ToggleLitInternal();
    }

    private void ToggleLitInternal()
    {
        if (!IsLit && BurnTimer <= 0f && FuelAmount <= 0) return;
        IsLit = !IsLit;
    }

    public override void Spawned()
    {
        if (!HasStateAuthority) return;

        BurnTimer = 0f;
        IsLit = false;
        FuelType = -1;
        FuelAmount = 0;

        for (int i = 0; i < SlotCount; i++)
        {
            InputTypes.Set(i, -1);
            InputAmounts.Set(i, 0);
            CookTimers.Set(i, 0f);
            OutputTypes.Set(i, -1);
            OutputAmounts.Set(i, 0);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || !IsLit) return;

        if (BurnTimer <= 0f)
        {
            if (FuelAmount <= 0)
            {
                IsLit = false;
                return;
            }

            FuelAmount -= 1;
            BurnTimer = GetFuelBurnTime(FuelType);
            if (FuelAmount <= 0) FuelType = -1;
        }

        float delta = Runner.DeltaTime;
        BurnTimer = Mathf.Max(0f, BurnTimer - delta);

        for (int i = 0; i < SlotCount; i++)
        {
            int inputType = InputTypes.Get(i);
            int inputAmount = InputAmounts.Get(i);
            if (inputType < 0 || inputAmount <= 0) continue;

            int outputType = GetOutputTypeForInput(inputType);
            int outputSlot = FindOutputSlot(outputType);
            if (outputSlot < 0) continue;

            float progress = CookTimers.Get(i) + delta;
            float cookTime = GetCookTime(inputType);
            if (progress >= cookTime)
            {
                progress = 0f;
                InputAmounts.Set(i, inputAmount - 1);
                AddOutput(outputSlot, outputType, 1);

                if (inputAmount - 1 <= 0)
                {
                    InputTypes.Set(i, -1);
                    CookTimers.Set(i, 0f);
                }
                else
                {
                    CookTimers.Set(i, progress);
                }
            }
            else
            {
                CookTimers.Set(i, progress);
            }
        }
    }

    public override void Render()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            bool hasInput = InputAmounts.Get(i) > 0;
            bool hasOutput = OutputAmounts.Get(i) > 0;
            bool shouldShow = hasInput || hasOutput;

            if (shouldShow && slotVisuals[i] == null)
            {
                slotVisuals[i] = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slotVisuals[i].name = hasOutput ? "FurnaceOutputVisual" : "FurnaceInputVisual";
                slotVisuals[i].transform.SetParent(transform, false);
                slotVisuals[i].transform.localPosition = slotPositions[i];
                slotVisuals[i].transform.localScale = hasOutput
                    ? new Vector3(0.15f, 0.06f, 0.25f)
                    : new Vector3(0.2f, 0.2f, 0.2f);
            }
            else if (!shouldShow && slotVisuals[i] != null)
            {
                Destroy(slotVisuals[i]);
                slotVisuals[i] = null;
            }
        }
    }

    public bool TryAddFuel(PlayerInventory inventory)
    {
        if (inventory == null || !inventory.HasItem(ItemType.Wood, 1)) return false;
        NetworkObject inventoryObject = inventory.GetComponentInParent<NetworkObject>();
        if (inventoryObject == null) return false;

        if (HasStateAuthority) AddFuelInternal(inventory, -1);
        else RPC_AddFuel(inventoryObject, -1);
        return true;
    }

    public bool TryAddToFurnaceFromSlot(PlayerInventory inventory, int playerSlot, bool isFuel, int furnaceSlot)
    {
        if (inventory == null) return false;
        NetworkObject inventoryObject = inventory.GetComponentInParent<NetworkObject>();
        if (inventoryObject == null) return false;

        if (isFuel)
        {
            if (playerSlot >= 0 && inventory.GetSlotItemType(playerSlot) != ItemType.Wood) return false;
            if (playerSlot >= 0 && inventory.GetSlotAmount(playerSlot) <= 0) return false;
        }
        else
        {
            ItemType? itemType = inventory.GetSlotItemType(playerSlot);
            if (!IsValidInput(itemType)) return false;
        }

        if (HasStateAuthority) AddToFurnaceInternal(inventory, playerSlot, isFuel, furnaceSlot);
        else RPC_AddToFurnace(inventoryObject, playerSlot, isFuel, furnaceSlot);
        return true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_AddFuel(NetworkObject inventoryObject, int playerSlot)
    {
        PlayerInventory inventory = inventoryObject != null ? inventoryObject.GetComponentInChildren<PlayerInventory>() : null;
        if (inventory == null) return;
        AddFuelInternal(inventory, playerSlot);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_AddToFurnace(NetworkObject inventoryObject, int playerSlot, bool isFuel, int furnaceSlot)
    {
        PlayerInventory inventory = inventoryObject != null ? inventoryObject.GetComponentInChildren<PlayerInventory>() : null;
        if (inventory == null) return;
        AddToFurnaceInternal(inventory, playerSlot, isFuel, furnaceSlot);
    }

    private void AddToFurnaceInternal(PlayerInventory inventory, int playerSlot, bool isFuel, int furnaceSlot)
    {
        if (isFuel)
        {
            AddFuelInternal(inventory, playerSlot);
            return;
        }

        ItemType? itemType = inventory.GetSlotItemType(playerSlot);
        if (!IsValidInput(itemType)) return;

        int inputType = (int)itemType.Value;
        int targetSlot = furnaceSlot >= 0 ? furnaceSlot : FindInputSlot(inputType);
        if (targetSlot < 0) return;

        int currentType = InputTypes.Get(targetSlot);
        int currentAmount = InputAmounts.Get(targetSlot);
        if (currentType >= 0 && currentType != inputType) return;

        int freeSpace = MaxStack - currentAmount;
        if (freeSpace <= 0) return;

        int transferAmount = Mathf.Min(inventory.GetSlotAmount(playerSlot), freeSpace);
        if (transferAmount <= 0) return;
        if (!inventory.RemoveItemFromSlot(playerSlot, transferAmount, out ItemType removedType)) return;
        if (removedType != itemType.Value)
        {
            inventory.AddItemToSlot(removedType, transferAmount, playerSlot);
            return;
        }

        InputTypes.Set(targetSlot, inputType);
        InputAmounts.Set(targetSlot, currentAmount + transferAmount);
    }

    private void AddFuelInternal(PlayerInventory inventory, int playerSlot)
    {
        int availableAmount = playerSlot >= 0 ? inventory.GetSlotAmount(playerSlot) : 1;
        if (availableAmount <= 0) return;

        int freeSpace = FuelType == -1 || FuelType == (int)ItemType.Wood ? MaxStack - FuelAmount : 0;
        if (freeSpace <= 0) return;

        int transferAmount = Mathf.Min(availableAmount, freeSpace);
        if (playerSlot >= 0)
        {
            if (!inventory.RemoveItemFromSlot(playerSlot, transferAmount, out ItemType removedType)) return;
            if (removedType != ItemType.Wood)
            {
                inventory.AddItemToSlot(removedType, transferAmount, playerSlot);
                return;
            }
        }
        else if (!inventory.RemoveItem(ItemType.Wood, transferAmount))
        {
            return;
        }

        FuelType = (int)ItemType.Wood;
        FuelAmount += transferAmount;
    }

    public bool TryPickupOutput(PlayerInventory inventory, int slot)
    {
        if (inventory == null) return false;
        NetworkObject inventoryObject = inventory.GetComponentInParent<NetworkObject>();
        if (inventoryObject == null) return false;

        if (HasStateAuthority) PickupOutputInternal(inventory, slot);
        else RPC_PickupOutput(inventoryObject, slot);
        return true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_PickupOutput(NetworkObject inventoryObject, int slot)
    {
        PlayerInventory inventory = inventoryObject != null ? inventoryObject.GetComponentInChildren<PlayerInventory>() : null;
        if (inventory == null) return;
        PickupOutputInternal(inventory, slot);
    }

    private void PickupOutputInternal(PlayerInventory inventory, int slot)
    {
        if (slot < 0 || slot >= SlotCount) return;
        int outputType = OutputTypes.Get(slot);
        int outputAmount = OutputAmounts.Get(slot);
        if (outputType < 0 || outputAmount <= 0) return;

        int accepted = inventory.AddItem((ItemType)outputType, outputAmount);
        int remaining = outputAmount - accepted;
        OutputAmounts.Set(slot, Mathf.Max(0, remaining));
        if (remaining <= 0) OutputTypes.Set(slot, -1);
    }

    private void AddOutput(int slot, int outputType, int amount)
    {
        if (OutputTypes.Get(slot) == -1) OutputTypes.Set(slot, outputType);
        OutputAmounts.Set(slot, Mathf.Min(MaxStack, OutputAmounts.Get(slot) + amount));
    }

    private int FindInputSlot(int inputType)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (InputTypes.Get(i) == inputType && InputAmounts.Get(i) < MaxStack)
                return i;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (InputTypes.Get(i) < 0 && InputAmounts.Get(i) <= 0)
                return i;
        }

        return -1;
    }

    private int FindOutputSlot(int outputType)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (OutputTypes.Get(i) == outputType && OutputAmounts.Get(i) < MaxStack)
                return i;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (OutputTypes.Get(i) < 0 && OutputAmounts.Get(i) <= 0)
                return i;
        }

        return -1;
    }

    private static bool IsValidInput(ItemType? itemType)
    {
        return itemType == ItemType.Iron
            || itemType == ItemType.RawChicken
            || itemType == ItemType.RawFish
            || itemType == ItemType.Wood;
    }

    private static int GetOutputTypeForInput(int inputType)
    {
        ItemType itemType = (ItemType)inputType;
        if (itemType == ItemType.Iron) return (int)ItemType.IronIngot;
        if (itemType == ItemType.RawChicken) return (int)ItemType.CookedChicken;
        if (itemType == ItemType.RawFish) return (int)ItemType.CookedFish;
        if (itemType == ItemType.Wood) return (int)ItemType.Ash;
        return -1;
    }

    private static float GetCookTime(int inputType)
    {
        ItemType itemType = (ItemType)inputType;
        if (itemType == ItemType.Iron) return SmeltTimeSeconds;
        if (itemType == ItemType.Wood) return WoodToAshTimeSeconds;
        return FoodCookTimeSeconds;
    }

    private static float GetFuelBurnTime(int fuelType)
    {
        return fuelType == (int)ItemType.Wood ? FuelBurnTimePerWood : 0f;
    }
}
