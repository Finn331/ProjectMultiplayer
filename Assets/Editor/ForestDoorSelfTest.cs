using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class ForestDoorSelfTest
{
    [MenuItem("Project Multiplayer/Run Forest Door Self Test")]
    public static void Run()
    {
        GameObject host = new GameObject("ForestDoorTestHost");
        PhotonFusionSceneLoader loader = host.AddComponent<PhotonFusionSceneLoader>();
        GameObject doorGo = new GameObject("ForestDoorTestDoor");
        ForestDoor door = doorGo.AddComponent<ForestDoor>();

        FieldInfo loaderField = typeof(ForestDoor).GetField("sceneLoader", BindingFlags.NonPublic | BindingFlags.Instance);
        loaderField.SetValue(door, loader);

        bool interacted = door.TryInteract();
        if (!interacted)
        {
            throw new System.InvalidOperationException("ForestDoor.TryInteract should return true when a scene loader exists.");
        }

        UnityEngine.Object.DestroyImmediate(doorGo);
        UnityEngine.Object.DestroyImmediate(host);
        Debug.Log("ForestDoorSelfTest passed.");
    }
}
