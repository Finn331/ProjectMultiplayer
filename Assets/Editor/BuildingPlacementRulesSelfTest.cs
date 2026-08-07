using System;
using UnityEditor;
using UnityEngine;

public static class BuildingPlacementRulesSelfTest
{
    [MenuItem("Project Multiplayer/Run Building Placement Rules Self Test")]
    public static void Run()
    {
        Expect(BuildingPlacementRules.IsBuildingItem(ItemType.WallItem), "WallItem should be a building item.");
        Expect(BuildingPlacementRules.IsBuildingItem(ItemType.FloorItem), "FloorItem should be a building item.");
        Expect(BuildingPlacementRules.IsBuildingItem(ItemType.RoofItem), "RoofItem should be a building item.");
        Expect(BuildingPlacementRules.IsBuildingItem(ItemType.DoorItem), "DoorItem should be a building item.");
        Expect(!BuildingPlacementRules.IsBuildingItem(ItemType.Wood), "Wood should not be a building item.");
        Expect(!BuildingPlacementRules.IsBuildingItem(ItemType.Campfire), "Campfire should not be a building item.");

        Expect(BuildingPlacementRules.TryGetPieceType(ItemType.WallItem, out BuildingPieceType wall) && wall == BuildingPieceType.Wall, "WallItem should map to Wall.");
        Expect(BuildingPlacementRules.TryGetPieceType(ItemType.FloorItem, out BuildingPieceType floor) && floor == BuildingPieceType.Floor, "FloorItem should map to Floor.");
        Expect(BuildingPlacementRules.TryGetPieceType(ItemType.RoofItem, out BuildingPieceType roof) && roof == BuildingPieceType.Roof, "RoofItem should map to Roof.");
        Expect(BuildingPlacementRules.TryGetPieceType(ItemType.DoorItem, out BuildingPieceType door) && door == BuildingPieceType.Door, "DoorItem should map to Door.");
        Expect(!BuildingPlacementRules.TryGetPieceType(ItemType.Stone, out _), "Stone should not map to a building piece.");

        ExpectEqual(new Vector3(1f, 2f, 0.2f), BuildingPlacementRules.GetBounds(ItemType.WallItem), "Wall bounds");
        ExpectEqual(new Vector3(1f, 0.1f, 1f), BuildingPlacementRules.GetBounds(ItemType.FloorItem), "Floor bounds");
        ExpectEqual(new Vector3(1f, 0.1f, 1.5f), BuildingPlacementRules.GetBounds(ItemType.RoofItem), "Roof bounds");
        ExpectEqual(new Vector3(0.8f, 2f, 0.1f), BuildingPlacementRules.GetBounds(ItemType.DoorItem), "Door bounds");
        ExpectEqual(Vector3.one, BuildingPlacementRules.GetBounds(ItemType.Wood), "Fallback bounds");

        Vector3 wallCheckBounds = BuildingPlacementRules.GetPlacementCheckBounds(new Vector3(1f, 2f, 0.2f));
        ExpectEqual(new Vector3(0.94f, 1.94f, 0.14f), wallCheckBounds, "Wall check bounds");

        Vector3 tiny = BuildingPlacementRules.GetPlacementCheckBounds(new Vector3(0.01f, 0.01f, 0.01f));
        ExpectEqual(Vector3.one * 0.05f, tiny, "Minimum check bounds");

        ExpectEqual(new Vector3(1f, 0f, -3f), BuildingPlacementRules.SnapToGrid(new Vector3(1.2f, 0.49f, -2.6f)), "Positive snap");
        ExpectEqual(new Vector3(-2f, 1f, 4f), BuildingPlacementRules.SnapToGrid(new Vector3(-1.6f, 1.49f, 3.5f)), "Negative snap");

        ExpectYaw(0f, BuildingPlacementRules.NormalizeBuildingRotation(Quaternion.Euler(0f, 12f, 0f)), "Yaw 12");
        ExpectYaw(90f, BuildingPlacementRules.NormalizeBuildingRotation(Quaternion.Euler(0f, 47f, 0f)), "Yaw 47");
        ExpectYaw(180f, BuildingPlacementRules.NormalizeBuildingRotation(Quaternion.Euler(0f, 181f, 0f)), "Yaw 181");
        ExpectYaw(270f, BuildingPlacementRules.NormalizeBuildingRotation(Quaternion.Euler(0f, 314f, 0f)), "Yaw 314");

        Expect(BuildingPlacementRules.GetRotationIndex(Quaternion.Euler(0f, 0f, 0f)) == 0, "0 degrees index");
        Expect(BuildingPlacementRules.GetRotationIndex(Quaternion.Euler(0f, 90f, 0f)) == 1, "90 degrees index");
        Expect(BuildingPlacementRules.GetRotationIndex(Quaternion.Euler(0f, 180f, 0f)) == 2, "180 degrees index");
        Expect(BuildingPlacementRules.GetRotationIndex(Quaternion.Euler(0f, 270f, 0f)) == 3, "270 degrees index");
        Expect(BuildingPlacementRules.GetRotationIndex(Quaternion.Euler(0f, 360f, 0f)) == 0, "360 degrees index");

        Debug.Log("BuildingPlacementRulesSelfTest passed.");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static void ExpectEqual(Vector3 expected, Vector3 actual, string message)
    {
        if ((expected - actual).sqrMagnitude > 0.0001f)
        {
            throw new Exception(message + " expected " + expected + " actual " + actual);
        }
    }

    private static void ExpectYaw(float expected, Quaternion actual, string message)
    {
        if (Mathf.Abs(Mathf.DeltaAngle(expected, actual.eulerAngles.y)) > 0.001f)
        {
            throw new Exception(message + " expected yaw " + expected + " actual yaw " + actual.eulerAngles.y);
        }
    }
}
