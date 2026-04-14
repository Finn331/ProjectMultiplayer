using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class HotbarHeldItemPresenter : NetworkBehaviour
{
    [Serializable]
    private class HeldItemBinding
    {
        public ItemType itemType;
        public PickableItem visualPrefab;
        public Vector3 localPosition = new Vector3(0.04f, 0.02f, 0.02f);
        public Vector3 localEulerAngles = new Vector3(0f, 90f, 90f);
        public Vector3 localScale = Vector3.one;
    }

    [Header("References")]
    [SerializeField] private MobileHotbarUI hotbarUI;
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PlayerAxeCombat axeCombat;
    [SerializeField] private Animator animator;
    [SerializeField] private Transform handBone;

    [Header("Held Item Visual")]
    [SerializeField] private List<HeldItemBinding> heldItemBindings = new List<HeldItemBinding>();
    [SerializeField] private Vector3 defaultLocalPosition = new Vector3(0.04f, 0.02f, 0.02f);
    [SerializeField] private Vector3 defaultLocalEulerAngles = new Vector3(0f, 90f, 90f);
    [SerializeField] private Vector3 defaultLocalScale = Vector3.one;
    [SerializeField] private bool hideAxeWhileHoldingInventoryItem = true;

    private readonly NetworkVariable<int> selectedHeldItemValue =
        new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private GameObject currentHeldVisualInstance;
    private int currentAppliedValue = int.MinValue;
    private bool defaultAxeEquippedState = true;

    private void Awake()
    {
        ResolveReferences();
        defaultAxeEquippedState = axeCombat == null || axeCombat.IsAxeEquipped();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeHotbar();
        ApplyImmediateSelection();
    }

    private void OnDisable()
    {
        UnsubscribeHotbar();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        selectedHeldItemValue.OnValueChanged += OnSelectedHeldItemChanged;
        ApplyImmediateSelection();
    }

    public override void OnNetworkDespawn()
    {
        selectedHeldItemValue.OnValueChanged -= OnSelectedHeldItemChanged;
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        DestroyHeldVisual();
        base.OnDestroy();
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

        if (axeCombat == null)
        {
            axeCombat = GetComponent<PlayerAxeCombat>();
        }

        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (handBone == null)
        {
            handBone = ResolveRightHandBone();
        }
    }

    private void SubscribeHotbar()
    {
        if (hotbarUI == null)
        {
            return;
        }

        hotbarUI.SelectedSlotChanged -= OnHotbarSelectedSlotChanged;
        hotbarUI.SelectedSlotChanged += OnHotbarSelectedSlotChanged;
    }

    private void UnsubscribeHotbar()
    {
        if (hotbarUI == null)
        {
            return;
        }

        hotbarUI.SelectedSlotChanged -= OnHotbarSelectedSlotChanged;
    }

    private void ApplyImmediateSelection()
    {
        if (ShouldUseLocalHotbarSelection())
        {
            ItemType? selectedItem = hotbarUI != null ? hotbarUI.SelectedItem : null;
            ApplyHeldItemSelection(selectedItem);
            PushSelectedItemToNetwork(selectedItem);
            return;
        }

        ApplyHeldItemSelection(ConvertNetworkValueToItemType(selectedHeldItemValue.Value));
    }

    private void OnHotbarSelectedSlotChanged(int slotIndex, ItemType? selectedItem)
    {
        if (!ShouldUseLocalHotbarSelection())
        {
            return;
        }

        ApplyHeldItemSelection(selectedItem);
        PushSelectedItemToNetwork(selectedItem);
    }

    private void PushSelectedItemToNetwork(ItemType? selectedItem)
    {
        if (!CanWriteNetworkSelection())
        {
            return;
        }

        int networkValue = selectedItem.HasValue ? (int)selectedItem.Value : -1;
        if (selectedHeldItemValue.Value != networkValue)
        {
            selectedHeldItemValue.Value = networkValue;
        }
    }

    private void OnSelectedHeldItemChanged(int previousValue, int newValue)
    {
        if (ShouldUseLocalHotbarSelection())
        {
            return;
        }

        ApplyHeldItemSelection(ConvertNetworkValueToItemType(newValue));
    }

    private void ApplyHeldItemSelection(ItemType? selectedItem)
    {
        int appliedValue = selectedItem.HasValue ? (int)selectedItem.Value : -1;
        if (currentAppliedValue == appliedValue && currentHeldVisualInstance != null)
        {
            UpdateAxeVisibility(selectedItem.HasValue);
            return;
        }

        currentAppliedValue = appliedValue;
        DestroyHeldVisual();
        UpdateAxeVisibility(selectedItem.HasValue);

        if (!selectedItem.HasValue)
        {
            return;
        }

        Transform targetHand = handBone != null ? handBone : ResolveRightHandBone();
        if (targetHand == null)
        {
            return;
        }

        if (!TryResolveHeldVisualPrefab(selectedItem.Value, out PickableItem prefab))
        {
            return;
        }

        GameObject spawnedVisual = Instantiate(prefab.gameObject, targetHand);
        spawnedVisual.name = selectedItem.Value + "_HeldVisual";
        ConfigureHeldVisual(spawnedVisual, selectedItem.Value);
        currentHeldVisualInstance = spawnedVisual;
    }

    private void UpdateAxeVisibility(bool isHoldingInventoryItem)
    {
        if (axeCombat == null || !hideAxeWhileHoldingInventoryItem)
        {
            return;
        }

        axeCombat.SetAxeEquipped(!isHoldingInventoryItem && defaultAxeEquippedState);
    }

    private bool TryResolveHeldVisualPrefab(ItemType itemType, out PickableItem prefab)
    {
        prefab = null;

        for (int i = 0; i < heldItemBindings.Count; i++)
        {
            HeldItemBinding binding = heldItemBindings[i];
            if (binding != null && binding.itemType == itemType && binding.visualPrefab != null)
            {
                prefab = binding.visualPrefab;
                return true;
            }
        }

        if (inventory != null && inventory.TryResolveDropPrefab(itemType, out PickableItem resolvedPrefab))
        {
            prefab = resolvedPrefab;
            return true;
        }

        return false;
    }

    private void ConfigureHeldVisual(GameObject visualObject, ItemType itemType)
    {
        if (visualObject == null)
        {
            return;
        }

        HeldItemBinding binding = GetBinding(itemType);
        Vector3 localPosition = binding != null ? binding.localPosition : defaultLocalPosition;
        Vector3 localEulerAngles = binding != null ? binding.localEulerAngles : defaultLocalEulerAngles;
        Vector3 localScale = binding != null ? binding.localScale : defaultLocalScale;

        visualObject.transform.localPosition = localPosition;
        visualObject.transform.localRotation = Quaternion.Euler(localEulerAngles);
        visualObject.transform.localScale = localScale;

        NetworkObject networkObject = visualObject.GetComponent<NetworkObject>();
        if (networkObject != null)
        {
            networkObject.enabled = false;
        }

        PickableItem pickableItem = visualObject.GetComponent<PickableItem>();
        if (pickableItem != null)
        {
            pickableItem.enabled = false;
        }

        Interactable interactable = visualObject.GetComponent<Interactable>();
        if (interactable != null)
        {
            interactable.enabled = false;
        }

        Rigidbody[] rigidbodies = visualObject.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            rigidbodies[i].isKinematic = true;
            rigidbodies[i].detectCollisions = false;
        }

        Collider[] colliders = visualObject.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }
    }

    private HeldItemBinding GetBinding(ItemType itemType)
    {
        for (int i = 0; i < heldItemBindings.Count; i++)
        {
            HeldItemBinding binding = heldItemBindings[i];
            if (binding != null && binding.itemType == itemType)
            {
                return binding;
            }
        }

        return null;
    }

    private void DestroyHeldVisual()
    {
        if (currentHeldVisualInstance != null)
        {
            Destroy(currentHeldVisualInstance);
            currentHeldVisualInstance = null;
        }
    }

    private bool ShouldUseLocalHotbarSelection()
    {
        return !IsNetworkSelectionActive() || IsOwner;
    }

    private bool CanWriteNetworkSelection()
    {
        return IsNetworkSelectionActive() && IsOwner;
    }

    private bool IsNetworkSelectionActive()
    {
        return NetworkManager != null && NetworkManager.IsListening && IsSpawned;
    }

    private ItemType? ConvertNetworkValueToItemType(int value)
    {
        if (!Enum.IsDefined(typeof(ItemType), value))
        {
            return null;
        }

        return (ItemType)value;
    }

    private Transform ResolveRightHandBone()
    {
        if (handBone != null)
        {
            return handBone;
        }

        if (animator != null && animator.isHuman)
        {
            handBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
        }

        if (handBone == null)
        {
            handBone = FindDeepChildContains(transform, "RightHand");
        }

        return handBone;
    }

    private Transform FindDeepChildContains(Transform root, string keyword)
    {
        if (root == null || string.IsNullOrWhiteSpace(keyword))
        {
            return null;
        }

        string loweredKeyword = keyword.ToLowerInvariant();
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null)
            {
                continue;
            }

            if (child.name.ToLowerInvariant().Contains(loweredKeyword))
            {
                return child;
            }

            Transform nested = FindDeepChildContains(child, loweredKeyword);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }
}
