using UnityEngine;

public static class FusionSceneDropUtility
{
    public static int ComputeSceneDropId(Vector3 sourcePosition, ItemType itemType, int dropIndex)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + Mathf.RoundToInt(sourcePosition.x * 100f);
            hash = hash * 31 + Mathf.RoundToInt(sourcePosition.y * 100f);
            hash = hash * 31 + Mathf.RoundToInt(sourcePosition.z * 100f);
            hash = hash * 31 + (int)itemType;
            hash = hash * 31 + dropIndex;
            return hash == 0 ? 1 : hash;
        }
    }

    public static Vector2 ComputeDeterministicScatter(int sceneDropId, float radius)
    {
        float clampedRadius = Mathf.Max(0f, radius);
        if (clampedRadius <= 0f)
        {
            return Vector2.zero;
        }

        uint hash = unchecked((uint)sceneDropId);
        float angle = (hash % 3600u) * (Mathf.PI * 2f / 3600f);
        hash = hash * 1664525u + 1013904223u;
        float distance = ((hash & 0xffffu) / 65535f) * clampedRadius;

        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
    }
}
