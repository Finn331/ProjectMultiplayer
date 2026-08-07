using UnityEngine;

public static class BuildingPlacementRules
{
    public const float PlacementContactSkin = 0.06f;

    public static bool IsBuildingItem(ItemType itemType)
    {
        return itemType == ItemType.WallItem
            || itemType == ItemType.FloorItem
            || itemType == ItemType.RoofItem
            || itemType == ItemType.DoorItem;
    }

    public static bool TryGetPieceType(ItemType itemType, out BuildingPieceType pieceType)
    {
        switch (itemType)
        {
            case ItemType.WallItem:
                pieceType = BuildingPieceType.Wall;
                return true;
            case ItemType.FloorItem:
                pieceType = BuildingPieceType.Floor;
                return true;
            case ItemType.RoofItem:
                pieceType = BuildingPieceType.Roof;
                return true;
            case ItemType.DoorItem:
                pieceType = BuildingPieceType.Door;
                return true;
            default:
                pieceType = default;
                return false;
        }
    }

    public static Vector3 GetBounds(ItemType itemType)
    {
        return itemType switch
        {
            ItemType.WallItem => new Vector3(1f, 2f, 0.2f),
            ItemType.FloorItem => new Vector3(1f, 0.1f, 1f),
            ItemType.RoofItem => new Vector3(1f, 0.1f, 1.5f),
            ItemType.DoorItem => new Vector3(0.8f, 2f, 0.1f),
            _ => Vector3.one
        };
    }

    public static Vector3 GetBounds(BuildingPieceType pieceType)
    {
        return pieceType switch
        {
            BuildingPieceType.Wall => new Vector3(1f, 2f, 0.2f),
            BuildingPieceType.Floor => new Vector3(1f, 0.1f, 1f),
            BuildingPieceType.Roof => new Vector3(1f, 0.1f, 1.5f),
            BuildingPieceType.Door => new Vector3(0.8f, 2f, 0.1f),
            _ => Vector3.one
        };
    }

    public static Vector3 GetPlacementCheckBounds(Vector3 bounds)
    {
        return Vector3.Max(bounds - Vector3.one * PlacementContactSkin, Vector3.one * 0.05f);
    }

    public static Vector3 SnapToGrid(Vector3 worldPosition)
    {
        const float gridSize = 1f;
        float snappedX = Mathf.Round(worldPosition.x / gridSize) * gridSize;
        float snappedY = Mathf.Round(worldPosition.y / gridSize) * gridSize;
        float snappedZ = Mathf.Round(worldPosition.z / gridSize) * gridSize;
        return new Vector3(snappedX, snappedY, snappedZ);
    }

    public static Quaternion NormalizeBuildingRotation(Quaternion rotation)
    {
        float yaw = Mathf.Round(rotation.eulerAngles.y / 90f) * 90f;
        return Quaternion.Euler(0f, yaw, 0f);
    }

    public static int GetRotationIndex(Quaternion rotation)
    {
        float normalizedYaw = NormalizeBuildingRotation(rotation).eulerAngles.y;
        int index = Mathf.RoundToInt(normalizedYaw / 90f) % 4;
        return index < 0 ? index + 4 : index;
    }
}
