using System.Collections;
using UnityEngine;

/// <summary>
/// Playtest automation (headless): probe kill sinkron antar pemain.
/// Aktif HANYA dengan flag CLI -killProbe. Dibuat oleh AutoForestDriver
/// setelah forest ter-load. Alur:
///   1. Tunggu hewan networked (AnimalAI) tereplikasi ke klien lokal.
///   2. Pilih target hidup pertama, catat HP-nya, lalu panggil TakeDamage()
///      dari KLIEN LOKAL (bukan authority) — mengetes jalur RPC_RequestHit.
///   3. Poll sampai IsDead=true, log hasil.
/// Semua output ke wildlife_test.log (non-dev build tidak menulis Debug.Log).
/// </summary>
public class WildlifeKillProbe : MonoBehaviour
{
    private const float TimeoutSeconds = 60f;
    private const float DamageAmount = 9999f;

    private float timer;
    private bool fired;

    public static void TryCreate()
    {
        string value = null;
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-killProbe")
            {
                value = args[i + 1];
                break;
            }
        }

        if (string.IsNullOrEmpty(value) || value == "0" || FindObjectOfType<WildlifeKillProbe>() != null)
        {
            return;
        }

        GameObject go = new GameObject("WildlifeKillProbe");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<WildlifeKillProbe>();
    }

    private static void Log(string message)
    {
        try
        {
            string path = Application.persistentDataPath + "/wildlife_test.log";
            System.IO.File.AppendAllText(path,
                System.DateTime.Now.ToString("HH:mm:ss.fff") + " [KillProbe] " + message + "\n");
        }
        catch (System.Exception)
        {
            // best-effort
        }
    }

    private void Update()
    {
        if (fired)
        {
            return;
        }

        timer += Time.unscaledDeltaTime;

        // Probe hanya relevan di scene forest; tunggu replikasi hewan dulu.
        AnimalAI[] animals = FindObjectsOfType<AnimalAI>();
        if (animals.Length == 0)
        {
            if (timer > TimeoutSeconds)
            {
                fired = true;
                Log("ABORT - no replicated animals within " + TimeoutSeconds + "s");
            }
            return;
        }

        AnimalAI target = null;
        foreach (AnimalAI animal in animals)
        {
            if (!animal.IsDead && animal.Object != null && animal.Object.IsValid
                && !animal.Object.HasStateAuthority)
            {
                target = animal;
                break;
            }
        }
        if (target == null)
        {
            if (timer > TimeoutSeconds)
            {
                fired = true;
                Log("ABORT - no non-authority live target");
            }
            return;
        }

        fired = true;
        string species = target.speciesName;
        StartCoroutine(RunKillTest(target, species));
    }

    private IEnumerator RunKillTest(AnimalAI target, string species)
    {
        Log("target=" + species
            + " hpBefore=" + target.Health.ToString("F0")
            + " isAuthority=" + target.Object.HasStateAuthority
            + " localPlayer=" + target.Runner.LocalPlayer.PlayerId);

        // Jalur yang sama dipakai PlayerAxeCombat di klien mana pun:
        // proxy -> RPC_RequestHit -> authority ApplyHitLocally.
        target.TakeDamage(DamageAmount);

        float waited = 0f;
        while (!target.IsDead && waited < TimeoutSeconds)
        {
            yield return new WaitForSeconds(0.5f);
            waited += 0.5f;
        }

        Log(target.IsDead
            ? ("KILL SYNC OK - " + species + " dead after " + waited.ToString("F1") + "s")
            : ("KILL SYNC TIMEOUT - " + species + " still alive after " + waited.ToString("F0") + "s"));
    }
}
