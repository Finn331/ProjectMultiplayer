using System.Collections.Generic;
using UnityEngine;

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

    [Header("UI")]
    public GameObject pickButton; // 🔥 button UI

    private List<Interactable> currentInteractables = new List<Interactable>();
    private Interactable currentTarget;

    void Start()
    {
        pickButton.SetActive(false);
    }

    void Update()
    {
        DetectInteractable();
        CheckInteractableInFront();
    }

    // ================= DETEKSI OBJECT =================
    void DetectInteractable()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, detectionRadius, interactableLayer);

        // disable semua dulu
        foreach (var item in currentInteractables)
        {
            if (item != null)
                item.DisableOutline();
        }

        currentInteractables.Clear();

        foreach (Collider col in hits)
        {
            Interactable interactable = col.GetComponent<Interactable>();
            if (interactable == null) continue;

            Vector3 direction = (col.transform.position - playerCamera.transform.position).normalized;
            float distance = Vector3.Distance(playerCamera.transform.position, col.transform.position);

            // 🔥 CEK HALANGAN
            if (Physics.Raycast(playerCamera.transform.position, direction, out RaycastHit hit, distance, obstacleLayer))
            {
                continue;
            }

            // 🔥 AKTIFKAN OUTLINE
            interactable.EnableOutline();
            currentInteractables.Add(interactable);
        }
    }

    // ================= CEK YANG BISA DI PICK =================
    void CheckInteractableInFront()
    {
        currentTarget = null;
        pickButton.SetActive(false);

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactableLayer))
        {
            Interactable interactable = hit.collider.GetComponent<Interactable>();

            if (interactable != null)
            {
                // 🔥 CEK HALANGAN LAGI
                Vector3 direction = (hit.collider.transform.position - playerCamera.transform.position).normalized;
                float distance = Vector3.Distance(playerCamera.transform.position, hit.collider.transform.position);

                if (!Physics.Raycast(playerCamera.transform.position, direction, distance, obstacleLayer))
                {
                    currentTarget = interactable;

                    // 🔥 TAMPILKAN BUTTON
                    pickButton.SetActive(true);
                }
            }
        }
    }

    // ================= INTERACT =================
    public void TryInteract()
    {
        if (currentTarget == null) return;

        PickableItem item = currentTarget.GetComponent<PickableItem>();

        if (item != null)
        {
            inventory.AddItem(item);
            Destroy(currentTarget.gameObject);

            pickButton.SetActive(false);
        }
    }
}