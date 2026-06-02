using UnityEngine;

[DisallowMultipleComponent]
public class FusionSceneDropMarker : MonoBehaviour
{
    [SerializeField] private int sceneDropId;

    public int SceneDropId => sceneDropId;

    public void Initialize(int id)
    {
        sceneDropId = id;
    }
}
