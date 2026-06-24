using Fusion;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class CraftingStationInteractor : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerInventoryUI inventoryUI;
    [SerializeField] private FusionPlayerSurvival survival;
    [SerializeField] private Button craftButton;

    [Header("Detection")]
    [SerializeField] private float scanRadius = 3f;
    [SerializeField] private float scanInterval = 0.2f;
    [SerializeField] private LayerMask stationMask = ~0;
    [SerializeField] private bool hideWhenUnavailable = true;

    private readonly Collider[] stationColliderBuffer = new Collider[16];
    private CraftingTableStation currentStation;
    private float nextScanTime;
    private bool buttonBound;
    private bool wasLocalAuthority;

    private void Awake()
    {
        ResolvePlayerReferences();
    }

    private void OnEnable()
    {
        ResolvePlayerReferences();
        if (!HasLocalAuthority())
        {
            currentStation = null;
            return;
        }

        wasLocalAuthority = true;
        ResolveButtonReference();
        BindButton();
        ScanForStation();
        RefreshButton();
    }

    private void OnDisable()
    {
        UnbindButton();

        if (wasLocalAuthority)
        {
            DowngradeCraftingContextIfNeeded();
        }

        wasLocalAuthority = false;
    }

    private void Update()
    {
        bool hasLocalAuthority = HasLocalAuthority();
        if (!hasLocalAuthority)
        {
            currentStation = null;
            UnbindButton();
            if (wasLocalAuthority)
            {
                DowngradeCraftingContextIfNeeded();
            }

            wasLocalAuthority = false;
            return;
        }

        wasLocalAuthority = true;
        ResolveButtonReference();
        BindButton();

        if (Time.time >= nextScanTime)
        {
            ScanForStation();
        }

        RefreshButton();
    }

    public void OpenCraftingTable()
    {
        if (!CanUseCurrentStation())
        {
            return;
        }

        inventoryUI.OpenCrafting(CraftingContext.CraftingTable);
    }

    private void ResolvePlayerReferences()
    {
        if (inventoryUI == null)
        {
            inventoryUI = GetComponent<PlayerInventoryUI>();
        }

        if (survival == null)
        {
            survival = GetComponent<FusionPlayerSurvival>();
        }
    }

    private void ResolveButtonReference()
    {
        if (craftButton == null)
        {
            craftButton = FindButtonByName("craft");
        }
    }

    private void BindButton()
    {
        if (buttonBound || craftButton == null || !HasLocalAuthority())
        {
            return;
        }

        craftButton.onClick.RemoveListener(OpenCraftingTable);
        craftButton.onClick.AddListener(OpenCraftingTable);
        buttonBound = true;
    }

    private void UnbindButton()
    {
        if (!buttonBound || craftButton == null)
        {
            buttonBound = false;
            return;
        }

        craftButton.onClick.RemoveListener(OpenCraftingTable);
        buttonBound = false;
    }

    private void ScanForStation()
    {
        nextScanTime = Time.time + Mathf.Max(0.05f, scanInterval);
        currentStation = FindNearestStation();
    }

    private CraftingTableStation FindNearestStation()
    {
        CraftingTableStation nearest = null;
        float bestDistance = Mathf.Max(0.5f, scanRadius);
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, bestDistance, stationColliderBuffer, stationMask, QueryTriggerInteraction.Collide);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = stationColliderBuffer[i];
            if (hit == null)
            {
                continue;
            }

            CraftingTableStation station = hit.GetComponentInParent<CraftingTableStation>();
            if (station == null)
            {
                station = hit.GetComponentInChildren<CraftingTableStation>();
            }

            if (station == null)
            {
                continue;
            }

            float distance = Vector3.Distance(transform.position, station.transform.position);
            if (distance <= bestDistance && station.IsInRange(transform.position))
            {
                bestDistance = distance;
                nearest = station;
            }
        }

        if (nearest != null)
        {
            return nearest;
        }

        CraftingTableStation[] stations = FindObjectsOfType<CraftingTableStation>();

        for (int i = 0; i < stations.Length; i++)
        {
            CraftingTableStation station = stations[i];
            if (station == null)
            {
                continue;
            }

            float distance = Vector3.Distance(transform.position, station.transform.position);
            if (distance <= bestDistance && station.IsInRange(transform.position))
            {
                bestDistance = distance;
                nearest = station;
            }
        }

        return nearest;
    }

    private bool CanUseCurrentStation()
    {
        return inventoryUI != null
            && currentStation != null
            && !IsDowned()
            && HasLocalAuthority()
            && currentStation.IsInRange(transform.position);
    }

    private void RefreshButton()
    {
        bool canUse = CanUseCurrentStation();
        SetButtonAvailable(canUse);

        if (!canUse)
        {
            DowngradeCraftingContextIfNeeded();
        }
    }

    private void SetButtonAvailable(bool available)
    {
        if (craftButton == null || !HasLocalAuthority())
        {
            return;
        }

        craftButton.interactable = available;
        if (hideWhenUnavailable)
        {
            craftButton.gameObject.SetActive(available);
        }
    }

    private void DowngradeCraftingContextIfNeeded()
    {
        if (inventoryUI != null && inventoryUI.CurrentCraftingContext == CraftingContext.CraftingTable)
        {
            inventoryUI.SetCraftingContext(CraftingContext.Simple);
        }
    }

    private bool IsDowned()
    {
        return survival != null && survival.IsDowned;
    }

    private bool HasLocalAuthority()
    {
        Fusion.NetworkObject fusionObject = GetComponent<Fusion.NetworkObject>();
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

        Unity.Netcode.NetworkObject netcodeObject = GetComponent<Unity.Netcode.NetworkObject>();
        if (netcodeObject != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            return netcodeObject.IsSpawned && netcodeObject.IsOwner;
        }

        return true;
    }

    private static Button FindButtonByName(string keyword)
    {
        Button[] buttons = FindObjectsOfType<Button>(true);
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
