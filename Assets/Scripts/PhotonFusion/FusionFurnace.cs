using Fusion;
using UnityEngine;

public class FusionFurnace : NetworkBehaviour
{
    private const int SlotCount = 4;
    public const float SmeltTimeSeconds = 10f;
    private const float FuelBurnTimePerWood = 30f;

    [Networked] private float FuelTimer { get; set; }
    [Networked, Capacity(SlotCount)]
    private NetworkArray<float> SlotTimers { get; }
    [Networked, Capacity(SlotCount)]
    private NetworkArray<bool> SlotHasOutput { get; }

    private readonly Vector3[] slotPositions = new Vector3[]
    {
        new Vector3(0.15f, 0.55f, 0.15f),
        new Vector3(-0.15f, 0.55f, 0.15f),
        new Vector3(0.15f, 0.55f, -0.15f),
        new Vector3(-0.15f, 0.55f, -0.15f)
    };

    private readonly GameObject[] slotVisuals = new GameObject[SlotCount];

    public bool HasFuel => FuelTimer > 0f;
    public bool HasOutput(int slot) => slot >= 0 && slot < SlotCount && SlotHasOutput.Get(slot);

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            FuelTimer = 0f;
            for (int i = 0; i < SlotCount; i++)
            {
                SlotTimers.Set(i, -1f);
                SlotHasOutput.Set(i, false);
            }
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        float delta = Runner.DeltaTime;

        if (FuelTimer > 0f)
        {
            FuelTimer = Mathf.Max(0f, FuelTimer - delta);
        }

        bool hasFuel = FuelTimer > 0f;

        for (int i = 0; i < SlotCount; i++)
        {
            float timer = SlotTimers.Get(i);
            if (timer > 0f && hasFuel)
            {
                timer = Mathf.Max(0f, timer - delta);
                SlotTimers.Set(i, timer);

                if (timer <= 0f)
                {
                    SlotHasOutput.Set(i, true);
                }
            }
        }
    }

    public override void Render()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            bool hasOutput = SlotHasOutput.Get(i);
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
        if (!HasStateAuthority || inventory == null) return false;
        if (!inventory.HasItem(ItemType.Wood, 1)) return false;
        if (!inventory.RemoveItem(ItemType.Wood, 1)) return false;

        FuelTimer += FuelBurnTimePerWood;
        return true;
    }

    public bool TryAddIron(PlayerInventory inventory)
    {
        if (!HasStateAuthority || inventory == null) return false;
        if (!inventory.HasItem(ItemType.Iron, 1)) return false;

        int freeSlot = FindFreeSlot();
        if (freeSlot < 0) return false;

        if (!inventory.RemoveItem(ItemType.Iron, 1)) return false;

        SlotTimers.Set(freeSlot, SmeltTimeSeconds);
        SlotHasOutput.Set(freeSlot, false);
        return true;
    }

    public bool TryPickupOutput(PlayerInventory inventory, int slot)
    {
        if (!HasStateAuthority || inventory == null) return false;
        if (slot < 0 || slot >= SlotCount) return false;
        if (!SlotHasOutput.Get(slot)) return false;

        inventory.AddItem(ItemType.IronIngot, 1);
        SlotTimers.Set(slot, -1f);
        SlotHasOutput.Set(slot, false);
        return true;
    }

    private int FindFreeSlot()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (SlotTimers.Get(i) < 0f && !SlotHasOutput.Get(i))
            {
                return i;
            }
        }
        return -1;
    }
}
