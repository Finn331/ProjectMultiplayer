using UnityEngine;

public class BuildingPiece : MonoBehaviour
{
    public const float DefaultMaxHealth = 100f;

    private float health = DefaultMaxHealth;
    private int pieceTypeValue;
    private Vector3Int gridPosition;
    private int rotationIndex;

    public BuildingPieceType PieceType => (BuildingPieceType)pieceTypeValue;
    public float HealthValue => health;
    public float MaxHealthValue => DefaultMaxHealth;
    public float HealthRatio => health / DefaultMaxHealth;
    public bool IsDestroyed => health <= 0f;

    private MeshRenderer meshRenderer;
    private Material instanceMaterial;
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

    private void Start()
    {
        meshRenderer = GetComponentInChildren<MeshRenderer>();
        if (meshRenderer != null)
        {
            instanceMaterial = meshRenderer.material;
        }
    }

    private void Update()
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
        pieceTypeValue = (int)pieceType;
        gridPosition = gridPos;
        rotationIndex = rotIndex;
        health = DefaultMaxHealth;
        CreateModel(pieceType);
    }

    public void TakeDamage(float amount)
    {
        health = Mathf.Max(0f, health - amount);
        if (health <= 0f)
        {
            DropDemolishResources();
            Destroy(gameObject);
        }
    }

    public void Demolish()
    {
        DropDemolishResources();
        Destroy(gameObject);
    }

    private void DropDemolishResources()
    {
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
        var handler = FindObjectOfType<FusionPlayerInventory>();
        if (handler == null)
        {
            GameObject drop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            drop.transform.position = position;
            drop.transform.localScale = new Vector3(0.25f, 0.15f, 0.25f);
            PickableItem pi = drop.AddComponent<PickableItem>();
            pi.itemType = itemType;
            pi.amount = amount;
            pi.itemName = itemType.ToString();
            return;
        }
        handler.SpawnTreeDropsFromData(position, position, Vector3.forward, itemType, 1, amount, 0.2f);
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
            model.transform.SetParent(transform, false);
            model.transform.localPosition = Vector3.zero;
            if (pieceType != BuildingPieceType.Floor)
                model.transform.localPosition += Vector3.up * GetModelYOffset(pieceType);
        }

        BoxCollider collider = gameObject.AddComponent<BoxCollider>();
        switch (pieceType)
        {
            case BuildingPieceType.Wall:
                collider.size = new Vector3(1f, 2f, 0.2f);
                collider.center = new Vector3(0f, 1f, 0f);
                break;
            case BuildingPieceType.Floor:
                collider.size = new Vector3(1f, 0.1f, 1f);
                collider.center = Vector3.zero;
                break;
            case BuildingPieceType.Roof:
                collider.size = new Vector3(1f, 0.1f, 1.5f);
                collider.center = new Vector3(0f, 0.05f, 0f);
                break;
            case BuildingPieceType.Door:
                collider.size = new Vector3(0.8f, 2f, 0.1f);
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
