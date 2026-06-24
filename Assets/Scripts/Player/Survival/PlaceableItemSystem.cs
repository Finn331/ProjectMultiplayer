using Fusion;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class PlaceableItemSystem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private MobileHotbarUI hotbarUI;
    [SerializeField] private FusionPlayerInventory fusionInventory;
    [SerializeField] private FusionPlayerSurvival fusionSurvival;
    [SerializeField] private Button placeButton;

    [Header("Placement")]
    [SerializeField] private float placementDistance = 2.5f;
    [SerializeField] private LayerMask placementSurfaceMask = ~0;
    [SerializeField] private LayerMask placementBlockedMask = ~0;
    [SerializeField] private Vector3 previewBounds = Vector3.one;
    [SerializeField] private Material validPreviewMaterial;
    [SerializeField] private Material invalidPreviewMaterial;

    private GameObject previewObject;
    private Renderer[] previewRenderers;
    private bool placementMode;
    private bool currentPlacementValid;
    private int selectedGlobalSlot = -1;
    private ItemType selectedItemType;
    private bool buttonBound;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
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
            RefreshButton();
            return;
        }

        RefreshSelection();
        RefreshButton();

        if (placementMode)
        {
            UpdatePreview();
        }
    }

    public static bool IsPlaceable(ItemType itemType)
    {
        return itemType == ItemType.CraftingTable || itemType == ItemType.Campfire;
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
            placed = fusionInventory.RequestPlaceFromSlot(selectedGlobalSlot, position, rotation);
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
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, transform.forward, out RaycastHit hit, placementDistance + 1f, placementSurfaceMask, QueryTriggerInteraction.Ignore))
        {
            targetPosition = hit.point;
        }

        Quaternion targetRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        previewObject.transform.SetPositionAndRotation(targetPosition, targetRotation);

        Vector3 halfExtents = Vector3.Max(previewBounds, Vector3.one * 0.1f) * 0.5f;
        currentPlacementValid = !Physics.CheckBox(targetPosition, halfExtents, targetRotation, placementBlockedMask, QueryTriggerInteraction.Ignore);
        ApplyPreviewMaterial(currentPlacementValid ? validPreviewMaterial : invalidPreviewMaterial);
    }

    private void EnsurePreviewObject()
    {
        if (previewObject != null)
        {
            return;
        }

        previewObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        previewObject.name = selectedItemType + " Placement Preview";
        previewObject.transform.localScale = previewBounds;

        Collider previewCollider = previewObject.GetComponent<Collider>();
        if (previewCollider != null)
        {
            previewCollider.enabled = false;
        }

        previewRenderers = previewObject.GetComponentsInChildren<Renderer>(true);
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
        if (placeButton == null)
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
        NetworkObject fusionObject = GetComponent<NetworkObject>();
        if (fusionObject != null && fusionObject.IsValid && !fusionObject.HasInputAuthority)
        {
            return false;
        }

        if (fusionSurvival != null && fusionSurvival.IsDowned)
        {
            return false;
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
        if (buttonBound || placeButton == null)
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
}
