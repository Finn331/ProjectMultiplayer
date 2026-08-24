using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Aturan roguelike forest: jika SEMUA player downed (mati) bersamaan,
/// room otomatis dipindahkan kembali ke scene Gameplay oleh master client.
/// Satu-satunya cara keluar forest selain itu adalah Quit ke Main Menu
/// lewat menu Settings (EscapeQuitMenuUI).
///
/// Transisi memakai Runner.LoadScene langsung (sama seperti yang dilakukan
/// PhotonFusionSceneLoader secara internal) agar tidak bergantung pada
/// keberadaan bootstrap/loader di sesi tertentu - termasuk sesi dev dari
/// DevAutoSessionStarter yang tidak memiliki PhotonFusionBootstrap.
///
/// Komponen dibuat otomatis saat runtime dan menempel DontDestroyOnLoad,
/// sehingga tidak perlu dipasang manual di scene mana pun.
/// </summary>
[DisallowMultipleComponent]
public class ForestWipeReturn : MonoBehaviour
{
    [Header("Konfigurasi")]
    [SerializeField] private float pollIntervalSeconds = 0.4f;
    [SerializeField] private float wipeGraceSeconds = 2.5f;

    private const string ForestSceneName = "Environment";
    private const string LobbySceneName = "Gameplay";

    private static ForestWipeReturn instance;

    private float wipeTimer;
    private float nextPollTime;
    private bool transitionInitiated;

    public static bool WipeTriggeredForTest { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (instance != null)
        {
            return;
        }

        GameObject root = new GameObject("ForestWipeReturn");
        instance = root.AddComponent<ForestWipeReturn>();
        DontDestroyOnLoad(root);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        if (Time.unscaledTime < nextPollTime)
        {
            return;
        }

        nextPollTime = Time.unscaledTime + Mathf.Max(0.1f, pollIntervalSeconds);
        PollWipeCondition();
    }

    private void PollWipeCondition()
    {
        // Deteksi forest dari nama scene aktif - andal di semua jalur sesi
        // (bootstrap MainMenu maupun DevAutoSessionStarter), karena stage
        // networked kadang tidak ter-update di sesi dev.
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        bool inForest = string.Equals(sceneName, ForestSceneName, System.StringComparison.OrdinalIgnoreCase);

        // Sudah selamat kembali ke lobby -> izinkan wipe berikutnya (run baru).
        if (string.Equals(sceneName, LobbySceneName, System.StringComparison.OrdinalIgnoreCase))
        {
            transitionInitiated = false;
        }

        if (!inForest)
        {
            ResetTimer();
            return;
        }

        NetworkRunner runner = ResolveRunner();
        if (runner == null)
        {
            ResetTimer();
            return;
        }

        // Keputusan pindah scene hanya boleh dari master.
        if (!runner.IsSharedModeMasterClient)
        {
            return;
        }

        if (transitionInitiated)
        {
            // Transisi sudah diminta; tunggu scene berganti tanpa spam.
            return;
        }

        WipeCheckResult result = EvaluatePlayers(runner);

        if (!result.HasAnyPlayerObject)
        {
            // Belum ada player valid (mis. baru join) -> jangan menilai wipe.
            ResetTimer();
            return;
        }

        if (result.AllDowned)
        {
            wipeTimer += Mathf.Max(0.1f, pollIntervalSeconds);
            if (wipeTimer >= Mathf.Max(0f, wipeGraceSeconds))
            {
                TriggerReturnToLobby(runner);
            }
        }
        else
        {
            ResetTimer();
        }
    }

    private static NetworkRunner ResolveRunner()
    {
        PhotonFusionBootstrap bootstrap = FindObjectOfType<PhotonFusionBootstrap>(true);
        if (bootstrap != null && bootstrap.Runner != null && bootstrap.Runner.IsRunning)
        {
            return bootstrap.Runner;
        }

        NetworkRunner runner = FindObjectOfType<NetworkRunner>();
        if (runner != null && runner.IsRunning)
        {
            return runner;
        }

        return null;
    }

    private WipeCheckResult EvaluatePlayers(NetworkRunner runner)
    {
        WipeCheckResult result = default;
        result.HasAnyPlayerObject = false;
        result.AllDowned = true;

        foreach (PlayerRef player in runner.ActivePlayers)
        {
            if (!runner.TryGetPlayerObject(player, out NetworkObject playerObject) || playerObject == null)
            {
                // Player belum spawn -> tidak menggugurkan kondisi wipe, tapi
                // juga tidak dihitung sebagai bukti semua sudah mati.
                continue;
            }

            FusionPlayerSurvival survival = playerObject.GetComponent<FusionPlayerSurvival>();
            if (survival == null)
            {
                continue;
            }

            result.HasAnyPlayerObject = true;
            if (!survival.IsDowned)
            {
                result.AllDowned = false;
                break;
            }
        }

        return result;
    }

    private void TriggerReturnToLobby(NetworkRunner runner)
    {
        transitionInitiated = true;
        ResetTimer();
        WipeTriggeredForTest = true;
        Debug.LogWarning("[ForestWipeReturn] Semua player mati - room kembali ke Gameplay.");

        int buildIndex = GetSceneBuildIndex(LobbySceneName);
        if (buildIndex < 0)
        {
            Debug.LogWarning("[ForestWipeReturn] Scene Gameplay tidak ada di Build Settings.");
            return;
        }

        try
        {
            NetworkSceneAsyncOp asyncOp = runner.LoadScene(SceneRef.FromIndex(buildIndex), UnityEngine.SceneManagement.LoadSceneMode.Single);
            if (!asyncOp.IsValid)
            {
                Debug.LogWarning("[ForestWipeReturn] Runner.LoadScene gagal dimulai.");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[ForestWipeReturn] Runner.LoadScene exception: " + ex.Message);
        }
    }

    private void ResetTimer()
    {
        wipeTimer = 0f;
    }

    private static int GetSceneBuildIndex(string sceneName)
    {
        int index = UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath(sceneName);
        if (index >= 0)
        {
            return index;
        }

        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i);
            if (System.IO.Path.GetFileNameWithoutExtension(path).Equals(sceneName, System.StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private struct WipeCheckResult
    {
        public bool HasAnyPlayerObject;
        public bool AllDowned;
    }
}
