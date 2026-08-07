using Fusion;
using UnityEngine;

public class BuildingPiece : NetworkBehaviour
{
    public const float DefaultMaxHealth = 100f;
    private const float InteractDistance = 3f;

    private float offlineHealth = DefaultMaxHealth;

    [Networked] public float Health { get; private set; }
    [Networked] public int PieceTypeValue { get; private set; }
    [Networked] public int GridX { get; private set; }
    [Networked] public int GridY { get; private set; }
    [Networked] public int GridZ { get; private set; }
    [Networked] public int RotationIndex { get; private set; }
    [Networked] public PlayerRef Placer { get; private set; }

    public BuildingPieceType PieceType => (BuildingPieceType)PieceTypeValue;
    public Vector3Int GridPosition => new Vector3Int(GridX, GridY, GridZ);
    public float HealthValue => IsNetworkedRuntime ? Health : offlineHealth;
    public float MaxHealthValue => DefaultMaxHealth;
    public float HealthRatio => Mathf.Clamp01(HealthValue / DefaultMaxHealth);
    public bool IsDestroyed => HealthValue <= 0f;

    private bool IsNetworkedRuntime => Object != null && Object.IsValid;

    private MeshRenderer meshRenderer;
    private Material instanceMaterial;
    private GameObject generatedModel;
    private BoxCollider rootCollider;
    private int builtPieceTypeValue = int.MinValue;
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

    private void Start()
    {
        EnsureVisualBuilt();
    }

    public override void Spawned()
    {
        EnsureVisualBuilt();
    }

    public override void Render()
    {
        EnsureVisualBuilt();
        UpdateDamageTint();
    }

    private void Update()
    {
        if (!IsNetworkedRuntime)
        {
            EnsureVisualBuilt();
            UpdateDamageTint();
        }
    }

    private void EnsureVisualBuilt()
    {
        if (!System.Enum.IsDefined(typeof(BuildingPieceType), PieceTypeValue))
        {
            return;
        }

        if (generatedModel != null && builtPieceTypeValue == PieceTypeValue && rootCollider != null)
        {
            return;
        }

        ClearGeneratedModel();
        CreateModel(PieceType);
        builtPieceTypeValue = PieceTypeValue;
        meshRenderer = GetComponentInChildren<MeshRenderer>();
        instanceMaterial = meshRenderer != null ? meshRenderer.material : null;
    }

    private void ClearGeneratedModel()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }

        BoxCollider[] colliders = GetComponents<BoxCollider>();
        for (int i = colliders.Length - 1; i >= 0; i--)
        {
            if (Application.isPlaying)
            {
                Destroy(colliders[i]);
            }
            else
            {
                DestroyImmediate(colliders[i]);
            }
        }

        generatedModel = null;
        rootCollider = null;
        meshRenderer = null;
        instanceMaterial = null;
    }

    private void UpdateDamageTint()
    {
        if (instanceMaterial == null) return;

        float ratio = HealthRatio;
        Color color;
        if (ratio > 0.66f) color = Color.Lerp(Color.yellow, Color.green, (ratio - 0.66f) / 0.34f);
        else if (ratio > 0.33f) color = Color.Lerp(Color.red, Color.yellow, (ratio - 0.33f) / 0.33f);
        else color = Color.red;
        instanceMaterial.SetColor(ColorPropertyId, color);
    }

    public void Initialize(BuildingPieceType pieceType, Vector3Int gridPos, int rotIndex)
    {
        Initialize(pieceType, gridPos, rotIndex, PlayerRef.None);
    }

    public bool Initialize(BuildingPieceType pieceType, Vector3Int gridPos, int rotIndex, PlayerRef placer)
    {
        if (IsNetworkedRuntime && !HasStateAuthority)
        {
            return false;
        }

        PieceTypeValue = (int)pieceType;
        GridX = gridPos.x;
        GridY = gridPos.y;
        GridZ = gridPos.z;
        RotationIndex = Mathf.Clamp(rotIndex, 0, 3);
        Placer = placer;

        if (IsNetworkedRuntime)
        {
            Health = DefaultMaxHealth;
        }
        else
        {
            offlineHealth = DefaultMaxHealth;
        }

        transform.rotation = Quaternion.Euler(0f, RotationIndex * 90f, 0f);
        EnsureVisualBuilt();
        return true;
    }

    public void TakeDamage(float amount)
    {
        RequestDamage(null, amount);
    }

    public void RequestDamage(NetworkObject requester, float amount)
    {
        if (amount <= 0f)
        {
            return;
        }

        if (!IsNetworkedRuntime)
        {
            ApplyOfflineDamage(amount);
            return;
        }

        if (HasStateAuthority)
        {
            ApplyNetworkDamage(requester, amount);
            return;
        }

        RPC_RequestDamage(requester, amount);
    }

    public void Demolish()
    {
        RequestDemolish(null);
    }

    public void RequestDemolish(NetworkObject requester)
    {
        if (!IsNetworkedRuntime)
        {
            DropDemolishResources();
            Destroy(gameObject);
            return;
        }

        if (HasStateAuthority)
        {
            ApplyNetworkDemolish(requester);
            return;
        }

        RPC_RequestDemolish(requester);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDamage(NetworkObject requester, float amount, RpcInfo info = default)
    {
        if (!IsAuthorizedRequester(requester, info))
        {
            return;
        }

        ApplyNetworkDamage(requester, amount);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDemolish(NetworkObject requester, RpcInfo info = default)
    {
        if (!IsAuthorizedRequester(requester, info))
        {
            return;
        }

        ApplyNetworkDemolish(requester);
    }

    private void ApplyOfflineDamage(float amount)
    {
        offlineHealth = Mathf.Max(0f, offlineHealth - amount);
        if (offlineHealth <= 0f)
        {
            DropDemolishResources();
            Destroy(gameObject);
        }
    }

    private void ApplyNetworkDamage(NetworkObject requester, float amount)
    {
        if (!HasStateAuthority || amount <= 0f || !IsRequesterInRange(requester))
        {
            return;
        }

        Health = Mathf.Max(0f, Health - amount);
        if (Health <= 0f)
        {
            DropDemolishResources();
            Runner.Despawn(Object);
        }
    }

    private void ApplyNetworkDemolish(NetworkObject requester)
    {
        if (!HasStateAuthority || !IsRequesterInRange(requester))
        {
            return;
        }

        DropDemolishResources();
        Runner.Despawn(Object);
    }

    private bool IsAuthorizedRequester(NetworkObject requester, RpcInfo info)
    {
        if (requester == null)
        {
            return info.Source.IsNone;
        }

        if (requester.InputAuthority == info.Source)
        {
            return true;
        }

        return requester.HasStateAuthority && info.Source.IsNone;
    }

    private bool IsRequesterInRange(NetworkObject requester)
    {
        return requester == null || Vector3.Distance(requester.transform.position, transform.position) <= InteractDistance;
    }

    private void DropDemolishResources()
    {
        if (IsNetworkedRuntime && !HasStateAuthority) return;

        var recipe = GetCraftRecipe(PieceType);
        if (recipe == null) return;

        for (int i = 0; i < recipe.ingredients.Count; i++)
        {
            var ingredient = recipe.ingredients[i];
            int refund = Mathf.Max(1, ingredient.Amount / 2);
            Vector3 dropPos = transform.position + Vector3.up * 0.5f + Random.insideUnitSphere * 0.3f;
            SpawnResourceDrop(ingredient.itemType, refund, dropPos);
        }
    }

    private void SpawnResourceDrop(ItemType itemType, int amount, Vector3 position)
    {
        FusionPlayerInventory[] handlers = FindObjectsOfType<FusionPlayerInventory>();
        for (int i = 0; i < handlers.Length; i++)
        {
            FusionPlayerInventory handler = handlers[i];
            if (handler != null && handler.SpawnTreeDropsFromData(position, position, Vector3.forward, itemType, 1, amount, 0.2f))
            {
                return;
            }
        }

        if (IsNetworkedRuntime)
        {
            Debug.LogWarning($"Building refund drop failed for {itemType} x{amount}; no local authoritative FusionPlayerInventory could spawn it.", this);
            return;
        }

        GameObject drop = GameObject.CreatePrimitive(PrimitiveType.Cube);
        drop.transform.position = position;
        drop.transform.localScale = new Vector3(0.25f, 0.15f, 0.25f);
        PickableItem pickableItem = drop.AddComponent<PickableItem>();
        pickableItem.itemType = itemType;
        pickableItem.amount = amount;
        pickableItem.itemName = itemType.ToString();
    }

    private CraftingRecipe GetCraftRecipe(BuildingPieceType pieceType)
    {
        var system = FindObjectOfType<BandageCraftingSystem>();
        if (system == null) return null;
        ItemType outputType = pieceType switch
        {
            BuildingPieceType.Wall => ItemType.WallItem,
            BuildingPieceType.Floor => ItemType.FloorItem,
            BuildingPieceType.Roof => ItemType.RoofItem,
            BuildingPieceType.Door => ItemType.DoorItem,
            _ => default
        };
        var recipes = system.GetAvailableRecipes(CraftingContext.CraftingTable);
        for (int i = 0; i < recipes.Count; i++)
        {
            if (recipes[i].outputItemType == outputType)
                return recipes[i];
        }
        return null;
    }

    private void CreateModel(BuildingPieceType pieceType)
    {
        GameObject model = null;
        switch (pieceType)
        {
            case BuildingPieceType.Wall:
                model = GameObject.CreatePrimitive(PrimitiveType.Cube);
                model.transform.localScale = new Vector3(1f, 2f, 0.2f);
                break;
            case BuildingPieceType.Floor:
                model = GameObject.CreatePrimitive(PrimitiveType.Cube);
                model.transform.localScale = new Vector3(1f, 0.1f, 1f);
                break;
            case BuildingPieceType.Roof:
                model = GameObject.CreatePrimitive(PrimitiveType.Cube);
                model.transform.localScale = new Vector3(1f, 0.1f, 1.5f);
                model.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);
                break;
            case BuildingPieceType.Door:
                model = GameObject.CreatePrimitive(PrimitiveType.Cube);
                model.transform.localScale = new Vector3(0.8f, 2f, 0.1f);
                break;
        }
        if (model != null)
        {
            generatedModel = model;
            Collider defaultCollider = model.GetComponent<Collider>();
            if (defaultCollider != null)
                DestroyImmediate(defaultCollider);
            model.transform.SetParent(transform, false);
            model.transform.localPosition = Vector3.zero;
            if (pieceType != BuildingPieceType.Floor)
                model.transform.localPosition += Vector3.up * GetModelYOffset(pieceType);
        }

        rootCollider = gameObject.AddComponent<BoxCollider>();
        BoxCollider collider = rootCollider;
        switch (pieceType)
        {
            case BuildingPieceType.Wall:
                collider.size = BuildingPlacementRules.GetBounds(pieceType);
                collider.center = new Vector3(0f, 1f, 0f);
                break;
            case BuildingPieceType.Floor:
                collider.size = BuildingPlacementRules.GetBounds(pieceType);
                collider.center = Vector3.zero;
                break;
            case BuildingPieceType.Roof:
                collider.size = BuildingPlacementRules.GetBounds(pieceType);
                collider.center = new Vector3(0f, 0.05f, 0f);
                break;
            case BuildingPieceType.Door:
                collider.size = BuildingPlacementRules.GetBounds(pieceType);
                collider.center = new Vector3(0f, 1f, 0f);
                break;
        }
    }

    private static float GetModelYOffset(BuildingPieceType pieceType)
    {
        switch (pieceType)
        {
            case BuildingPieceType.Wall: return 1f;
            case BuildingPieceType.Roof: return 0.05f;
            case BuildingPieceType.Door: return 1f;
            default: return 0f;
        }
    }
}
