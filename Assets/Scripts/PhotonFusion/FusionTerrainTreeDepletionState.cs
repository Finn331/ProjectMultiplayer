using System.Collections;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class FusionTerrainTreeDepletionState : NetworkBehaviour
{
    private const int MaxDepletedTrees = 512;
    private const float RegistryResolveRetryIntervalSeconds = 0.5f;

    public static FusionTerrainTreeDepletionState Instance { get; private set; }

    [Networked, Capacity(MaxDepletedTrees)]
    private NetworkArray<int> DepletedTreeIds { get; }

    private ChangeDetector changeDetector;
    private DepletionIdBuffer buffer;
    private TerrainTreeChoppingRegistry registry;
    private bool warnedMissingRegistry;

    public override void Spawned()
    {
        Instance = this;
        changeDetector = GetChangeDetector(ChangeDetector.Source.SnapshotFrom);
        if (!TrySyncToRegistry())
        {
            StartCoroutine(ResolveRegistryWhenAvailable());
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public override void Render()
    {
        if (changeDetector == null || HasStateAuthority)
        {
            return;
        }

        foreach (string changedProperty in changeDetector.DetectChanges(this))
        {
            if (changedProperty == nameof(DepletedTreeIds))
            {
                TrySyncToRegistry();
                break;
            }
        }
    }

    public void AddDepletedTree(int treeId)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority || treeId == 0)
        {
            return;
        }

        if (buffer == null)
        {
            buffer = new DepletionIdBuffer();
        }

        if (!buffer.Add(treeId))
        {
            return;
        }

        WriteBufferToNetwork();
    }

    public int[] GetDepletedTreeIds()
    {
        if (buffer == null)
        {
            buffer = new DepletionIdBuffer();
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Test hook: resolves the terrain tree chopping registry without touching
    /// any networked state, so EditMode self-tests can exercise the late-load
    /// recovery path outside a running runner.
    /// </summary>
    public bool TryResolveRegistryForTests()
    {
        return EnsureRegistryResolved();
    }

    private IEnumerator ResolveRegistryWhenAvailable()
    {
        while (!TrySyncToRegistry())
        {
            yield return new WaitForSecondsRealtime(RegistryResolveRetryIntervalSeconds);
        }
    }

    private bool TrySyncToRegistry()
    {
        if (!EnsureRegistryResolved())
        {
            WarnMissingRegistryOnce();
            return false;
        }

        if (buffer == null)
        {
            buffer = new DepletionIdBuffer();
        }

        buffer.Load(ReadBufferFromNetwork());

        int[] depletedIds = buffer.ToArray();
        registry.ApplyNetworkedDepletion(depletedIds);

        if (depletedIds.Length > 0 && !HasStateAuthority)
        {
            Debug.Log("[FusionTerrainTreeDepletionState] Applied " + depletedIds.Length
                + " depleted tree id(s) to registry.");
        }

        return true;
    }

    private bool EnsureRegistryResolved()
    {
        if (registry != null)
        {
            return true;
        }

        registry = FindObjectOfType<TerrainTreeChoppingRegistry>();
        return registry != null;
    }

    private void WarnMissingRegistryOnce()
    {
        if (warnedMissingRegistry)
        {
            return;
        }

        warnedMissingRegistry = true;
        Debug.LogWarning("[FusionTerrainTreeDepletionState] TerrainTreeChoppingRegistry not found yet; "
            + "will keep retrying every " + RegistryResolveRetryIntervalSeconds + "s until the forest scene finishes loading.");
    }

    private int[] ReadBufferFromNetwork()
    {
        List<int> result = new List<int>();
        for (int i = 0; i < DepletedTreeIds.Length; i++)
        {
            int value = DepletedTreeIds[i];
            if (value != DepletionIdBuffer.SentinelId)
            {
                result.Add(value);
            }
        }

        return result.ToArray();
    }

    private void WriteBufferToNetwork()
    {
        int[] values = buffer.ToArray();
        for (int i = 0; i < values.Length && i < MaxDepletedTrees; i++)
        {
            DepletedTreeIds.Set(i, values[i]);
        }

        for (int i = values.Length; i < MaxDepletedTrees; i++)
        {
            DepletedTreeIds.Set(i, DepletionIdBuffer.SentinelId);
        }
    }
}
