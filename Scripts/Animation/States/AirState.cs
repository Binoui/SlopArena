#nullable enable
using Godot;

/// <summary>
/// Air state — drives the BlendSpace1D parameter for jump↔fall blend.
/// While rising: full jump animation.
/// After apex: blends toward fall with a slow timer (2s to reach full fall).
/// </summary>
public sealed partial class AirState : State
{
	/// <summary>
	/// |VY| at which fall blend = 100%
	/// </summary>
	private const float FallBlendSpeed = 20f;

	public AirState()
	{
		AnimationName = "air";
	}

	public override void Enter()
	{
		Player.SetModelEmission(new Color(0.5f, 0.8f, 1.0f)); // Light blue
		base.Enter();
		StateMachine.SetAnimParameter("parameters/air/blend_position", -1f);
	}

	public override void OnProcess(float delta)
	{
		float vy = Player.Velocity.Y;

		// Detect any jump (first from ground, or double jump mid-air)
		if (InputCtrl.JumpJustPressed)
		{
			StateMachine.SetAnimParameter("parameters/air/blend_position", -1f);
			return; // skip blend calc this frame, next frame will blend from -1
		}

		if (vy > 0f)
		{
			// Still rising — keep full jump animation
			StateMachine.SetAnimParameter("parameters/air/blend_position", -1f);
		}
		else
		{
			// Apex+falling — blend toward fall with a quadratic curve
			//   t = |VY| / FallBlendSpeed  (clamped to 1)
			//   blend = lerp(-1, +1, t²)
			//
			// Normal jump landing (VY≈-10): t=0.5, t²=0.25 → 25% fall
			// Short hop (VY≈-5):          t=0.25, t²=0.06 → 6% fall
			// Platform drop (VY≈-20):     t=1.0, t²=1.0  → 100% fall
			float t = Mathf.Clamp(Mathf.Abs(vy) / FallBlendSpeed, 0f, 1f);
			StateMachine.SetAnimParameter("parameters/air/blend_position", Mathf.Lerp(-1f, 1f, t * t));
		}

		// Transition to Landing when grounded
		if (Movement.IsGrounded && vy <= 0f)
		{
			StateMachine.TransitionTo("landing");
		}
	}
}
