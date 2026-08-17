using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor-only helper: when pressing Play from gameplay/test scenes, automatically opens
/// the Environment scene first (so the game enters it directly), then restores the
/// previously open scene after exiting Play mode. MainMenu is intentionally preserved
/// so the normal menu flow still works. Can be toggled from the menu
/// "Project Multiplayer/Play Mode/Launch Environment on Play".
/// </summary>
[InitializeOnLoad]
public static class PlayModeSceneBootstrapper
{
    private const string EnvironmentScenePath = "Assets/Scenes/Environment.unity";
    private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
    private const string V2MainMenuScenePath = "Assets/Scenes/V2MainMenu.unity";
    private const string LaunchOnPlayKey = "ProjectMultiplayer.LaunchEnvironmentOnPlay";
    private const string PreviousSceneKey = "ProjectMultiplayer.PreviousScenePath";

    static PlayModeSceneBootstrapper()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Project Multiplayer/Play Mode/Launch Environment on Play")]
    private static void ToggleLaunchEnvironmentOnPlay()
    {
        bool enabled = EditorPrefs.GetBool(LaunchOnPlayKey, true);
        EditorPrefs.SetBool(LaunchOnPlayKey, !enabled);
    }

    [MenuItem("Project Multiplayer/Play Mode/Launch Environment on Play", true)]
    private static bool ToggleLaunchEnvironmentOnPlayValidate()
    {
        Menu.SetChecked("Project Multiplayer/Play Mode/Launch Environment on Play", EditorPrefs.GetBool(LaunchOnPlayKey, true));
        return true;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (!EditorPrefs.GetBool(LaunchOnPlayKey, true))
        {
            return;
        }

        if (state == PlayModeStateChange.ExitingEditMode)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.isLoaded && ShouldLaunchEnvironmentFromScenePath(activeScene.path))
            {
                EditorPrefs.SetString(PreviousSceneKey, activeScene.path);
                EditorSceneManager.OpenScene(EnvironmentScenePath, OpenSceneMode.Single);
            }
            else
            {
                EditorPrefs.DeleteKey(PreviousSceneKey);
            }
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            string previousPath = EditorPrefs.GetString(PreviousSceneKey, string.Empty);
            EditorPrefs.DeleteKey(PreviousSceneKey);
            if (!string.IsNullOrEmpty(previousPath) && AssetDatabase.LoadAssetAtPath<SceneAsset>(previousPath) != null)
            {
                EditorSceneManager.OpenScene(previousPath, OpenSceneMode.Single);
            }
        }
    }

    private static bool ShouldLaunchEnvironmentFromScenePath(string scenePath)
    {
        return !string.IsNullOrEmpty(scenePath)
            && scenePath != EnvironmentScenePath
            && scenePath != MainMenuScenePath
            && scenePath != V2MainMenuScenePath;
    }
}
