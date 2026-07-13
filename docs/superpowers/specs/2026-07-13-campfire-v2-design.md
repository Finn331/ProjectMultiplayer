# Campfire Cooking System v2 — Design

**Date**: 2026-07-13  
**Status**: Approved  
**Approach**: Standalone refactor (Pendekatan 1)

## Summary
Refactor `CampfireCooking` menjadi sistem full-feature seperti furnace: fuel system, 3 input + 3 output + 1 fuel slot, RPC relay, procedural `CampfireUI`, dan ignition toggle.

---

## Networked State (`CampfireCooking.cs`)

| Field | Type | Default |
|---|---|---|
| `BurnTimer` | float | 0 |
| `IsLit` | NetworkBool | false |
| `FuelAmount` | int | 0 |
| `InputTypes[3]` | NetworkArray<int> | ItemType.None |
| `InputAmounts[3]` | NetworkArray<int> | 0 |
| `CookTimers[3]` | NetworkArray<float> | 0 |
| `OutputTypes[3]` | NetworkArray<int> | ItemType.None |
| `OutputAmounts[3]` | NetworkArray<int> | 0 |

**Constants**: SlotCount=3, MaxStack=8, CookTime=15s, FuelBurnTime=30s

---

## Valid Items
- **Input**: RawChicken → CookedChicken, RawFish → CookedFish
- **Fuel**: Wood (burns 30s each, produces 1 Ash in output)
- **Output**: CookedChicken, CookedFish, Ash

---

## FixedUpdateNetwork (State Authority)
1. If `IsLit == false` → return
2. If `BurnTimer <= 0` and `FuelAmount > 0`: consume 1 fuel, BurnTimer=30, produce 1 Ash in output
3. If `BurnTimer <= 0` and `FuelAmount == 0`: IsLit=false, return
4. BurnTimer -= deltaTime
5. For each input slot: advance CookTimers[i], when >=15s consume input & produce output

---

## RPC Relay
`RPC_AddFuel`, `RPC_AddToCampfire`, `RPC_PickupOutput`, `RPC_PickupInput`, `RPC_PickupFuel`, `RPC_ToggleLit`
All: `RpcSources.All` → `RpcTargets.StateAuthority`

---

## CampfireUI.cs (new)
- Procedural Canvas, same pattern as FurnaceUI
- Left: Inventory + Hotbar, Right: FUEL(1) + INPUT(3) + OUTPUT(3) + IGNITE/STOP + CLOSE
- Drag-drop with split dialog, 4m auto-close

---

## PlayerInteractionSystem Update
Update `TryInteractCampfire()` to match furnace priority: fuel > input > pickup output > open UI.

---

## Files
- **Rewrite**: `Assets/Scripts/PhotonFusion/CampfireCooking.cs`
- **New**: `Assets/Scripts/Object/CampfireUI.cs`
- **Update**: `Assets/Scripts/Player/Survival/PlayerInteractionSystem.cs`

## Deferred
- Fire particle visual (nanti)
