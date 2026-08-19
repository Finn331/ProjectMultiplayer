using UnityEngine;
using UnityEditor;

public static class FusionPlayerInventoryDeathDropSelfTest
{
    [MenuItem("Project Multiplayer/Run Fusion Player Inventory Death Drop Self Test")]
    public static void Run()
    {
        GameObject host = null;
        try
        {
            host = new GameObject("FusionPlayerInventoryDeathDropSelfTest_Host");
            var inventory = host.AddComponent<PlayerInventory>();

            int droppedEmpty = FusionPlayerInventory.DropAllItemsForDeathForTest(inventory, Vector3.zero, 20);
            Debug.Log("droppedEmpty=" + droppedEmpty);

            inventory.AddItem(ItemType.Wood, 5);
            var stacks = FusionPlayerInventory.EnumerateDeathDropStacksForTest(inventory, 20);
            Debug.Log("stackCount=" + stacks.Count + " first=" + (stacks.Count > 0 ? stacks[0].ItemType + ":" + stacks[0].Amount : "none"));

            int removed = FusionPlayerInventory.DropAllItemsForDeathForTest(inventory, Vector3.zero, 20);

            bool ok = stacks.Count == 1
                      && droppedEmpty == 0
                      && removed == 1
                      && inventory.CurrentTotalItems == 0;
            if (!ok)
            {
                throw new System.Exception(
                    "FusionPlayerInventoryDeathDropSelfTest FAILED: expected stackCount=1, droppedEmpty=0, removed=1, CurrentTotalItems=0" +
                    " (got stackCount=" + stacks.Count + ", droppedEmpty=" + droppedEmpty + ", removed=" + removed + ", CurrentTotalItems=" + inventory.CurrentTotalItems + ")");
            }

            Debug.Log("FusionPlayerInventoryDeathDropSelfTest passed.");
        }
        finally
        {
            if (host != null) Object.DestroyImmediate(host);
        }
    }
}
