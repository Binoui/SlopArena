#nullable enable
using Godot;

/// <summary>
/// Camera mount — follows a target with orbital rotation.
/// Uses h/v node hierarchy for rotation (h = yaw, v SpringArm = pitch).
/// Sibling of the player in the world tree (not a child) — multiplayer-safe.
///
/// Camera modes allow smooth transitions (Default ↔ Aiming) for skill targeting.
/// Mouse orbit and scroll zoom override mode targets on interaction.
/// </summary>
public partial class CameraMount : Node3D
{
    [Export] public Node3D? Target;
    [Export] public float Sensitivity = 0.003f;
    [Export] public float MinZoom = 5.0f;
    [Export] public float MaxZoom = 80.0f;
    [Export] public float ZoomSpeed = 3.0f;
    /// <summary>
    /// lerp speed for mode transitions
    /// </summary>
    [Export] public float TransitionSpeed = 5.0f;

    public enum CameraMode { Default, Aiming }

    private CameraMode _mode = CameraMode.Default;

    /// <summary>
    /// horizontal yaw node
    /// </summary>
    private Node3D? _h;
    /// <summary>
    /// vertical pitch + zoom node
    /// </summary>
    private SpringArm3D? _v;
    private Camera3D? _camera;

    /// <summary>
    /// Current lerped values (smooth transitions)
    /// </summary>
    private float _currentOffsetY = 1.5f;
    private float _currentDistance = 60f;

    /// <summary>
    /// Target values (set by mode switch)
    /// </summary>
    private float _targetOffsetY = 1.5f;
    private float _targetDistance = 60f;

    /// <summary>
    /// absolute camera yaw in radians, controlled only by mouse
    /// </summary>
    private float _cameraYaw = 0f;

    public override void _Ready()
    {
        _h = GetNodeOrNull<Node3D>("h");
        _v = GetNodeOrNull<SpringArm3D>("h/v");
        _camera = GetNodeOrNull<Camera3D>("h/v/Camera");

        if (_v != null)
        {
            _v.SpringLength = _currentDistance;
            _v.Margin = 0.5f;
        }

        // Exclude player body from camera collision
        if (Target is CollisionObject3D col && _v != null)
            _v.AddExcludedObject(col.GetRid());

        // Initial pitch: -45° (looks downward)
        if (_v != null)
            _v.Rotation = new Vector3(Mathf.DegToRad(-45f), 0f, 0f);

        // Initialize yaw
        if (Target != null)
            _cameraYaw = 0f;

        if (_camera != null)
            _camera.Current = true;
    }

    public override void _Process(double delta)
    {
        if (Target == null) return;

        // Smoothly lerp toward mode targets
        float dt = (float)delta;
        _currentOffsetY = Mathf.Lerp(_currentOffsetY, _targetOffsetY, dt * TransitionSpeed);
        _currentDistance = Mathf.Lerp(_currentDistance, _targetDistance, dt * TransitionSpeed);

        // Follow target position only — camera yaw is purely mouse-controlled.
        Vector3 targetPos = Target.GlobalPosition;
        GlobalPosition = new Vector3(targetPos.X, targetPos.Y + _currentOffsetY, targetPos.Z);

        // Apply distance to SpringArm
        if (_v != null)
            _v.SpringLength = _currentDistance;

        // Camera yaw is absolute, controlled ONLY by mouse orbit (not player facing).
        if (_h != null)
            _h.Rotation = new Vector3(0f, _cameraYaw, 0f);
    }

    /// <summary>
    /// Switch camera mode with smooth transition.
    /// Each mode defines default distance and height offset.
    /// </summary>
    public void SetMode(CameraMode mode)
    {
        if (mode == _mode) return;
        _mode = mode;

        switch (mode)
        {
            case CameraMode.Default:
                _targetOffsetY = 1.5f;
                _targetDistance = 60f;
                break;

            case CameraMode.Aiming:
                _targetOffsetY = 2.5f; // higher — better overhead view
                _targetDistance = 80f; // further back — wider FOV for aiming
                break;
        }
    }

    public CameraMode GetMode() => _mode;

    /// <summary>
    /// Rotate camera orbit. Called by PlayerController on mouse move.
    /// Camera yaw is absolute — only modified here, never by player facing.
    /// </summary>
    public void RotateCamera(Vector2 relativeMotion)
    {
        if (_v == null) return;

        // Yaw: modify absolute camera yaw directly
        _cameraYaw -= relativeMotion.X * Sensitivity;

        // Pitch: rotate the v (SpringArm) around X directly (not overwritten by _Process)
        float pitch = _v.Rotation.X - (relativeMotion.Y * Sensitivity);
        pitch = Mathf.Clamp(pitch, Mathf.DegToRad(-85f), Mathf.DegToRad(-5f));
        _v.Rotation = new Vector3(pitch, 0f, 0f);
    }

    /// <summary>
    /// Zoom in/out. Overrides mode's target distance so scroll doesn't fight with mode transition.
    /// </summary>
    public void ZoomCamera(float direction)
    {
        _currentDistance = Mathf.Clamp(_currentDistance + (direction * ZoomSpeed), MinZoom, MaxZoom);
        _targetDistance = _currentDistance;
    }

    public Vector3 GetForwardDirection()
    {
        // Read from _h whose GlobalTransform carries the camera yaw.
        // Camera yaw is absolute — mouse-controlled only.
        if (_h == null) return -Vector3.Forward;
        Vector3 forward = -_h.GlobalTransform.Basis.Z;
        forward.Y = 0;
        return forward.Normalized();
    }

    public Vector3 GetRightDirection()
    {
        if (_h == null) return Vector3.Right;
        Vector3 right = _h.GlobalTransform.Basis.X;
        right.Y = 0;
        return right.Normalized();
    }

    public Camera3D? GetCamera() => _camera;
}
