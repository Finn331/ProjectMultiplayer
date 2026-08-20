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

        value = UnwrapCloudSaveValue(value);

        if (value is string s) { int.TryParse(s, out int r); return r; }
        if (value is long l) return (int)l;
        if (value is int i) return i;
        if (value is double d) return (int)d;
        if (value is float f) return (int)f;
        return System.Convert.ToInt32(value);
    }

    public static int ParseIntForTest(object value)
    {
        return ParseInt(value);
    }

    private static object UnwrapCloudSaveValue(object value)
    {
        if (value == null)
        {
            return null;
        }

        // Cloud Save SDK v3 returns Item wrappers whose Value is an IDeserializable.
        // Avoid a hard dependency on the internal assembly by resolving via reflection.
        System.Type type = value.GetType();
        if (type.Name == "Item" && type.Namespace == "Unity.Services.CloudSave.Models")
        {
            var valueProperty = type.GetProperty("Value");
            object inner = valueProperty != null ? valueProperty.GetValue(value, null) : null;
            if (inner == null)
            {
                return null;
            }
            return UnwrapDeserializable(inner);
        }

        return UnwrapDeserializable(value);
    }

    private static object UnwrapDeserializable(object value)
    {
        if (value == null)
        {
            return null;
        }

        System.Type type = value.GetType();
        var getAsString = type.GetMethod("GetAsString", System.Type.EmptyTypes);
        if (getAsString != null && getAsString.DeclaringType.Namespace == "Unity.Services.CloudSave.Internal.Http")
        {
            object stringValue = getAsString.Invoke(value, null);
            return stringValue as string ?? stringValue;
        }

        return value;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}