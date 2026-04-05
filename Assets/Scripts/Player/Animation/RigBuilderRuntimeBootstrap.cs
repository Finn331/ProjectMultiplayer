using UnityEngine;
using UnityEngine.Animations.Rigging;

[ExecuteAlways]
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
        if (!Application.isPlaying)
        {
            if (rigBuilder != null)
            {
                rigBuilder.enabled = false;
            }
            return;
        }

        if (rigBuilder != null)
        {
            rigBuilder.enabled = true;
        }

        if (rebuildOnEnable)
        {
            this.TryBuild();
        }
    }

    private void Start()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (rebuildOnStart)
        {
            this.TryBuild();
        }
    }

    private void OnDisable()
    {
        if (!Application.isPlaying && rigBuilder != null)
        {
            rigBuilder.enabled = false;
        }
    }

    private void TryBuild()
    {
        if (!Application.isPlaying || rigBuilder == null)
        {
            return;
        }

        rigBuilder.Clear();
        rigBuilder.Build();
    }
}
