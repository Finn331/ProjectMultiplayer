# Survival Bar Critical Warning Design

## Goal
Add flash/glow effect on HUD bars when health, hunger, or thirst drops below critical threshold.

## Design

Add to `PlayerSurvivalUI.cs`:
- `criticalThreshold` (default 0.2 = 20%)
- In `Update()`, when any bar is below threshold, alternate the fill color between normal and a warning color using `Mathf.PingPong(Time.time * flashSpeed, 1)`
- Colors: health flashes red-white, thirst flashes cyan-white, hunger flashes orange-white
- Flash speed: 3 cycles/second

Only file changed: `Assets/Scripts/Player/Survival/PlayerSurvivalUI.cs`
