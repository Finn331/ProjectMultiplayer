# Photon Fusion 2 Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current Unity Netcode for GameObjects multiplayer runtime with Photon Fusion 2 Shared Mode for room flow, player spawning, movement, pickup/drop, inventory, chest, survival, and basic combat sync.

**Architecture:** Add a new Photon Fusion runtime layer beside the existing NGO code, then switch menu/scenes/prefabs to the Fusion path once each subsystem works. Keep local gameplay logic where possible, but remove NGO runtime dependencies from Photon V1.

**Tech Stack:** Unity 2022.3.62f1, Photon Fusion 2, UGUI/TMP, CharacterController, existing survival/inventory/UI scripts, Unity Test Framework where practical, UnityMCP for validation.

---

## File Structure

Create these new files:

- `Assets/Scripts/PhotonFusion/PhotonFusionBootstrap.cs`: Owns Fusion runner lifecycle, create/join/leave room, exposes status events.
- `Assets/Scripts/PhotonFusion/PhotonFusionRoomController.cs`: Menu-facing room flow controller; replaces NGO path in MainMenu for Photon V1.
- `Assets/Scripts/PhotonFusion/PhotonFusionSceneLoader.cs`: Wraps Fusion scene loading and active scene stage state.
- `Assets/Scripts/PhotonFusion/PhotonFusionSessionState.cs`: Local session data for player name, room code, max players, and stage.
- `Assets/Scripts/PhotonFusion/FusionPlayerSpawner.cs`: Spawns/despawns player objects for each Fusion player.
- `Assets/Scripts/PhotonFusion/FusionPlayerOwnerSetup.cs`: Enables local-only camera/input/UI/audio and binds scene controls.
- `Assets/Scripts/PhotonFusion/FusionPlayerMovement.cs`: Fusion-aware owner movement using joystick/look/jump.
- `Assets/Scripts/PhotonFusion/FusionAnimatorSync.cs`: Syncs minimal animation state.
- `Assets/Scripts/PhotonFusion/FusionPlayerInventory.cs`: Fusion wrapper around `PlayerInventory` for pickup/drop/use state.
- `Assets/Scripts/PhotonFusion/FusionPickableItem.cs`: Networked world item metadata and pickup handling.
- `Assets/Scripts/PhotonFusion/FusionWorldItemSpawner.cs`: Spawns initial scene items as Fusion objects.
- `Assets/Scripts/PhotonFusion/FusionStorageChest.cs`: Networked shared chest slots and transactions.
- `Assets/Scripts/PhotonFusion/FusionPlayerSurvival.cs`: Networked survival state wrapper.
- `Assets/Scripts/PhotonFusion/FusionPlayerCombat.cs`: Networked axe swing/combat visual sync.
- `Assets/Scripts/PhotonFusion/FusionTreeChoppable.cs`: Networked tree/choppable state.
- `Assets/Scripts/PhotonFusion/FusionSpawnPoint.cs`: Scene marker for player spawn positions.

Modify these existing files:

- `Packages/manifest.json`: Add Photon Fusion package if installed via UPM/git, or document manual Asset Store/package import if package is not available from a registry.
- `Assets/Scripts/Menu/MainMenuController.cs`: Disable NGO multiplayer actions or route V1 UI to `PhotonFusionRoomController`.
- `Assets/Scripts/Player/Movement/FPSController.cs`: Avoid direct NGO authority checks in Photon path, or leave it unused and move authority into `FusionPlayerMovement`.
- `Assets/Scripts/Player/Survival/PlayerInteractionSystem.cs`: Add optional Fusion pickup routing or leave detection-only and call Fusion wrapper.
- `Assets/Scenes/MainMenu.unity`: Add Photon bootstrap/room controller references and disable NGO bootstrap for Photon V1.
- `Assets/Scenes/Gameplay.unity`: Remove active scene player prototype, add spawn points and Fusion world item spawner.
- `Assets/Scenes/Environment.unity`: Add spawn points and Fusion world item/chest/tree setup.
- `Assets/Assets/Prefabs/NetworkPlayer.prefab`: Duplicate into `Assets/Assets/Prefabs/FusionPlayer.prefab` and replace NGO components with Fusion components.
- `Assets/Assets/Prefabs/Wood.prefab`, `Assets/Assets/Prefabs/Stone.prefab`, and item prefabs: Duplicate or convert into Fusion spawnable item prefabs.

---

### Task 1: Install And Verify Photon Fusion 2

**Files:**
- Modify: `Packages/manifest.json` only if Photon Fusion is installed through UPM/git.
- Create: no code yet.

- [ ] **Step 1: Check package availability**

Run in Unity Package Manager or use the existing imported package if Photon Fusion 2 is installed from Asset Store. Confirm these namespaces compile in a scratch script later: `Fusion`, `Fusion.Sockets`.

- [ ] **Step 2: Add Photon Fusion 2 package**

If using Asset Store package, import Photon Fusion 2 into the project using Unity Package Manager/My Assets. If using UPM registry or downloaded package, add it via Unity Package Manager and let Unity update `Packages/manifest.json`.

- [ ] **Step 3: Configure Photon AppId**

Open Photon Fusion settings in Unity and set the Fusion AppId from the Photon dashboard. Use a development AppId for this project.

- [ ] **Step 4: Compile and verify**

Run UnityMCP:

```text
read_console(types=["error"], count=20)
```

Expected: no compile errors from missing Photon/Fusion namespaces.

- [ ] **Step 5: Commit**

```powershell
git status
git add Packages/manifest.json Packages/packages-lock.json ProjectSettings Assets/Photon Assets/PhotonAppSettings.asset
git commit -m "chore: add Photon Fusion 2"
```

Only stage files that actually changed. Do not stage unrelated scene/code changes.

---

### Task 2: Create Photon Session State And Bootstrap

**Files:**
- Create: `Assets/Scripts/PhotonFusion/PhotonFusionSessionState.cs`
- Create: `Assets/Scripts/PhotonFusion/PhotonFusionBootstrap.cs`

- [ ] **Step 1: Create session state**

Create `Assets/Scripts/PhotonFusion/PhotonFusionSessionState.cs`:

```csharp
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
```

- [ ] **Step 2: Create bootstrap skeleton**

Create `Assets/Scripts/PhotonFusion/PhotonFusionBootstrap.cs`:

```csharp
using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PhotonFusionBootstrap : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField] private NetworkRunner runnerPrefab;
    [SerializeField] private string gameplaySceneName = "Gameplay";
    [SerializeField] private string forestSceneName = "Environment";

    private NetworkRunner runner;

    public event Action<string> StatusChanged;
    public event Action<NetworkRunner> RunnerStarted;
    public event Action RunnerStopped;

    public NetworkRunner Runner => runner;
    public bool IsRunning => runner != null && runner.IsRunning;
    public bool IsMasterClient => runner != null && runner.IsSharedModeMasterClient;
    public string GameplaySceneName => gameplaySceneName;
    public string ForestSceneName => forestSceneName;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    public async void CreateRoom(string roomCode, string playerName, int maxPlayers)
    {
        PhotonFusionSessionState.Set(new PhotonFusionSessionState.Session
        {
            PlayerName = string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim(),
            RoomCode = NormalizeRoomCode(roomCode),
            RoomName = NormalizeRoomCode(roomCode),
            MaxPlayers = Mathf.Clamp(maxPlayers, 1, 8),
            Stage = PhotonFusionRoomStage.Waiting,
            IsRoomCreator = true
        });

        await StartSharedRunner(PhotonFusionSessionState.Active.RoomCode);
    }

    public async void JoinRoom(string roomCode, string playerName)
    {
        PhotonFusionSessionState.Set(new PhotonFusionSessionState.Session
        {
            PlayerName = string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim(),
            RoomCode = NormalizeRoomCode(roomCode),
            RoomName = NormalizeRoomCode(roomCode),
            MaxPlayers = 8,
            Stage = PhotonFusionRoomStage.Waiting,
            IsRoomCreator = false
        });

        await StartSharedRunner(PhotonFusionSessionState.Active.RoomCode);
    }

    public void LeaveRoom()
    {
        if (runner != null)
        {
            runner.Shutdown();
        }

        PhotonFusionSessionState.Clear();
        SetStatus("Disconnected from Photon room.");
    }

    private async System.Threading.Tasks.Task StartSharedRunner(string sessionName)
    {
        if (runner != null && runner.IsRunning)
        {
            await runner.Shutdown();
        }

        runner = Instantiate(runnerPrefab != null ? runnerPrefab : new GameObject("PhotonFusionRunner").AddComponent<NetworkRunner>());
        runner.name = "PhotonFusionRunner";
        runner.ProvideInput = true;
        runner.AddCallbacks(this);
        DontDestroyOnLoad(runner.gameObject);

        StartGameResult result = await runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = sessionName,
            PlayerCount = PhotonFusionSessionState.Active.MaxPlayers,
            SceneManager = runner.GetComponent<NetworkSceneManagerDefault>() ?? runner.gameObject.AddComponent<NetworkSceneManagerDefault>()
        });

        if (!result.Ok)
        {
            SetStatus("Photon start failed: " + result.ShutdownReason);
            return;
        }

        SetStatus("Photon room ready: " + sessionName);
        RunnerStarted?.Invoke(runner);
    }

    private static string NormalizeRoomCode(string roomCode)
    {
        return string.IsNullOrWhiteSpace(roomCode) ? "ROOM01" : roomCode.Trim().ToUpperInvariant();
    }

    private void SetStatus(string message)
    {
        StatusChanged?.Invoke(message);
        Debug.Log(message);
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        SetStatus("Photon shutdown: " + shutdownReason);
        RunnerStopped?.Invoke();
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { request.Accept(); }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { SetStatus("Photon connect failed: " + reason); }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}
```

- [ ] **Step 3: Validate scripts**

Run UnityMCP validation on both files.

Expected: no compile errors. If Fusion callback signatures differ for the installed Fusion version, fix signatures to match the installed API.

- [ ] **Step 4: Commit**

```powershell
git add Assets/Scripts/PhotonFusion/PhotonFusionSessionState.cs Assets/Scripts/PhotonFusion/PhotonFusionBootstrap.cs
git commit -m "feat: add Photon Fusion bootstrap"
```

---

### Task 3: Add Photon Room Menu Controller

**Files:**
- Create: `Assets/Scripts/PhotonFusion/PhotonFusionRoomController.cs`
- Modify: `Assets/Scenes/MainMenu.unity`

- [ ] **Step 1: Create room controller**

Create `Assets/Scripts/PhotonFusion/PhotonFusionRoomController.cs`:

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PhotonFusionRoomController : MonoBehaviour
{
    [SerializeField] private PhotonFusionBootstrap bootstrap;
    [SerializeField] private TMP_InputField playerNameInput;
    [SerializeField] private TMP_InputField roomCodeInput;
    [SerializeField] private Slider maxPlayersSlider;
    [SerializeField] private Button createRoomButton;
    [SerializeField] private Button joinRoomButton;
    [SerializeField] private Button leaveRoomButton;
    [SerializeField] private Button startLobbyButton;
    [SerializeField] private Button startForestButton;
    [SerializeField] private TMP_Text statusText;

    private void Awake()
    {
        if (bootstrap == null)
        {
            bootstrap = FindObjectOfType<PhotonFusionBootstrap>(true);
        }

        Bind(createRoomButton, CreateRoom);
        Bind(joinRoomButton, JoinRoom);
        Bind(leaveRoomButton, LeaveRoom);
        Bind(startLobbyButton, StartLobby);
        Bind(startForestButton, StartForest);
        RefreshButtons();
    }

    private void OnEnable()
    {
        if (bootstrap != null)
        {
            bootstrap.StatusChanged -= SetStatus;
            bootstrap.StatusChanged += SetStatus;
        }
    }

    private void OnDisable()
    {
        if (bootstrap != null)
        {
            bootstrap.StatusChanged -= SetStatus;
        }
    }

    private void Update()
    {
        RefreshButtons();
    }

    private void CreateRoom()
    {
        bootstrap.CreateRoom(ReadRoomCode(), ReadPlayerName(), ReadMaxPlayers());
        SetStatus("Creating Photon room...");
    }

    private void JoinRoom()
    {
        bootstrap.JoinRoom(ReadRoomCode(), ReadPlayerName());
        SetStatus("Joining Photon room...");
    }

    private void LeaveRoom()
    {
        bootstrap.LeaveRoom();
        SetStatus("Left Photon room.");
    }

    private void StartLobby()
    {
        PhotonFusionSceneLoader loader = FindObjectOfType<PhotonFusionSceneLoader>(true);
        if (loader == null)
        {
            SetStatus("Fusion scene loader not found.");
            return;
        }

        loader.LoadGameplayLobby();
    }

    private void StartForest()
    {
        PhotonFusionSceneLoader loader = FindObjectOfType<PhotonFusionSceneLoader>(true);
        if (loader == null)
        {
            SetStatus("Fusion scene loader not found.");
            return;
        }

        loader.LoadForest();
    }

    private void RefreshButtons()
    {
        bool running = bootstrap != null && bootstrap.IsRunning;
        bool canStart = running && bootstrap.IsMasterClient;
        if (createRoomButton != null) createRoomButton.interactable = !running;
        if (joinRoomButton != null) joinRoomButton.interactable = !running;
        if (leaveRoomButton != null) leaveRoomButton.interactable = running;
        if (startLobbyButton != null) startLobbyButton.interactable = canStart;
        if (startForestButton != null) startForestButton.interactable = canStart;
    }

    private string ReadPlayerName()
    {
        return playerNameInput != null && !string.IsNullOrWhiteSpace(playerNameInput.text) ? playerNameInput.text.Trim() : "Player";
    }

    private string ReadRoomCode()
    {
        return roomCodeInput != null && !string.IsNullOrWhiteSpace(roomCodeInput.text) ? roomCodeInput.text.Trim().ToUpperInvariant() : "ROOM01";
    }

    private int ReadMaxPlayers()
    {
        return maxPlayersSlider != null ? Mathf.Clamp(Mathf.RoundToInt(maxPlayersSlider.value), 1, 8) : 8;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private static void Bind(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }
}
```

- [ ] **Step 2: Validate script**

Run UnityMCP validate on `PhotonFusionRoomController.cs`.

Expected: no errors.

- [ ] **Step 3: Add MainMenu scene objects**

In `Assets/Scenes/MainMenu.unity` using Unity Editor or UnityMCP:

- Add an empty GameObject named `PhotonFusionBootstrap`.
- Add `PhotonFusionBootstrap` component.
- Add `NetworkRunner` and `NetworkSceneManagerDefault` components if needed by installed Fusion API.
- Add an empty GameObject named `PhotonFusionRoomController`.
- Add `PhotonFusionRoomController` component.
- Wire existing input fields/buttons/texts to the new controller.
- Disable old `CoopNetworkBootstrap` object or old multiplayer button bindings for Photon V1.

- [ ] **Step 4: Manual verify room create does not load Gameplay**

Run Editor Play from MainMenu:

1. Click Create Room.
2. Verify status says Photon room ready or meaningful Photon error.
3. Verify active scene remains `MainMenu`.
4. Verify Start Lobby button becomes interactable only for creator/master.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Scripts/PhotonFusion/PhotonFusionRoomController.cs Assets/Scenes/MainMenu.unity
git commit -m "feat: add Photon room menu flow"
```

---

### Task 4: Add Fusion Scene Loader

**Files:**
- Create: `Assets/Scripts/PhotonFusion/PhotonFusionSceneLoader.cs`
- Modify: `Assets/Scenes/MainMenu.unity`

- [ ] **Step 1: Create scene loader**

Create `Assets/Scripts/PhotonFusion/PhotonFusionSceneLoader.cs`:

```csharp
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PhotonFusionSceneLoader : MonoBehaviour
{
    [SerializeField] private PhotonFusionBootstrap bootstrap;
    [SerializeField] private string gameplaySceneName = "Gameplay";
    [SerializeField] private string forestSceneName = "Environment";

    private void Awake()
    {
        if (bootstrap == null)
        {
            bootstrap = FindObjectOfType<PhotonFusionBootstrap>(true);
        }
    }

    public void LoadGameplayLobby()
    {
        LoadNetworkScene(gameplaySceneName, PhotonFusionRoomStage.Lobby);
    }

    public void LoadForest()
    {
        LoadNetworkScene(forestSceneName, PhotonFusionRoomStage.Forest);
    }

    private void LoadNetworkScene(string sceneName, PhotonFusionRoomStage stage)
    {
        if (bootstrap == null || bootstrap.Runner == null || !bootstrap.Runner.IsRunning)
        {
            Debug.LogWarning("Cannot load Fusion scene because runner is not running.");
            return;
        }

        if (!bootstrap.IsMasterClient)
        {
            Debug.LogWarning("Only room master can start scene transitions.");
            return;
        }

        int buildIndex = SceneUtility.GetBuildIndexByScenePath(sceneName);
        if (buildIndex < 0)
        {
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == sceneName)
                {
                    buildIndex = i;
                    break;
                }
            }
        }

        if (buildIndex < 0)
        {
            Debug.LogWarning("Scene is not in Build Settings: " + sceneName);
            return;
        }

        PhotonFusionSessionState.SetStage(stage);
        bootstrap.Runner.LoadScene(SceneRef.FromIndex(buildIndex), LoadSceneMode.Single);
    }
}
```

- [ ] **Step 2: Validate script**

Run UnityMCP validate.

Expected: no errors. If Fusion 2 API uses a different scene loading signature, update `Runner.LoadScene` call to match installed docs.

- [ ] **Step 3: Attach loader in MainMenu**

Add `PhotonFusionSceneLoader` to the `PhotonFusionBootstrap` object or a new `PhotonFusionSceneLoader` GameObject. Wire it to `PhotonFusionRoomController` implicitly through `FindObjectOfType` or direct serialized reference.

- [ ] **Step 4: Manual verify scene transition**

Run two Editor/player instances if possible:

1. Create Photon room in instance A.
2. Join same room in instance B.
3. Click Start Lobby in A.
4. Verify both clients load `Gameplay`.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Scripts/PhotonFusion/PhotonFusionSceneLoader.cs Assets/Scenes/MainMenu.unity
git commit -m "feat: add Photon scene loading"
```

---

### Task 5: Create Fusion Player Spawning

**Files:**
- Create: `Assets/Scripts/PhotonFusion/FusionSpawnPoint.cs`
- Create: `Assets/Scripts/PhotonFusion/FusionPlayerSpawner.cs`
- Modify: `Assets/Scenes/Gameplay.unity`
- Modify: `Assets/Scenes/Environment.unity`
- Create/Modify prefab: `Assets/Assets/Prefabs/FusionPlayer.prefab`

- [ ] **Step 1: Create spawn point script**

Create `Assets/Scripts/PhotonFusion/FusionSpawnPoint.cs`:

```csharp
using UnityEngine;

public class FusionSpawnPoint : MonoBehaviour
{
    [SerializeField] private int index;
    public int Index => index;
}
```

- [ ] **Step 2: Create player spawner**

Create `Assets/Scripts/PhotonFusion/FusionPlayerSpawner.cs`:

```csharp
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class FusionPlayerSpawner : MonoBehaviour, IPlayerJoined, IPlayerLeft
{
    [SerializeField] private NetworkPrefabRef playerPrefab;

    private readonly Dictionary<PlayerRef, NetworkObject> spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();

    public void PlayerJoined(PlayerRef player)
    {
        NetworkRunner runner = FindObjectOfType<NetworkRunner>();
        if (runner == null || !runner.IsRunning || !runner.IsSharedModeMasterClient)
        {
            return;
        }

        Vector3 position = GetSpawnPosition(player);
        Quaternion rotation = Quaternion.identity;
        NetworkObject playerObject = runner.Spawn(playerPrefab, position, rotation, player);
        spawnedPlayers[player] = playerObject;
    }

    public void PlayerLeft(PlayerRef player)
    {
        NetworkRunner runner = FindObjectOfType<NetworkRunner>();
        if (runner == null)
        {
            return;
        }

        if (spawnedPlayers.TryGetValue(player, out NetworkObject playerObject) && playerObject != null)
        {
            runner.Despawn(playerObject);
        }

        spawnedPlayers.Remove(player);
    }

    private static Vector3 GetSpawnPosition(PlayerRef player)
    {
        FusionSpawnPoint[] points = FindObjectsOfType<FusionSpawnPoint>(true);
        if (points == null || points.Length == 0)
        {
            return new Vector3(0f, 1.2f, -8f);
        }

        int index = Mathf.Abs(player.PlayerId) % points.Length;
        return points[index].transform.position;
    }
}
```

- [ ] **Step 3: Create FusionPlayer prefab**

Duplicate `Assets/Assets/Prefabs/NetworkPlayer.prefab` to `Assets/Assets/Prefabs/FusionPlayer.prefab`.

On `FusionPlayer.prefab`:

- Remove NGO `NetworkObject`.
- Remove `OwnerDrivenNetworkTransform`.
- Remove `NetworkPlayerOwnerSetup`.
- Remove `NetworkInventoryBridge`.
- Remove `NetworkSurvivalBridge`.
- Remove `NetworkAnimatorStateSync`.
- Add Fusion `NetworkObject`.
- Add Fusion `NetworkTransform` if using built-in transform sync.

- [ ] **Step 4: Add spawner and spawn points to gameplay scenes**

In `Gameplay` and `Environment`:

- Add GameObject `FusionPlayerSpawner` with `FusionPlayerSpawner` component.
- Assign `FusionPlayer.prefab` as `playerPrefab`.
- Add at least four spawn point GameObjects named `SpawnPoint_0` to `SpawnPoint_3` with `FusionSpawnPoint` component.
- Disable or remove the scene `Player` prototype as active gameplay player.

- [ ] **Step 5: Manual verify player spawn**

Run create/join/start lobby. Expected:

- Each client gets one player object.
- Players spawn at different spawn points.
- No active scene prototype player controls input.

- [ ] **Step 6: Commit**

```powershell
git add Assets/Scripts/PhotonFusion/FusionSpawnPoint.cs Assets/Scripts/PhotonFusion/FusionPlayerSpawner.cs Assets/Assets/Prefabs/FusionPlayer.prefab Assets/Scenes/Gameplay.unity Assets/Scenes/Environment.unity
git commit -m "feat: spawn Photon Fusion players"
```

---

### Task 6: Port Owner Setup And Movement

**Files:**
- Create: `Assets/Scripts/PhotonFusion/FusionPlayerOwnerSetup.cs`
- Create: `Assets/Scripts/PhotonFusion/FusionPlayerMovement.cs`
- Modify: `Assets/Assets/Prefabs/FusionPlayer.prefab`

- [ ] **Step 1: Create owner setup**

Create `Assets/Scripts/PhotonFusion/FusionPlayerOwnerSetup.cs`:

```csharp
using Fusion;
using UnityEngine;

public class FusionPlayerOwnerSetup : NetworkBehaviour
{
    [SerializeField] private Behaviour[] ownerOnlyBehaviours;
    [SerializeField] private Camera[] ownerOnlyCameras;
    [SerializeField] private AudioListener[] ownerOnlyAudioListeners;

    public override void Spawned()
    {
        ApplyOwnerState(Object.HasInputAuthority);
    }

    private void ApplyOwnerState(bool isOwner)
    {
        if (ownerOnlyBehaviours != null)
        {
            for (int i = 0; i < ownerOnlyBehaviours.Length; i++)
            {
                if (ownerOnlyBehaviours[i] != null)
                {
                    ownerOnlyBehaviours[i].enabled = isOwner;
                }
            }
        }

        if (ownerOnlyCameras != null)
        {
            for (int i = 0; i < ownerOnlyCameras.Length; i++)
            {
                if (ownerOnlyCameras[i] != null)
                {
                    ownerOnlyCameras[i].enabled = isOwner;
                }
            }
        }

        if (ownerOnlyAudioListeners != null)
        {
            for (int i = 0; i < ownerOnlyAudioListeners.Length; i++)
            {
                if (ownerOnlyAudioListeners[i] != null)
                {
                    ownerOnlyAudioListeners[i].enabled = isOwner;
                }
            }
        }
    }
}
```

- [ ] **Step 2: Create movement script**

Create `Assets/Scripts/PhotonFusion/FusionPlayerMovement.cs`:

```csharp
using Fusion;
using UnityEngine;

public class FusionPlayerMovement : NetworkBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private CharacterController controller;
    [SerializeField] private Transform cameraHolder;
    [SerializeField] private float lookSensitivity = 0.2f;
    [SerializeField] private float maxLookAngle = 80f;

    private FloatingJoystick moveJoystick;
    private LookArea lookArea;
    private float verticalVelocity;
    private float xRotation;

    public override void Spawned()
    {
        if (controller == null)
        {
            controller = GetComponent<CharacterController>();
        }

        if (Object.HasInputAuthority)
        {
            RefreshSceneBindings();
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasInputAuthority || controller == null)
        {
            return;
        }

        if (moveJoystick == null || lookArea == null)
        {
            RefreshSceneBindings();
        }

        Move();
        Look();
        ApplyGravity();
    }

    private void RefreshSceneBindings()
    {
        moveJoystick = FindObjectOfType<FloatingJoystick>(true);
        lookArea = FindObjectOfType<LookArea>(true);
    }

    private void Move()
    {
        if (moveJoystick == null)
        {
            return;
        }

        Vector2 input = new Vector2(moveJoystick.Horizontal, moveJoystick.Vertical);
        if (input.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Vector3 direction = transform.right * input.x + transform.forward * input.y;
        controller.Move(direction.normalized * (moveSpeed * Mathf.Clamp01(input.magnitude) * Runner.DeltaTime));
    }

    private void Look()
    {
        if (lookArea == null || cameraHolder == null)
        {
            return;
        }

        Vector2 delta = lookArea.LookDelta;
        if (delta.sqrMagnitude < 0.01f)
        {
            return;
        }

        float lookX = delta.x * lookSensitivity;
        float lookY = delta.y * lookSensitivity;
        xRotation = Mathf.Clamp(xRotation - lookY, -maxLookAngle, maxLookAngle);
        cameraHolder.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * lookX);
        lookArea.ResetDelta();
    }

    private void ApplyGravity()
    {
        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        verticalVelocity += gravity * Runner.DeltaTime;
        controller.Move(Vector3.up * verticalVelocity * Runner.DeltaTime);
    }
}
```

- [ ] **Step 3: Update FusionPlayer prefab**

On `FusionPlayer.prefab`:

- Add `FusionPlayerOwnerSetup`.
- Add `FusionPlayerMovement`.
- Disable or remove `FPSControllerMobile` from Photon path if both movement scripts conflict.
- Assign owner-only behaviours: movement, interaction, inventory UI, combat input where applicable.
- Assign local camera and audio listener arrays.

- [ ] **Step 4: Manual verify movement**

Run two clients:

- Local joystick moves only local player.
- Remote player is visible and does not respond to local joystick.
- No camera/audio duplication warnings.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Scripts/PhotonFusion/FusionPlayerOwnerSetup.cs Assets/Scripts/PhotonFusion/FusionPlayerMovement.cs Assets/Assets/Prefabs/FusionPlayer.prefab
git commit -m "feat: add Fusion player movement"
```

---

### Task 7: Implement Fusion Pickup And Drop

**Files:**
- Create: `Assets/Scripts/PhotonFusion/FusionPickableItem.cs`
- Create: `Assets/Scripts/PhotonFusion/FusionPlayerInventory.cs`
- Modify: `Assets/Assets/Prefabs/FusionPlayer.prefab`
- Create/Modify item prefabs under `Assets/Assets/Prefabs/`

- [ ] **Step 1: Create Fusion pickable item**

Create `Assets/Scripts/PhotonFusion/FusionPickableItem.cs`:

```csharp
using Fusion;
using UnityEngine;

public class FusionPickableItem : NetworkBehaviour
{
    [Networked] public int ItemTypeValue { get; set; }
    [Networked] public int Amount { get; set; }

    [SerializeField] private ItemType defaultItemType;
    [SerializeField] private int defaultAmount = 1;

    public ItemType ItemType => (ItemType)ItemTypeValue;

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            ItemTypeValue = (int)defaultItemType;
            Amount = Mathf.Max(1, defaultAmount);
        }
    }

    public bool CanPickup(Transform player, float maxDistance)
    {
        return player != null && Vector3.Distance(transform.position, player.position) <= maxDistance;
    }
}
```

- [ ] **Step 2: Create Fusion player inventory**

Create `Assets/Scripts/PhotonFusion/FusionPlayerInventory.cs`:

```csharp
using Fusion;
using UnityEngine;

public class FusionPlayerInventory : NetworkBehaviour
{
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private float pickupDistance = 4f;

    public override void Spawned()
    {
        if (inventory == null)
        {
            inventory = GetComponent<PlayerInventory>();
        }
    }

    public void RequestPickup(FusionPickableItem item)
    {
        if (!Object.HasInputAuthority || item == null)
        {
            return;
        }

        RPC_RequestPickup(item.Object);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestPickup(NetworkObject itemObject)
    {
        if (inventory == null || itemObject == null)
        {
            return;
        }

        FusionPickableItem item = itemObject.GetComponent<FusionPickableItem>();
        if (item == null || !item.CanPickup(transform, pickupDistance))
        {
            return;
        }

        int accepted = inventory.AddItem(item.ItemType, item.Amount);
        if (accepted <= 0)
        {
            return;
        }

        if (accepted >= item.Amount)
        {
            Runner.Despawn(itemObject);
        }
        else
        {
            item.Amount -= accepted;
        }
    }
}
```

- [ ] **Step 3: Wire pickup from interaction**

Update player interaction path for Photon V1 so pressing pick calls `FusionPlayerInventory.RequestPickup` when the target has `FusionPickableItem`. Prefer a small adapter if editing `PlayerInteractionSystem` directly creates NGO conflicts.

- [ ] **Step 4: Convert item prefabs**

For each Fusion item prefab:

- Add Fusion `NetworkObject`.
- Add `FusionPickableItem`.
- Assign default item type and amount.
- Keep collider/interactable layer so raycast detection works.

- [ ] **Step 5: Manual verify pickup**

Two-client test:

- Spawn one Fusion item.
- Client A picks it up.
- Item disappears on both clients.
- Client A inventory increases.
- Client B inventory does not change.

- [ ] **Step 6: Commit**

```powershell
git add Assets/Scripts/PhotonFusion/FusionPickableItem.cs Assets/Scripts/PhotonFusion/FusionPlayerInventory.cs Assets/Assets/Prefabs/FusionPlayer.prefab Assets/Assets/Prefabs
git commit -m "feat: add Fusion pickup and inventory"
```

---

### Task 8: Implement Shared Chest Sync

**Files:**
- Create: `Assets/Scripts/PhotonFusion/FusionStorageChest.cs`
- Modify: chest prefab or scene chest objects.

- [ ] **Step 1: Create chest state script**

Create `Assets/Scripts/PhotonFusion/FusionStorageChest.cs`:

```csharp
using Fusion;
using UnityEngine;

public class FusionStorageChest : NetworkBehaviour
{
    private const int SlotCount = 12;

    [Networked, Capacity(SlotCount)] private NetworkArray<int> ItemTypes => default;
    [Networked, Capacity(SlotCount)] private NetworkArray<int> Amounts => default;

    public bool TryReadSlot(int slot, out ItemType itemType, out int amount)
    {
        itemType = default;
        amount = 0;
        if (slot < 0 || slot >= SlotCount || Amounts[slot] <= 0)
        {
            return false;
        }

        itemType = (ItemType)ItemTypes[slot];
        amount = Amounts[slot];
        return true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_TakeFromChest(NetworkObject playerObject, int chestSlot, int preferredPlayerSlot)
    {
        if (playerObject == null || chestSlot < 0 || chestSlot >= SlotCount || Amounts[chestSlot] <= 0)
        {
            return;
        }

        FusionPlayerInventory fusionInventory = playerObject.GetComponent<FusionPlayerInventory>();
        PlayerInventory playerInventory = playerObject.GetComponent<PlayerInventory>();
        if (fusionInventory == null || playerInventory == null)
        {
            return;
        }

        ItemType itemType = (ItemType)ItemTypes[chestSlot];
        int accepted = playerInventory.AddItemToSlot(itemType, Amounts[chestSlot], preferredPlayerSlot);
        if (accepted <= 0)
        {
            return;
        }

        Amounts.Set(chestSlot, Amounts[chestSlot] - accepted);
        if (Amounts[chestSlot] <= 0)
        {
            ItemTypes.Set(chestSlot, 0);
            Amounts.Set(chestSlot, 0);
        }
    }
}
```

- [ ] **Step 2: Wire chest UI actions**

Update chest UI path so slot transfer calls `FusionStorageChest.RPC_TakeFromChest` for Photon V1.

- [ ] **Step 3: Manual verify no duplicate chest loot**

Two-client test:

- Both clients open the same chest.
- Both try to take the same item.
- Only one transfer succeeds.
- Chest state updates for both clients.

- [ ] **Step 4: Commit**

```powershell
git add Assets/Scripts/PhotonFusion/FusionStorageChest.cs Assets/Scenes/Gameplay.unity Assets/Scenes/Environment.unity
git commit -m "feat: add Fusion shared chests"
```

---

### Task 9: Implement Survival And Combat Sync

**Files:**
- Create: `Assets/Scripts/PhotonFusion/FusionPlayerSurvival.cs`
- Create: `Assets/Scripts/PhotonFusion/FusionPlayerCombat.cs`
- Create: `Assets/Scripts/PhotonFusion/FusionAnimatorSync.cs`
- Create: `Assets/Scripts/PhotonFusion/FusionTreeChoppable.cs`
- Modify: `Assets/Assets/Prefabs/FusionPlayer.prefab`

- [ ] **Step 1: Create survival wrapper**

Create `Assets/Scripts/PhotonFusion/FusionPlayerSurvival.cs`:

```csharp
using Fusion;
using UnityEngine;

public class FusionPlayerSurvival : NetworkBehaviour
{
    [Networked] public float Health { get; set; }
    [Networked] public float Hunger { get; set; }
    [Networked] public float Thirst { get; set; }
    [Networked] public NetworkBool Injured { get; set; }

    [SerializeField] private PlayerSurvivalSystem survivalSystem;

    public override void Spawned()
    {
        if (survivalSystem == null)
        {
            survivalSystem = GetComponent<PlayerSurvivalSystem>();
        }

        if (Object.HasStateAuthority)
        {
            Health = 100f;
            Hunger = 100f;
            Thirst = 100f;
            Injured = false;
        }
    }
}
```

- [ ] **Step 2: Create combat wrapper**

Create `Assets/Scripts/PhotonFusion/FusionPlayerCombat.cs`:

```csharp
using Fusion;
using UnityEngine;

public class FusionPlayerCombat : NetworkBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private string swingTrigger = "Swing";

    public void RequestSwing()
    {
        if (!Object.HasInputAuthority)
        {
            return;
        }

        RPC_Swing();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
    private void RPC_Swing()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (animator != null && !string.IsNullOrWhiteSpace(swingTrigger))
        {
            animator.SetTrigger(swingTrigger);
        }
    }
}
```

- [ ] **Step 3: Create animator sync skeleton**

Create `Assets/Scripts/PhotonFusion/FusionAnimatorSync.cs`:

```csharp
using Fusion;
using UnityEngine;

public class FusionAnimatorSync : NetworkBehaviour
{
    [Networked] private float Speed { get; set; }
    [Networked] private NetworkBool Grounded { get; set; }

    [SerializeField] private Animator animator;
    [SerializeField] private CharacterController controller;

    public override void FixedUpdateNetwork()
    {
        if (Object.HasInputAuthority && controller != null)
        {
            Vector3 velocity = controller.velocity;
            Speed = new Vector3(velocity.x, 0f, velocity.z).magnitude;
            Grounded = controller.isGrounded;
        }

        if (animator != null)
        {
            animator.SetFloat("Speed", Speed);
            animator.SetBool("IsGrounded", Grounded);
        }
    }
}
```

- [ ] **Step 4: Create tree/choppable skeleton**

Create `Assets/Scripts/PhotonFusion/FusionTreeChoppable.cs`:

```csharp
using Fusion;
using UnityEngine;

public class FusionTreeChoppable : NetworkBehaviour
{
    [Networked] public int Health { get; set; }
    [SerializeField] private int startHealth = 3;

    public override void Spawned()
    {
        if (Object.HasStateAuthority && Health <= 0)
        {
            Health = Mathf.Max(1, startHealth);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Chop(int damage)
    {
        if (Health <= 0)
        {
            return;
        }

        Health -= Mathf.Max(1, damage);
        if (Health <= 0)
        {
            Runner.Despawn(Object);
        }
    }
}
```

- [ ] **Step 5: Attach to prefabs and scenes**

- Add survival/combat/animator components to `FusionPlayer.prefab`.
- Add `FusionTreeChoppable` to tree/choppable prefabs or scene objects that should sync.

- [ ] **Step 6: Manual verify**

- Axe swing by client A appears on client B.
- Survival values initialize and local UI can read them.
- Tree health decreases and despawns consistently.

- [ ] **Step 7: Commit**

```powershell
git add Assets/Scripts/PhotonFusion/FusionPlayerSurvival.cs Assets/Scripts/PhotonFusion/FusionPlayerCombat.cs Assets/Scripts/PhotonFusion/FusionAnimatorSync.cs Assets/Scripts/PhotonFusion/FusionTreeChoppable.cs Assets/Assets/Prefabs/FusionPlayer.prefab Assets/Scenes
git commit -m "feat: add Fusion survival and combat sync"
```

---

### Task 10: Disable NGO Runtime Path For Photon V1

**Files:**
- Modify: `Assets/Scenes/MainMenu.unity`
- Modify: `Assets/Scenes/Gameplay.unity`
- Modify: `Assets/Scenes/Environment.unity`
- Modify: `Assets/Scripts/Menu/MainMenuController.cs` if required.

- [ ] **Step 1: Disable old bootstrap objects**

In scenes, disable GameObjects that run old NGO flow during Photon V1:

- `CoopNetworkBootstrap`
- `NetworkManager` if only used by NGO
- `RoomDirectoryClient` objects
- `CoopNetworkTestUI`

Do not delete them until Photon V1 is fully verified.

- [ ] **Step 2: Ensure UI routes to Photon controller**

Verify buttons in MainMenu call `PhotonFusionRoomController`, not old `MainMenuController` NGO create/join/start methods.

- [ ] **Step 3: Run compile and console check**

UnityMCP:

```text
refresh_unity(scope="scripts", compile="request")
read_console(types=["error", "warning"], count=30)
```

Expected: no compile errors. Warnings unrelated to Photon migration must be documented, not ignored.

- [ ] **Step 4: Manual two-client acceptance test**

Run this sequence:

1. Client A Create Room.
2. Client A stays in MainMenu waiting panel.
3. Client B Join Room.
4. Client A Start Lobby.
5. Both load Gameplay.
6. Both spawn players.
7. Client A moves; Client B sees movement.
8. Client A pickups item; item disappears for both, inventory A changes.
9. Client A drops item; both see dropped item.
10. Both interact with chest; no duplicate item.
11. Client A Start Forest; both load Environment.
12. Disconnect returns to MainMenu.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Scenes/MainMenu.unity Assets/Scenes/Gameplay.unity Assets/Scenes/Environment.unity Assets/Scripts/Menu/MainMenuController.cs
git commit -m "chore: switch runtime flow to Photon Fusion"
```

---

## Self-Review Notes

Spec coverage:

- Photon setup covered by Task 1.
- Room create/join/wait/start covered by Tasks 2-4.
- Player spawn/movement/owner setup covered by Tasks 5-6.
- Pickup/drop/inventory covered by Task 7.
- Chest shared loot covered by Task 8.
- Survival/combat/animator/tree sync covered by Task 9.
- Disabling old NGO runtime and final acceptance covered by Task 10.

Known implementation risk:

- Fusion callback and scene loading APIs can differ slightly by installed Fusion 2 version. The worker must validate against the installed package and adapt compile signatures while preserving the plan behavior.
- The plan uses practical manual tests because the existing project has no established gameplay test harness for networked Unity scenes.
