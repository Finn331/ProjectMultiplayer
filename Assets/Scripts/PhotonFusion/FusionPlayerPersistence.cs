using UnityEngine;

/// <summary>
/// Stores the local player's inventory and survival state as plain values so they can
/// survive Fusion scene transitions (Gameplay -> Environment forest). In Shared Mode the
/// player NetworkObject is re-created on scene load, so carrying data is captured before
/// the old object is destroyed and re-applied to the freshly spawned player.
/// </summary>
public static class FusionPlayerPersistence
{
    private sealed class Snapshot
    {
        public string RoomCode = string.Empty;
        public int RunnerInstanceId;
        public string InventorySnapshot = string.Empty;
        public float Health;
        public float Hunger;
        public float Thirst;
    }

    private static Snapshot pending;

    public static bool HasPendingForSession(string roomCode, int runnerInstanceId)
    {
        return pending != null
            && pending.RunnerInstanceId == runnerInstanceId
            && string.Equals(pending.RoomCode, roomCode ?? string.Empty, System.StringComparison.Ordinal);
    }

    public static void Capture(string roomCode, int runnerInstanceId, PlayerInventory inventory, PlayerSurvivalSystem survival)
    {
        if (inventory == null)
        {
            return;
        }

        pending = new Snapshot
        {
            RoomCode = string.IsNullOrEmpty(roomCode) ? string.Empty : roomCode,
            RunnerInstanceId = runnerInstanceId,
            InventorySnapshot = inventory.BuildSnapshotString(),
            Health = survival != null ? survival.CurrentHealth : 0f,
            Hunger = survival != null ? survival.CurrentHunger : 0f,
            Thirst = survival != null ? survival.CurrentThirst : 0f
        };
    }

    public static bool TryRestore(string roomCode, int runnerInstanceId, PlayerInventory inventory, PlayerSurvivalSystem survival)
    {
        if (pending == null || !HasPendingForSession(roomCode, runnerInstanceId) || inventory == null)
        {
            return false;
        }

        inventory.SetInventorySnapshot(pending.InventorySnapshot);
        if (survival != null)
        {
            survival.ApplyNetworkSnapshot(pending.Health, pending.Hunger, pending.Thirst);
        }

        pending = null;
        return true;
    }

    public static void Clear()
    {
        pending = null;
    }
}