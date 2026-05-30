using System;

public enum PhotonFusionRoomStage
{
    MainMenu,
    Waiting,
    Lobby,
    Forest
}

public static class PhotonFusionSessionState
{
    [Serializable]
    public struct Session
    {
        public string PlayerName;
        public string RoomCode;
        public string RoomName;
        public int MaxPlayers;
        public PhotonFusionRoomStage Stage;
        public bool IsRoomCreator;
    }

    public static bool HasSession { get; private set; }
    public static Session Active { get; private set; }

    public static void Set(Session session)
    {
        Active = session;
        HasSession = true;
    }

    public static void SetStage(PhotonFusionRoomStage stage)
    {
        if (!HasSession)
        {
            return;
        }

        Session session = Active;
        session.Stage = stage;
        Active = session;
    }

    public static void Clear()
    {
        Active = default;
        HasSession = false;
    }
}
