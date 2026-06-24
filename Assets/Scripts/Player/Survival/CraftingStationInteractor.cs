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
    [SerializeField] private bool hideWhenUnavailable = true;

    private CraftingTableStation currentStation;
    private float nextScanTime;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        BindButton();
        ScanForStation();
        RefreshButton();
    }

    private void OnDisable()
    {
        if (craftButton != null)
        {
            craftButton.onClick.RemoveListener(OpenCraftingTable);
        }
    }

    private void Update()
    {
        if (!HasLocalAuthority())
        {
            currentStation = null;
            SetButtonAvailable(false);
            return;
        }

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

    private void ResolveReferences()
    {
        if (inventoryUI == null)
        {
            inventoryUI = GetComponent<PlayerInventoryUI>();
        }

        if (survival == null)
        {
            survival = GetComponent<FusionPlayerSurvival>();
        }

        if (craftButton == null)
        {
            craftButton = FindButtonByName("craft");
        }
    }

    private void BindButton()
    {
        if (craftButton == null)
        {
            return;
        }

        craftButton.onClick.RemoveListener(OpenCraftingTable);
        craftButton.onClick.AddListener(OpenCraftingTable);
    }

    private void ScanForStation()
    {
        nextScanTime = Time.time + Mathf.Max(0.05f, scanInterval);
        currentStation = FindNearestStation();
    }

    private CraftingTableStation FindNearestStation()
    {
        CraftingTableStation[] stations = FindObjectsOfType<CraftingTableStation>();
        CraftingTableStation nearest = null;
        float bestDistance = Mathf.Max(0.5f, scanRadius);

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
        SetButtonAvailable(CanUseCurrentStation());
    }

    private void SetButtonAvailable(bool available)
    {
        if (craftButton == null)
        {
            return;
        }

        craftButton.interactable = available;
        if (hideWhenUnavailable)
        {
            craftButton.gameObject.SetActive(available);
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
