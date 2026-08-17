using System.IO;
using UnityEditor;
using UnityEngine;

public static class PersistentGameplayUISelfTest
{
    [MenuItem("Project Multiplayer/Run Persistent Gameplay UI Self Test")]
    public static void Run()
    {
        Expect(PersistentGameplayUI.IsMenuScene("MainMenu"), "MainMenu should be treated as a menu scene.");
        Expect(PersistentGameplayUI.IsMenuScene("V2MainMenu"), "V2MainMenu should be treated as a menu scene.");
        Expect(!PersistentGameplayUI.IsMenuScene("Gameplay"), "Gameplay should not be a menu scene.");
        Expect(!PersistentGameplayUI.IsMenuScene("Environment"), "Environment should not be a menu scene.");

        string gameplayScenePath = "Assets/Scenes/Gameplay.unity";
        if (!File.Exists(gameplayScenePath))
        {
            throw new System.InvalidOperationException("Gameplay scene not found at " + gameplayScenePath);
        }

        string contents = File.ReadAllText(gameplayScenePath);
        string gameplayUiGuid = GetScriptGuid(typeof(PersistentGameplayUI));
        if (string.IsNullOrEmpty(gameplayUiGuid) || !contents.Contains(gameplayUiGuid))
        {
            throw new System.InvalidOperationException("Gameplay scene does not reference PersistentGameplayUI (guid " + gameplayUiGuid + ") on its gameplay Canvas.");
        }

        string eventSystemGuid = GetScriptGuid(typeof(PersistentEventSystem));
        if (string.IsNullOrEmpty(eventSystemGuid) || !contents.Contains(eventSystemGuid))
        {
            throw new System.InvalidOperationException("Gameplay scene does not reference PersistentEventSystem (guid " + eventSystemGuid + ") on its EventSystem root.");
        }

        Debug.Log("PersistentGameplayUISelfTest passed.");
    }

    private static string GetScriptGuid(System.Type scriptType)
    {
        string[] guids = AssetDatabase.FindAssets(scriptType.Name + " t:MonoScript");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!path.EndsWith(scriptType.Name + ".cs", System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return guids[i];
        }

        return string.Empty;
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new System.InvalidOperationException(message);
        }
    }
}
