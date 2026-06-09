using System;
using System.Collections.Generic;
using Fusion;
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
    [SerializeField] private bool equipAxeOnlyWhenSelectedInHotbar = true;

    private GameObject currentHeldVisualInstance;
    private int currentAppliedValue = int.MinValue;

    public override void Spawned()
    {
        ApplyImmediateSelection();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        DestroyHeldVisual();
    }

    private void Awake()
    {
        ResolveReferences();
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

    private void OnDestroy()
    {
        DestroyHeldVisual();
    }

    private void ResolveReferences()
    {
        if (hotbarUI == null) hotbarUI = GetComponent<MobileHotbarUI>();
        if (inventory == null) inventory = GetComponent<PlayerInventory>();
        if (axeCombat == null) axeCombat = GetComponent<PlayerAxeCombat>();
        if (animator == null) animator = GetComponent<Animator>();
        if (handBone == null) handBone = ResolveRightHandBone();
    }

    private void SubscribeHotbar()
    {
        if (hotbarUI == null) return;
        hotbarUI.SelectedSlotChanged -= OnHotbarSelectedSlotChanged;
        hotbarUI.SelectedSlotChanged += OnHotbarSelectedSlotChanged;
    }

    private void UnsubscribeHotbar()
    {
        if (hotbarUI == null) return;
        hotbarUI.SelectedSlotChanged -= OnHotbarSelectedSlotChanged;
    }

    private void ApplyImmediateSelection()
    {
        if (!IsLocalAuthority()) return;
        ItemType? selectedItem = hotbarUI != null ? hotbarUI.SelectedItem : null;
        ApplyHeldItemSelection(selectedItem);
    }

    private void OnHotbarSelectedSlotChanged(int slotIndex, ItemType? selectedItem)
    {
        if (!IsLocalAuthority()) return;
        ApplyHeldItemSelection(selectedItem);
    }

    private bool IsLocalAuthority()
    {
        return Object != null && Object.HasStateAuthority;
    }

    private void ApplyHeldItemSelection(ItemType? selectedItem)
    {
        int appliedValue = selectedItem.HasValue ? (int)selectedItem.Value : -1;
        bool wantsAxeEquipped = ShouldEquipAxeForSelection(selectedItem);
        if (currentAppliedValue == appliedValue && !wantsAxeEquipped && currentHeldVisualInstance != null)
        {
            UpdateAxeVisibility(selectedItem);
            return;
        }

        currentAppliedValue = appliedValue;
        DestroyHeldVisual();
        UpdateAxeVisibility(selectedItem);

        if (!selectedItem.HasValue) return;
        if (wantsAxeEquipped) return;

        Transform targetHand = handBone != null ? handBone : ResolveRightHandBone();
        if (targetHand == null) return;

        if (!TryResolveHeldVisualPrefab(selectedItem.Value, out PickableItem prefab)) return;

        GameObject spawnedVisual = Instantiate(prefab.gameObject, targetHand);
        spawnedVisual.name = selectedItem.Value + "_HeldVisual";
        ConfigureHeldVisual(spawnedVisual, selectedItem.Value);
        currentHeldVisualInstance = spawnedVisual;
    }

    private bool ShouldEquipAxeForSelection(ItemType? selectedItem)
    {
        if (axeCombat == null) return false;
        if (!selectedItem.HasValue) return false;
        if (!equipAxeOnlyWhenSelectedInHotbar) return false;
        return selectedItem.Value == ItemType.Axe;
    }

    private void UpdateAxeVisibility(ItemType? selectedItem)
    {
        if (axeCombat == null) return;
        axeCombat.SetAxeEquipped(ShouldEquipAxeForSelection(selectedItem));
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
        if (visualObject == null) return;

        HeldItemBinding binding = GetBinding(itemType);
        visualObject.transform.localPosition = binding != null ? binding.localPosition : defaultLocalPosition;
        visualObject.transform.localRotation = Quaternion.Euler(binding != null ? binding.localEulerAngles : defaultLocalEulerAngles);
        visualObject.transform.localScale = binding != null ? binding.localScale : defaultLocalScale;

        if (visualObject.GetComponent<NetworkObject>() is var no && no != null) no.enabled = false;
        if (visualObject.GetComponent<PickableItem>() is var pi && pi != null) pi.enabled = false;
        if (visualObject.GetComponent<Interactable>() is var ia && ia != null) ia.enabled = false;

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
            if (heldItemBindings[i] != null && heldItemBindings[i].itemType == itemType)
                return heldItemBindings[i];
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

    private Transform ResolveRightHandBone()
    {
        if (handBone != null) return handBone;
        if (animator != null && animator.isHuman) handBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (handBone == null) handBone = FindDeepChildContains(transform, "RightHand");
        return handBone;
    }

    private Transform FindDeepChildContains(Transform root, string keyword)
    {
        if (root == null || string.IsNullOrWhiteSpace(keyword)) return null;

        string loweredKeyword = keyword.ToLowerInvariant();
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null) continue;
            if (child.name.ToLowerInvariant().Contains(loweredKeyword)) return child;
            Transform nested = FindDeepChildContains(child, loweredKeyword);
            if (nested != null) return nested;
        }
        return null;
    }
}
