using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Menu pengaturan in-game (tombol Escape): satu-satunya jalan keluar manual
/// dari sesi adalah "Kembali ke Main Menu" - shutdown runner Fusion lalu
/// load scene menu secara lokal. Panel dibangun runtime (tanpa prefab).
/// </summary>
[DisallowMultipleComponent]
public class EscapeQuitMenuUI : MonoBehaviour
{
    private const float ToggleCooldownSeconds = 0.25f;
    private const string MenuSceneName = "MainMenu";

    private static EscapeQuitMenuUI instance;

    private CanvasGroup canvasGroup;
    private bool isOpen;
    private bool leaveInProgress;
    private float lastToggleTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (instance != null)
        {
            return;
        }

        GameObject go = new GameObject("EscapeQuitMenuUI");
        instance = go.AddComponent<EscapeQuitMenuUI>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        BuildUi();
        ApplyVisibility(false);
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape) ||
            Time.unscaledTime - lastToggleTime < ToggleCooldownSeconds)
        {
            return;
        }

        // Jangan tampilkan saat masih di menu utama.
        Scene active = SceneManager.GetActiveScene();
        if (PersistentGameplayUI.IsMenuScene(active.name))
        {
            return;
        }

        lastToggleTime = Time.unscaledTime;
        SetOpen(!isOpen);
    }

    private void BuildUi()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;

        gameObject.AddComponent<GraphicRaycaster>();

        canvasGroup = gameObject.AddComponent<CanvasGroup>();
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        BuildBackdrop();
        BuildTitle("Pengaturan");
        BuildButton(
            label: "Kembali ke Main Menu",
            anchorY: 0.42f,
            onClick: OnLeaveClicked);
        BuildHintText("Esc untuk menutup");
    }

    private void BuildBackdrop()
    {
        GameObject backdrop = new GameObject("Backdrop", typeof(RectTransform));
        backdrop.transform.SetParent(transform, false);

        RectTransform rect = backdrop.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = backdrop.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.55f);
        image.raycastTarget = true;
    }

    private void BuildTitle(string text)
    {
        GameObject title = new GameObject("Title", typeof(RectTransform));
        title.transform.SetParent(transform, false);

        RectTransform rect = title.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(360f, 48f);
        rect.anchoredPosition = new Vector2(0f, -28f);

        Text label = title.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 26;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.text = text;
    }

    private void BuildButton(string label, float anchorY, UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonGo = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonGo.transform.SetParent(transform, false);

        RectTransform rect = buttonGo.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, anchorY);
        rect.anchorMax = new Vector2(0.5f, anchorY);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(300f, 54f);

        Image image = buttonGo.GetComponent<Image>();
        image.color = new Color(0.16f, 0.17f, 0.19f, 0.95f);

        Button button = buttonGo.GetComponent<Button>();
        button.onClick.AddListener(onClick);

        GameObject textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(buttonGo.transform, false);

        RectTransform textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        Text text = textGo.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 20;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.text = label;
    }

    private void BuildHintText(string text)
    {
        GameObject hint = new GameObject("Hint", typeof(RectTransform));
        hint.transform.SetParent(transform, false);

        RectTransform rect = hint.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(360f, 30f);
        rect.anchoredPosition = new Vector2(0f, 12f);

        Text label = hint.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 14;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(1f, 1f, 1f, 0.6f);
        label.text = text;
    }

    private void OnLeaveClicked()
    {
        if (leaveInProgress)
        {
            return;
        }

        leaveInProgress = true;
        ApplyVisibility(false);
        StartCoroutine(LeaveToMainMenuRoutine());
    }

    private System.Collections.IEnumerator LeaveToMainMenuRoutine()
    {
        PhotonFusionBootstrap bootstrap = FindObjectOfType<PhotonFusionBootstrap>(true);
        if (bootstrap != null && bootstrap.Runner != null && bootstrap.Runner.IsRunning)
        {
            bootstrap.LeaveRoom();
            // Beri waktu shutdown runner sebelum ganti scene.
            yield return new WaitForSecondsRealtime(0.8f);
        }

        AsyncOperation op = SceneManager.LoadSceneAsync(MenuSceneName);
        while (op != null && !op.isDone)
        {
            yield return null;
        }

        leaveInProgress = false;
    }

    private void SetOpen(bool open)
    {
        isOpen = open;
        ApplyVisibility(open);
    }

    private void ApplyVisibility(bool visible)
    {
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.blocksRaycasts = visible;
        canvasGroup.interactable = visible;
    }
}
