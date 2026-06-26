using Fusion;
using UnityEngine;
using UnityEngine.Rendering;

public class FusionLocalBodyVisibility : NetworkBehaviour
{
    [Header("Local Authority")]
    [SerializeField] private bool useStateAuthorityFallback = true;

    [Header("Local First Person Visibility")]
    [SerializeField] private Renderer[] hideForLocalPlayer;
    [SerializeField] private Transform[] scaleToHideForLocalPlayer;
    [SerializeField] private bool keepHiddenRenderersCastingShadows = true;

    private bool[] originalEnabledStates;
    private bool[] originalForceRenderingOffStates;
    private ShadowCastingMode[] originalShadowCastingModes;
    private Vector3[] originalLocalScales;
    private bool originalStatesCaptured;
    private bool isLocalHidden;

    private void Awake()
    {
        CaptureOriginalStates();
        ApplyVisibility(IsLocalPlayerInstance());
    }

    private void OnEnable()
    {
        ApplyVisibility(IsLocalPlayerInstance());
    }

    public override void Spawned()
    {
        CaptureOriginalStates();
        ApplyVisibility(IsLocalPlayerInstance());
    }

    public override void FixedUpdateNetwork()
    {
        bool shouldHide = IsLocalPlayerInstance();
        if (shouldHide != isLocalHidden)
        {
            ApplyVisibility(shouldHide);
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        RestoreOriginalStates();
    }

    private void OnDisable()
    {
        RestoreOriginalStates();
    }

    public void ApplyVisibilityForDiagnostics(bool shouldHide)
    {
        CaptureOriginalStates();
        ApplyVisibility(shouldHide);
    }

    public bool IsRendererHiddenForDiagnostics(Renderer renderer)
    {
        if (renderer == null)
        {
            return false;
        }

        return renderer.forceRenderingOff || !renderer.enabled;
    }

    private bool IsLocalPlayerInstance()
    {
        if (Object == null)
        {
            return false;
        }

        return Object.HasInputAuthority || (useStateAuthorityFallback && Object.HasStateAuthority);
    }

    private void ApplyVisibility(bool shouldHide)
    {
        CaptureOriginalStates();

        if (!shouldHide)
        {
            RestoreOriginalStates();
            return;
        }

        if (hideForLocalPlayer == null)
        {
            ApplyTransformVisibility();
            isLocalHidden = true;
            return;
        }

        for (int i = 0; i < hideForLocalPlayer.Length; i++)
        {
            Renderer renderer = hideForLocalPlayer[i];
            if (renderer == null)
            {
                continue;
            }

            if (keepHiddenRenderersCastingShadows)
            {
                renderer.enabled = true;
                renderer.forceRenderingOff = false;
                renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                continue;
            }

            renderer.forceRenderingOff = true;
            renderer.enabled = false;
        }

        ApplyTransformVisibility();

        isLocalHidden = true;
    }

    private void ApplyTransformVisibility()
    {
        if (scaleToHideForLocalPlayer == null)
        {
            return;
        }

        for (int i = 0; i < scaleToHideForLocalPlayer.Length; i++)
        {
            Transform target = scaleToHideForLocalPlayer[i];
            if (target != null)
            {
                target.localScale = Vector3.zero;
            }
        }
    }

    private void CaptureOriginalStates()
    {
        if (originalStatesCaptured)
        {
            return;
        }

        int count = hideForLocalPlayer != null ? hideForLocalPlayer.Length : 0;
        originalEnabledStates = new bool[count];
        originalForceRenderingOffStates = new bool[count];
        originalShadowCastingModes = new ShadowCastingMode[count];
        int transformCount = scaleToHideForLocalPlayer != null ? scaleToHideForLocalPlayer.Length : 0;
        originalLocalScales = new Vector3[transformCount];

        for (int i = 0; i < count; i++)
        {
            Renderer renderer = hideForLocalPlayer[i];
            if (renderer == null)
            {
                originalEnabledStates[i] = true;
                originalForceRenderingOffStates[i] = false;
                originalShadowCastingModes[i] = ShadowCastingMode.On;
                continue;
            }

            originalEnabledStates[i] = renderer.enabled;
            originalForceRenderingOffStates[i] = renderer.forceRenderingOff;
            originalShadowCastingModes[i] = renderer.shadowCastingMode;
        }

        for (int i = 0; i < transformCount; i++)
        {
            Transform target = scaleToHideForLocalPlayer[i];
            originalLocalScales[i] = target != null ? target.localScale : Vector3.one;
        }

        originalStatesCaptured = true;
    }

    private void RestoreOriginalStates()
    {
        if (!originalStatesCaptured || hideForLocalPlayer == null)
        {
            isLocalHidden = false;
            return;
        }

        for (int i = 0; i < hideForLocalPlayer.Length; i++)
        {
            Renderer renderer = hideForLocalPlayer[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.enabled = i < originalEnabledStates.Length ? originalEnabledStates[i] : true;
            renderer.forceRenderingOff = i < originalForceRenderingOffStates.Length && originalForceRenderingOffStates[i];
            renderer.shadowCastingMode = i < originalShadowCastingModes.Length ? originalShadowCastingModes[i] : ShadowCastingMode.On;
        }

        if (scaleToHideForLocalPlayer != null)
        {
            for (int i = 0; i < scaleToHideForLocalPlayer.Length; i++)
            {
                Transform target = scaleToHideForLocalPlayer[i];
                if (target != null)
                {
                    target.localScale = i < originalLocalScales.Length ? originalLocalScales[i] : Vector3.one;
                }
            }
        }

        isLocalHidden = false;
    }
}
