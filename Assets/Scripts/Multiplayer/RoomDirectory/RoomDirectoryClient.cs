using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class RoomDirectoryClient : MonoBehaviour
{
    [SerializeField] private string baseUrl = "http://31.56.56.8:9011";
    [SerializeField] private string fallbackBaseUrl = "http://31.56.56.8:9011";
    [SerializeField] private float requestTimeoutSeconds = 8f;
    private const string DefaultBaseUrl = "http://31.56.56.8:9011";

    public string BaseUrl
    {
        get => baseUrl;
        set => baseUrl = string.IsNullOrWhiteSpace(value) ? baseUrl : value.Trim();
    }

    public void CreateRoom(RoomCreateRequest request, Action<RoomCreateResponse, string> callback)
    {
        this.StartCoroutine(this.PostJson("/rooms/create", request, callback));
    }

    public void SearchPublicRooms(string searchName, Action<RoomSearchResponse, string> callback)
    {
        string encoded = UnityWebRequest.EscapeURL(searchName ?? string.Empty);
        this.StartCoroutine(this.GetJson<RoomSearchResponse>($"/rooms/public?search={encoded}", callback));
    }

    public void JoinRoom(RoomJoinRequest request, Action<RoomJoinResponse, string> callback)
    {
        this.StartCoroutine(this.PostJson("/rooms/join", request, callback));
    }

    public void UpdateRoomStage(string roomId, string stage, Action<bool, string> callback)
    {
        RoomStageUpdateRequest payload = new RoomStageUpdateRequest
        {
            roomId = roomId,
            stage = stage
        };

        this.StartCoroutine(this.PostJson("/rooms/stage", payload, (RoomCreateResponse _, string error) =>
        {
            callback?.Invoke(string.IsNullOrEmpty(error), error);
        }));
    }

    private IEnumerator GetJson<T>(string path, Action<T, string> callback) where T : class
    {
        string[] endpoints = this.BuildEndpoints(path);
        string lastError = "No endpoint available.";

        for (int i = 0; i < endpoints.Length; i++)
        {
            string endpoint = endpoints[i];
            using (UnityWebRequest request = UnityWebRequest.Get(endpoint))
            {
                request.timeout = Mathf.Max(2, Mathf.RoundToInt(requestTimeoutSeconds));

                UnityWebRequestAsyncOperation operation;
                string startError;
                if (!TrySendRequest(request, endpoint, out operation, out startError))
                {
                    lastError = startError;
                    continue;
                }

                yield return operation;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    lastError = request.error;
                    continue;
                }

                try
                {
                    T result = JsonUtility.FromJson<T>(request.downloadHandler.text);
                    callback?.Invoke(result, null);
                    yield break;
                }
                catch (Exception exception)
                {
                    lastError = exception.Message;
                }
            }
        }

        callback?.Invoke(null, lastError);
    }

    private IEnumerator PostJson<TRequest, TResponse>(string path, TRequest body, Action<TResponse, string> callback)
        where TResponse : class
    {
        string[] endpoints = this.BuildEndpoints(path);
        string json = JsonUtility.ToJson(body);

        string lastError = "No endpoint available.";
        for (int i = 0; i < endpoints.Length; i++)
        {
            string endpoint = endpoints[i];
            using (UnityWebRequest request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = Mathf.Max(2, Mathf.RoundToInt(requestTimeoutSeconds));

                UnityWebRequestAsyncOperation operation;
                string startError;
                if (!TrySendRequest(request, endpoint, out operation, out startError))
                {
                    lastError = startError;
                    continue;
                }

                yield return operation;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    lastError = request.error;
                    continue;
                }

                try
                {
                    TResponse result = JsonUtility.FromJson<TResponse>(request.downloadHandler.text);
                    callback?.Invoke(result, null);
                    yield break;
                }
                catch (Exception exception)
                {
                    lastError = exception.Message;
                }
            }
        }

        callback?.Invoke(null, lastError);
    }

    private string[] BuildEndpoints(string path)
    {
        string root = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim();
        string fallback = string.IsNullOrWhiteSpace(fallbackBaseUrl) ? string.Empty : fallbackBaseUrl.Trim();
        string primaryEndpoint = this.CombineEndpoint(root, path);

        if (string.IsNullOrWhiteSpace(fallback))
        {
            return new[] { primaryEndpoint };
        }

        string fallbackEndpoint = this.CombineEndpoint(fallback, path);
        if (string.Equals(primaryEndpoint, fallbackEndpoint, StringComparison.OrdinalIgnoreCase))
        {
            return new[] { primaryEndpoint };
        }

        return new[] { primaryEndpoint, fallbackEndpoint };
    }

    private string CombineEndpoint(string root, string path)
    {
        if (root.EndsWith("/"))
        {
            root = root.Substring(0, root.Length - 1);
        }

        if (!path.StartsWith("/"))
        {
            path = "/" + path;
        }

        return root + path;
    }

    private static bool TrySendRequest(
        UnityWebRequest request,
        string endpoint,
        out UnityWebRequestAsyncOperation operation,
        out string error)
    {
        operation = null;
        error = null;

        try
        {
            operation = request.SendWebRequest();
            return true;
        }
        catch (InvalidOperationException exception)
        {
            if (endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                error =
                    "HTTP diblokir oleh Player Settings (Allow downloads over HTTP = Not Allowed). " +
                    "Ubah ke Development Only / Always Allowed atau pakai HTTPS. Detail: " + exception.Message;
            }
            else
            {
                error = exception.Message;
            }

            return false;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }
}
