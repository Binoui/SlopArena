# Aim Camera System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the frozen-camera aim system for Manki's R and E abilities with a dedicated behind-the-shoulder Cinemachine camera that moves with mouse input, blending to/from the orbital camera on key press/release.

**Architecture:** A new `AimCameraMount` component owns a second `CinemachineCamera` (priority-based blending). `AimHandler` calls into it instead of accumulating a pitch offset. `CameraMount` gains a new `Aiming` mode that freezes orbital input while the aim camera is active.

**Tech Stack:** Unity 6, Cinemachine 3.x (`CinemachineCamera`, `CinemachineFollow`), Unity InputSystem (`Mouse.current.delta`), C# (.NET 8 / netstandard2.1 for Shared)

**Spec:** `docs/superpowers/specs/2026-07-08-aim-camera-design.md`

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `client/Unity/Assets/Scripts/Runtime/Camera/AimCameraMount.cs` | **Create** | Owns aim camera pivot, mouse delta → yaw/pitch, priority toggle |
| `client/Unity/Assets/Scripts/Runtime/Camera/CameraMount.cs` | **Modify** | Add `CameraMode.Aiming`, skip orbital input in that mode |
| `client/Unity/Assets/Scripts/Runtime/Combat/AimHandler.cs` | **Modify** | Replace `_aimPitchOffsetDeg` with `AimCameraMount` calls |
| `client/Unity/Assets/Scripts/Runtime/World/TrainingMatch.cs` | **Modify** | Add crosshair texture fields, replace `OnGUI` draw with `GUI.DrawTexture` |

Scene wiring (Task 4) is done manually in the Unity Editor — no `.cs` file.

---

## Task 1: Add `CameraMode.Aiming` to `CameraMount`

**Files:**
- Modify: `client/Unity/Assets/Scripts/Runtime/Camera/CameraMount.cs`

- [ ] **Step 1: Add `Aiming` to the enum**

Open `client/Unity/Assets/Scripts/Runtime/Camera/CameraMount.cs`. The `CameraMode` enum is at the top of the file (lines 8–12). Add the new value:

```csharp
public enum CameraMode
{
    Normal,       // Cursor locked, camera orbits freely
    Frozen,       // Cursor locked, camera yaw/pitch held constant
    FreeCursor,   // Cursor unlocked, camera yaw/pitch held constant
    Aiming,       // Cursor locked, orbital camera frozen, AimCameraMount drives aim camera
}
```

- [ ] **Step 2: Handle `Aiming` in `SetMode`**

In `CameraMount.SetMode(CameraMode mode)` (around line 69), add a case for `Aiming`. It behaves like `Frozen` for cursor purposes — cursor stays locked and hidden:

```csharp
public void SetMode(CameraMode mode)
{
    _mode = mode;
    switch (mode)
    {
        case CameraMode.Normal:
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            break;
        case CameraMode.Frozen:
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            break;
        case CameraMode.FreeCursor:
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            break;
        case CameraMode.Aiming:
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            break;
    }
}
```

- [ ] **Step 3: Skip orbital input in `Update` when `Aiming`**

In `CameraMount.Update()` (around line 43), the existing code handles `Normal`, `Frozen`, and `FreeCursor`. Add an `Aiming` branch that does nothing — the orbital camera's axes must not change while the aim camera is active:

```csharp
private void Update()
{
    if (_orbital == null) return;

    if (_mode == CameraMode.Normal)
    {
        float dy = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(dy) > 0.001f)
            _orbital.RadialAxis.Value -= dy * 0.05f;

        SetCameraPitchDeg(_frozenPitch);
    }
    else if (_mode == CameraMode.Frozen)
    {
        SetCameraPitchDeg(_frozenPitch);
    }
    else if (_mode == CameraMode.FreeCursor)
    {
        SetCameraYawDeg(_frozenYaw);
        SetCameraPitchDeg(_frozenPitch);
    }
    // CameraMode.Aiming: do nothing — AimCameraMount owns all mouse input
}
```

- [ ] **Step 4: Commit**

```bash
git add client/Unity/Assets/Scripts/Runtime/Camera/CameraMount.cs
git commit -m "feat: add CameraMode.Aiming — orbital freezes while aim camera active"
```

---

## Task 2: Create `AimCameraMount`

**Files:**
- Create: `client/Unity/Assets/Scripts/Runtime/Camera/AimCameraMount.cs`

- [ ] **Step 1: Write the full component**

Create `client/Unity/Assets/Scripts/Runtime/Camera/AimCameraMount.cs`:

```csharp
using Unity.Cinemachine;
using UnityEngine;

namespace SlopArena.Client.Camera
{
    /// <summary>
    /// Owns the dedicated aim camera for CameraForward3D abilities (Bazooka, Grapple).
    ///
    /// Attach to the AimCamera GameObject alongside CinemachineCamera + CinemachineFollow.
    ///
    /// Usage:
    ///   Activate(playerTransform, facingYawRad)  — snap behind character, raise priority
    ///   Tick(playerTransform)                    — called every FixedUpdate while active
    ///   ApplyMouseDelta(delta, sensitivity)       — rotate pivot from mouse input
    ///   Deactivate()                             — lower priority, Cinemachine blends back
    /// </summary>
    public class AimCameraMount : MonoBehaviour
    {
        [SerializeField] private CinemachineCamera _aimCinemachineCamera;
        [SerializeField] private Transform _pivot;

        [SerializeField] private float _pitchMin = -60f;
        [SerializeField] private float _pitchMax =  60f;

        private float _yawDeg;
        private float _pitchDeg;
        private bool _active;

        /// <summary>
        /// Snap the aim camera behind the character and raise priority so Cinemachine blends in.
        /// facingYawRad: character's current facing direction in radians.
        /// </summary>
        public void Activate(Transform player, float facingYawRad)
        {
            if (_pivot == null || _aimCinemachineCamera == null) return;

            _yawDeg   = facingYawRad * Mathf.Rad2Deg;
            _pitchDeg = 0f;

            _pivot.position = player.position;
            _pivot.rotation = Quaternion.Euler(_pitchDeg, _yawDeg, 0f);

            _aimCinemachineCamera.Priority = 20;
            _active = true;
        }

        /// <summary>
        /// Lower priority so Cinemachine blends back to the orbital camera.
        /// </summary>
        public void Deactivate()
        {
            if (_aimCinemachineCamera != null)
                _aimCinemachineCamera.Priority = 0;
            _active = false;
        }

        /// <summary>
        /// Reposition the pivot to track the player each FixedUpdate tick.
        /// The player moves during aiming — keep the camera following.
        /// </summary>
        public void Tick(Transform player)
        {
            if (!_active || _pivot == null) return;
            _pivot.position = player.position;
        }

        /// <summary>
        /// Accumulate mouse input into aim yaw and pitch.
        /// Call after Tick(), before reading GetAimYawRad/GetAimPitchRad.
        /// </summary>
        public void ApplyMouseDelta(Vector2 delta, float sensitivity)
        {
            if (!_active || _pivot == null) return;

            _yawDeg   += delta.x * sensitivity;
            _pitchDeg -= delta.y * sensitivity; // invert Y: mouse down = aim down

            _pitchDeg = Mathf.Clamp(_pitchDeg, _pitchMin, _pitchMax);
            _pivot.rotation = Quaternion.Euler(_pitchDeg, _yawDeg, 0f);
        }

        /// <summary>Aim yaw in radians — fed into AimContext.AimYawRad.</summary>
        public float GetAimYawRad()   => _yawDeg * Mathf.Deg2Rad;

        /// <summary>Aim pitch in radians — fed into AimContext.AimPitchRad. Negative = below horizon.</summary>
        public float GetAimPitchRad() => _pitchDeg * Mathf.Deg2Rad;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add client/Unity/Assets/Scripts/Runtime/Camera/AimCameraMount.cs
git commit -m "feat: add AimCameraMount — pivot-driven aim camera for CameraForward3D abilities"
```

---

## Task 3: Update `AimHandler` to use `AimCameraMount`

**Files:**
- Modify: `client/Unity/Assets/Scripts/Runtime/Combat/AimHandler.cs`

- [ ] **Step 1: Add `_aimCameraMount` field and remove `_aimPitchOffsetDeg`**

In the field declarations (lines 21–26), replace `_aimPitchOffsetDeg` with `_aimCameraMount` and add `_aimSensitivity`. The `_characterTransform` also needs to be stored — currently `Evaluate` doesn't hold a reference across ticks. Store it in `Init`:

Replace the existing fields block:

```csharp
[SerializeField] private AimIndicator _aimIndicator;
[SerializeField] private CameraMount _cameraMount;
[SerializeField] private AimCameraMount _aimCameraMount;
[SerializeField] private float _aimSensitivity = 0.15f;

private CameraMode _activeMode = CameraMode.Normal;
private byte _aimingSlot;
private Transform _characterTransform;
```

(Remove `private float _aimPitchOffsetDeg;` — it no longer exists.)

- [ ] **Step 2: Update `Init` to store character transform**

The existing `Init` signature is:
```csharp
public void Init(CameraMount cameraMount, UnityEngine.Camera renderCamera, Transform characterTransform, float capsuleHeight)
```

Store `characterTransform` for use in `Evaluate`:

```csharp
public void Init(CameraMount cameraMount, UnityEngine.Camera renderCamera, Transform characterTransform, float capsuleHeight)
{
    _cameraMount = cameraMount;
    _characterTransform = characterTransform;
    if (_aimIndicator != null)
    {
        _aimIndicator.SetCamera(renderCamera);
        _aimIndicator.SetCharacter(characterTransform, capsuleHeight);
    }
    _cameraMount?.SetMode(CameraMode.Normal);
    _activeMode = CameraMode.Normal;
}
```

- [ ] **Step 3: Replace the `CameraForward3D` branch in `Evaluate`**

The existing branch (lines 133–146) reads mouse delta and accumulates `_aimPitchOffsetDeg`. Replace it entirely with `AimCameraMount` calls:

```csharp
if (aimMode == AimMode.CameraForward3D && _aimCameraMount != null)
{
    _aimCameraMount.Tick(_characterTransform);
    Vector2 mouseDelta = Mouse.current.delta.ReadValue();
    _aimCameraMount.ApplyMouseDelta(mouseDelta, _aimSensitivity);

    ctx = new AimContext
    {
        IsAiming    = true,
        AimYawRad   = _aimCameraMount.GetAimYawRad(),
        AimPitchRad = _aimCameraMount.GetAimPitchRad(),
    };
}
```

- [ ] **Step 4: Update the camera mode transition block for `Aiming`**

The existing transition (lines 95–112) maps `CameraForward3D` → `CameraMode.Frozen`. Change it to map to `CameraMode.Aiming` and call `AimCameraMount.Activate` on entry and `Deactivate` on exit:

```csharp
// ── 2. Drive camera mode (transitions only) ──
CameraMode desired = aimMode switch
{
    AimMode.GroundCursor    => CameraMode.FreeCursor,
    AimMode.CameraForward3D => CameraMode.Aiming,
    _                       => CameraMode.Normal,
};

if (desired != _activeMode)
{
    if (desired == CameraMode.Aiming && _characterTransform != null)
    {
        // Snap aim camera behind character, then let Cinemachine blend
        _aimCameraMount?.Activate(_characterTransform, playerState.FacingYaw);
    }
    else if (_activeMode == CameraMode.Aiming)
    {
        // Leaving aim mode — lower aim camera priority
        _aimCameraMount?.Deactivate();
    }

    // GroundCursor still needs FreezeAtCurrentAngles for the orbital camera
    if (desired == CameraMode.FreeCursor)
        _cameraMount?.FreezeAtCurrentAngles();

    // Frozen mode (not used by CameraForward3D anymore) still needs angle capture
    if (desired == CameraMode.Frozen)
        _cameraMount?.FreezeAtCurrentAngles();

    _cameraMount?.SetMode(desired);
    _activeMode = desired;
}
```

- [ ] **Step 5: Commit**

```bash
git add client/Unity/Assets/Scripts/Runtime/Combat/AimHandler.cs
git commit -m "feat: AimHandler uses AimCameraMount for CameraForward3D — replaces pitch offset accumulation"
```

---

## Task 4: Update `TrainingMatch` — Crosshair and Wiring

**Files:**
- Modify: `client/Unity/Assets/Scripts/Runtime/World/TrainingMatch.cs`

- [ ] **Step 1: Add crosshair serialized fields**

In the `[Header("Aiming")]` block (around line 41), add:

```csharp
[Header("Aiming")]
[SerializeField] private AimHandler _aimHandler;
[SerializeField] private HUDManager _hudManager;
[SerializeField] private bool _showHitboxes;
[SerializeField] private Texture2D _crosshairTexture;
[SerializeField] private float _crosshairSize = 32f;
```

- [ ] **Step 2: Replace the `OnGUI` crosshair draw**

The existing `OnGUI` method (lines 411–425) draws a `"+"` label. Replace it with a `GUI.DrawTexture` call using the supplied asset, with a plain `+` label fallback when no texture is assigned:

```csharp
private void OnGUI()
{
    if (!_showCrosshair) return;

    float cx = Screen.width  * 0.5f;
    float cy = Screen.height * 0.5f;

    if (_crosshairTexture != null)
    {
        float half = _crosshairSize * 0.5f;
        GUI.DrawTexture(new Rect(cx - half, cy - half, _crosshairSize, _crosshairSize), _crosshairTexture);
    }
    else
    {
        // Fallback: plain + label until texture is assigned in Inspector
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 28,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };
        GUI.Label(new Rect(cx - 20, cy - 20, 40, 40), "+", style);
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add client/Unity/Assets/Scripts/Runtime/World/TrainingMatch.cs
git commit -m "feat: TrainingMatch crosshair draws texture asset with + fallback"
```

---

## Task 5: Scene Wiring in Unity Editor

This task has no `.cs` file — it's done in the Unity Editor.

- [ ] **Step 1: Create `AimPivot` GameObject**

In `Arena_Offline` scene, under the `CameraRig` GameObject, create an empty child named `AimPivot`. This is the pivot that `AimCameraMount` will rotate.

- [ ] **Step 2: Create `AimCamera` GameObject**

Under `CameraRig`, create a child named `AimCamera`. Add these components:
- `CinemachineCamera` — set **Priority = 0**
- `CinemachineFollow` — set **Follow Offset = (0.3, 0.5, -2.5)** (right shoulder, slightly up, 2.5m back)
- `AimCameraMount`

- [ ] **Step 3: Wire `AimCamera` targets**

On the `CinemachineCamera` component of `AimCamera`:
- **Follow** = `AimPivot` transform
- **LookAt** = player capsule root transform (same target as the orbital camera)

On `AimCameraMount`:
- `_aimCinemachineCamera` = the `CinemachineCamera` on this same GameObject
- `_pivot` = the `AimPivot` transform created in Step 1

- [ ] **Step 4: Wire `AimCameraMount` into `AimHandler` and `TrainingMatch`**

In the Inspector:
- `AimHandler._aimCameraMount` → the `AimCameraMount` component on `AimCamera`
- `TrainingMatch._aimCameraMount` is not needed (it's wired through `AimHandler`)
- `TrainingMatch._crosshairTexture` → assign your circle crosshair texture asset

- [ ] **Step 5: Set Cinemachine blend settings**

On the `CinemachineBrain` component (on the main Camera): set **Default Blend** to `Ease In Out, 0.2s`. This gives a smooth but snappy transition.

- [ ] **Step 6: Smoke test**

Enter Play mode in the Editor. Press and hold R:
- Camera should blend to a tighter behind-the-shoulder view
- Mouse movement should rotate the camera (both X and Y)
- Aiming down should be possible (aim at Manki's feet for rocket jump)
- Releasing R should fire the rocket and blend the camera back to orbital
- Repeat with E (grapple) — same camera behaviour

- [ ] **Step 7: Commit scene**

```bash
git add client/Unity/Assets/Scenes/Arena_Offline.unity
git commit -m "feat: wire AimCamera and AimPivot in Arena_Offline scene"
```

---

## Self-Review Checklist

After all tasks are complete:

- [ ] Hold R → aim camera activates, blends in, mouse rotates camera
- [ ] Release R → rocket fires in aimed direction, camera blends back
- [ ] Hold E → same camera behaviour; grapple fires in aimed direction
- [ ] Q (bomb) → unaffected, still uses ground ring cursor
- [ ] ZQSD movement works during aim state
- [ ] Pitching down to -60° reaches Manki's feet (rocket jump)
- [ ] Releasing R/E mid-aim while looking up fires correctly (pitch > 0)
- [ ] No camera state corruption after rapid press/release of R
