using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ForestMobileOptimizationSelfTest
{
    private const string EnvironmentScenePath = "Assets/Scenes/Environment.unity";
    private const string OcclusionDataPath = "Assets/Scenes/Environment/OcclusionCullingData.asset";
    private const string FusionPlayerPrefabPath = "Assets/Assets/Prefabs/FusionPlayer.prefab";
    private const float MaxTreeDistance = 350f;
    private const float MaxCameraFarClip = 600f;

    [MenuItem("Project Multiplayer/Run Forest Mobile Optimization Self Test")]
    public static void Run()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!string.Equals(activeScene.path, EnvironmentScenePath, System.StringComparison.OrdinalIgnoreCase))
        {
            if (activeScene.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                throw new System.InvalidOperationException("Environment scene test cancelled because the active scene has unsaved changes.");
            }

            EditorSceneManager.OpenScene(EnvironmentScenePath, OpenSceneMode.Single);
        }

        Terrain[] terrains = Object.FindObjectsOfType<Terrain>(true);
        Expect(terrains.Length > 0, "Environment scene should contain Terrain objects.");
        Expect(File.Exists(OcclusionDataPath), "Environment occlusion culling data asset should exist at " + OcclusionDataPath + ".");

        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain terrain = terrains[i];
            Expect(terrain.treeDistance <= MaxTreeDistance,
                terrain.name + " treeDistance should be <= " + MaxTreeDistance + ", got " + terrain.treeDistance + ".");

            StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(terrain.gameObject);
            Expect((flags & StaticEditorFlags.OccluderStatic) != 0,
                terrain.name + " should be marked Occluder Static for terrain occlusion.");
            Expect((flags & StaticEditorFlags.OccludeeStatic) != 0,
                terrain.name + " should be marked Occludee Static for terrain occlusion.");
        }

        Camera[] cameras = Object.FindObjectsOfType<Camera>(true);
        Expect(cameras.Length > 0, "Environment scene should contain at least one Camera.");

        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (!camera.gameObject.activeInHierarchy)
            {
                continue;
            }

            Expect(camera.useOcclusionCulling, camera.name + " should have occlusion culling enabled.");
            Expect(camera.farClipPlane <= MaxCameraFarClip,
                camera.name + " farClipPlane should be <= " + MaxCameraFarClip + ", got " + camera.farClipPlane + ".");
        }

        ValidateFusionPlayerCameraPrefab();

        Debug.Log("ForestMobileOptimizationSelfTest passed.");
    }

    private static void ValidateFusionPlayerCameraPrefab()
    {
        Expect(File.Exists(FusionPlayerPrefabPath), "FusionPlayer prefab should exist at " + FusionPlayerPrefabPath + ".");

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(FusionPlayerPrefabPath);
        try
        {
            Camera[] playerCameras = prefabRoot.GetComponentsInChildren<Camera>(true);
            Expect(playerCameras.Length > 0, "FusionPlayer prefab should contain a player Camera.");
            for (int i = 0; i < playerCameras.Length; i++)
            {
                Camera camera = playerCameras[i];
                Expect(camera.useOcclusionCulling, "FusionPlayer camera should have occlusion culling enabled.");
                Expect(camera.farClipPlane <= MaxCameraFarClip,
                    "FusionPlayer camera farClipPlane should be <= " + MaxCameraFarClip + ", got " + camera.farClipPlane + ".");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new System.InvalidOperationException(message);
        }
    }
}
