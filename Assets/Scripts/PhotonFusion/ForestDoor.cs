using UnityEngine;

public class ForestDoor : MonoBehaviour
{
    private PhotonFusionSceneLoader sceneLoader;

    public bool TryInteract()
    {
        ResolveSceneLoader();
        if (sceneLoader == null)
        {
            return false;
        }

        sceneLoader.LoadForest();
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
