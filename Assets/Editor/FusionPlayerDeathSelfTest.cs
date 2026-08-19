using UnityEditor;
using UnityEngine;

public static class FusionPlayerDeathSelfTest
{
    [MenuItem("Project Multiplayer/Run Fusion Player Death Self Test")]
    public static void Run()
    {
        GameObject go = null;
        try
        {
            string log = "";
            go = new GameObject("FusionPlayerDeathSelfTest_Player");
            var state = go.AddComponent<FusionPlayerDeath>();

            bool canRespawnInitially = state.CanRespawnNowForTest();
            log += "canRespawnDowned=" + canRespawnInitially + "\n";

            state.SetDownedForTest(true);
            bool timerArmed = state.IsRespawnTimerArmedForTest();
            log += "timerArmed=" + timerArmed + "\n";

            state.SetReviveInProgressForTest(true);

            state.SetDownedForTest(false);
            bool timerCancelled = !state.IsRespawnTimerArmedForTest();
            log += "timerCancelled=" + timerCancelled + "\n";

            if (canRespawnInitially || !timerArmed || !timerCancelled)
            {
                throw new System.Exception("FusionPlayerDeathSelfTest assertions failed:\n" + log);
            }

            Debug.Log("FusionPlayerDeathSelfTest passed.\n" + log);
        }
        finally
        {
            if (go != null) Object.DestroyImmediate(go);
        }
    }
}