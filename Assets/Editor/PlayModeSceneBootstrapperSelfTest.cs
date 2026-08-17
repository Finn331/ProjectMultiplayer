using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PlayModeSceneBootstrapperSelfTest
{
    private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
    private const string V2MainMenuScenePath = "Assets/Scenes/V2MainMenu.unity";
    private const string GameplayScenePath = "Assets/Scenes/Gameplay.unity";
    private const string EnvironmentScenePath = "Assets/Scenes/Environment.unity";
    private const string LaunchOnPlayKey = "ProjectMultiplayer.LaunchEnvironmentOnPlay";
    private const string PreviousSceneKey = "ProjectMultiplayer.PreviousScenePath";

    [MenuItem("Project Multiplayer/Run Play Mode Scene Bootstrapper Self Test")]
    public static void Run()
    {
        string originalScenePath = SceneManager.GetActiveScene().path;
        bool originalLaunchOnPlay = EditorPrefs.GetBool(LaunchOnPlayKey, true);
        string originalPreviousScene = EditorPrefs.GetString(PreviousSceneKey, string.Empty);

        try
        {
            EditorPrefs.SetBool(LaunchOnPlayKey, true);
            EditorPrefs.DeleteKey(PreviousSceneKey);

            AssertSceneAfterExitingEditMode(MainMenuScenePath, "MainMenu");
            AssertSceneAfterExitingEditMode(V2MainMenuScenePath, "V2MainMenu");
            AssertSceneAfterExitingEditMode(GameplayScenePath, "Environment");
            AssertSceneAfterExitingEditMode(EnvironmentScenePath, "Environment");

            Debug.Log("PlayModeSceneBootstrapperSelfTest passed.");
        }
        finally
        {
            EditorPrefs.SetBool(LaunchOnPlayKey, originalLaunchOnPlay);
            if (string.IsNullOrEmpty(originalPreviousScene))
            {
                EditorPrefs.DeleteKey(PreviousSceneKey);
            }
            else
            {
                EditorPrefs.SetString(PreviousSceneKey, originalPreviousScene);
            }

            if (!string.IsNullOrEmpty(originalScenePath) && AssetDatabase.LoadAssetAtPath<SceneAsset>(originalScenePath) != null)
            {
                EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
            }
        }
    }

    private static void AssertSceneAfterExitingEditMode(string startScenePath, string expectedSceneName)
    {
        EditorSceneManager.OpenScene(startScenePath, OpenSceneMode.Single);
        InvokeExitingEditMode();

        string actualSceneName = SceneManager.GetActiveScene().name;
        if (!string.Equals(actualSceneName, expectedSceneName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Expected PlayModeSceneBootstrapper to keep/open scene '" + expectedSceneName +
                "' when starting from '" + startScenePath + "', but active scene is '" + actualSceneName + "'.");
        }
    }

    private static void InvokeExitingEditMode()
    {
        MethodInfo method = typeof(PlayModeSceneBootstrapper).GetMethod(
            "OnPlayModeStateChanged",
            BindingFlags.NonPublic | BindingFlags.Static);

        if (method == null)
        {
            throw new MissingMethodException("PlayModeSceneBootstrapper.OnPlayModeStateChanged was not found.");
        }

        method.Invoke(null, new object[] { PlayModeStateChange.ExitingEditMode });
    }

}
