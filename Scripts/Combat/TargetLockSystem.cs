#nullable enable
using Godot;

/// <summary>
/// Range-based soft lock system.
/// Continuously tracks nearest enemy in camera view (no button press required).
/// Shows visual indicator (red ring under target).
/// </summary>
public partial class TargetLockSystem : Node3D
{
    [Export] public Camera3D? Camera;
    /// <summary>
    /// Max lock distance
    /// </summary>
    [Export] public float LockRange = 20f;
    /// <summary>
    /// Half-angle cone (45° = 90° total)
    /// </summary>
    [Export] public float LockAngle = 45f;
    /// <summary>
    /// Update every 100ms
    /// </summary>
    [Export] public float UpdateInterval = 0.1f;
    /// <summary>
    /// Hysteresis: keep current target if within 120% range
    /// </summary>
    [Export] public float StickyMultiplier = 1.2f;

    private Node3D? _currentTarget;
    private float _updateTimer = 0f;
    /// <summary>
    /// Visual reticle
    /// </summary>
    private MeshInstance3D? _targetIndicator;
    private Node3D? _ownerEntity;

    public Node3D? CurrentTarget => _currentTarget;

    /// <summary>
    /// Set camera after initialization (if not set in constructor).
    /// </summary>
    public void SetCamera(Camera3D? camera)
    {
        Camera = camera;
    }

    public override void _Ready()
    {
        _ownerEntity = GetParent() as Node3D;
        GD.Print($"[TargetLock] System created! Owner: {_ownerEntity?.Name ?? "null"}, Camera: {Camera != null}");

        // Create target indicator (red ring under target)
        _targetIndicator = new MeshInstance3D
        {
            Name = "TargetIndicator",
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        var ring = new TorusMesh { InnerRadius = 1.5f, OuterRadius = 1.8f };
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.2f, 0.2f, 0.8f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _targetIndicator.Mesh = ring;
        _targetIndicator.MaterialOverride = mat;
        _targetIndicator.Visible = false;
        AddChild(_targetIndicator);
    }

    public override void _Process(double delta)
    {
        // Try to get camera if not set (delayed initialization)
        if (Camera == null && _ownerEntity is PlayerController player)
        {
            var cam = player.GetCamera();
            if (cam != null)
            {
                Camera = cam.GetCamera();
                if (Camera != null)
                    GD.Print("[TargetLock] Camera acquired!");
            }
        }

        _updateTimer += (float)delta;
        if (_updateTimer < UpdateInterval)
            return;

        _updateTimer = 0f;
        UpdateSoftLock();
        UpdateVisualIndicator();
    }

    private void UpdateSoftLock()
    {
        if (Camera == null || _ownerEntity == null)
            return;

        Vector3 cameraForward = -Camera.GlobalTransform.Basis.Z;
        Vector3 origin = _ownerEntity.GlobalPosition;

        Node3D? bestTarget = null;
        float bestScore = float.MaxValue;

        // Find all enemies in range
        foreach (var node in (Godot.Collections.Array<Node>)GetTree().GetNodesInGroup("enemies"))
        {
            if (node is not Node3D enemy || enemy == _ownerEntity)
                continue;

            Vector3 toEnemy = enemy.GlobalPosition - origin;
            float dist = toEnemy.Length();

            if (dist > LockRange)
                continue;

            // Check cone angle
            if (dist > 0.001f)
            {
                float angle = cameraForward.AngleTo(toEnemy.Normalized());
                float angleDeg = angle * 180f / Mathf.Pi;
                if (angleDeg > LockAngle)
                    continue;

                // Score: prefer close + centered in camera view
                float score = dist + (angleDeg * 0.5f);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestTarget = enemy;
                }
            }
        }

        // Sticky lock: keep current target if still valid (hysteresis)
        if (_currentTarget != null && IsInstanceValid(_currentTarget))
        {
            Vector3 toCurrent = _currentTarget.GlobalPosition - origin;
            float currentDist = toCurrent.Length();

            if (currentDist > 0.001f)
            {
                float currentAngle = cameraForward.AngleTo(toCurrent.Normalized()) * 180f / Mathf.Pi;

                // Keep current if within extended range (hysteresis)
                if (currentDist < LockRange * StickyMultiplier && currentAngle < LockAngle * StickyMultiplier)
                {
                    // Only switch if new target is MUCH better
                    float currentScore = currentDist + (currentAngle * 0.5f);
                    if (bestScore > currentScore * 0.7f)  // 30% threshold
                        return;
                }
            }
        }

        // Log when target changes
        if (_currentTarget != bestTarget)
        {
            if (bestTarget != null)
                GD.Print($"[TargetLock] Locked onto: {bestTarget.Name}");
            else if (_currentTarget != null)
                GD.Print("[TargetLock] Lost lock");
        }

        _currentTarget = bestTarget;
    }

    private void UpdateVisualIndicator()
    {
        if (_targetIndicator == null)
            return;

        if (_currentTarget != null && IsInstanceValid(_currentTarget))
        {
            _targetIndicator.Visible = true;
            // Place ring at ground level (Y=0.05 to avoid z-fighting)
            Vector3 pos = _currentTarget.GlobalPosition;
            pos.Y = 0.05f;
            _targetIndicator.GlobalPosition = pos;

            // Rotate ring slowly
            _targetIndicator.RotateY(0.05f);
        }
        else
        {
            _targetIndicator.Visible = false;
        }
    }

    /// <summary>
    /// Get distance from owner to current target.
    /// Returns float.MaxValue if no valid target.
    /// </summary>
    public float GetDistanceToTarget()
    {
        if (_currentTarget == null || !IsInstanceValid(_currentTarget) || _ownerEntity == null)
            return float.MaxValue;

        return _ownerEntity.GlobalPosition.DistanceTo(_currentTarget.GlobalPosition);
    }

    /// <summary>
    /// Get normalized direction from owner to current target.
    /// Returns owner forward if no valid target.
    /// </summary>
    public Vector3 GetDirectionToTarget()
    {
        if (_currentTarget == null || !IsInstanceValid(_currentTarget) || _ownerEntity == null)
            return -_ownerEntity?.GlobalTransform.Basis.Z ?? Vector3.Forward;

        Vector3 dir = _currentTarget.GlobalPosition - _ownerEntity.GlobalPosition;
        return dir.Length() > 0.001f ? dir.Normalized() : Vector3.Forward;
    }

    /// <summary>
    /// Get target position. Returns owner position if no valid target.
    /// </summary>
    public Vector3 GetTargetPosition()
    {
        if (_currentTarget == null || !IsInstanceValid(_currentTarget))
            return _ownerEntity?.GlobalPosition ?? Vector3.Zero;

        return _currentTarget.GlobalPosition;
    }
}
