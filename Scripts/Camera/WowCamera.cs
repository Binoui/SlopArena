using Godot;
using System;

public partial class WowCamera : Node3D
{
	[Export] public Node3D? Target;
	[Export] public float Sensitivity = 0.003f;
	
	[Export] public float MinZoom = 5.0f;
	[Export] public float MaxZoom = 80.0f;
	[Export] public float ZoomSpeed = 3.0f;
	
	private SpringArm3D? _springArm;
	private Camera3D? _camera;
	
	// _targetYaw = player's facing direction (changed by right-click only)
	private float _targetYaw = 0.0f;
	// _cameraYaw = camera orbit yaw (changed by left-click OR right-click)
	// This allows the camera to orbit independently while the player faces a different direction.
	private float _cameraYaw = 0.0f;
	private float _targetPitch = 0.0f;
	
	public override void _Ready()
	{
		// Create the SpringArm3D
		_springArm = new SpringArm3D();
		_springArm.Name = "SpringArm";
		_springArm.SpringLength = 60f;
		_springArm.Margin = 0.5f;
		_springArm.CollisionMask = 0; // Disable SpringArm collision to prevent retracting into the player
		AddChild(_springArm);
		
		// Create camera, child of SpringArm
		_camera = new Camera3D();
		_camera.Name = "Camera";
		_camera.Current = true;
		_springArm.AddChild(_camera);
		
		if (Target != null && Target is CollisionObject3D collisionTarget)
		{
			_springArm.AddExcludedObject(collisionTarget.GetRid());
		}
		
		// Initial pitch: -45° (looks downward)
		_targetPitch = Mathf.DegToRad(-45.0f);
		
		if (Target != null)
		{
			_targetYaw = Target.GlobalRotation.Y;
			_cameraYaw = _targetYaw;
		}
		
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}
	
	public override void _Process(double delta)
	{
		if (Target == null || _springArm == null) return;
		
		// Suivi XZ strict avec hauteur des yeux
		Vector3 targetPos = Target.GlobalPosition;
		GlobalPosition = new Vector3(targetPos.X, targetPos.Y + 1.5f, targetPos.Z);
		
		// _cameraYaw is the desired GLOBAL Y rotation for the camera (orbit).
		// Local rotation = global - parent rotation.
		float localYaw = _cameraYaw - Target.GlobalRotation.Y;
		Rotation = new Vector3(0f, localYaw, 0f);
		_springArm.Rotation = new Vector3(_targetPitch, 0f, 0f);
	}
	
	/// <summary>
"Handles orbital camera rotation. Called by PlayerController."
	/// isClickingLeft : yaw and pitch change (camera orbits freely)
	/// isClickingRight : same + character rotates with camera
	/// </summary>
	public void RotateCamera(Vector2 relativeMotion, bool isClickingLeft, bool isClickingRight)
	{
		if (isClickingRight)
		{
			// Right click: camera AND character rotate
			_targetYaw -= relativeMotion.X * Sensibilite;
			_cameraYaw = _targetYaw;
			_targetPitch -= relativeMotion.Y * Sensibilite;
			
			// Limite verticale entre -85° (plongee) et -5° (ras du sol)
			_targetPitch = Mathf.Clamp(_targetPitch, Mathf.DegToRad(-85f), Mathf.DegToRad(-5f));
			
			// Character rotates with camera
			if (Target is CharacterBody3D player)
			{
				player.GlobalRotation = new Vector3(0f, _targetYaw, 0f);
			}
		}
		else if (isClickingLeft)
		{
			// Left click: only camera rotates (character stays)
			_cameraYaw -= relativeMotion.X * Sensibilite;
			_targetPitch -= relativeMotion.Y * Sensibilite;
			
			// Limite verticale entre -85° (plongee) et -5° (ras du sol)
			_targetPitch = Mathf.Clamp(_targetPitch, Mathf.DegToRad(-85f), Mathf.DegToRad(-5f));
		}
	}
	
	/// <summary>
"Handles SpringArm zoom. Called by PlayerController."
	/// </summary>
	public void ZoomCamera(float direction)
	{
		if (_springArm == null) return;
		_springArm.SpringLength = Mathf.Clamp(_springArm.SpringLength + (direction * ZoomSpeed), MinZoom, MaxZoom);
	}

	/// <summary>
	/// Returns the player's facing yaw (changed by right-click only).
	/// </summary>
	public float GetYaw()
	{
		return _targetYaw;
	}
	
	/// <summary>
	/// Returns the camera's orbit yaw (changed by left-click or right-click).
	/// </summary>
	public float GetCameraYaw()
	{
		return _cameraYaw;
	}
	
	public void SetYaw(float yaw)
	{
		_targetYaw = yaw;
		_cameraYaw = yaw;
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
