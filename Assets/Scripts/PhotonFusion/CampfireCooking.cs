using Fusion;
using UnityEngine;

public class CampfireCooking : NetworkBehaviour
{
    private const int BaseSlotCount = 4;
    private const int PotSlotCount = 8;
    public const float CookTimeSeconds = 20f;
    private const float PotCookTimeSeconds = 12f;

    [SerializeField] private GameObject drumstickRawPrefab;
    [SerializeField] private GameObject drumstickCookedPrefab;
    [SerializeField] private GameObject steakRawPrefab;
    [SerializeField] private GameObject steakCookedPrefab;
    [SerializeField] private GameObject fishFilletRawPrefab;
    [SerializeField] private GameObject fishFilletCookedPrefab;
    [SerializeField] private GameObject wholeBirdRawPrefab;
    [SerializeField] private GameObject wholeBirdCookedPrefab;

    [Networked] private NetworkBool HasPot { get; set; }

    [Networked, Capacity(PotSlotCount)]
    private NetworkArray<float> SlotTimers { get; }
    [Networked, Capacity(PotSlotCount)]
    private NetworkArray<int> SlotFoodIndices { get; }

    private readonly GameObject[] slotVisuals = new GameObject[PotSlotCount];
    private readonly bool[] slotHasCooked = new bool[PotSlotCount];
    private readonly Vector3[] baseSlotPositions = new Vector3[]
    {
        new Vector3(0.2f, 0.55f, 0.2f),
        new Vector3(-0.2f, 0.55f, 0.2f),
        new Vector3(0.2f, 0.55f, -0.2f),
        new Vector3(-0.2f, 0.55f, -0.2f)
    };
    private readonly Vector3[] potSlotPositions = new Vector3[]
    {
        new Vector3(0.35f, 0.7f, 0.35f),
        new Vector3(-0.35f, 0.7f, 0.35f),
        new Vector3(0.35f, 0.7f, -0.35f),
        new Vector3(-0.35f, 0.7f, -0.35f),
        new Vector3(0.15f, 0.7f, 0.15f),
        new Vector3(-0.15f, 0.7f, 0.15f),
        new Vector3(0.15f, 0.7f, -0.15f),
        new Vector3(-0.15f, 0.7f, -0.15f)
    };

    private GameObject potVisual;
    private (GameObject raw, GameObject cooked)[] foodPairs;

    private int CurrentSlotCount => HasPot ? PotSlotCount : BaseSlotCount;
    private float CurrentCookTime => HasPot ? PotCookTimeSeconds : CookTimeSeconds;

    public bool HasCookingPot => HasPot;

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
            for (int i = 0; i < PotSlotCount; i++)
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
        for (int i = 0; i < CurrentSlotCount; i++)
        {
            float timer = SlotTimers.Get(i);
            if (timer > 0f)
            {
                timer = Mathf.Max(0f, timer - delta);
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
            if (PickupUIManager.instance != null)
            {
                PickupUIManager.instance.ShowInfo("Campfire Full");
            }
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

        SlotTimers.Set(freeSlot, CurrentCookTime);
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

        if (slot < 0 || slot >= CurrentSlotCount)
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
        if (slot < 0 || slot >= CurrentSlotCount)
        {
            return false;
        }

        float timer = SlotTimers.Get(slot);
        int foodIndex = SlotFoodIndices.Get(slot);
        return timer <= 0f && foodIndex >= 0;
    }

    private int FindFreeSlot()
    {
        for (int i = 0; i < CurrentSlotCount; i++)
        {
            if (SlotTimers.Get(i) < 0f && SlotFoodIndices.Get(i) < 0)
            {
                return i;
            }
        }

        return -1;
    }

    public bool TryPlaceCookingPot(PlayerInventory inventory)
    {
        if (!HasStateAuthority || inventory == null)
        {
            return false;
        }

        if (HasPot)
        {
            return false;
        }

        if (!inventory.HasItem(ItemType.CookingPot, 1))
        {
            return false;
        }

        if (!inventory.RemoveItem(ItemType.CookingPot, 1))
        {
            return false;
        }

        HasPot = true;
        SpawnPotVisual();
        return true;
    }

    public bool TryRemoveCookingPot(PlayerInventory inventory)
    {
        if (!HasStateAuthority || inventory == null)
        {
            return false;
        }

        if (!HasPot)
        {
            return false;
        }

        for (int i = 0; i < CurrentSlotCount; i++)
        {
            if (SlotTimers.Get(i) >= 0f)
            {
                return false;
            }
        }

        HasPot = false;
        inventory.AddItem(ItemType.CookingPot, 1);
        DestroyPotVisual();
        return true;
    }

    private void SpawnPotVisual()
    {
        if (potVisual != null) Destroy(potVisual);
        potVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        potVisual.name = "CookingPotVisual";
        potVisual.transform.SetParent(transform, false);
        potVisual.transform.localPosition = new Vector3(0f, 0.75f, 0f);
        potVisual.transform.localScale = new Vector3(0.4f, 0.2f, 0.4f);
    }

    private void DestroyPotVisual()
    {
        if (potVisual != null)
        {
            Destroy(potVisual);
            potVisual = null;
        }
    }

    private Vector3 GetSlotPosition(int slot)
    {
        if (HasPot && slot < potSlotPositions.Length)
        {
            return potSlotPositions[slot];
        }

        if (slot < baseSlotPositions.Length)
        {
            return baseSlotPositions[slot];
        }

        return Vector3.up * 0.55f;
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
            slotVisuals[slot].transform.localPosition = GetSlotPosition(slot);
            slotVisuals[slot].transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        }
    }

    private void SwapToCookedVisual(int slot)
    {
        if (slot < 0 || slot >= CurrentSlotCount)
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
            slotVisuals[slot].transform.localPosition = GetSlotPosition(slot);
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
