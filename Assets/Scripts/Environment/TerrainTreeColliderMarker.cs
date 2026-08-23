using UnityEngine;

/// <summary>
/// Penanda ID pohon terrain pada collider yang digenerate oleh
/// <see cref="TerrainTreeChoppingRegistry"/>. Dipakai PlayerAxeCombat untuk
/// memetakan hit kapak (fisika) ke pohon registry yang tepat.
/// </summary>
public class TerrainTreeColliderMarker : MonoBehaviour
{
    public int TreeId;
}
