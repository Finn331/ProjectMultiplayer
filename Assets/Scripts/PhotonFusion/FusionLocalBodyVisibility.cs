using Fusion;
using UnityEngine;
using UnityEngine.Rendering;

public class FusionLocalBodyVisibility : NetworkBehaviour
{
    [Header("Local Authority")]
    [SerializeField] private bool useStateAuthorityFallback = true;

    [Header("Local First Person Visibility")]
    [SerializeField] private Renderer[] hideForLocalPlayer;
    [SerializeField] private bool disableRenderer = true;
    [SerializeField] private bool forceRenderingOff = true;

    private bool[] originalEnabledStates;
    private bool[] originalForceRenderingOffStates;
    private ShadowCastingMode[] originalShadowCastingModes;
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
            return;
        }

        for (int i = 0; i < hideForLocalPlayer.Length; i++)
        {
            Renderer renderer = hideForLocalPlayer[i];
            if (renderer == null)
            {
                continue;
            }

            if (forceRenderingOff)
            {
                renderer.forceRenderingOff = true;
            }

            if (disableRenderer)
            {
                renderer.enabled = false;
            }
        }

        isLocalHidden = true;
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

        isLocalHidden = false;
    }
}
