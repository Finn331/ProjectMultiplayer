using System;
using System.Collections.Generic;

[Serializable]
public class RoomCreateRequest
{
    public string roomName;
    public string roomCode;
    public string password;
    public bool isPrivate;
    public int maxPlayers;
    public string hostAddress;
    public int hostPort;
    public string hostPlayerName;
}

[Serializable]
public class RoomCreateResponse
{
    public bool success;
    public string message;
    public string roomId;
    public string roomName;
    public string roomCode;
    public bool isPrivate;
    public int maxPlayers;
    public int currentPlayers;
    public string hostAddress;
    public int hostPort;
}

[Serializable]
public class RoomJoinRequest
{
    public string roomName;
    public string roomCode;
    public string password;
    public string playerName;
}

[Serializable]
public class RoomJoinResponse
{
    public bool success;
    public string message;
    public string roomId;
    public string roomName;
    public string roomCode;
    public bool isPrivate;
    public int currentPlayers;
    public int maxPlayers;
    public string hostAddress;
    public int hostPort;
}

[Serializable]
public class RoomStageUpdateRequest
{
    public string roomId;
    public string stage;
}

[Serializable]
public class RoomLeaveRequest
{
    public string roomId;
    public string roomCode;
}

[Serializable]
public class RoomHeartbeatRequest
{
    public string roomId;
    public string roomCode;
}

[Serializable]
public class RoomPublicInfo
{
    public string roomId;
    public string roomName;
    public string roomCode;
    public bool isPrivate;
    public int currentPlayers;
    public int maxPlayers;
    public string status;
}

[Serializable]
public class RoomSearchResponse
{
    public bool success;
    public string message;
    public List<RoomPublicInfo> rooms = new List<RoomPublicInfo>();
}
