#if UNITY_EDITOR
using Fusion;
using UnityEditor;
using UnityEngine;

public static class NetworkBuildingPiecePrefabBuilder
{
    private const string PrefabPath = "Assets/Assets/Prefabs/NetworkBuildingPiece.prefab";
    private const string FusionPrefabLabel = "FusionPrefab";

    [MenuItem("Project Multiplayer/Build Network Building Piece Prefab")]
    public static void BuildPrefab()
    {
        string folder = System.IO.Path.GetDirectoryName(PrefabPath)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
        {
            EnsureFolder(folder);
        }

        GameObject root = new GameObject("NetworkBuildingPiece");
        root.AddComponent<NetworkObject>();
        root.AddComponent<BuildingPiece>();

        BoxCollider collider = root.AddComponent<BoxCollider>();
        collider.size = Vector3.one;
        collider.center = Vector3.zero;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SetLabels(prefab, new[] { FusionPrefabLabel });
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorApplication.ExecuteMenuItem("Tools/Fusion/Rebuild Prefab Table");
        Debug.Log("NetworkBuildingPiece prefab created and Fusion prefab table rebuild requested.");
    }

    private static void EnsureFolder(string folder)
    {
        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }
}
#endif
