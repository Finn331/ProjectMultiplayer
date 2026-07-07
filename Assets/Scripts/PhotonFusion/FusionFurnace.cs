using Fusion;
using UnityEngine;

public class FusionFurnace : NetworkBehaviour
{
    private const int SlotCount = 4;
    public const float SmeltTimeSeconds = 10f;
    private const float FuelBurnTimePerWood = 30f;

    [Networked] private float FuelTimer { get; set; }
    [Networked] private NetworkBool IsLit { get; set; }
    [Networked, Capacity(SlotCount)]
    private NetworkArray<float> SlotTimers { get; }
    [Networked, Capacity(SlotCount)]
    private NetworkArray<int> SlotOutputCounts { get; }
    [Networked, Capacity(SlotCount)]
    private NetworkArray<int> SlotInputTypes { get; }
    [Networked, Capacity(SlotCount)]
    private NetworkArray<int> SlotQuantities { get; }

    private readonly Vector3[] slotPositions = new Vector3[]
    {
        new Vector3(0.15f, 0.55f, 0.15f),
        new Vector3(-0.15f, 0.55f, 0.15f),
        new Vector3(0.15f, 0.55f, -0.15f),
        new Vector3(-0.15f, 0.55f, -0.15f)
    };

    private readonly GameObject[] slotVisuals = new GameObject[SlotCount];

    public bool HasFuel => FuelTimer > 0f;
    public bool HasOutput(int slot) => slot >= 0 && slot < SlotCount && SlotOutputCounts.Get(slot) > 0;
    public int GetOutputCount(int slot) => slot >= 0 && slot < SlotCount ? SlotOutputCounts.Get(slot) : 0;
    public float FuelTimerValue => FuelTimer;
    public float GetSlotTimer(int slot) => slot >= 0 && slot < SlotCount ? SlotTimers.Get(slot) : -1f;
    public int GetSlotInputType(int slot) => slot >= 0 && slot < SlotCount ? SlotInputTypes.Get(slot) : -1;
    public int GetSlotQuantity(int slot) => slot >= 0 && slot < SlotCount ? SlotQuantities.Get(slot) : 0;
    public bool IsLitValue => IsLit;

    public void ToggleLit()
    {
        RPC_ToggleLit();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_ToggleLit()
    {
        if (!HasFuel) return;
        IsLit = !IsLit;
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            FuelTimer = 0f;
            for (int i = 0; i < SlotCount; i++)
            {
                SlotTimers.Set(i, -1f);
                SlotOutputCounts.Set(i, 0);
                SlotInputTypes.Set(i, -1);
                SlotQuantities.Set(i, 0);
            }
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        float delta = Runner.DeltaTime;

        if (IsLit && FuelTimer > 0f)
        {
            FuelTimer = Mathf.Max(0f, FuelTimer - delta);
        }

        bool hasFuel = FuelTimer > 0f;
        if (!hasFuel)
        {
            IsLit = false;
        }

        if (!IsLit)
        {
            return;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            float timer = SlotTimers.Get(i);
            if (timer > 0f && hasFuel && IsLit)
            {
                timer = Mathf.Max(0f, timer - delta);
                SlotTimers.Set(i, timer);

                if (timer <= 0f)
                {
                    SlotOutputCounts.Set(i, SlotOutputCounts.Get(i) + 1);
                }
            }
        }
    }

    public override void Render()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            bool hasOutput = SlotOutputCounts.Get(i) > 0;
            float timer = SlotTimers.Get(i);
            bool hasInput = timer > 0f || hasOutput;

            if (hasInput && slotVisuals[i] == null)
            {
                slotVisuals[i] = GameObject.CreatePrimitive(hasOutput ? PrimitiveType.Cube : PrimitiveType.Cube);
                slotVisuals[i].name = hasOutput ? "IronIngotVisual" : "IronVisual";
                slotVisuals[i].transform.SetParent(transform, false);
                slotVisuals[i].transform.localPosition = slotPositions[i];
                slotVisuals[i].transform.localScale = hasOutput
                    ? new Vector3(0.15f, 0.06f, 0.25f)
                    : new Vector3(0.2f, 0.2f, 0.2f);

                if (hasOutput)
                {
                    Renderer r = slotVisuals[i].GetComponent<Renderer>();
                    if (r != null) r.material.color = new Color(0.8f, 0.8f, 0.85f);
                }
                else
                {
                    Renderer r = slotVisuals[i].GetComponent<Renderer>();
                    if (r != null) r.material.color = new Color(0.4f, 0.3f, 0.25f);
                }
            }
            else if (!hasInput && slotVisuals[i] != null)
            {
                Destroy(slotVisuals[i]);
                slotVisuals[i] = null;
            }
        }
    }

    public bool TryAddFuel(PlayerInventory inventory)
    {
        return TryAddToFurnaceFromSlot(inventory, -1, true, -1);
    }

    public bool TryAddToFurnaceFromSlot(PlayerInventory inventory, int playerSlot, bool isFuel, int furnaceSlot)
    {
        if (inventory == null) return false;

        ItemType? itemType = isFuel ? null : (playerSlot >= 0 ? inventory.GetSlotItemType(playerSlot) : null);
        if (isFuel)
        {
            if (!inventory.HasItem(ItemType.Wood, 1)) return false;
        }
        else
        {
            if (itemType == null) return false;
            if (itemType != ItemType.Iron && itemType != ItemType.RawChicken && itemType != ItemType.RawFish && itemType != ItemType.Wood) return false;
            if (inventory.GetSlotAmount(playerSlot) <= 0) return false;
        }

        NetworkObject inventoryObject = inventory.GetComponentInParent<NetworkObject>();
        if (inventoryObject == null) return false;

        if (HasStateAuthority) AddToFurnaceInternal(inventory, playerSlot, isFuel, furnaceSlot);
        else RPC_AddToFurnace(inventoryObject, playerSlot, isFuel, furnaceSlot);
        return true;
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
            if (!inventory.HasItem(ItemType.Wood, 1)) return;
            if (!inventory.RemoveItem(ItemType.Wood, 1)) return;
            FuelTimer += FuelBurnTimePerWood;
            return;
        }

        ItemType? itemType = inventory.GetSlotItemType(playerSlot);
        if (itemType == null) return;

        int inputType = itemType == ItemType.Iron ? 0 : (itemType == ItemType.RawChicken ? 1 : (itemType == ItemType.RawFish ? 2 : (itemType == ItemType.Wood ? 3 : -1)));
        if (inputType < 0) return;

        if (inventory.GetSlotAmount(playerSlot) <= 0) return;

        int targetSlot = furnaceSlot >= 0 ? furnaceSlot : FindSlotForType(inputType);
        if (targetSlot < 0) return;

        if (!inventory.RemoveItemFromSlot(playerSlot, 1, out ItemType removedType)) return;
        if (removedType != itemType.Value)
        {
            inventory.AddItemToSlot(removedType, 1, playerSlot);
            return;
        }

        int existingQty = SlotQuantities.Get(targetSlot);
        SlotQuantities.Set(targetSlot, existingQty + 1);

        if (existingQty == 0)
        {
            float cookTime = GetCookTime(inputType);
            SlotTimers.Set(targetSlot, cookTime);
            SlotOutputCounts.Set(targetSlot, 0);
            SlotInputTypes.Set(targetSlot, inputType);
        }
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
        if (SlotOutputCounts.Get(slot) <= 0) return;

        int inputType = SlotInputTypes.Get(slot);
        ItemType outputItem = inputType == 0 ? ItemType.IronIngot
            : (inputType == 1 ? ItemType.CookedChicken
            : (inputType == 2 ? ItemType.CookedFish
            : (inputType == 3 ? ItemType.Ash
            : ItemType.IronIngot)));

        inventory.AddItem(outputItem, 1);
        SlotOutputCounts.Set(slot, Mathf.Max(0, SlotOutputCounts.Get(slot) - 1));

        int remaining = SlotQuantities.Get(slot) - 1;
        SlotQuantities.Set(slot, Mathf.Max(0, remaining));

        if (remaining > 0)
        {
            float nextTime = GetCookTime(inputType);
            SlotTimers.Set(slot, nextTime);
        }
        else
        {
            SlotTimers.Set(slot, -1f);
            SlotInputTypes.Set(slot, -1);
            SlotQuantities.Set(slot, 0);
        }
    }

    private float GetCookTime(int inputType)
    {
        return inputType == 0 ? SmeltTimeSeconds : 8f;
    }

    private int FindSlotForType(int inputType)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (SlotInputTypes.Get(i) == inputType && SlotQuantities.Get(i) < 16)
                return i;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (SlotTimers.Get(i) < 0f && SlotOutputCounts.Get(i) == 0)
                return i;
        }

        return -1;
    }
}
