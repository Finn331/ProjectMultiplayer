using Fusion;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class PlaceableItemSystem : MonoBehaviour
{
    [System.Serializable]
    private class GhostPrefabBinding
    {
        public ItemType itemType;
        public GameObject ghostPrefab;
        public Vector3 previewBounds = Vector3.one;
    }

    [Header("References")]
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private MobileHotbarUI hotbarUI;
    [SerializeField] private FusionPlayerInventory fusionInventory;
    [SerializeField] private FusionPlayerSurvival fusionSurvival;
    [SerializeField] private Button placeButton;

    [Header("Placement")]
    [SerializeField] private float placementDistance = 2.5f;
    [SerializeField] private float groundRaycastDistance = 4f;
    [SerializeField] private float groundOffset = 0.02f;
    [SerializeField] private LayerMask placementSurfaceMask = ~0;
    [SerializeField] private LayerMask placementBlockedMask = 0;
    [SerializeField] private Vector3 previewBounds = Vector3.one;
    [SerializeField] private GhostPrefabBinding[] ghostPrefabs;
    [SerializeField] private Material validPreviewMaterial;
    [SerializeField] private Material invalidPreviewMaterial;

    private GameObject previewObject;
    private Renderer[] previewRenderers;
    private bool placementMode;
    private bool currentPlacementValid;
    private int selectedGlobalSlot = -1;
    private ItemType selectedItemType;
    private bool buttonBound;
    private Vector3 currentPreviewBounds;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ResolveButtonReference();
        BindButton();

        if (hotbarUI != null)
        {
            hotbarUI.SelectedSlotChanged -= OnSelectedSlotChanged;
            hotbarUI.SelectedSlotChanged += OnSelectedSlotChanged;
        }

        RefreshSelection();
        RefreshButton();
    }

    private void OnDisable()
    {
        ExitPlacementMode();
        UnbindButton();

        if (hotbarUI != null)
        {
            hotbarUI.SelectedSlotChanged -= OnSelectedSlotChanged;
        }
    }

    private void Update()
    {
        if (!CanUseLocalPlacement())
        {
            ExitPlacementMode();
            UnbindButton();
            RefreshButton();
            return;
        }

        ResolveButtonReference();
        BindButton();
        RefreshSelection();
        RefreshButton();

        if (placementMode)
        {
            UpdatePreview();
        }
    }

    public static bool IsPlaceable(ItemType itemType)
    {
        return itemType == ItemType.CraftingTable
            || itemType == ItemType.Campfire
            || itemType == ItemType.StorageChest;
    }

    public void TogglePlacementMode()
    {
        if (!CanPlaceSelectedItem())
        {
            ExitPlacementMode();
            return;
        }

        if (placementMode)
        {
            ConfirmPlacement();
        }
        else
        {
            EnterPlacementMode();
        }
    }

    private void ConfirmPlacement()
    {
        if (!placementMode || !currentPlacementValid || selectedGlobalSlot < 0)
        {
            return;
        }

        Vector3 position = previewObject != null ? previewObject.transform.position : transform.position + transform.forward * placementDistance;
        Quaternion rotation = previewObject != null ? previewObject.transform.rotation : Quaternion.identity;
        bool placed = false;

        if (fusionInventory != null)
        {
            fusionInventory.RequestPlaceFromSlot(selectedGlobalSlot, position, rotation);
            return;
        }
        else if (inventory != null && inventory.RemoveItemFromSlot(selectedGlobalSlot, 1, out ItemType removedItemType))
        {
            placed = SpawnOfflinePlaceable(removedItemType, position, rotation);
            if (!placed)
            {
                inventory.AddItemToSlot(removedItemType, 1, selectedGlobalSlot);
            }
        }

        if (placed)
        {
            ExitPlacementMode();
        }
    }

    private void EnterPlacementMode()
    {
        placementMode = true;
        EnsurePreviewObject();
        UpdatePreview();
    }

    private void ExitPlacementMode()
    {
        placementMode = false;
        currentPlacementValid = false;
        if (previewObject != null)
        {
            Destroy(previewObject);
            previewObject = null;
            previewRenderers = null;
        }
    }

    private void UpdatePreview()
    {
        EnsurePreviewObject();
        if (previewObject == null)
        {
            return;
        }

        Vector3 targetPosition = transform.position + transform.forward * placementDistance;
        bool hasGround = TryGetPlacementGround(out RaycastHit hit);
        if (hasGround)
        {
            targetPosition = hit.point + hit.normal * groundOffset;
        }

        Quaternion targetRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        previewObject.transform.SetPositionAndRotation(targetPosition, targetRotation);

        currentPlacementValid = hasGround && !IsPlacementBlocked(targetPosition, targetRotation, hit.collider);
        ApplyPreviewMaterial(currentPlacementValid ? validPreviewMaterial : invalidPreviewMaterial);
    }

    private bool IsPlacementBlocked(Vector3 groundPosition, Quaternion rotation, Collider groundCollider)
    {
        Vector3 halfExtents = Vector3.Max(currentPreviewBounds, Vector3.one * 0.1f) * 0.5f;
        Vector3 center = groundPosition + Vector3.up * (halfExtents.y + Mathf.Max(0.01f, groundOffset));
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

    private bool TryGetPlacementGround(out RaycastHit hit)
    {
        Vector3 origin = transform.position + Vector3.up * 0.5f + transform.forward * placementDistance;
        float distance = Mathf.Max(0.5f, groundRaycastDistance);
        return Physics.Raycast(origin, Vector3.down, out hit, distance, placementSurfaceMask, QueryTriggerInteraction.Ignore);
    }

    private void EnsurePreviewObject()
    {
        if (previewObject != null)
        {
            return;
        }

        GameObject ghostPrefab = GetGhostPrefab(selectedItemType, out Vector3 bindingBounds);
        if (ghostPrefab != null)
        {
            previewObject = Instantiate(ghostPrefab);
            previewObject.name = selectedItemType + " Placement Preview";
            currentPreviewBounds = Vector3.Max(bindingBounds, Vector3.one * 0.1f);
        }
        else
        {
            previewObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            previewObject.name = selectedItemType + " Placement Preview";
            previewObject.transform.localScale = previewBounds;
            currentPreviewBounds = previewBounds;
        }

        Collider[] previewColliders = previewObject.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < previewColliders.Length; i++)
        {
            if (previewColliders[i] != null)
            {
                previewColliders[i].enabled = false;
            }
        }

        previewRenderers = previewObject.GetComponentsInChildren<Renderer>(true);
    }

    private GameObject GetGhostPrefab(ItemType itemType, out Vector3 bounds)
    {
        bounds = previewBounds;
        if (ghostPrefabs == null)
        {
            return null;
        }

        for (int i = 0; i < ghostPrefabs.Length; i++)
        {
            GhostPrefabBinding binding = ghostPrefabs[i];
            if (binding != null && binding.itemType == itemType && binding.ghostPrefab != null)
            {
                bounds = binding.previewBounds;
                return binding.ghostPrefab;
            }
        }

        return null;
    }

    private void ApplyPreviewMaterial(Material material)
    {
        if (material == null || previewRenderers == null)
        {
            return;
        }

        for (int i = 0; i < previewRenderers.Length; i++)
        {
            if (previewRenderers[i] != null)
            {
                previewRenderers[i].sharedMaterial = material;
            }
        }
    }

    private bool SpawnOfflinePlaceable(ItemType itemType, Vector3 position, Quaternion rotation)
    {
        if (!IsPlaceable(itemType))
        {
            return false;
        }

        GameObject placedObject = GameObject.CreatePrimitive(itemType == ItemType.Campfire ? PrimitiveType.Cylinder : PrimitiveType.Cube);
        placedObject.name = itemType.ToString();
        placedObject.transform.SetPositionAndRotation(position, rotation);
        if (itemType == ItemType.CraftingTable && placedObject.GetComponent<CraftingTableStation>() == null)
        {
            placedObject.AddComponent<CraftingTableStation>();
        }

        return true;
    }

    private void RefreshSelection()
    {
        selectedGlobalSlot = -1;
        selectedItemType = default;

        if (hotbarUI == null)
        {
            return;
        }

        int hotbarSlot = hotbarUI.SelectedSlotIndex;
        int globalSlot = hotbarUI.GetHotbarGlobalSlotIndex(hotbarSlot);
        ItemType? itemType = globalSlot >= 0 && inventory != null ? inventory.GetSlotItemType(globalSlot) : null;
        if (itemType == null || !IsPlaceable(itemType.Value))
        {
            if (placementMode)
            {
                ExitPlacementMode();
            }

            return;
        }

        selectedGlobalSlot = globalSlot;
        selectedItemType = itemType.Value;
    }

    private void OnSelectedSlotChanged(int slotIndex, ItemType? itemType)
    {
        RefreshSelection();
        if (itemType == null || !IsPlaceable(itemType.Value))
        {
            ExitPlacementMode();
        }

        RefreshButton();
    }

    private void RefreshButton()
    {
        if (placeButton == null || !HasLocalAuthority())
        {
            return;
        }

        bool canPlace = CanPlaceSelectedItem();
        placeButton.gameObject.SetActive(canPlace);
        placeButton.interactable = canPlace;
    }

    private bool CanPlaceSelectedItem()
    {
        return CanUseLocalPlacement()
            && selectedGlobalSlot >= 0
            && inventory != null
            && inventory.GetSlotAmount(selectedGlobalSlot) > 0
            && IsPlaceable(selectedItemType);
    }

    private bool CanUseLocalPlacement()
    {
        if (!HasLocalAuthority())
        {
            return false;
        }

        if (IsFusionPlayerDowned())
        {
            return false;
        }

        return true;
    }

    private bool IsFusionPlayerDowned()
    {
        if (fusionSurvival == null || fusionSurvival.Object == null || !fusionSurvival.Object.IsValid)
        {
            return false;
        }

        return fusionSurvival.IsDowned;
    }

    private bool HasLocalAuthority()
    {
        NetworkObject fusionObject = GetComponent<NetworkObject>();
        if (fusionObject != null && fusionObject.IsValid)
        {
            if (fusionObject.HasInputAuthority)
            {
                return true;
            }

            if (!fusionObject.HasStateAuthority)
            {
                return false;
            }

            if (fusionObject.InputAuthority.IsNone)
            {
                return true;
            }

            return fusionObject.Runner != null && fusionObject.InputAuthority == fusionObject.Runner.LocalPlayer;
        }

        return true;
    }

    private void ResolveReferences()
    {
        if (inventory == null)
        {
            inventory = GetComponent<PlayerInventory>();
        }

        if (hotbarUI == null)
        {
            hotbarUI = GetComponent<MobileHotbarUI>();
        }

        if (fusionInventory == null)
        {
            fusionInventory = GetComponent<FusionPlayerInventory>();
        }

        if (fusionSurvival == null)
        {
            fusionSurvival = GetComponent<FusionPlayerSurvival>();
        }
    }

    private void BindButton()
    {
        if (buttonBound || placeButton == null || !HasLocalAuthority())
        {
            return;
        }

        placeButton.onClick.RemoveListener(TogglePlacementMode);
        placeButton.onClick.AddListener(TogglePlacementMode);
        buttonBound = true;
    }

    private void UnbindButton()
    {
        if (!buttonBound || placeButton == null)
        {
            buttonBound = false;
            return;
        }

        placeButton.onClick.RemoveListener(TogglePlacementMode);
        buttonBound = false;
    }

    private void ResolveButtonReference()
    {
        if (placeButton != null || !HasLocalAuthority())
        {
            return;
        }

        placeButton = FindButtonByName("station place");
        if (placeButton == null)
        {
            placeButton = FindButtonByName("place");
        }
    }

    private static Button FindButtonByName(string keyword)
    {
        Button[] buttons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        string loweredKeyword = keyword.ToLowerInvariant();
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button != null && button.name.ToLowerInvariant().Contains(loweredKeyword))
            {
                return button;
            }
        }

        return null;
    }
}
