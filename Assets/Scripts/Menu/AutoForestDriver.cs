using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Playtest automation: driver lintas-scene untuk flag CLI -autoForest.
/// MainMenuController hancur saat pindah scene (tanpa DontDestroyOnLoad),
/// sehingga loop Update() autoForest-nya mati sebelum sempat trigger
/// HostStartForest. Komponen ini dibuat dari TryAutoJoinFromCommandLine,
/// bertahan lintas scene, dan menunggu:
///   1. runner Fusion running + karakter lokal ter-spawn (di lobby/forest),
///   2. klien menjadi Shared-mode MasterClient (session creator),
/// lalu memanggil Runner.LoadScene(Environment) SEKALI.
/// Non-master TIDAK load sendiri (mengikuti replikasi scene dari master).
/// </summary>
public class AutoForestDriver : MonoBehaviour
{
    private const float MasterWaitTimeoutSeconds = 90f;
    private const float LogIntervalSeconds = 5f;

    private bool started;
    private float timer;
    private float nextLogTime;
    private PhotonFusionBootstrap bootstrap;

    public static void CreateFromCommandLine()
    {
        if (FindObjectOfType<AutoForestDriver>() != null)
        {
            return;
        }

        GameObject go = new GameObject("AutoForestDriver");
        Application.quitting += () => { if (go != null) { Destroy(go); } };
        DontDestroyOnLoad(go);
        go.AddComponent<AutoForestDriver>();
        WildlifeTestLog("[AutoForest] driver created");
    }

    private static void WildlifeTestLog(string message)
    {
        try
        {
            string path = Application.persistentDataPath + "/wildlife_test.log";
            System.IO.File.AppendAllText(path,
                System.DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + "\n");
        }
        catch (System.Exception)
        {
            // Logging best-effort; jangan ganggu gameplay.
        }
    }

    private void Update()
    {
        if (started)
        {
            return;
        }

        timer += Time.unscaledDeltaTime;

        if (bootstrap == null)
        {
            bootstrap = FindObjectOfType<PhotonFusionBootstrap>();
        }

        if (bootstrap == null || bootstrap.Runner == null || !bootstrap.Runner.IsRunning)
        {
            LogThrottled("waiting runner");
            if (timer > MasterWaitTimeoutSeconds)
            {
                started = true;
                WildlifeTestLog("[AutoForest] ABORT - runner never started within " + MasterWaitTimeoutSeconds + "s");
            }
            return;
        }

        Fusion.NetworkRunner runner = bootstrap.Runner;

        // Tunggu karakter lokal: bukti bahwa spawn awal di scene sekarang selesai.
        bool localCharacterReady = false;
        foreach (Fusion.NetworkObject networkObject in FindObjectsOfType<Fusion.NetworkObject>())
        {
            if (networkObject.StateAuthority == runner.LocalPlayer
                && networkObject.name.Contains("FusionPlayer"))
            {
                localCharacterReady = true;
                break;
            }
        }
        if (!localCharacterReady)
        {
            LogThrottled("runner ok, waiting local character");
            if (timer > MasterWaitTimeoutSeconds)
            {
                started = true;
                WildlifeTestLog("[AutoForest] ABORT - local character never spawned within " + MasterWaitTimeoutSeconds + "s");
            }
            return;
        }

        if (SceneManager.GetActiveScene().name == "Environment")
        {
            started = true;
            WildlifeTestLog("[AutoForest] already in Environment - done");
            // Klien follower tidak lewat LoadForestWhenIdle; buat probe di sini.
            WildlifeKillProbe.TryCreate();
            return;
        }

        if (!bootstrap.IsMasterClient)
        {
            LogThrottled("character ok, waiting master client (isMaster=" + bootstrap.IsMasterClient + ")");
            if (timer > MasterWaitTimeoutSeconds)
            {
                started = true;
                WildlifeTestLog("[AutoForest] ABORT - never became master within " + MasterWaitTimeoutSeconds + "s");
            }
            return;
        }

        started = true;
        WildlifeTestLog("[AutoForest] master confirmed after " + timer.ToString("F1") + "s - loading Environment");
        StartCoroutine(LoadForestWhenIdle(runner));
    }

    private IEnumerator LoadForestWhenIdle(Fusion.NetworkRunner runner)
    {
        // Beri jeda kecil agar state Fusion stabil setelah spawn awal.
        yield return new WaitForSeconds(2f);

        try
        {
            runner.LoadScene(Fusion.SceneRef.FromIndex(2));
            WildlifeTestLog("[AutoForest] HostStartForest equivalent: LoadScene(Environment) called");
        }
        catch (System.Exception exception)
        {
            WildlifeTestLog("[AutoForest] LoadScene FAILED: " + exception.Message);
        }

        // Probe kill sinkron (hanya bila flag -killProbe ada di command line).
        yield return new WaitForSeconds(3f); // beri waktu replikasi hewan ke klien.
        WildlifeKillProbe.TryCreate();
    }

    private void LogThrottled(string stage)
    {
        if (Time.unscaledTime >= nextLogTime)
        {
            nextLogTime = Time.unscaledTime + LogIntervalSeconds;
            WildlifeTestLog("[AutoForest] t=" + timer.ToString("F0") + "s " + stage);
        }
    }
}
