using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

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

    private readonly List<Interactable> currentInteractables = new List<Interactable>();
    private Interactable currentTarget;
    private Button pickButtonComponent;
    private bool pickButtonBound;
    private float nextInteractTime;

    private void Start()
    {
        if (inventory == null)
        {
            inventory = GetComponent<PlayerInventory>();
        }

        if (networkInventoryBridge == null)
        {
            networkInventoryBridge = GetComponent<NetworkInventoryBridge>();
        }

        this.ResolvePickButton();
        this.BindPickButtonClick();

        if (pickButton != null)
        {
            pickButton.SetActive(false);
        }
    }

    private void Update()
    {
        if (!this.HasLocalInteractAuthority())
        {
            if (pickButton != null)
            {
                pickButton.SetActive(false);
            }
            return;
        }

        if (pickButton == null && autoBindPickButton)
        {
            this.ResolvePickButton();
            this.BindPickButtonClick();
        }

        this.DetectInteractable();
        this.CheckInteractableInFront();
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
        if (Physics.Raycast(playerCamera.transform.position, direction, distance, obstacleLayer))
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
        if (Time.unscaledTime < nextInteractTime)
        {
            return;
        }

        nextInteractTime = Time.unscaledTime + Mathf.Max(0.01f, interactDebounceSeconds);

        if (!this.HasLocalInteractAuthority() || currentTarget == null)
        {
            return;
        }

        PickableItem item = currentTarget.GetComponent<PickableItem>();
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
            Destroy(currentTarget.gameObject);
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
        NetworkObject networkObject = GetComponent<NetworkObject>();
        if (networkObject != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
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

        if (networkInventoryBridge == null || !networkInventoryBridge.UseNetworkedInventory)
        {
            return true;
        }

        return networkInventoryBridge.HasInputAuthority;
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
}
