# Forest Door (Gameplay -> Environment) Design

## Goal

Add a door in the `Gameplay` scene that, when interacted with, transitions the whole
Fusion room from `Gameplay` (lobby) into `Environment` (forest). All players in the
session move together.

## Context

- `PhotonFusionSceneLoader.LoadForest()` already performs the room-wide scene
  transition using `bootstrap.Runner.LoadScene(...)` in Shared mode. It is the single
  existing path to enter `Environment`.
- `MainMenuController.HostStartForest()` already calls the same transition from the
  host control panel. This feature is an in-world trigger for the same action.
- `PhotonFusionSceneLoader.LoadNetworkScene(...)` guards:
  - runner not running -> warning + no-op
  - `!bootstrap.IsMasterClient` -> warning + no-op (host only)
  - scene not in Build Settings -> warning + no-op
- `Environment` already contains `FusionPlayerSpawner` + 4 `FusionSpawnPoint`, so
  players spawn correctly after the transition.

## Requirements

1. A `ForestDoor` component whose interaction triggers `PhotonFusionSceneLoader.LoadForest()`.
2. Only the host (master client) can trigger the transition; non-host interaction is a no-op.
   (The host-only guard already lives inside `LoadNetworkScene`.)
3. One-way: Gameplay -> Environment only. No return door.
4. Interaction uses the existing `Interactable`/`PlayerInteractionSystem` flow:
   - door shows outline when looked at,
   - pressing interact (E / pick button) triggers the door.

## Design

### New script: `ForestDoor`

`Assets/Scripts/PhotonFusion/ForestDoor.cs` (runtime `MonoBehaviour`).

```csharp
public class ForestDoor : MonoBehaviour
{
    private PhotonFusionSceneLoader sceneLoader;

    public bool TryInteract()
    {
        ResolveSceneLoader();
        if (sceneLoader == null) return false;

        sceneLoader.LoadForest();
        return true;
    }

    private void ResolveSceneLoader()
    {
        if (sceneLoader == null)
        {
            sceneLoader = FindObjectOfType<PhotonFusionSceneLoader>(true);
        }
    }
}
```

`LoadForest()` already logs and no-ops for non-host / no runner, so `TryInteract()`
can return true optimistically and let `PhotonFusionSceneLoader` handle the guard
messages.

### `PlayerInteractionSystem` change

Add a branch in `TryInteract()` (before the fallback `currentTarget.Interact()` call),
mirroring the existing `FusionStorageChest` / `FusionFurnace` pattern:

```csharp
ForestDoor forestDoor = currentTarget.GetComponent<ForestDoor>();
if (forestDoor != null)
{
    if (forestDoor.TryInteract() && pickButton != null)
    {
        pickButton.SetActive(false);
    }
    return;
}
```

### Scene object

In `Gameplay`, add a `ForestDoor` GameObject:

- Primitive `Cube` (visual placeholder; no door model asset exists in the project).
- `BoxCollider` (trigger not required; the interaction raycast needs a collider).
- `Interactable` component (for outline + `PlayerInteractionSystem` detection).
- `ForestDoor` component.
- Layer: `Item` (3), consistent with other interactables (`axe`, `StorageChest`, `vending_food`).

`PlayerInteractionSystem.interactableLayer` on `FusionPlayer.prefab` is `m_Bits: 8`
(layer 3 = Item), so the door must be on layer 3 to be detected.

## Out of scope

- Return door (Environment -> Gameplay).
- Door art/animation.
- Per-player doors or doors for other scene pairs.

## Testing

- Editor self-test: `ForestDoor` resolves `PhotonFusionSceneLoader` and `TryInteract()`
  invokes `LoadForest()` only when host. Since scene transition requires a live runner,
  the automated check validates wiring, not the actual transition.
- Manual multiplayer QA: host creates room -> joins Gameplay -> interacts with door ->
  both host and client transition to Environment and spawn.
