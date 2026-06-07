using Godot;

/// <summary>
/// Camera mount — follows a target with orbital rotation.
/// Uses h/v node hierarchy for rotation (h = yaw, v SpringArm = pitch).
/// Attached to the player scene. Controls: mouse motion for orbit, scroll for zoom.
/// </summary>
public partial class CameraMount : Node3D
{
	[Export] public Node3D? Target;
	[Export] public float Sensitivity = 0.003f;
	[Export] public float MinZoom = 5.0f;
	[Export] public float MaxZoom = 80.0f;
	[Export] public float ZoomSpeed = 3.0f;

	private Node3D? _h;           // horizontal yaw node
	private SpringArm3D? _v;      // vertical pitch + zoom node
	private Camera3D? _camera;

	public override void _Ready()
	{
		_h = GetNodeOrNull<Node3D>("h");
		_v = GetNodeOrNull<SpringArm3D>("h/v");
		_camera = GetNodeOrNull<Camera3D>("h/v/Camera");

		if (_v != null)
		{
			_v.SpringLength = 60f;
			_v.Margin = 0.5f;
		}

		// Exclude player body from camera collision
		if (Target is CollisionObject3D col && _v != null)
			_v.AddExcludedObject(col.GetRid());

		// Initial pitch: -45° (looks downward)
		if (_v != null)
			_v.Rotation = new Vector3(Mathf.DegToRad(-45f), 0f, 0f);

		if (_camera != null)
			_camera.Current = true;
	}

	public override void _Process(double delta)
	{
		if (Target == null) return;

		// Follow target with eye-level offset
		Vector3 targetPos = Target.GlobalPosition;
		GlobalPosition = new Vector3(targetPos.X, targetPos.Y + 1.5f, targetPos.Z);

		// Yaw follows target facing by default, overridden by mouse
		if (_h != null)
			_h.Rotation = new Vector3(0f, _h.Rotation.Y, 0f);
	}

	/// <summary>
	/// Rotate camera orbit. Called by PlayerController on mouse move.
	/// </summary>
	public void RotateCamera(Vector2 relativeMotion)
	{
		if (_h == null || _v == null) return;

		// Yaw: rotate the h node around Y
		_h.Rotation -= new Vector3(0f, relativeMotion.X * Sensitivity, 0f);

		// Pitch: rotate the v (SpringArm) around X
		float pitch = _v.Rotation.X - relativeMotion.Y * Sensitivity;
		pitch = Mathf.Clamp(pitch, Mathf.DegToRad(-85f), Mathf.DegToRad(-5f));
		_v.Rotation = new Vector3(pitch, 0f, 0f);
	}

	/// <summary>
	/// Zoom in/out. Called by PlayerController on scroll wheel.
	/// </summary>
	public void ZoomCamera(float direction)
	{
		if (_v == null) return;
		_v.SpringLength = Mathf.Clamp(_v.SpringLength + direction * ZoomSpeed, MinZoom, MaxZoom);
	}

	public Vector3 GetForwardDirection()
	{
		Vector3 forward = -GlobalTransform.Basis.Z;
		forward.Y = 0;
		return forward.Normalized();
	}

	public Vector3 GetRightDirection()
	{
		Vector3 right = GlobalTransform.Basis.X;
		right.Y = 0;
		return right.Normalized();
	}

	public Camera3D? GetCamera() => _camera;
}
