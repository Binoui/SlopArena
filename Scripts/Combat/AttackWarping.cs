#nullable enable
using Godot;
using System;

/// <summary>
/// Range-based attack warping: auto-dash toward target before attacking.
/// Triggered when target is within WarpRange but outside AttackRange.
/// </summary>
public partial class AttackWarping : Node
{
    private Node3D? _owner;
    private TargetLockSystem? _targetLock;

    /// <summary>
    /// Warp state
    /// </summary>
    private bool _isWarping = false;
    private Vector3 _warpDirection;
    private float _warpSpeed;
    private float _warpDistanceRemaining;
    private float _warpTimeElapsed = 0f;
    /// <summary>
    /// Safety timeout
    /// </summary>
    private float _maxWarpDuration = 1.0f;
    private Action? _onWarpComplete;

    /// <summary>
    /// Cache final warp direction for hitbox spawning (survives target lock loss)
    /// </summary>
    private Vector3 _finalWarpDirection;

    public bool IsWarping => _isWarping;

    /// <summary>
    /// Get the direction the warp traveled. Valid after warp completes.
    /// Use this for hitbox spawning to ensure hitbox matches warp direction.
    /// </summary>
    public Vector3 GetFinalWarpDirection() => _finalWarpDirection;

    public void Setup(Node3D owner, TargetLockSystem targetLock)
    {
        _owner = owner;
        _targetLock = targetLock;
    }

    /// <summary>
    /// Start warping toward target.
    /// Called when attack input received and target in warp range.
    /// </summary>
    public void StartWarp(float attackRange, float warpSpeed, Action onComplete)
    {
        // Already warping → skip warp for this request, execute immediately
        if (_isWarping)
        {
            onComplete?.Invoke();
            return;
        }

        if (_targetLock?.CurrentTarget == null || _owner == null)
        {
            onComplete?.Invoke();  // No warp, execute immediately
            return;
        }

        float distToTarget = _targetLock.GetDistanceToTarget();

        // Already in attack range → no warp needed
        if (distToTarget <= attackRange)
        {
            onComplete?.Invoke();
            return;
        }

        // Start warp
        _isWarping = true;
        _warpDirection = _targetLock.GetDirectionToTarget();
        _warpSpeed = warpSpeed;
        _warpDistanceRemaining = distToTarget - attackRange;  // Stop at attack range
        _warpTimeElapsed = 0f;
        _onWarpComplete = onComplete;

        GD.Print($"[Warp] Starting warp: {_warpDistanceRemaining:F1}m at {_warpSpeed}m/s");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_isWarping || _owner == null)
            return;

        float dt = (float)delta;
        _warpTimeElapsed += dt;
        float moveDistance = _warpSpeed * dt;

        // Update direction dynamically (track moving targets)
        if (_targetLock?.CurrentTarget != null)
        {
            _warpDirection = _targetLock.GetDirectionToTarget();
        }

        // Movement is handled by WarpState — only track distance + timing here
        _warpDistanceRemaining -= moveDistance;

        // Check completion
        if (_warpDistanceRemaining <= 0f)
        {
            EndWarp();
        }

        // Safety: max warp duration (in case target moves away or is unreachable)
        if (_warpTimeElapsed > _maxWarpDuration)
        {
            GD.Print("[Warp] Safety timeout, ending warp");
            EndWarp();
        }
    }

    private void EndWarp()
    {
        _isWarping = false;
        _finalWarpDirection = _warpDirection;

        GD.Print("[Warp] Warp complete, executing attack");
        _onWarpComplete?.Invoke();
        _onWarpComplete = null;
    }

    /// <summary>
    /// Cancel active warp (e.g., player got hit during warp).
    /// </summary>
    public void CancelWarp()
    {
        if (!_isWarping)
            return;

        GD.Print("[Warp] Warp cancelled");
        _isWarping = false;

        if (_owner is CharacterBody3D body)
            body.Velocity = Vector3.Zero;

        _onWarpComplete = null;
    }
}
