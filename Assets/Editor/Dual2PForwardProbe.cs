// Auto-generated 2P movement probe for ProjectMultiplayer
// Run via: Tools > Run 2P Forward Probe
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Demo.Scripts.Runtime.Character;

public static class Dual2PForwardProbe
{
    const string LOG = "C:/Users/carlo/AppData/Local/Temp/p1_forward_result.txt";

    [MenuItem("Tools/Run 2P Forward Probe")]
    public static async void Run()
    {
        var bridges = Object.FindObjectsOfType<FusionFpsSasBridge>(true);
        File.WriteAllText(LOG, "bridges=" + (bridges != null ? bridges.Length.ToString() : "null") + "\n");
        if (bridges == null || bridges.Length == 0) { File.WriteAllText(LOG, "NO_BRIDGE"); return; }

        // find auth bridge (P1 owns state authority)
        FPSMovement p1vel = null;
        GameObject p1go = null;
        foreach (var b in bridges)
        {
            if (b == null) continue;
            var no = b.GetComponent<Fusion.NetworkObject>();
            if (no != null && no.HasStateAuthority && b.Object != null && b.Object.HasStateAuthority)
            {
                p1vel = b.GetComponent<FPSMovement>();
                p1go = b.gameObject;
                break;
            }
        }
        if (p1vel == null) { File.WriteAllText(LOG, "NO_P1VEL\n"); return; }

        var pos0 = p1go.transform.position;
        p1vel.SetInputDirection(new Vector2(0, 1f)); // full forward (SAS Y-axis)
        await Task.Delay(350);
        p1vel.SetInputDirection(Vector2.zero);
        await Task.Delay(60);
        var pos1 = p1go.transform.position;
        float dist = Vector3.Distance(pos0, pos1);
        var sb = new StringBuilder();
        sb.AppendLine("PROBE_DONE");
        sb.AppendLine("P1 forward 0.35s: dist=" + dist.ToString("F3") + "m");
        sb.AppendLine("speed=" + (dist / 0.35f).ToString("F2") + " m/s (walk target=3.0)");
        sb.AppendLine("pos0=" + pos0);
        sb.AppendLine("pos1=" + pos1);
        File.WriteAllText(LOG, sb.ToString());
    }
}