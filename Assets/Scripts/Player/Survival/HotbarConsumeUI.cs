using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class HotbarConsumeUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MobileHotbarUI hotbarUI;
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private NetworkInventoryBridge networkInventoryBridge;
    [SerializeField] private PlayerSurvivalSystem survivalSystem;

    [Header("UI")]
    [SerializeField] private GameObject eatButton;
    [SerializeField] private bool autoBindEatButton = true;
    [SerializeField] private string eatButtonNameContains = "eat";
    [SerializeField] private bool hideWhenUnavailable = true;

    [Header("Behavior")]
    [SerializeField] private float consumeDebounceSeconds = 0.1f;

    private Button eatButtonComponent;
    private bool eatButtonBound;
    private float nextConsumeTime;

    private void Awake()
    {
        this.ResolveReferences();
    }

    private void OnEnable()
    {
        this.ResolveReferences();
        this.SubscribeEvents();
        this.ResolveEatButton();
        this.BindEatButton();
        this.RefreshEatButtonState();
    }

    private void OnDisable()
    {
        this.UnsubscribeEvents();
        this.UnbindEatButton();
    }

    public void TryConsumeSelectedHotbarItem()
    {
        if (Time.unscaledTime < nextConsumeTime)
        {
            return;
        }

        nextConsumeTime = Time.unscaledTime + Mathf.Max(0.01f, consumeDebounceSeconds);

        if (!this.HasLocalConsumeAuthority() || hotbarUI == null || inventory == null)
        {
            return;
        }

        int selectedHotbarSlot = hotbarUI.SelectedSlotIndex;
        int selectedGlobalSlot = hotbarUI.GetHotbarGlobalSlotIndex(selectedHotbarSlot);
        if (selectedGlobalSlot < 0)
        {
            return;
        }

        ItemType? selectedItemType = inventory.GetSlotItemType(selectedGlobalSlot);
        if (selectedItemType == null || !ConsumableItemCatalog.TryGetEffect(selectedItemType.Value, out _))
        {
            return;
        }

        bool consumed = false;
        if (networkInventoryBridge != null)
        {
            consumed = networkInventoryBridge.TryRequestConsumeFromSlot(selectedGlobalSlot);
        }
        else
        {
            if (survivalSystem == null)
            {
                survivalSystem = GetComponent<PlayerSurvivalSystem>();
            }

            if (survivalSystem != null && inventory.RemoveItemFromSlot(selectedGlobalSlot, 1, out ItemType removedItemType))
            {
                consumed = ConsumableItemCatalog.TryApply(survivalSystem, removedItemType);
                if (!consumed)
                {
                    inventory.AddItemToSlot(removedItemType, 1, selectedGlobalSlot);
                }
            }
        }

        if (!consumed && PickupUIManager.instance != null)
        {
            PickupUIManager.instance.ShowInfo("Item tidak bisa dikonsumsi saat ini.");
        }

        this.RefreshEatButtonState();
    }

    private void ResolveReferences()
    {
        if (hotbarUI == null)
        {
            hotbarUI = GetComponent<MobileHotbarUI>();
        }

        if (inventory == null)
        {
            inventory = GetComponent<PlayerInventory>();
        }

        if (networkInventoryBridge == null)
        {
            networkInventoryBridge = GetComponent<NetworkInventoryBridge>();
        }

        if (survivalSystem == null)
        {
            survivalSystem = GetComponent<PlayerSurvivalSystem>();
        }
    }

    private void ResolveEatButton()
    {
        if (eatButton == null && autoBindEatButton)
        {
            eatButton = this.FindEatButtonObject();
        }

        eatButtonComponent = eatButton != null ? eatButton.GetComponent<Button>() : null;
    }

    private void BindEatButton()
    {
        if (eatButtonComponent == null || eatButtonBound)
        {
            return;
        }

        if (this.HasPersistentTryConsumeBinding())
        {
            eatButtonBound = true;
            return;
        }

        eatButtonComponent.onClick.AddListener(this.TryConsumeSelectedHotbarItem);
        eatButtonBound = true;
    }

    private void UnbindEatButton()
    {
        if (eatButtonComponent != null && eatButtonBound)
        {
            eatButtonComponent.onClick.RemoveListener(this.TryConsumeSelectedHotbarItem);
        }

        eatButtonBound = false;
    }

    private bool HasPersistentTryConsumeBinding()
    {
        if (eatButtonComponent == null)
        {
            return false;
        }

        int persistentCount = eatButtonComponent.onClick.GetPersistentEventCount();
        for (int i = 0; i < persistentCount; i++)
        {
            Object target = eatButtonComponent.onClick.GetPersistentTarget(i);
            string methodName = eatButtonComponent.onClick.GetPersistentMethodName(i);
            if (target == (Object)this && methodName == nameof(TryConsumeSelectedHotbarItem))
            {
                return true;
            }
        }

        return false;
    }

    private void SubscribeEvents()
    {
        if (hotbarUI != null)
        {
            hotbarUI.SelectedSlotChanged -= this.OnSelectedSlotChanged;
            hotbarUI.SelectedSlotChanged += this.OnSelectedSlotChanged;
        }

        if (inventory != null)
        {
            inventory.InventoryChanged -= this.OnInventoryChanged;
            inventory.InventoryChanged += this.OnInventoryChanged;
        }
    }

    private void UnsubscribeEvents()
    {
        if (hotbarUI != null)
        {
            hotbarUI.SelectedSlotChanged -= this.OnSelectedSlotChanged;
        }

        if (inventory != null)
        {
            inventory.InventoryChanged -= this.OnInventoryChanged;
        }
    }

    private void OnSelectedSlotChanged(int slotIndex, ItemType? itemType)
    {
        this.RefreshEatButtonState();
    }

    private void OnInventoryChanged()
    {
        this.RefreshEatButtonState();
    }

    private void RefreshEatButtonState()
    {
        if (eatButtonComponent == null)
        {
            return;
        }

        bool canConsume = this.CanConsumeSelectedItem();
        eatButtonComponent.interactable = canConsume;

        if (hideWhenUnavailable)
        {
            eatButtonComponent.gameObject.SetActive(canConsume);
        }
    }

    private bool CanConsumeSelectedItem()
    {
        if (!this.HasLocalConsumeAuthority() || hotbarUI == null || inventory == null)
        {
            return false;
        }

        int selectedHotbarSlot = hotbarUI.SelectedSlotIndex;
        int selectedGlobalSlot = hotbarUI.GetHotbarGlobalSlotIndex(selectedHotbarSlot);
        if (selectedGlobalSlot < 0)
        {
            return false;
        }

        ItemType? itemType = inventory.GetSlotItemType(selectedGlobalSlot);
        if (itemType == null || inventory.GetSlotAmount(selectedGlobalSlot) <= 0)
        {
            return false;
        }

        return ConsumableItemCatalog.TryGetEffect(itemType.Value, out _);
    }

    private bool HasLocalConsumeAuthority()
    {
        NetworkObject networkObject = GetComponent<NetworkObject>();
        if (networkObject != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (!networkObject.IsSpawned || !networkObject.IsOwner)
            {
                return false;
            }
        }

        if (networkInventoryBridge == null || !networkInventoryBridge.UseNetworkedInventory)
        {
            return true;
        }

        return networkInventoryBridge.HasInputAuthority;
    }

    private GameObject FindEatButtonObject()
    {
        Button[] buttons = FindObjectsOfType<Button>(true);
        string keyword = string.IsNullOrWhiteSpace(eatButtonNameContains)
            ? "eat"
            : eatButtonNameContains.Trim().ToLowerInvariant();

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
}
