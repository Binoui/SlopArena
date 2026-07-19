# Task 8: Wire Into Scenes - Report

## Status: DONE

## Scenes with CombatFeedback Found
- **Arena_Offline.unity** (`Assets/Scenes/Arena_Offline.unity`) — FOUND on `TrainingMatch` GameObject
- **Arena_PvP.unity** (`Assets/Scenes/Arena_PvP.unity`) — FOUND on `TrainingMatch` GameObject
- **MainMenu.unity**, **Lobby.unity**, **CharSelect.unity**, **StageSelect.unity** — no CombatFeedback (not gameplay scenes)

## Prefab Assignment
- **Arena_Offline**: `HitSpark.prefab` assigned to `CombatFeedback._hitSparkPrefab` — OK
- **Arena_PvP**: `HitSpark.prefab` assigned to `CombatFeedback._hitSparkPrefab` — OK

## Scenes Saved
- `Assets/Scenes/Arena_Offline.unity` — saved
- `Assets/Scenes/Arena_PvP.unity` — saved

## Errors
None. All assignments succeeded, no missing fields or prefabs.

## Summary
Both gameplay scenes (Arena_Offline, Arena_PvP) have their CombatFeedback `_hitSparkPrefab` serialized field updated from the old placeholder to `Assets/Prefabs/VFX/HitSpark.prefab`. All other build scenes (MainMenu, Lobby, CharSelect, StageSelect) do not contain CombatFeedback components and were left untouched.
