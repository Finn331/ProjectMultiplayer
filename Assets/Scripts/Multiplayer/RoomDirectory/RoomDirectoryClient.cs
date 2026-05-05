using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class RoomDirectoryClient : MonoBehaviour
{
    [SerializeField] private string baseUrl = "http://31.56.56.8:9010";
    [SerializeField] private float requestTimeoutSeconds = 8f;

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
        string endpoint = this.BuildEndpoint(path);
        using UnityWebRequest request = UnityWebRequest.Get(endpoint);
        request.timeout = Mathf.Max(2, Mathf.RoundToInt(requestTimeoutSeconds));

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            callback?.Invoke(null, request.error);
            yield break;
        }

        try
        {
            T result = JsonUtility.FromJson<T>(request.downloadHandler.text);
            callback?.Invoke(result, null);
        }
        catch (Exception exception)
        {
            callback?.Invoke(null, exception.Message);
        }
    }

    private IEnumerator PostJson<TRequest, TResponse>(string path, TRequest body, Action<TResponse, string> callback)
        where TResponse : class
    {
        string endpoint = this.BuildEndpoint(path);
        string json = JsonUtility.ToJson(body);

        using UnityWebRequest request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST);
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = Mathf.Max(2, Mathf.RoundToInt(requestTimeoutSeconds));

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            callback?.Invoke(null, request.error);
            yield break;
        }

        try
        {
            TResponse result = JsonUtility.FromJson<TResponse>(request.downloadHandler.text);
            callback?.Invoke(result, null);
        }
        catch (Exception exception)
        {
            callback?.Invoke(null, exception.Message);
        }
    }

    private string BuildEndpoint(string path)
    {
        string root = string.IsNullOrWhiteSpace(baseUrl) ? "http://31.56.56.8:9010" : baseUrl.Trim();
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
}
