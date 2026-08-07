using Fusion;
using UnityEngine;

public class BuildingPiece : NetworkBehaviour
{
    public const float DefaultMaxHealth = 100f;
    private const float InteractDistance = 3f;
    private const float MaxRequestedDamage = DefaultMaxHealth;

    private float offlineHealth = DefaultMaxHealth;
    private bool offlineInitialized;

    [Networked] public float Health { get; private set; }
    [Networked] public int PieceTypeValue { get; private set; }
    [Networked] public int GridX { get; private set; }
    [Networked] public int GridY { get; private set; }
    [Networked] public int GridZ { get; private set; }
    [Networked] public int RotationIndex { get; private set; }
    [Networked] public PlayerRef Placer { get; private set; }
    [Networked] private NetworkBool IsInitialized { get; set; }

    public BuildingPieceType PieceType => (BuildingPieceType)PieceTypeValue;
    public Vector3Int GridPosition => new Vector3Int(GridX, GridY, GridZ);
    public float HealthValue => IsNetworkedRuntime ? Health : offlineHealth;
    public float MaxHealthValue => DefaultMaxHealth;
    public float HealthRatio => Mathf.Clamp01(HealthValue / DefaultMaxHealth);
    public bool IsDestroyed => HealthValue <= 0f;

    private bool IsNetworkedRuntime => Object != null && Object.IsValid;
    private bool IsInitializedForVisuals => IsNetworkedRuntime ? IsInitialized : offlineInitialized;

    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock materialPropertyBlock;
    private GameObject generatedModel;
    private BoxCollider rootCollider;
    private bool rootColliderCreatedByBuildingPiece;
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
        if (!IsInitializedForVisuals)
        {
            return;
        }

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
    }

    private void ClearGeneratedModel()
    {
        if (generatedModel != null)
        {
            DestroyOwnedObject(generatedModel);
        }

        if (rootCollider != null && rootColliderCreatedByBuildingPiece)
        {
            DestroyOwnedObject(rootCollider);
        }

        generatedModel = null;
        rootCollider = null;
        rootColliderCreatedByBuildingPiece = false;
        meshRenderer = null;
        builtPieceTypeValue = int.MinValue;
    }

    private void UpdateDamageTint()
    {
        if (meshRenderer == null) return;

        float ratio = HealthRatio;
        Color color;
        if (ratio > 0.66f) color = Color.Lerp(Color.yellow, Color.green, (ratio - 0.66f) / 0.34f);
        else if (ratio > 0.33f) color = Color.Lerp(Color.red, Color.yellow, (ratio - 0.33f) / 0.33f);
        else color = Color.red;

        materialPropertyBlock ??= new MaterialPropertyBlock();
        meshRenderer.GetPropertyBlock(materialPropertyBlock);
        materialPropertyBlock.SetColor(ColorPropertyId, color);
        meshRenderer.SetPropertyBlock(materialPropertyBlock);
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
            IsInitialized = true;
        }
        else
        {
            offlineHealth = DefaultMaxHealth;
            offlineInitialized = true;
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
        if (!TryGetClampedDamage(amount, out float clampedAmount))
        {
            return;
        }

        if (!IsNetworkedRuntime)
        {
            ApplyOfflineDamage(clampedAmount);
            return;
        }

        NetworkObject resolvedRequester = ResolveLocalRequester(requester);
        if (!IsAuthorizedLocalRequester(resolvedRequester) || !IsValidRequesterForAction(resolvedRequester))
        {
            return;
        }

        if (HasStateAuthority)
        {
            ApplyNetworkDamage(resolvedRequester, clampedAmount);
            return;
        }

        RPC_RequestDamage(resolvedRequester, clampedAmount);
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

        NetworkObject resolvedRequester = ResolveLocalRequester(requester);
        if (!IsAuthorizedLocalRequester(resolvedRequester) || !IsValidRequesterForAction(resolvedRequester))
        {
            return;
        }

        if (HasStateAuthority)
        {
            ApplyNetworkDemolish(resolvedRequester);
            return;
        }

        RPC_RequestDemolish(resolvedRequester);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDamage(NetworkObject requester, float amount, RpcInfo info = default)
    {
        if (!TryGetClampedDamage(amount, out float clampedAmount))
        {
            return;
        }

        NetworkObject resolvedRequester = ResolveRpcRequester(requester, info.Source);
        if (!IsAuthorizedRequester(resolvedRequester, info))
        {
            return;
        }

        ApplyNetworkDamage(resolvedRequester, clampedAmount);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDemolish(NetworkObject requester, RpcInfo info = default)
    {
        NetworkObject resolvedRequester = ResolveRpcRequester(requester, info.Source);
        if (!IsAuthorizedRequester(resolvedRequester, info))
        {
            return;
        }

        ApplyNetworkDemolish(resolvedRequester);
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
        if (!TryGetClampedDamage(amount, out float clampedAmount) || !HasStateAuthority || !IsValidRequesterForAction(requester))
        {
            return;
        }

        Health = Mathf.Max(0f, Health - clampedAmount);
        if (Health <= 0f)
        {
            DropDemolishResources();
            Runner.Despawn(Object);
        }
    }

    private static bool TryGetClampedDamage(float amount, out float clampedAmount)
    {
        clampedAmount = 0f;
        if (float.IsNaN(amount) || float.IsInfinity(amount))
        {
            return false;
        }

        clampedAmount = Mathf.Clamp(amount, 0f, MaxRequestedDamage);
        return clampedAmount > 0f;
    }

    private void ApplyNetworkDemolish(NetworkObject requester)
    {
        if (!HasStateAuthority || !IsValidRequesterForAction(requester))
        {
            return;
        }

        DropDemolishResources();
        Runner.Despawn(Object);
    }

    private bool IsAuthorizedRequester(NetworkObject requester, RpcInfo info)
    {
        if (requester == null || !requester.IsValid)
        {
            return false;
        }

        if (requester.InputAuthority == info.Source)
        {
            return true;
        }

        return requester.HasStateAuthority && info.Source.IsNone;
    }

    private bool IsAuthorizedLocalRequester(NetworkObject requester)
    {
        if (requester == null || !requester.IsValid || Runner == null)
        {
            return false;
        }

        if (requester.InputAuthority == Runner.LocalPlayer)
        {
            return true;
        }

        return requester.HasStateAuthority && Runner.LocalPlayer.IsNone;
    }

    private NetworkObject ResolveLocalRequester(NetworkObject requester)
    {
        if (requester != null)
        {
            return requester;
        }

        if (Runner == null || Runner.LocalPlayer.IsNone)
        {
            return null;
        }

        return Runner.GetPlayerObject(Runner.LocalPlayer);
    }

    private NetworkObject ResolveRpcRequester(NetworkObject requester, PlayerRef source)
    {
        if (requester != null)
        {
            return requester;
        }

        if (Runner == null || source.IsNone)
        {
            return null;
        }

        return Runner.GetPlayerObject(source);
    }

    private bool IsValidRequesterForAction(NetworkObject requester)
    {
        return requester != null
            && requester.IsValid
            && IsRequesterInRange(requester)
            && !IsRequesterDeadOrDowned(requester);
    }

    private bool IsRequesterInRange(NetworkObject requester)
    {
        return requester != null && Vector3.Distance(requester.transform.position, transform.position) <= InteractDistance;
    }

    private static bool IsRequesterDeadOrDowned(NetworkObject requester)
    {
        PlayerSurvivalSystem survival = requester.GetComponent<PlayerSurvivalSystem>();
        if (survival != null && survival.IsDead)
        {
            return true;
        }

        FusionPlayerSurvival fusionSurvival = requester.GetComponent<FusionPlayerSurvival>();
        return fusionSurvival != null && fusionSurvival.IsDowned;
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
                DestroyOwnedObject(defaultCollider);
            model.transform.SetParent(transform, false);
            model.transform.localPosition = Vector3.zero;
            if (pieceType != BuildingPieceType.Floor)
                model.transform.localPosition += Vector3.up * GetModelYOffset(pieceType);
        }

        rootCollider = GetComponent<BoxCollider>();
        if (rootCollider == null)
        {
            rootCollider = gameObject.AddComponent<BoxCollider>();
            rootColliderCreatedByBuildingPiece = true;
        }

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

    private static void DestroyOwnedObject(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            if (target is Collider collider)
            {
                collider.enabled = false;
            }

            Destroy(target);
            return;
        }

        DestroyImmediate(target);
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
