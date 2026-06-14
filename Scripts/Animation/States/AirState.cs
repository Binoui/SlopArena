#nullable enable
using Godot;

/// <summary>
/// Air state — drives the BlendSpace1D parameter for jump/fall blend.
/// </summary>
public sealed partial class AirState : State
{
	private const float FallBlendSpeed = 20f;
	/// <summary>Consecutive grounded frames needed to transition out of air.</summary>
	private const int GroundedThreshold = 3;
	private int _groundedCount;

	public AirState()
	{
		AnimationName = "air";
	}

	public override void Enter()
	{
		_groundedCount = 0;
		Player.SetModelEmission(new Color(0.5f, 0.8f, 1.0f));
		base.Enter();
		StateMachine.SetAnimParameter("parameters/air/blend_position", -1f);
	}

	public override void OnProcess(float delta)
	{
		float vy = Player.Velocity.Y;

		// Jump input
		if (InputCtrl.JumpJustPressed)
		{
			StateMachine.SetAnimParameter("parameters/air/blend_position", -1f);
			return;
		}

		// Rising
		if (vy > 0f)
		{
			StateMachine.SetAnimParameter("parameters/air/blend_position", -1f);
		}
		else
		{
			float t = Mathf.Clamp(Mathf.Abs(vy) / FallBlendSpeed, 0f, 1f);
			StateMachine.SetAnimParameter("parameters/air/blend_position", Mathf.Lerp(-1f, 1f, t * t));
		}

		// Ground detection with threshold to prevent flicker
		if (Movement.IsGrounded)
		{
			_groundedCount++;
			if (_groundedCount >= GroundedThreshold)
			{
				Vector3 vel = Player.Velocity;
				bool moving = (vel.X * vel.X + vel.Z * vel.Z) > 1f;
				GD.Print($"[Air] Land tick={Engine.GetPhysicsFrames()} count={_groundedCount} -> {(moving ? "run" : "landing")}");
				StateMachine.TransitionTo(moving ? "run" : "landing");
			}
		}
		else
		{
			_groundedCount = 0;
		}
	}
}
