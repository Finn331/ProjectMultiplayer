using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;

public class PlayerStatsPersistence : MonoBehaviour
{
    public static PlayerStatsPersistence Instance { get; private set; }

    [Header("State")]
    [SerializeField] private int totalKills;
    [SerializeField] private int totalDowns;

    private bool servicesInitialized;
    private bool dirty;

    public int TotalKillsForTest => totalKills;
    public int TotalDownsForTest => totalDowns;

    private async void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        await InitializeServicesAsync();
    }

    private async Task InitializeServicesAsync()
    {
        try
        {
            await UnityServices.InitializeAsync();
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            servicesInitialized = true;
            await TryLoadStatsAsync();
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("[PlayerStatsPersistence] UGS unavailable, running in memory-only mode: " + exception.Message);
        }
    }

    public async void RecordKill()
    {
        totalKills++;
        dirty = true;
        await PersistIfReadyAsync();
    }

    public async void RecordDown()
    {
        totalDowns++;
        dirty = true;
        await PersistIfReadyAsync();
    }

    public void ResetForTest()
    {
        totalKills = 0;
        totalDowns = 0;
        dirty = false;
    }

    private async Task PersistIfReadyAsync()
    {
        if (!servicesInitialized)
        {
            return;
        }
        if (!dirty)
        {
            return;
        }

        try
        {
            var data = new Dictionary<string, object>
            {
                { "kill_count", totalKills },
                { "downed_count", totalDowns }
            };
            await CloudSaveService.Instance.Data.Player.SaveAsync(data);
            dirty = false;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("[PlayerStatsPersistence] Cloud save failed (will retry): " + exception.Message);
        }
    }

    private async Task TryLoadStatsAsync()
    {
        try
        {
            var data = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { "kill_count", "downed_count" });
            if (data.TryGetValue("kill_count", out var kills))
            {
                totalKills = ParseInt(kills);
            }
            if (data.TryGetValue("downed_count", out var downs))
            {
                totalDowns = ParseInt(downs);
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("[PlayerStatsPersistence] Cloud load failed, starting at zero: " + exception.Message);
        }
    }

    private static int ParseInt(object value)
    {
        if (value == null) return 0;
        if (value is string s) { int.TryParse(s, out int r); return r; }
        if (value is long l) return (int)l;
        return System.Convert.ToInt32(value);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}