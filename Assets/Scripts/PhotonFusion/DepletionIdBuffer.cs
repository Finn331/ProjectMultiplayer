using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "DepletionIdBuffer", menuName = "Multiplayer/DepletionIdBuffer")]
public class DepletionIdBuffer : ScriptableObject
{
    [SerializeField] private List<int> ids = new List<int>();
    private readonly HashSet<int> set = new HashSet<int>();

    public int Count => ids.Count;

    public void ResetForTest()
    {
        ids.Clear();
        set.Clear();
    }

    public void Load(IEnumerable<int> values)
    {
        ids.Clear();
        set.Clear();
        if (values == null)
        {
            return;
        }

        foreach (int value in values)
        {
            if (value != 0 && set.Add(value))
            {
                ids.Add(value);
            }
        }
    }

    public bool Contains(int treeId)
    {
        return set.Contains(treeId);
    }

    public bool Add(int treeId)
    {
        if (treeId == 0 || !set.Add(treeId))
        {
            return false;
        }

        ids.Add(treeId);
        return true;
    }

    public int[] ToArray()
    {
        return ids.ToArray();
    }
}
