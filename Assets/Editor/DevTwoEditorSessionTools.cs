using UnityEditor;
using UnityEngine;

public static class DevTwoEditorSessionTools
{
    private const string RoomCodeOverrideKey = "DevAutoSessionRoomCode";

    [MenuItem("Project Multiplayer/Dev/Set Two-Editor Room Code (VERIFY1)")]
    public static void SetRoomCodeVerify1()
    {
        PlayerPrefs.SetString(RoomCodeOverrideKey, "VERIFY1");
        Debug.Log("[DevTwoEditor] Room code override set to VERIFY1. Enter play mode to create/join this session.");
    }

    [MenuItem("Project Multiplayer/Dev/Clear Room Code Override")]
    public static void ClearRoomCodeOverride()
    {
        PlayerPrefs.DeleteKey(RoomCodeOverrideKey);
        Debug.Log("[DevTwoEditor] Room code override cleared.");
    }
}
