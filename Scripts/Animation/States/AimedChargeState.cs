#nullable enable
using Godot;
using SlopArena.Shared;

/// <summary>
/// Aimed charge state — locks movement, shows a ground-projected cone indicator,
/// plays a looping charge animation, then fires the attack on release.
///
/// Modular: configured via AimedChargeData from the ability definition.
/// Reusable for any ability that needs a charge-aim-release pattern.
///
/// Flow:
///   Enter → charge loop anim + cone visible
///   RMB release → ExecuteSlot → C# FSM transitions to "attack"
///   Exit → hide cone
/// </summary>
public sealed partial class AimedChargeState : State
{
	private AimedChargeData _config;
	private int _slotIndex;
	private int _chargeTicks;
	private bool _attackFired;
	private MeshInstance3D? _coneMesh;
	private bool _airborne;

	public AimedChargeState()
	{
		AnimationName = "rmb_loop"; // default, overridden by Configure
		CanMove = false;
	}

	/// <summary>
	/// Configure with data from the ability definition.
	/// Must be called before this state is entered.
	/// </summary>
	public void Configure(AimedChargeData config, int slotIndex, bool airborne)
	{
		_config = config;
		_slotIndex = slotIndex;
		_airborne = airborne;
		AnimationName = config.ChargeAnimName;
	}

	public override void Enter()
	{
		_chargeTicks = 0;
		_attackFired = false;
		base.Enter(); // plays charge loop anim
		ShowCone();
	}

	public override void Exit()
	{
		base.Exit();
		HideCone();
	}

	public override void OnProcess(float delta)
	{
		if (_attackFired) return;

		_chargeTicks++;

		// Clamp charge ticks to MaxChargeTicks for scaling
		if (_config.MaxChargeTicks > 0 && _chargeTicks > _config.MaxChargeTicks)
			_chargeTicks = _config.MaxChargeTicks;

		// Detect RMB release → fire attack
		if (!Input.IsMouseButtonPressed(MouseButton.Right))
		{
			FireAttack();
		}
	}

	private void ShowCone()
	{
		if (_coneMesh != null) return;

		float halfAngle = Mathf.DegToRad(_config.ConeAngle) * 0.5f;
		float range = _config.ConeRange;
		int segments = 20;

		// Build cone mesh (flat on XZ plane, tip at origin)
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);

		// Tip vertex
		st.SetUV(new Vector2(0.5f, 1f));
		st.AddVertex(Vector3.Zero);

		// Arc vertices: fan around the tip
		for (int i = 0; i <= segments; i++)
		{
			float a = -halfAngle + (float)i / segments * _config.ConeAngle;
			float x = Mathf.Sin(a) * range;
			float z = Mathf.Cos(a) * range;
			st.SetUV(new Vector2((float)i / segments, 0f));
			st.AddVertex(new Vector3(x, 0f, z));
		}

		// Triangle fan: tip + arc segment pairs
		for (int i = 0; i < segments; i++)
		{
			st.AddIndex(0);     // tip
			st.AddIndex(i + 1); // arc point A
			st.AddIndex(i + 2); // arc point B
		}

		// Material (semi-transparent orange)
		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Color(1f, 0.5f, 0f, 0.25f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
		mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		mat.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled;

		// Create from surface tool
		var mesh = st.Commit();
		if (mesh != null)
			mesh.SurfaceSetMaterial(0, mat);

		_coneMesh = new MeshInstance3D();
		_coneMesh.Mesh = mesh;
		_coneMesh.Name = "ConeIndicator";

		// Position at player's feet on the ground, facing player direction
		Player.AddChild(_coneMesh);
		UpdateConePosition();
	}

	private void UpdateConePosition()
	{
		if (_coneMesh == null) return;

		// Follow player position at ground level
		Vector3 pos = Player.GlobalPosition;
		pos.Y = 0.05f; // slightly above ground to avoid z-fighting
		_coneMesh.GlobalPosition = pos;

		// Face player's facing direction (Y rotation)
		_coneMesh.GlobalRotation = new Vector3(0f, Player.GlobalRotation.Y, 0f);
	}

	private void HideCone()
	{
		if (_coneMesh == null) return;
		_coneMesh.QueueFree();
		_coneMesh = null;
	}

	private void FireAttack()
	{
		if (_attackFired) return;
		_attackFired = true;

		// Determine if fully charged
		bool fullyCharged = _config.MaxChargeTicks > 0 && _chargeTicks >= _config.MaxChargeTicks;

		// Execute the ability slot — this transitions the C# FSM to "attack"
		Player.ExecuteSlot(_slotIndex, fullyCharged, _airborne);
	}
}
