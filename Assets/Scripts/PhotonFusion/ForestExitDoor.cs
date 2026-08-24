using UnityEngine;

/// <summary>
/// Portal/pintu di snowy forest untuk kembali ke scene Gameplay (lobby stage).
/// Mirror dari ForestDoor yang mengarah sebaliknya. Transisi scene tetap
/// hanya boleh dipicu room master (dicek oleh PhotonFusionSceneLoader).
/// </summary>
public class ForestExitDoor : MonoBehaviour
{
    private PhotonFusionSceneLoader sceneLoader;

    public bool TryInteract()
    {
        ResolveSceneLoader();
        if (sceneLoader == null)
        {
            return false;
        }

        sceneLoader.LoadGameplayLobby();
        return true;
    }

    private void ResolveSceneLoader()
    {
        if (sceneLoader == null)
        {
            sceneLoader = FindObjectOfType<PhotonFusionSceneLoader>(true);
        }
    }
}
