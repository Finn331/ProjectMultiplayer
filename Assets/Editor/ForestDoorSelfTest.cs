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

    [MenuItem("Project Multiplayer/Run Forest Door Interaction Routing Self Test")]
    public static void RunRoutingTest()
    {
        const string playerInteractionSystemPath = "Assets/Scripts/Player/Survival/PlayerInteractionSystem.cs";
        string contents = System.IO.File.ReadAllText(playerInteractionSystemPath);
        if (!contents.Contains("GetComponent<ForestDoor>()"))
        {
            throw new System.InvalidOperationException("PlayerInteractionSystem.TryInteract does not route to ForestDoor.");
        }

        Debug.Log("ForestDoorInteractionRoutingSelfTest passed.");
    }
}
