using UnityEditor;
using UnityEngine;

public static class KillFeedHUDSelfTest
{
    [MenuItem("Project Multiplayer/Run Kill Feed HUD Self Test")]
    public static void Run()
    {
        GameObject go = new GameObject("KillFeedHUDSelfTest");
        try
        {
            var hud = go.AddComponent<KillFeedHUD>();

            string downed = hud.FormatMessageForTest("", "Victim", false);
            string kill = hud.FormatMessageForTest("Killer", "Victim", true);
            string nature = hud.FormatMessageForTest("", "Victim", true);

            bool ok = downed == "Nature downed Victim"
                && kill == "Killer killed Victim"
                && nature == "Nature killed Victim";

            if (!ok)
            {
                throw new System.Exception("KillFeedHUDSelfTest FAILED:\n" + downed + "\n" + kill + "\n" + nature);
            }

            Debug.Log("KillFeedHUDSelfTest passed.");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
