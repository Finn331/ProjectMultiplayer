using UnityEditor;
using UnityEngine;

public static class FusionPlayerOwnerSetupSelfTest
{
    [MenuItem("Project Multiplayer/Run Fusion Player Owner Setup Self Test")]
    public static void Run()
    {
        GameObject player = new GameObject("OwnerSetupPlayer");
        GameObject playerCameraObject = new GameObject("PlayerCamera");
        GameObject externalCameraObject = new GameObject("ExternalSceneCamera");

        try
        {
            playerCameraObject.transform.SetParent(player.transform, false);
            Camera playerCamera = playerCameraObject.AddComponent<Camera>();
            AudioListener playerListener = playerCameraObject.AddComponent<AudioListener>();

            Camera externalCamera = externalCameraObject.AddComponent<Camera>();
            AudioListener externalListener = externalCameraObject.AddComponent<AudioListener>();

            FusionPlayerOwnerSetup setup = player.AddComponent<FusionPlayerOwnerSetup>();
            setup.RefreshOwnerOnlyReferencesForDiagnostics();
            setup.ApplyOwnerStateForDiagnostics(true);

            Expect(playerCamera.enabled, "Owner player camera should remain enabled.");
            Expect(playerListener.enabled, "Owner player AudioListener should remain enabled.");
            Expect(!externalCamera.enabled, "External scene Camera should be disabled while owner player camera is active.");
            Expect(!externalListener.enabled, "External scene AudioListener should be disabled while owner player listener is active.");

            setup.ApplyOwnerStateForDiagnostics(false);
            Expect(externalCamera.enabled, "External scene Camera should be restored when owner state is disabled.");
            Expect(externalListener.enabled, "External scene AudioListener should be restored when owner state is disabled.");
        }
        finally
        {
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(externalCameraObject);
        }

        Debug.Log("FusionPlayerOwnerSetupSelfTest passed.");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new System.InvalidOperationException(message);
        }
    }
}
