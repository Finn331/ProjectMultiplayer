using Fusion;
using UnityEngine;

public class CampfireCooking : NetworkBehaviour
{
    private const int SlotCount = 4;
    public const float CookTimeSeconds = 20f;

    [SerializeField] private GameObject drumstickRawPrefab;
    [SerializeField] private GameObject drumstickCookedPrefab;
    [SerializeField] private GameObject steakRawPrefab;
    [SerializeField] private GameObject steakCookedPrefab;
    [SerializeField] private GameObject fishFilletRawPrefab;
    [SerializeField] private GameObject fishFilletCookedPrefab;
    [SerializeField] private GameObject wholeBirdRawPrefab;
    [SerializeField] private GameObject wholeBirdCookedPrefab;

    [Networked, Capacity(SlotCount)]
    private NetworkArray<float> SlotTimers { get; }
    [Networked, Capacity(SlotCount)]
    private NetworkArray<int> SlotFoodIndices { get; }

    private readonly GameObject[] slotVisuals = new GameObject[SlotCount];
    private readonly bool[] slotHasCooked = new bool[SlotCount];
    private readonly Vector3[] slotPositions = new Vector3[]
    {
        new Vector3(0.2f, 0.55f, 0.2f),
        new Vector3(-0.2f, 0.55f, 0.2f),
        new Vector3(0.2f, 0.55f, -0.2f),
        new Vector3(-0.2f, 0.55f, -0.2f)
    };

    private (GameObject raw, GameObject cooked)[] foodPairs;

    public override void Spawned()
    {
        foodPairs = new (GameObject raw, GameObject cooked)[]
        {
            (drumstickRawPrefab, drumstickCookedPrefab),
            (steakRawPrefab, steakCookedPrefab),
            (fishFilletRawPrefab, fishFilletCookedPrefab),
            (wholeBirdRawPrefab, wholeBirdCookedPrefab)
        };

        if (HasStateAuthority)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                SlotTimers.Set(i, -1f);
                SlotFoodIndices.Set(i, -1);
            }
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
        {
            return;
        }

        float delta = Runner.DeltaTime;
        for (int i = 0; i < SlotCount; i++)
        {
            float timer = SlotTimers.Get(i);
            if (timer >= 0f)
            {
                timer -= delta;
                SlotTimers.Set(i, timer);

                if (timer <= 0f && !slotHasCooked[i])
                {
                    slotHasCooked[i] = true;
                    SwapToCookedVisual(i);
                }
            }
        }
    }

    public bool TryPlaceRawMeat(PlayerInventory inventory, ItemType rawType)
    {
        if (!HasStateAuthority || inventory == null)
        {
            return false;
        }

        if (rawType != ItemType.RawChicken && rawType != ItemType.RawFish)
        {
            return false;
        }

        if (!inventory.HasItem(rawType, 1))
        {
            return false;
        }

        int freeSlot = FindFreeSlot();
        if (freeSlot < 0)
        {
            return false;
        }

        if (!inventory.RemoveItem(rawType, 1))
        {
            return false;
        }

        int foodIndex;
        if (rawType == ItemType.RawFish)
        {
            foodIndex = 2;
        }
        else
        {
            int[] chickenIndices = new int[] { 0, 1, 3 };
            foodIndex = chickenIndices[Random.Range(0, chickenIndices.Length)];
        }

        SlotTimers.Set(freeSlot, CookTimeSeconds);
        SlotFoodIndices.Set(freeSlot, foodIndex);
        slotHasCooked[freeSlot] = false;

        SpawnRawVisual(freeSlot, foodIndex);
        return true;
    }

    public bool TryPickupCooked(PlayerInventory inventory, int slot)
    {
        if (!HasStateAuthority || inventory == null)
        {
            return false;
        }

        if (slot < 0 || slot >= SlotCount)
        {
            return false;
        }

        if (!slotHasCooked[slot])
        {
            return false;
        }

        ItemType cookedType = GetCookedItemType(slot);
        inventory.AddItem(cookedType, 1);
        ClearSlot(slot);
        return true;
    }

    private ItemType GetCookedItemType(int slot)
    {
        int foodIndex = SlotFoodIndices.Get(slot);
        return foodIndex == 2 ? ItemType.CookedFish : ItemType.CookedChicken;
    }

    public bool HasCookedFood(int slot)
    {
        if (slot < 0 || slot >= SlotCount)
        {
            return false;
        }

        float timer = SlotTimers.Get(slot);
        int foodIndex = SlotFoodIndices.Get(slot);
        return timer <= 0f && foodIndex >= 0;
    }

    private int FindFreeSlot()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (SlotTimers.Get(i) < 0f)
            {
                return i;
            }
        }

        return -1;
    }

    private void SpawnRawVisual(int slot, int foodIndex)
    {
        if (foodIndex < 0 || foodIndex >= foodPairs.Length)
        {
            return;
        }

        ClearSlotVisual(slot);

        GameObject rawPrefab = foodPairs[foodIndex].raw;
        if (rawPrefab != null)
        {
            slotVisuals[slot] = Instantiate(rawPrefab, transform);
            slotVisuals[slot].transform.localPosition = slotPositions[slot];
            slotVisuals[slot].transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        }
    }

    private void SwapToCookedVisual(int slot)
    {
        if (slot < 0 || slot >= SlotCount)
        {
            return;
        }

        int foodIndex = SlotFoodIndices.Get(slot);
        if (foodIndex < 0 || foodIndex >= foodPairs.Length)
        {
            return;
        }

        if (slotVisuals[slot] != null)
        {
            Destroy(slotVisuals[slot]);
        }

        GameObject cookedPrefab = foodPairs[foodIndex].cooked;
        if (cookedPrefab != null)
        {
            slotVisuals[slot] = Instantiate(cookedPrefab, transform);
            slotVisuals[slot].transform.localPosition = slotPositions[slot];
            slotVisuals[slot].transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        }
    }

    private void ClearSlot(int slot)
    {
        SlotTimers.Set(slot, -1f);
        SlotFoodIndices.Set(slot, -1);
        slotHasCooked[slot] = false;
        ClearSlotVisual(slot);
    }

    private void ClearSlotVisual(int slot)
    {
        if (slotVisuals[slot] != null)
        {
            Destroy(slotVisuals[slot]);
            slotVisuals[slot] = null;
        }
    }
}
