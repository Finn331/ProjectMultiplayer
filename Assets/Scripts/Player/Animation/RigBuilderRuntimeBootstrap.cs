using UnityEngine;
using UnityEngine.Animations.Rigging;

[DefaultExecutionOrder(-1000)]
public class RigBuilderRuntimeBootstrap : MonoBehaviour
{
    [SerializeField] private RigBuilder rigBuilder;
    [SerializeField] private bool rebuildOnEnable = true;
    [SerializeField] private bool rebuildOnStart = true;

    private void Awake()
    {
        if (rigBuilder == null)
        {
            rigBuilder = GetComponent<RigBuilder>();
        }
    }

    private void OnEnable()
    {
        if (rebuildOnEnable)
        {
            this.TryBuild();
        }
    }

    private void Start()
    {
        if (rebuildOnStart)
        {
            this.TryBuild();
        }
    }

    private void TryBuild()
    {
        if (rigBuilder == null)
        {
            return;
        }

        rigBuilder.Clear();
        rigBuilder.Build();
    }
}
