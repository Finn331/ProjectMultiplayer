using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Membuat/memperbarui prefab jaringan hewan arktik di Assets/Resources/Wildlife/ArcticAnimal.prefab.
/// Menu: Project Multiplayer > Create Arctic Animal Prefab.
/// Isi: NetworkObject + AnimalAI + NavMeshAgent + CapsuleCollider + visual kapsul prosedural.
/// </summary>
public static class WildlifePrefabBuilder
{
    [MenuItem("Project Multiplayer/Create Arctic Animal Prefab")]
    public static void CreatePrefab()
    {
        string dirPath = "Assets/Resources/Wildlife";
        if (!AssetDatabase.IsValidFolder(dirPath))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            AssetDatabase.CreateFolder("Assets/Resources", "Wildlife");
        }

        string prefabPath = dirPath + "/ArcticAnimal.prefab";

        GameObject root = new GameObject("ArcticAnimal");

        CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
        collider.height = 1.6f;
        collider.radius = 0.5f;
        collider.center = new Vector3(0f, 0.8f, 0f);

        NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
        agent.speed = 2f;
        agent.angularSpeed = 360f;
        agent.acceleration = 12f;
        agent.stoppingDistance = 0.4f;
        agent.radius = 0.45f;
        agent.height = 1.6f;

        root.AddComponent<AnimalAI>();
        root.AddComponent<Fusion.NetworkObject>();

        // Visual prosedural netral (tanpa collider fisik).
        Transform visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Object.DestroyImmediate(body.GetComponent<CapsuleCollider>());
        body.name = "Body";
        body.transform.SetParent(visual, false);
        body.transform.localScale = new Vector3(0.7f, 0.55f, 1.1f);
        body.transform.localPosition = new Vector3(0f, 0.9f, 0f);

        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(head.GetComponent<SphereCollider>());
        head.name = "Head";
        head.transform.SetParent(visual, false);
        head.transform.localScale = new Vector3(0.42f, 0.42f, 0.55f);
        head.transform.localPosition = new Vector3(0f, 1.25f, 0.85f);

        Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", new Color(0.85f, 0.87f, 0.9f));
        body.GetComponent<MeshRenderer>().sharedMaterial = material;
        head.GetComponent<MeshRenderer>().sharedMaterial = material;

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        if (saved != null)
        {
            Debug.Log("[WildlifePrefabBuilder] prefab tersimpan: " + prefabPath);
        }
        else
        {
            Debug.LogError("[WildlifePrefabBuilder] gagal menyimpan prefab.");
        }
    }
}
