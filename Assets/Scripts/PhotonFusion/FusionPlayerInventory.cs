using Fusion;
using UnityEngine;

public class FusionPlayerInventory : NetworkBehaviour
{
    private static readonly System.Collections.Generic.HashSet<int> ClaimedLocalPickupIds = new System.Collections.Generic.HashSet<int>();
    private static readonly System.Collections.Generic.Dictionary<ItemType, PickableItem> SceneDropTemplates = new System.Collections.Generic.Dictionary<ItemType, PickableItem>();

    [System.Serializable]
    private class DropPrefabBinding
    {
        public ItemType itemType;
        public NetworkPrefabRef prefab;
        public GameObject prefabObject;
    }

    [System.Serializable]
    private class PlaceablePrefabBinding
    {
        public ItemType itemType;
        public NetworkPrefabRef prefab;
        public GameObject prefabObject;
        public Vector3 bounds = Vector3.one;
    }

    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PlayerSurvivalSystem survivalSystem;
    [SerializeField] private float pickupDistance = 4f;
    [SerializeField] private DropPrefabBinding[] dropPrefabs;
    [SerializeField] private NetworkPrefabRef fallbackDropPrefab;
    [SerializeField] private Transform dropOrigin;
    [SerializeField] private float dropForwardDistance = 1.2f;
    [SerializeField] private float dropUpOffset = 0.2f;

    [Header("Food Drop Prefabs")]
    [SerializeField] private GameObject rawChickenDropPrefab;
    [SerializeField] private GameObject cookedChickenDropPrefab;
    [SerializeField] private GameObject rawFishDropPrefab;
    [SerializeField] private GameObject cookedFishDropPrefab;

    [Header("Placeables")]
    [SerializeField] private PlaceablePrefabBinding[] placeablePrefabs;
    [SerializeField] private float maxPlacementDistance = 4f;
    [SerializeField] private LayerMask placementSurfaceMask = ~0;
    [SerializeField] private float placementGroundRaycastDistance = 4f;
    [SerializeField] private float placementGroundTolerance = 0.15f;
    [SerializeField] private float placementGroundOffset = 0.02f;
    [SerializeField] private LayerMask placementBlockedMask = 0;

    public override void Spawned()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        ResolveReferences();
    }

    public bool RequestPickup(PickableItem item)
    {
        if (!IsNetworkReady() || !HasFusionInputAuthority() || item == null || inventory == null)
        {
            return false;
        }

        FusionPickableItem fusionItem = item.GetComponent<FusionPickableItem>();
        NetworkObject networkObject = item.GetComponent<NetworkObject>();
        if (fusionItem == null || networkObject == null || !networkObject.IsValid || Runner == null)
        {
            return RequestScenePickup(item);
        }

        float distance = Vector3.Distance(transform.position, item.transform.position);
        if (distance > pickupDistance)
        {
            return false;
        }

        item.itemType = fusionItem.ItemType;
        item.amount = fusionItem.ClampedAmount;
        if (!CanSpawnFusionDrop(item.itemType))
        {
            CacheSceneDropTemplate(item);
        }

        int requestedAmount = fusionItem.ClampedAmount;
        int acceptedAmount = inventory.AddItem(item);

        if (acceptedAmount <= 0)
        {
            return false;
        }

        if (acceptedAmount >= requestedAmount)
        {
            RPC_ConfirmPickupDespawn(networkObject);
        }
        else
        {
            RPC_ConfirmPickupPartial(networkObject, requestedAmount - acceptedAmount);
        }

        return true;
    }

    private bool RequestScenePickup(PickableItem item)
    {
        if (item == null || Runner == null || inventory == null)
        {
            return false;
        }

        float distance = Vector3.Distance(transform.position, item.transform.position);
        if (distance > pickupDistance)
        {
            return false;
        }

        int itemObjectId = item.gameObject.GetInstanceID();
        if (ClaimedLocalPickupIds.Contains(itemObjectId))
        {
            return false;
        }

        FusionSceneDropMarker sceneDropMarker = item.GetComponent<FusionSceneDropMarker>();
        int sceneDropId = sceneDropMarker != null ? sceneDropMarker.SceneDropId : 0;

        CacheSceneDropTemplate(item);

        int requestedAmount = Mathf.Max(1, item.amount);
        int acceptedAmount = inventory.AddItem(item);
        if (acceptedAmount <= 0)
        {
            return false;
        }

        ClaimedLocalPickupIds.Add(itemObjectId);
        if (acceptedAmount >= requestedAmount)
        {
            RPC_ConfirmScenePickup(item.transform.position, transform.position, item.itemType, requestedAmount, sceneDropId);
        }
        else
        {
            item.amount = requestedAmount - acceptedAmount;
            ClaimedLocalPickupIds.Remove(itemObjectId);
        }

        return true;
    }

    public bool RequestDrop(ItemType itemType, int amount = 1)
    {
        if (!IsNetworkReady() || !HasFusionInputAuthority() || inventory == null)
        {
            return false;
        }

        int clampedAmount = Mathf.Max(1, amount);
        int sourceSlot = inventory.FindFirstSlotWithItemType(itemType);
        if (sourceSlot < 0)
        {
            return false;
        }

        RPC_RequestDrop(sourceSlot, (int)itemType, clampedAmount);
        return true;
    }

    public bool RequestDropFromSlot(int slotIndex, int amount = 1)
    {
        if (!IsNetworkReady() || !HasFusionInputAuthority() || inventory == null)
        {
            return false;
        }

        ItemType? itemType = inventory.GetSlotItemType(slotIndex);
        if (itemType == null)
        {
            return false;
        }

        int clampedAmount = Mathf.Max(1, amount);
        if (CanSpawnFusionDrop(itemType.Value))
        {
            RPC_RequestDrop(slotIndex, (int)itemType.Value, clampedAmount);
            return true;
        }

        if (!TryRequestSceneDrop(slotIndex, itemType.Value, clampedAmount))
        {
            return false;
        }

        return true;
    }

    public bool RequestConsumeFromSlot(int slotIndex)
    {
        if (!IsNetworkReady() || !HasFusionInputAuthority() || inventory == null)
        {
            return false;
        }

        ItemType? itemType = inventory.GetSlotItemType(slotIndex);
        if (itemType == null || !ConsumableItemCatalog.TryGetEffect(itemType.Value, out _))
        {
            return false;
        }

        RPC_RequestConsume(slotIndex, (int)itemType.Value);
        return true;
    }

    public bool RequestPlaceFromSlot(int slotIndex, Vector3 position, Quaternion rotation)
    {
        if (!IsNetworkReady() || !HasFusionInputAuthority() || inventory == null)
        {
            return false;
        }

        ItemType? itemType = inventory.GetSlotItemType(slotIndex);
        if (itemType == null || inventory.GetSlotAmount(slotIndex) <= 0 || !PlaceableItemSystem.IsPlaceable(itemType.Value))
        {
            return false;
        }

        RPC_RequestPlace(slotIndex, (int)itemType.Value, position, rotation);
        return true;
    }

    public bool SpawnTreeDrops(TreeChoppable tree)
    {
        if (!IsNetworkReady() || !HasFusionLocalAuthority() || tree == null || !tree.HasDropPrefab)
        {
            return false;
        }

        return SpawnTreeDropsFromData(
            tree.transform.position,
            tree.DropBasePosition,
            tree.DropForward,
            tree.DropItemType,
            tree.FusionDropCount,
            tree.FusionAmountPerDrop,
            tree.DropScatterRadius);
    }

    public bool SpawnTreeDropsFromData(Vector3 treePosition, Vector3 dropBasePosition, Vector3 dropForward, ItemType itemType, int spawnCount, int amountPerDrop, float scatter)
    {
        if (!IsNetworkReady() || !HasFusionLocalAuthority())
        {
            return false;
        }

        if (!TryGetDropPrefab(itemType, out NetworkPrefabRef dropPrefab, out GameObject dropPrefabObject))
        {
            return false;
        }

        NetworkObject dropNetworkPrefab = dropPrefabObject != null
            ? dropPrefabObject.GetComponent<NetworkObject>()
            : null;

        int clampedSpawnCount = Mathf.Max(1, spawnCount);
        int clampedAmountPerDrop = Mathf.Max(1, amountPerDrop);
        float clampedScatter = Mathf.Max(0f, scatter);
        int successCount = 0;

        for (int i = 0; i < clampedSpawnCount; i++)
        {
            int sceneDropId = FusionSceneDropUtility.ComputeSceneDropId(treePosition, itemType, i);
            Vector2 offset2D = FusionSceneDropUtility.ComputeDeterministicScatter(sceneDropId, clampedScatter);
            Vector3 spawnPosition = dropBasePosition + new Vector3(offset2D.x, 0f, offset2D.y);

            NetworkObject droppedObject = dropNetworkPrefab != null
                ? Runner.Spawn(dropNetworkPrefab, spawnPosition, Quaternion.identity, Object.InputAuthority)
                : (dropPrefabObject != null
                    ? Runner.Spawn(dropPrefabObject, spawnPosition, Quaternion.identity, Object.InputAuthority)
                    : Runner.Spawn(dropPrefab, spawnPosition, Quaternion.identity, Object.InputAuthority));

            if (droppedObject == null)
            {
                Debug.LogWarning($"[SpawnTreeDrops] Runner.Spawn returned null for drop {i} of {clampedSpawnCount}, itemType={itemType}");
                continue;
            }

            FusionPickableItem droppedItem = droppedObject.GetComponent<FusionPickableItem>();
            if (droppedItem == null || !droppedItem.Initialize(itemType, clampedAmountPerDrop))
            {
                Debug.LogWarning($"[SpawnTreeDrops] Initialize failed for drop {i}, despawning");
                Runner.Despawn(droppedObject);
                continue;
            }

            Rigidbody rigidbody = droppedObject.GetComponent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.drag = 0f;
                rigidbody.angularDrag = 1f;
                Vector3 randomPush = dropForward + Vector3.up + new Vector3(offset2D.x, 0f, offset2D.y);
                rigidbody.AddForce(randomPush.normalized * 1.8f, ForceMode.VelocityChange);
            }

            successCount++;
        }

        if (successCount != clampedSpawnCount)
        {
            Debug.LogWarning($"[SpawnTreeDrops] Only {successCount}/{clampedSpawnCount} drops spawned successfully for {itemType}");
        }

        return successCount > 0;
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_ConfirmPickupDespawn(NetworkObject itemObject)
    {
        if (itemObject == null || Runner == null) return;

        PickableItem item = itemObject.GetComponent<PickableItem>();
        if (item != null && !CanSpawnFusionDrop(item.itemType))
        {
            CacheSceneDropTemplate(item);
        }
        
        if (itemObject.HasStateAuthority || (Runner.IsSharedModeMasterClient && itemObject.StateAuthority == PlayerRef.None))
        {
            Runner.Despawn(itemObject);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_ConfirmPickupPartial(NetworkObject itemObject, int remainingAmount)
    {
        if (itemObject == null || Runner == null) return;
        
        if (itemObject.HasStateAuthority || (Runner.IsSharedModeMasterClient && itemObject.StateAuthority == PlayerRef.None))
        {
            PickableItem item = itemObject.GetComponent<PickableItem>();
            if (item != null) item.amount = remainingAmount;
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ConfirmScenePickup(Vector3 itemPosition, Vector3 pickerPosition, ItemType itemType, int requestedAmount, int sceneDropId)
    {
        PickableItem item = FindMatchingScenePickup(itemPosition, pickerPosition, itemType, requestedAmount, sceneDropId);
        if (item == null)
        {
            return;
        }

        CacheSceneDropTemplate(item);
        ClaimedLocalPickupIds.Add(item.gameObject.GetInstanceID());
        Destroy(item.gameObject);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SpawnSceneDrop(Vector3 position, Vector3 forward, ItemType itemType, int amount)
    {
        SpawnSceneDropLocal(position, forward, itemType, amount, sceneDropId: 0);
    }

    private bool TryRequestSceneDrop(int slotIndex, ItemType itemType, int amount)
    {
        int removedAmount = Mathf.Min(Mathf.Max(1, amount), inventory.GetSlotAmount(slotIndex));
        if (removedAmount <= 0 || !CanSpawnSceneDrop(itemType))
        {
            return false;
        }

        if (inventory.GetSlotItemType(slotIndex) != itemType || !inventory.RemoveItemFromSlot(slotIndex, removedAmount, out ItemType removedItemType))
        {
            return false;
        }

        if (removedItemType != itemType)
        {
            inventory.AddItem(removedItemType, removedAmount);
            return false;
        }

        Transform origin = dropOrigin != null ? dropOrigin : transform;
        RPC_SpawnSceneDrop(GetDropPosition(), origin.forward, itemType, removedAmount);
        return true;
    }

    private static bool CanSpawnSceneDrop(ItemType itemType)
    {
        if (SceneDropTemplates.TryGetValue(itemType, out PickableItem template) && template != null)
        {
            return true;
        }

        if (TryCacheSceneDropTemplate(itemType))
        {
            return true;
        }

        return itemType == ItemType.Axe && Resources.Load<GameObject>("Prefabs/axe") != null
            || itemType == ItemType.RawChicken
            || itemType == ItemType.CookedChicken
            || itemType == ItemType.RawFish
            || itemType == ItemType.CookedFish;
    }

    private static void SpawnSceneDropLocal(Vector3 position, Vector3 forward, ItemType itemType, int amount, int sceneDropId)
    {
        GameObject droppedObject = null;
        if (SceneDropTemplates.TryGetValue(itemType, out PickableItem template) && template != null)
        {
            droppedObject = Instantiate(template.gameObject, position, Quaternion.identity);
        }
        else if (itemType == ItemType.Axe)
        {
            GameObject axePrefab = Resources.Load<GameObject>("Prefabs/axe");
            if (axePrefab != null)
            {
                droppedObject = Instantiate(axePrefab, position, Quaternion.identity);
            }
        }
        else if (itemType == ItemType.RawChicken || itemType == ItemType.CookedChicken
            || itemType == ItemType.RawFish || itemType == ItemType.CookedFish)
        {
            droppedObject = SpawnFoodDropObject(itemType);
        }

        if (droppedObject == null)
        {
            return;
        }

        droppedObject.name = itemType + " (Dropped)";
        droppedObject.SetActive(true);
        int itemLayer = LayerMask.NameToLayer("Item");
        if (itemLayer >= 0)
        {
            droppedObject.layer = itemLayer;
        }

        PickableItem pickableItem = droppedObject.GetComponent<PickableItem>();
        if (pickableItem == null)
        {
            pickableItem = droppedObject.AddComponent<PickableItem>();
        }

        pickableItem.enabled = true;
        pickableItem.itemType = itemType;
        pickableItem.itemName = itemType.ToString();
        pickableItem.amount = Mathf.Max(1, amount);

        if (sceneDropId != 0)
        {
            FusionSceneDropMarker marker = droppedObject.GetComponent<FusionSceneDropMarker>();
            if (marker == null)
            {
                marker = droppedObject.AddComponent<FusionSceneDropMarker>();
            }

            marker.Initialize(sceneDropId);
        }

        Interactable interactable = droppedObject.GetComponent<Interactable>();
        if (interactable == null)
        {
            interactable = droppedObject.AddComponent<Interactable>();
        }
        interactable.enabled = true;

        Collider[] colliders = droppedObject.GetComponentsInChildren<Collider>(true);
        if (colliders == null || colliders.Length == 0)
        {
            BoxCollider boxCollider = droppedObject.AddComponent<BoxCollider>();
            boxCollider.size = Vector3.one * 0.35f;
            colliders = new Collider[] { boxCollider };
        }

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
            {
                colliders[i].enabled = true;
            }
        }

        Rigidbody rigidbody = droppedObject.GetComponent<Rigidbody>();
        if (rigidbody == null)
        {
            rigidbody = droppedObject.AddComponent<Rigidbody>();
        }

        rigidbody.isKinematic = false;
        rigidbody.detectCollisions = true;
        rigidbody.drag = 0f;
        rigidbody.angularDrag = 1f;
        rigidbody.AddForce((forward.normalized + Vector3.up).normalized * 2f, ForceMode.VelocityChange);
    }

    private static GameObject SpawnFoodDropObject(ItemType itemType)
    {
        FusionPlayerInventory instance = FindObjectOfType<FusionPlayerInventory>();
        if (instance == null)
        {
            GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fallback.transform.localScale = new Vector3(0.25f, 0.15f, 0.25f);
            return fallback;
        }

        GameObject prefab = null;
        if (itemType == ItemType.RawChicken) prefab = instance.rawChickenDropPrefab;
        else if (itemType == ItemType.CookedChicken) prefab = instance.cookedChickenDropPrefab;
        else if (itemType == ItemType.RawFish) prefab = instance.rawFishDropPrefab;
        else if (itemType == ItemType.CookedFish) prefab = instance.cookedFishDropPrefab;

        if (prefab != null)
        {
            return Instantiate(prefab, Vector3.zero, Quaternion.identity);
        }

        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.localScale = new Vector3(0.25f, 0.15f, 0.25f);
        return cube;
    }

    private static void CacheSceneDropTemplate(PickableItem sourceItem)
    {
        if (sourceItem == null || SceneDropTemplates.ContainsKey(sourceItem.itemType))
        {
            return;
        }

        if (!Application.isPlaying)
        {
            SceneDropTemplates[sourceItem.itemType] = sourceItem;
            return;
        }

        PickableItem template = Instantiate(sourceItem);
        template.name = sourceItem.itemType + " FusionSceneDropTemplate";
        template.gameObject.SetActive(false);
        DontDestroyOnLoad(template.gameObject);

        SceneDropTemplates[sourceItem.itemType] = template;
    }

    private static bool TryCacheSceneDropTemplate(ItemType itemType)
    {
        PickableItem[] items = FindObjectsOfType<PickableItem>(true);
        for (int i = 0; i < items.Length; i++)
        {
            PickableItem candidate = items[i];
            if (candidate == null || candidate.itemType != itemType)
            {
                continue;
            }

            CacheSceneDropTemplate(candidate);
            return SceneDropTemplates.TryGetValue(itemType, out PickableItem template) && template != null;
        }

        return false;
    }

    private static PickableItem FindMatchingScenePickup(Vector3 itemPosition, Vector3 pickerPosition, ItemType itemType, int requestedAmount, int sceneDropId)
    {
        PickableItem[] items = FindObjectsOfType<PickableItem>(true);
        PickableItem bestMatch = null;
        float bestDistanceSqr = sceneDropId != 0 ? float.MaxValue : 1.44f;
        PickableItem bestNearPicker = null;
        float bestNearPickerSqr = 16f;

        for (int i = 0; i < items.Length; i++)
        {
            PickableItem candidate = items[i];
            if (candidate == null || !candidate.gameObject.activeInHierarchy || candidate.itemType != itemType || candidate.amount != requestedAmount)
            {
                continue;
            }

            FusionSceneDropMarker marker = candidate.GetComponent<FusionSceneDropMarker>();
            if (sceneDropId != 0)
            {
                if (marker == null || marker.SceneDropId != sceneDropId)
                {
                    continue;
                }

                return candidate;
            }

            float distanceSqr = (candidate.transform.position - itemPosition).sqrMagnitude;
            float pickerDistanceSqr = (candidate.transform.position - pickerPosition).sqrMagnitude;
            if (pickerDistanceSqr <= bestNearPickerSqr)
            {
                bestNearPickerSqr = pickerDistanceSqr;
                bestNearPicker = candidate;
            }

            if (distanceSqr > bestDistanceSqr)
            {
                continue;
            }

            bestDistanceSqr = distanceSqr;
            bestMatch = candidate;
        }

        return bestMatch != null ? bestMatch : bestNearPicker;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestDrop(int slotIndex, int expectedItemTypeValue, int amount)
    {
        ResolveReferences();

        if (inventory == null || Runner == null)
        {
            return;
        }

        if (!System.Enum.IsDefined(typeof(ItemType), expectedItemTypeValue))
        {
            return;
        }

        ItemType itemType = (ItemType)expectedItemTypeValue;
        if (!TryGetDropPrefab(itemType, out NetworkPrefabRef dropPrefab, out GameObject dropPrefabObject))
        {
            return;
        }

        if (inventory.GetSlotItemType(slotIndex) != itemType)
        {
            return;
        }

        int clampedAmount = Mathf.Max(1, amount);
        int removedAmount = Mathf.Min(clampedAmount, inventory.GetSlotAmount(slotIndex));
        if (removedAmount <= 0)
        {
            return;
        }

        Vector3 spawnPosition = GetDropPosition();
        Quaternion spawnRotation = Quaternion.identity;
        NetworkObject droppedObject = dropPrefabObject != null
            ? Runner.Spawn(dropPrefabObject, spawnPosition, spawnRotation, Object.InputAuthority)
            : Runner.Spawn(dropPrefab, spawnPosition, spawnRotation, Object.InputAuthority);
        if (droppedObject == null)
        {
            return;
        }

        FusionPickableItem droppedItem = droppedObject.GetComponent<FusionPickableItem>();
        if (droppedItem == null || !droppedItem.Initialize(itemType, removedAmount))
        {
            Runner.Despawn(droppedObject);
            return;
        }

        if (!inventory.RemoveItemFromSlot(slotIndex, clampedAmount, out ItemType removedItemType))
        {
            Runner.Despawn(droppedObject);
            return;
        }

        if (removedItemType != itemType)
        {
            inventory.AddItem(removedItemType, removedAmount);
            Runner.Despawn(droppedObject);
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestConsume(int slotIndex, int expectedItemTypeValue)
    {
        ResolveReferences();

        if (inventory == null || survivalSystem == null || !System.Enum.IsDefined(typeof(ItemType), expectedItemTypeValue))
        {
            return;
        }

        ItemType itemType = (ItemType)expectedItemTypeValue;
        if (inventory.GetSlotItemType(slotIndex) != itemType || !ConsumableItemCatalog.TryGetEffect(itemType, out _))
        {
            return;
        }

        if (!inventory.RemoveItemFromSlot(slotIndex, 1, out ItemType removedItemType))
        {
            return;
        }

        if (!ConsumableItemCatalog.TryApply(survivalSystem, removedItemType))
        {
            inventory.AddItemToSlot(removedItemType, 1, slotIndex);
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestPlace(int slotIndex, int expectedItemTypeValue, Vector3 position, Quaternion rotation, RpcInfo info = default)
    {
        ResolveReferences();

        if (inventory == null || Runner == null || survivalSystem == null || survivalSystem.IsDead)
        {
            return;
        }

        FusionPlayerSurvival fusionSurvival = GetComponent<FusionPlayerSurvival>();
        if (fusionSurvival != null && fusionSurvival.IsDowned)
        {
            return;
        }

        if (!System.Enum.IsDefined(typeof(ItemType), expectedItemTypeValue))
        {
            return;
        }

        ItemType itemType = (ItemType)expectedItemTypeValue;
        if (!PlaceableItemSystem.IsPlaceable(itemType) || inventory.GetSlotItemType(slotIndex) != itemType)
        {
            return;
        }

        if (!TryGetPlaceablePrefab(itemType, out NetworkPrefabRef placeablePrefab, out GameObject placeablePrefabObject, out Vector3 bounds))
        {
            return;
        }

        float maxDistance = Mathf.Max(0.5f, maxPlacementDistance);
        if ((position - transform.position).sqrMagnitude > maxDistance * maxDistance)
        {
            return;
        }

        if (!TryGetValidPlacementGround(position, out Collider groundCollider))
        {
            return;
        }

        if (IsPlacementBlocked(position, rotation, bounds, groundCollider))
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

        NetworkObject placedObject = placeablePrefabObject != null
            ? Runner.Spawn(placeablePrefabObject, position, rotation, info.Source)
            : Runner.Spawn(placeablePrefab, position, rotation, info.Source);
        if (placedObject == null)
        {
            inventory.AddItemToSlot(itemType, 1, slotIndex);
            return;
        }

        FusionPlaceableObject placeableObject = placedObject.GetComponent<FusionPlaceableObject>();
        if (placeableObject != null)
        {
            placeableObject.Initialize(itemType, info.Source);
        }
    }

    private void ResolveReferences()
    {
        if (inventory == null)
        {
            inventory = GetComponent<PlayerInventory>();
        }

        if (survivalSystem == null)
        {
            survivalSystem = GetComponent<PlayerSurvivalSystem>();
        }

        if (dropOrigin == null)
        {
            dropOrigin = transform;
        }
    }

    private Vector3 GetDropPosition()
    {
        Transform origin = dropOrigin != null ? dropOrigin : transform;
        return origin.position + origin.forward * dropForwardDistance + Vector3.up * dropUpOffset;
    }

    private bool TryGetDropPrefab(ItemType itemType, out NetworkPrefabRef prefab, out GameObject prefabObject)
    {
        prefabObject = null;
        if (dropPrefabs != null)
        {
            for (int i = 0; i < dropPrefabs.Length; i++)
            {
                DropPrefabBinding binding = dropPrefabs[i];
                if (binding != null && binding.itemType == itemType && binding.prefabObject != null && binding.prefabObject.GetComponent<NetworkObject>() != null)
                {
                    prefabObject = binding.prefabObject;
                    prefab = default;
                    return true;
                }

                if (binding != null && binding.itemType == itemType && binding.prefab.IsValid)
                {
                    prefab = binding.prefab;
                    return true;
                }
            }
        }

        if (fallbackDropPrefab.IsValid)
        {
            prefab = fallbackDropPrefab;
            return true;
        }

        prefab = default;
        return false;
    }

    private bool TryGetPlaceablePrefab(ItemType itemType, out NetworkPrefabRef prefab, out GameObject prefabObject, out Vector3 bounds)
    {
        prefabObject = null;
        bounds = Vector3.one;
        if (placeablePrefabs != null)
        {
            for (int i = 0; i < placeablePrefabs.Length; i++)
            {
                PlaceablePrefabBinding binding = placeablePrefabs[i];
                if (binding == null || binding.itemType != itemType)
                {
                    continue;
                }

                bounds = binding.bounds;
                if (binding.prefabObject != null && binding.prefabObject.GetComponent<NetworkObject>() != null)
                {
                    prefabObject = binding.prefabObject;
                    prefab = default;
                    return true;
                }

                if (binding.prefab.IsValid)
                {
                    prefab = binding.prefab;
                    return true;
                }
            }
        }

        prefab = default;
        return false;
    }

    private bool TryGetValidPlacementGround(Vector3 position, out Collider groundCollider)
    {
        groundCollider = null;
        float rayDistance = Mathf.Max(0.5f, placementGroundRaycastDistance);
        Vector3 origin = position + Vector3.up * Mathf.Min(1f, rayDistance * 0.5f);
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayDistance, placementSurfaceMask, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        if (Mathf.Abs(hit.point.y - position.y) > Mathf.Max(0.01f, placementGroundTolerance))
        {
            return false;
        }

        groundCollider = hit.collider;
        return true;
    }

    private bool IsPlacementBlocked(Vector3 groundPosition, Quaternion rotation, Vector3 bounds, Collider groundCollider)
    {
        Vector3 halfExtents = Vector3.Max(bounds, Vector3.one * 0.1f) * 0.5f;
        Vector3 center = groundPosition + Vector3.up * (halfExtents.y + Mathf.Max(0.01f, placementGroundOffset));
        Collider[] hits = Physics.OverlapBox(center, halfExtents, rotation, placementBlockedMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit != null && hit != groundCollider)
            {
                return true;
            }
        }

        return false;
    }

    private bool CanSpawnFusionDrop(ItemType itemType)
    {
        return TryGetDropPrefab(itemType, out NetworkPrefabRef _, out GameObject _);
    }

    private bool IsNetworkReady()
    {
        return Runner != null && Object != null && Object.IsValid;
    }

    private bool HasFusionInputAuthority()
    {
        return Object != null && Object.HasInputAuthority;
    }

    private bool HasFusionLocalAuthority()
    {
        return Object != null && (Object.HasInputAuthority || Object.HasStateAuthority);
    }
}
