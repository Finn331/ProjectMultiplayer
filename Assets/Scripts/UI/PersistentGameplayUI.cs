using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps the gameplay UI Canvas alive across Fusion scene transitions
/// (e.g. Gameplay -> Environment forest). The UI lives on the Gameplay scene,
/// so without this it would be destroyed when LoadScene(Single) unloads Gameplay.
/// The component is meant to live on the root Canvas GameObject.
/// </summary>
[DisallowMultipleComponent]
public class PersistentGameplayUI : MonoBehaviour
{
    private static PersistentGameplayUI instance;

    public static bool Exists => instance != null;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        if (instance == this)
        {
            instance = null;
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (IsMenuScene(scene.name))
        {
            Destroy(gameObject);
        }
    }

    public static bool IsMenuScene(string sceneName)
    {
        return string.Equals(sceneName, "MainMenu", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(sceneName, "V2MainMenu", System.StringComparison.OrdinalIgnoreCase);
    }
}
