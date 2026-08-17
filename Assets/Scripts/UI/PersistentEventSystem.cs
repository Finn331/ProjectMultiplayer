using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps the gameplay EventSystem alive across Fusion scene transitions together with the
/// gameplay Canvas. The EventSystem is required for UI raycasting so the Floating Joystick
/// keeps receiving drag input. Should live on the EventSystem root in the Gameplay scene.
/// </summary>
[DisallowMultipleComponent]
public class PersistentEventSystem : MonoBehaviour
{
    private static PersistentEventSystem instance;

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
        EnsureEventSystemValid();
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
        if (PersistentGameplayUI.IsMenuScene(scene.name))
        {
            Destroy(gameObject);
            return;
        }

        EnsureEventSystemValid();
    }

    private static void EnsureEventSystemValid()
    {
        if (instance == null)
        {
            return;
        }

        EventSystem eventSystem = instance.GetComponent<EventSystem>();
        if (eventSystem == null)
        {
            eventSystem = instance.gameObject.AddComponent<EventSystem>();
        }

        if (instance.GetComponent<StandaloneInputModule>() == null)
        {
            instance.gameObject.AddComponent<StandaloneInputModule>();
        }
    }
}