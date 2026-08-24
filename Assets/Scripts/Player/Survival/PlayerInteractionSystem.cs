using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.UI;

public class PlayerInteractionSystem : MonoBehaviour
{
    [Header("Detection")]
    public float detectionRadius = 5f;
    public float interactDistance = 2f;

    [Header("Layer")]
    public LayerMask interactableLayer;
    public LayerMask obstacleLayer;

    [Header("Reference")]
    public Camera playerCamera;
    public PlayerInventory inventory;
    [SerializeField] private NetworkInventoryBridge networkInventoryBridge;

    [Header("UI")]
    public GameObject pickButton;
    [SerializeField] private bool autoBindPickButton = true;
    [SerializeField] private string pickButtonNameContains = "pick";
    [SerializeField] private float interactDebounceSeconds = 0.1f;

    [Header("Building")]
    [SerializeField] private float demolishHoldTime = 1.5f;
    private float demolishHoldTimer;
    private BuildingPiece currentBuildingTarget;
    private GameObject hpBarObject;
    private UnityEngine.UI.Image hpBarFill;

    private readonly List<Interactable> currentInteractables = new List<Interactable>();
    private Interactable currentTarget;
    private Button pickButtonComponent;
    private bool pickButtonBound;
    private float nextInteractTime;
    private float baseDetectionRadius = -1f;
    private float baseInteractDistance = -1f;

    private void Start()
    {
        this.CacheBaseInteractionRange();
        this.RefreshSceneBindings();

        if (inventory == null)
        {
            inventory = GetComponent<PlayerInventory>();
        }

        if (networkInventoryBridge == null)
        {
            networkInventoryBridge = GetComponent<NetworkInventoryBridge>();
        }

        if (pickButton != null)
        {
            pickButton.SetActive(false);
        }
    }

    public void RefreshSceneBindings()
    {
        if (inventory == null)
        {
            inventory = GetComponent<PlayerInventory>();
        }

        if (networkInventoryBridge == null)
        {
            networkInventoryBridge = GetComponent<NetworkInventoryBridge>();
        }

        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>(true);
            if (playerCamera == null)
            {
                playerCamera = Camera.main;
            }
        }

        if (pickButton == null || pickButtonComponent == null)
        {
            pickButtonBound = false;
            pickButton = null;
            pickButtonComponent = null;
        }

        this.ResolvePickButton();
        this.BindPickButtonClick();
    }

    public void SetInteractionRangeMultipliers(float detectionMultiplier, float interactMultiplier)
    {
        this.CacheBaseInteractionRange();
        float clampedDetection = Mathf.Clamp(detectionMultiplier, 0.1f, 5f);
        float clampedInteract = Mathf.Clamp(interactMultiplier, 0.1f, 5f);
        detectionRadius = baseDetectionRadius * clampedDetection;
        interactDistance = baseInteractDistance * clampedInteract;
    }

    private void Update()
    {
        if (!this.HasLocalInteractAuthority())
        {
            return;
        }

        if (this.IsDowned())
        {
            currentTarget = null;
            if (pickButton != null)
            {
                pickButton.SetActive(false);
            }

            return;
        }

        if (pickButton == null && autoBindPickButton)
        {
            this.RefreshSceneBindings();
        }

        this.DetectInteractable();
        this.CheckInteractableInFront();
        this.DetectBuildingPiece();
    }

    private void DetectInteractable()
    {
        if (playerCamera == null)
        {
            return;
        }

        Collider[] hits = Physics.OverlapSphere(transform.position, detectionRadius, interactableLayer);

        for (int i = 0; i < currentInteractables.Count; i++)
        {
            if (currentInteractables[i] != null)
            {
                currentInteractables[i].DisableOutline();
            }
        }

        currentInteractables.Clear();

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i];
            Interactable interactable = col.GetComponent<Interactable>();
            if (interactable == null)
            {
                continue;
            }

            Vector3 direction = (col.transform.position - playerCamera.transform.position).normalized;
            float distance = Vector3.Distance(playerCamera.transform.position, col.transform.position);

            if (Physics.Raycast(playerCamera.transform.position, direction, out RaycastHit hit, distance, obstacleLayer))
            {
                continue;
            }

            interactable.EnableOutline();
            currentInteractables.Add(interactable);
        }
    }

    private void CheckInteractableInFront()
    {
        currentTarget = null;
        if (pickButton != null)
        {
            pickButton.SetActive(false);
        }

        if (playerCamera == null)
        {
            return;
        }

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactableLayer))
        {
            return;
        }

        Interactable interactable = hit.collider.GetComponent<Interactable>();
        if (interactable == null)
        {
            return;
        }

        Vector3 direction = (hit.collider.transform.position - playerCamera.transform.position).normalized;
        float distance = Vector3.Distance(playerCamera.transform.position, hit.collider.transform.position);
        
        RaycastHit[] obstacleHits = Physics.RaycastAll(playerCamera.transform.position, direction, distance, obstacleLayer);
        bool isBlocked = false;
        foreach (var ohit in obstacleHits)
        {
            if (ohit.collider.transform.root == transform.root) continue;
            if (ohit.collider.GetComponent<CharacterController>() != null) continue;
            if (ohit.collider.GetComponentInParent<FusionPlayerInventory>() != null) continue;
            
            isBlocked = true;
            break;
        }

        if (isBlocked)
        {
            return;
        }

        currentTarget = interactable;
        if (pickButton != null)
        {
            pickButton.SetActive(true);
        }
    }

    public void TryInteract()
    {
        if (this.IsDowned())
        {
            currentTarget = null;
            if (pickButton != null)
            {
                pickButton.SetActive(false);
            }

            return;
        }

        if (Time.unscaledTime < nextInteractTime)
        {
            return;
        }

        nextInteractTime = Time.unscaledTime + Mathf.Max(0.01f, interactDebounceSeconds);

        if (!this.HasLocalInteractAuthority())
        {
            return;
        }

        if (currentBuildingTarget != null)
        {
            demolishHoldTimer += Time.unscaledDeltaTime;
            if (demolishHoldTimer >= demolishHoldTime)
            {
                NetworkObject playerObject = GetComponent<NetworkObject>();
                currentBuildingTarget.RequestDemolish(playerObject);
                demolishHoldTimer = 0f;
                currentBuildingTarget = null;
                HideHpBar();
            }
            return;
        }

        if (currentTarget == null)
        {
            return;
        }

        PickableItem item = currentTarget.GetComponent<PickableItem>();
        if (item != null)
        {
            this.TryPickupItem(item);
            return;
        }

        FusionStorageChest fusionChest = currentTarget.GetComponent<FusionStorageChest>();
        if (fusionChest != null)
        {
            this.TryInteractFusionChest(fusionChest);
            return;
        }

        StorageChest chest = currentTarget.GetComponent<StorageChest>();
        if (chest != null)
        {
            if (chest.TryInteract(this) && pickButton != null)
            {
                pickButton.SetActive(false);
            }

            return;
        }

        CampfireCooking campfire = currentTarget.GetComponent<CampfireCooking>();
        if (campfire != null)
        {
            if (this.TryInteractCampfire(campfire) && pickButton != null)
            {
                pickButton.SetActive(false);
            }

            return;
        }

        FusionFurnace furnace = currentTarget.GetComponent<FusionFurnace>();
        if (furnace != null)
        {
            if (this.TryInteractFurnace(furnace) && pickButton != null)
            {
                pickButton.SetActive(false);
            }

            return;
        }

        ForestDoor forestDoor = currentTarget.GetComponent<ForestDoor>();
        if (forestDoor != null)
        {
            if (forestDoor.TryInteract() && pickButton != null)
            {
                pickButton.SetActive(false);
            }

            return;
        }

        ForestExitDoor forestExitDoor = currentTarget.GetComponent<ForestExitDoor>();
        if (forestExitDoor != null)
        {
            if (forestExitDoor.TryInteract() && pickButton != null)
            {
                pickButton.SetActive(false);
            }

            return;
        }

        currentTarget.Interact();
        if (pickButton != null)
        {
            pickButton.SetActive(false);
        }
    }

    private void TryInteractFusionChest(FusionStorageChest chest)
    {
        if (chest == null)
        {
            return;
        }

        if (chest.TryInteract(this) && pickButton != null)
        {
            pickButton.SetActive(false);
        }
    }

    private bool TryInteractCampfire(CampfireCooking campfire)
    {
        if (campfire == null || inventory == null) return false;

        var fusionInventory = GetComponent<FusionPlayerInventory>();
        if (fusionInventory != null)
        {
            return TryInteractCampfireFusion(campfire);
        }

        return false;
    }

    private bool TryInteractCampfireFusion(CampfireCooking campfire)
    {
        int hotbarSlot = 0;
        MobileHotbarUI hotbar = GetComponent<MobileHotbarUI>();
        if (hotbar != null) hotbarSlot = hotbar.SelectedSlotIndex;

        int globalSlot = inventory.HotbarStartIndex + hotbarSlot;
        ItemType? selectedItem = globalSlot >= 0 && globalSlot < inventory.TotalSlotCount
            ? inventory.GetSlotItemType(globalSlot) : null;

        if (selectedItem == ItemType.Wood && inventory.GetSlotAmount(globalSlot) > 0)
        {
            return campfire.TryAddToCampfireFromSlot(inventory, -1, true, -1);
        }

        if ((selectedItem == ItemType.RawChicken || selectedItem == ItemType.RawFish) && inventory.GetSlotAmount(globalSlot) > 0)
        {
            return campfire.TryAddToCampfireFromSlot(inventory, globalSlot, false, -1);
        }

        for (int i = 0; i < 3; i++)
        {
            if (campfire.HasOutput(i))
            {
                return campfire.TryPickupOutput(inventory, i);
            }
        }

        CampfireUI ui = GetComponent<CampfireUI>();
        if (ui == null) ui = gameObject.AddComponent<CampfireUI>();
        ui.Open(inventory, campfire);
        return true;
    }

    private bool TryInteractFurnace(FusionFurnace furnace)
    {
        if (furnace == null || inventory == null) return false;

        int hotbarSlot = 0;
        MobileHotbarUI hotbar = GetComponent<MobileHotbarUI>();
        if (hotbar != null) hotbarSlot = hotbar.SelectedSlotIndex;

        int globalSlot = inventory.HotbarStartIndex + hotbarSlot;
        ItemType? selectedItem = globalSlot >= 0 && globalSlot < inventory.TotalSlotCount
            ? inventory.GetSlotItemType(globalSlot) : null;

        if (selectedItem == ItemType.Wood && inventory.GetSlotAmount(globalSlot) > 0)
        {
            return furnace.TryAddToFurnaceFromSlot(inventory, -1, true, -1);
        }

        if ((selectedItem == ItemType.Iron || selectedItem == ItemType.RawChicken || selectedItem == ItemType.RawFish) && inventory.GetSlotAmount(globalSlot) > 0)
        {
            return furnace.TryAddToFurnaceFromSlot(inventory, globalSlot, false, -1);
        }

        for (int i = 0; i < 4; i++)
        {
            if (furnace.HasOutput(i))
            {
                return furnace.TryPickupOutput(inventory, i);
            }
        }

        FurnaceUI ui = GetComponent<FurnaceUI>();
        if (ui == null) ui = gameObject.AddComponent<FurnaceUI>();
        ui.Open(inventory, furnace);
        return true;
    }

    private void TryPickupItem(PickableItem item)
    {
        if (item == null)
        {
            return;
        }

        if (networkInventoryBridge != null && networkInventoryBridge.UseNetworkedInventory)
        {
            bool requested = networkInventoryBridge.TryRequestPickup(item);
            if (requested && pickButton != null)
            {
                pickButton.SetActive(false);
            }

            return;
        }

        var fusionInventory = GetComponent<FusionPlayerInventory>();
        if (fusionInventory != null)
        {
            bool requested = fusionInventory.RequestPickup(item);
            if (requested && pickButton != null)
            {
                pickButton.SetActive(false);
            }

            return;
        }

        if (inventory == null)
        {
            return;
        }

        int addedAmount = inventory.AddItem(item);
        if (addedAmount <= 0)
        {
            return;
        }

        if (addedAmount >= item.amount)
        {
            Destroy(item.gameObject);
        }
        else
        {
            item.amount -= addedAmount;
        }

        if (pickButton != null)
        {
            pickButton.SetActive(false);
        }
    }

    private bool HasLocalInteractAuthority()
    {
        var fusionObject = GetComponent<Fusion.NetworkObject>();
        if (fusionObject != null && fusionObject.IsValid)
        {
            return fusionObject.HasInputAuthority || fusionObject.HasStateAuthority;
        }

        Unity.Netcode.NetworkObject networkObject = GetComponent<Unity.Netcode.NetworkObject>();
        if (networkObject != null && Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
        {
            if (!networkObject.IsSpawned)
            {
                return false;
            }

            if (!networkObject.IsOwner)
            {
                return false;
            }
        }
        else if (networkInventoryBridge != null && networkInventoryBridge.UseNetworkedInventory)
        {
            return networkInventoryBridge.HasInputAuthority;
        }

        return true;
    }

    private void ResolvePickButton()
    {
        if (pickButton == null && autoBindPickButton)
        {
            pickButton = this.FindPickButtonObject();
        }

        pickButtonComponent = pickButton != null ? pickButton.GetComponent<Button>() : null;
    }

    private void BindPickButtonClick()
    {
        if (pickButtonComponent == null || pickButtonBound)
        {
            return;
        }

        if (this.HasPersistentTryInteractBinding())
        {
            pickButtonBound = true;
            return;
        }

        pickButtonComponent.onClick.AddListener(this.TryInteract);
        pickButtonBound = true;
    }

    private bool HasPersistentTryInteractBinding()
    {
        if (pickButtonComponent == null)
        {
            return false;
        }

        int persistentCount = pickButtonComponent.onClick.GetPersistentEventCount();
        for (int i = 0; i < persistentCount; i++)
        {
            Object target = pickButtonComponent.onClick.GetPersistentTarget(i);
            string methodName = pickButtonComponent.onClick.GetPersistentMethodName(i);
            if (target == (Object)this && methodName == nameof(TryInteract))
            {
                return true;
            }
        }

        return false;
    }

    private GameObject FindPickButtonObject()
    {
        Button[] buttons = FindObjectsOfType<Button>(true);
        string keyword = string.IsNullOrWhiteSpace(pickButtonNameContains)
            ? "pick"
            : pickButtonNameContains.Trim().ToLowerInvariant();

        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null || button.gameObject == null)
            {
                continue;
            }

            if (button.gameObject.name.ToLowerInvariant().Contains(keyword))
            {
                return button.gameObject;
            }
        }

        return null;
    }

    private void OnDestroy()
    {
        if (pickButtonComponent != null && pickButtonBound)
        {
            pickButtonComponent.onClick.RemoveListener(this.TryInteract);
        }

        pickButtonBound = false;
    }

    private void CacheBaseInteractionRange()
    {
        if (baseDetectionRadius < 0f)
        {
            baseDetectionRadius = Mathf.Max(0.1f, detectionRadius);
        }

        if (baseInteractDistance < 0f)
        {
            baseInteractDistance = Mathf.Max(0.1f, interactDistance);
        }
    }

    private bool IsDowned()
    {
        FusionPlayerSurvival survival = GetComponent<FusionPlayerSurvival>();
        return survival != null && survival.IsDowned;
    }

    private void DetectBuildingPiece()
    {
        currentBuildingTarget = null;
        if (playerCamera == null) return;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, interactDistance)) return;

        BuildingPiece piece = hit.collider.GetComponentInParent<BuildingPiece>();
        if (piece == null)
        {
            HideHpBar();
            demolishHoldTimer = 0f;
            return;
        }

        currentBuildingTarget = piece;
        ShowHpBar(piece);
    }

    private void ShowHpBar(BuildingPiece piece)
    {
        if (hpBarObject == null)
        {
            hpBarObject = new GameObject("BuildingHpBar", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            hpBarObject.transform.SetParent(FindFirstObjectByType<Canvas>()?.transform, false);
            hpBarObject.GetComponent<UnityEngine.UI.Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
            hpBarObject.GetComponent<RectTransform>().sizeDelta = new Vector2(200f, 8f);

            GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            fillGo.transform.SetParent(hpBarObject.transform, false);
            hpBarFill = fillGo.GetComponent<UnityEngine.UI.Image>();
            hpBarFill.color = Color.red;
            hpBarFill.type = UnityEngine.UI.Image.Type.Filled;
            hpBarFill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
            fillRt.sizeDelta = Vector2.zero; fillRt.anchoredPosition = Vector2.zero;
        }

        hpBarObject.SetActive(true);
        if (Camera.main != null)
        {
            Vector3 screenPos = Camera.main.WorldToScreenPoint(piece.transform.position + Vector3.up * 2f);
            ((RectTransform)hpBarObject.transform).position = screenPos;
        }
        if (hpBarFill != null) hpBarFill.fillAmount = piece.HealthRatio;
    }

    private void HideHpBar()
    {
        if (hpBarObject != null) hpBarObject.SetActive(false);
    }
}
