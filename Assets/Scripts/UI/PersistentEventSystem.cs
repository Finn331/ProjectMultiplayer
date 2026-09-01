using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem.UI;

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

        // The project runs with the Input System package (activeInputHandler: 2), so the legacy
        // StandaloneInputModule reads dead Input.touches / Input.mousePosition and never routes
        // pointer events to the Floating Joystick / LookArea. Use InputSystemUIInputModule instead,
        // otherwise the mobile joystick never receives OnDrag and the player cannot move/look.
        StandaloneInputModule legacy = instance.GetComponent<StandaloneInputModule>();
        if (legacy != null)
        {
            Object.Destroy(legacy);
        }

        if (instance.GetComponent<InputSystemUIInputModule>() == null)
        {
            InputSystemUIInputModule inputModule = instance.gameObject.AddComponent<InputSystemUIInputModule>();
            if (inputModule != null)
            {
                inputModule.AssignDefaultActions();
            }
        }
    }
}