using Fusion;
using UnityEngine;

public class BuildingPiece : NetworkBehaviour
{
    public const float DefaultMaxHealth = 100f;
    private const float InteractDistance = 3f;
    private const float MaxRequestedDamage = DefaultMaxHealth;

    private float offlineHealth = DefaultMaxHealth;
    private int offlinePieceTypeValue;
    private Vector3Int offlineGridPosition;
    private int offlineRotationIndex;
    private PlayerRef offlinePlacer;
    private bool offlineInitialized;

    [Networked] private float NetworkHealth { get; set; }
    [Networked] private int NetworkPieceTypeValue { get; set; }
    [Networked] private int NetworkGridX { get; set; }
    [Networked] private int NetworkGridY { get; set; }
    [Networked] private int NetworkGridZ { get; set; }
    [Networked] private int NetworkRotationIndex { get; set; }
    [Networked] private PlayerRef NetworkPlacer { get; set; }
    [Networked] private NetworkBool NetworkInitialized { get; set; }

    public float Health => IsNetworkedRuntime ? NetworkHealth : offlineHealth;
    public int PieceTypeValue => CurrentPieceTypeValue;
    public int GridX => CurrentGridPosition.x;
    public int GridY => CurrentGridPosition.y;
    public int GridZ => CurrentGridPosition.z;
    public int RotationIndex => CurrentRotationIndex;
    public PlayerRef Placer => IsNetworkedRuntime ? NetworkPlacer : offlinePlacer;
    public BuildingPieceType PieceType => (BuildingPieceType)CurrentPieceTypeValue;
    public Vector3Int GridPosition => CurrentGridPosition;
    public float HealthValue => Health;
    public float MaxHealthValue => DefaultMaxHealth;
    public float HealthRatio => Mathf.Clamp01(HealthValue / DefaultMaxHealth);
    public bool IsDestroyed => HealthValue <= 0f;

    private bool IsNetworkedRuntime => Object != null && Object.IsValid;
    private bool IsInitializedForVisuals => IsNetworkedRuntime ? NetworkInitialized : offlineInitialized;
    private int CurrentPieceTypeValue => IsNetworkedRuntime ? NetworkPieceTypeValue : offlinePieceTypeValue;
    private Vector3Int CurrentGridPosition => IsNetworkedRuntime ? new Vector3Int(NetworkGridX, NetworkGridY, NetworkGridZ) : offlineGridPosition;
    private int CurrentRotationIndex => IsNetworkedRuntime ? NetworkRotationIndex : offlineRotationIndex;

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

        int pieceTypeValue = CurrentPieceTypeValue;
        if (!System.Enum.IsDefined(typeof(BuildingPieceType), pieceTypeValue))
        {
            return;
        }

        if (generatedModel != null && builtPieceTypeValue == pieceTypeValue && rootCollider != null)
        {
            return;
        }

        ClearGeneratedModel();
        CreateModel((BuildingPieceType)pieceTypeValue);
        builtPieceTypeValue = pieceTypeValue;
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

        int clampedRotationIndex = Mathf.Clamp(rotIndex, 0, 3);

        if (IsNetworkedRuntime)
        {
            NetworkPieceTypeValue = (int)pieceType;
            NetworkGridX = gridPos.x;
            NetworkGridY = gridPos.y;
            NetworkGridZ = gridPos.z;
            NetworkRotationIndex = clampedRotationIndex;
            NetworkPlacer = placer;
            NetworkHealth = DefaultMaxHealth;
            NetworkInitialized = true;
        }
        else
        {
            offlinePieceTypeValue = (int)pieceType;
            offlineGridPosition = gridPos;
            offlineRotationIndex = clampedRotationIndex;
            offlinePlacer = placer;
            offlineHealth = DefaultMaxHealth;
            offlineInitialized = true;
        }

        transform.rotation = Quaternion.Euler(0f, CurrentRotationIndex * 90f, 0f);
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

        NetworkHealth = Mathf.Max(0f, NetworkHealth - clampedAmount);
        if (NetworkHealth <= 0f)
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
