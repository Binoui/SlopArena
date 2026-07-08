# Aim Camera System — Design Spec

**Date:** 2026-07-08  
**Scope:** Manki R (Bazooka) and E (Grapple Gun) aiming state  
**Status:** Approved, ready for implementation

---

## Problem

Manki's R and E abilities use `AimMode.CameraForward3D`, which currently freezes the orbital camera and accumulates mouse Y as a `_aimPitchOffsetDeg` on `AimHandler`. This gives no camera movement and no visual feedback for aim direction — aiming by feel rather than by sight.

The desired behaviour: holding R or E transitions to a dedicated behind-the-shoulder aim camera. The whole camera moves with mouse input. A fixed crosshair sits at screen center. Releasing the key fires and blends back to the orbital camera.

---

## Requirements

- Hold R or E → enter aiming state
- Camera snaps behind the character (facing yaw) then blends to a tighter behind-the-shoulder view
- Mouse X/Y rotates the aim camera freely (full yaw + pitch)
- Pitch clamped to -60° / +60° (covers rocket jump through overhead grapple)
- ZQSD movement works normally during aiming
- Fixed circle crosshair at screen center while aiming (crosshair asset supplied separately)
- Releasing the key triggers the firing phase; camera blends back to orbital
- Same system shared by R and E — no ability-specific camera logic
- No server changes — `MankiBazooka` and `MankiGrapple` already read `IsAiming`, `AimYaw`, `AimPitch`

---

## Scene Hierarchy

```
CameraRig (existing)
├── OrbitalCamera   [CinemachineCamera, CinemachineOrbitalFollow]  priority=10  ← existing
└── AimCamera       [CinemachineCamera, CinemachineFollow]          priority=0 normally, 20 while aiming  ← NEW
```

`AimCamera` uses `CinemachineFollow` (fixed offset, not orbital). Suggested body offset: `(0.3, 0.5, -2.5)` (right shoulder, slightly up, 2.5m behind). `LookAt` target: player capsule center. Priority 0 means Cinemachine ignores it entirely until aiming mode raises it to 20.

The `AimCamera` follows a **pivot Transform** — a world-sibling GameObject repositioned to the player each frame by `AimCameraMount`. Rotating the pivot drives both the camera position and the aim direction.

---

## New Component: `AimCameraMount`

**File:** `client/Unity/Assets/Scripts/Runtime/Camera/AimCameraMount.cs`

```
AimCameraMount
├── [SerializeField] CinemachineCamera _aimCinemachineCamera
├── [SerializeField] Transform _pivot               ← world-sibling, repositioned each frame
├── [SerializeField] float _pitchMin = -60f
├── [SerializeField] float _pitchMax = 60f
│
├── Activate(Transform player, float facingYawRad)
│     Set pivot position to player position
│     Set pivot yaw = facingYawRad (snap behind character)
│     Reset pitch to 0°
│     _aimCinemachineCamera.Priority = 20
│
├── Deactivate()
│     _aimCinemachineCamera.Priority = 0
│
├── Tick(Transform player)
│     Reposition pivot to player.position each frame (player moves during aiming)
│
├── ApplyMouseDelta(Vector2 delta, float sensitivity)
│     _yawDeg   += delta.x * sensitivity
│     _pitchDeg -= delta.y * sensitivity          ← invert Y so mouse-down = aim down
│     _pitchDeg  = Clamp(_pitchDeg, _pitchMin, _pitchMax)
│     pivot.rotation = Quaternion.Euler(_pitchDeg, _yawDeg, 0)
│
├── GetAimYawRad()   → _yawDeg * Deg2Rad
└── GetAimPitchRad() → _pitchDeg * Deg2Rad        ← negative = aimed below horizon
```

---

## Modified: `CameraMode` enum

Add `Aiming` to `CameraMount.cs`:

```csharp
public enum CameraMode
{
    Normal,       // Cursor locked, camera orbits freely
    Frozen,       // Cursor locked, camera yaw/pitch held constant
    FreeCursor,   // Cursor unlocked, camera yaw/pitch held constant
    Aiming,       // Cursor locked, orbital camera frozen, AimCameraMount drives aim camera
}
```

`CameraMount.Update` adds an `Aiming` case that skips all mouse input (orbital axes untouched for the whole aim window → clean snap-back on deactivation).

---

## Modified: `AimHandler`

**File:** `client/Unity/Assets/Scripts/Runtime/Combat/AimHandler.cs`

Changes:
- Add `[SerializeField] private AimCameraMount _aimCameraMount;`
- `Init()` receives and stores the `AimCameraMount` reference
- Remove `_aimPitchOffsetDeg` field entirely
- `Init()` wires `_aimCameraMount` reference alongside existing `_cameraMount`

In `Evaluate()`, replace the `CameraForward3D` branch:

```
Entering CameraForward3D aim:
  _aimCameraMount.Activate(characterTransform, playerState.FacingYaw)
  _cameraMount.SetMode(CameraMode.Aiming)

Each tick while CameraForward3D held:
  _aimCameraMount.Tick(characterTransform)
  _aimCameraMount.ApplyMouseDelta(Mouse.current.delta.ReadValue(), _aimSensitivity)
  ctx.AimYawRad   = _aimCameraMount.GetAimYawRad()
  ctx.AimPitchRad = _aimCameraMount.GetAimPitchRad()
  ctx.IsAiming    = true

Exiting CameraForward3D aim:
  _aimCameraMount.Deactivate()
  _cameraMount.SetMode(CameraMode.Normal)
```

Add `[SerializeField] private float _aimSensitivity = 0.15f;` for per-Inspector tuning.

---

## Modified: `TrainingMatch` — Crosshair

**File:** `client/Unity/Assets/Scripts/Runtime/World/TrainingMatch.cs`

- Add `[SerializeField] private Texture2D _crosshairTexture;`
- Add `[SerializeField] private float _crosshairSize = 32f;`
- Replace existing `OnGUI` crosshair placeholder draw:

```csharp
if (_aimHandler.ShowCrosshair && _crosshairTexture != null)
{
    float cx = Screen.width * 0.5f;
    float cy = Screen.height * 0.5f;
    float h  = _crosshairSize;
    float w  = _crosshairSize;
    GUI.DrawTexture(new Rect(cx - w/2f, cy - h/2f, w, h), _crosshairTexture);
}
```

Assign the crosshair asset in the Inspector. `ShowCrosshair` is already `true` for all `CameraForward3D` abilities.

---

## Wiring in Inspector (Arena_Offline scene)

1. Create `AimCamera` GameObject as child of `CameraRig`
2. Add `CinemachineCamera` + `CinemachineFollow` components; set priority = 0
3. Create `AimPivot` GameObject (world-sibling of player); assign to `AimCameraMount._pivot`
4. Set `AimCamera` Follow target = `AimPivot`; set LookAt = player capsule root
5. Add `AimCameraMount` component; wire `_aimCinemachineCamera` = `AimCamera`
6. In `TrainingMatch` Inspector: wire `_aimCameraMount`, assign `_crosshairTexture`
7. In `AimHandler` Inspector: wire `_aimCameraMount`

---

## Data Flow Summary

```
Hold R/E
  → AimHandler.Evaluate detects CameraForward3D
  → AimCameraMount.Activate (snap pivot yaw behind char, priority → 20)
  → CameraMount.SetMode(Aiming) (orbital freezes, releases mouse from orbital)
  → Cinemachine blends OrbitalCamera → AimCamera

Each tick while held:
  → Mouse delta → AimCameraMount.ApplyMouseDelta → pivot rotates
  → AimCamera follows pivot (tight behind-shoulder view moves with mouse)
  → AimContext { IsAiming=true, AimYawRad, AimPitchRad } → InputState
  → Server MankiBazooka/MankiGrapple: stays in Aiming phase, stores aim direction

Release R/E:
  → AimHandler detects !IsSlotKeyHeld
  → AimCameraMount.Deactivate (priority → 0)
  → CameraMount.SetMode(Normal)
  → Cinemachine blends AimCamera → OrbitalCamera
  → Server transitions Aiming → Firing using stored AimYaw/AimPitch
```

---

## Out of Scope

- Manki Q (RoundBomb) — uses `AimMode.GroundCursor` (ground ring), unchanged
- Aim camera for any character other than Manki — same system works for any `CameraForward3D` ability, no extra work needed
- Aim camera FOV change (zoom) — can be added later as a `CinemachineCamera.Lens.FieldOfView` tweak in `Activate()`
- Networked aim direction smoothing — deferred to PvP phase

---

## Files Touched

| File | Change |
|---|---|
| `Runtime/Camera/AimCameraMount.cs` | **New** |
| `Runtime/Camera/CameraMount.cs` | Add `CameraMode.Aiming`, handle in `Update` |
| `Runtime/Combat/AimHandler.cs` | Replace `_aimPitchOffsetDeg` with `AimCameraMount` calls |
| `Runtime/World/TrainingMatch.cs` | Wire `_aimCameraMount`, crosshair texture draw |
| `Arena_Offline` (Unity scene) | Add `AimCamera` + `AimPivot` GameObjects, wire Inspector refs |
