using UnityEngine;

public enum SessionPlayMode
{
    None = 0,
    Solo = 1,
    HostRoom = 2,
    JoinRoom = 3
}

public static class MainMenuSessionState
{
    public struct SessionConfig
    {
        public SessionPlayMode mode;
        public string playerName;
        public string roomName;
        public string roomCode;
        public string roomPassword;
        public bool roomPrivate;
        public int maxPlayers;
        public string hostAddress;
        public ushort hostPort;
        public string lobbySceneName;
    }

    private static SessionConfig active;

    public static SessionConfig Active => active;

    public static bool HasSession => active.mode != SessionPlayMode.None;

    public static void Set(SessionConfig config)
    {
        active = config;
    }

    public static void Clear()
    {
        active = default;
    }
}
