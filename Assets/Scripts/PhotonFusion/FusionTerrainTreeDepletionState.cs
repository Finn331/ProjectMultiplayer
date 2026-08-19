using System.Collections.Generic;
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class FusionTerrainTreeDepletionState : NetworkBehaviour
{
    private const int MaxDepletedTrees = 512;

    [Networked, Capacity(MaxDepletedTrees)]
    private NetworkArray<int> DepletedTreeIds { get; }

    private DepletionIdBuffer buffer;
    private TerrainTreeChoppingRegistry registry;
    private bool hasAppliedInitialState;
    private bool warnedMissingRegistry;

    public override void Spawned()
    {
        ResolveReferences();
        SyncToRegistry();
    }

    public override void Render()
    {
        if (!hasAppliedInitialState)
        {
            hasAppliedInitialState = true;
            SyncToRegistry();
            return;
        }

        if (HasStateAuthority)
        {
            return;
        }

        SyncToRegistry();
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

    private void ResolveReferences()
    {
        if (registry == null)
        {
            registry = FindObjectOfType<TerrainTreeChoppingRegistry>();
        }
    }

    private void SyncToRegistry()
    {
        if (buffer == null)
        {
            buffer = new DepletionIdBuffer();
        }

        buffer.Load(ReadBufferFromNetwork());

        if (registry == null)
        {
            ResolveReferences();
        }

        if (registry == null)
        {
            if (!warnedMissingRegistry)
            {
                warnedMissingRegistry = true;
                Debug.LogWarning("[FusionTerrainTreeDepletionState] TerrainTreeChoppingRegistry not found; skipping depletion sync.");
            }

            return;
        }

        registry.ApplyNetworkedDepletion(buffer.ToArray());
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
