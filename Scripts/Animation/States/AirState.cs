#nullable enable
using Godot;

/// <summary>
/// Air state — handles airborne animation and blend.
/// Jump force is applied by the simulation (Simulation.cs ProcessGroundMovement).
/// This state only manages the BlendSpace1D parameter and landing transitions.
/// </summary>
public sealed partial class AirState : State
{
	public AirState()
	{
		AnimationName = "air";
	}

	public override void OnProcess(float delta)
	{
		// Drive the BlendSpace1D parameter: -1 = up (jump), 0 = apex, +1 = down (fall)
		float maxSpeed = Player.GetCharacterDef().Movement.JumpForce;
		float normalized = Mathf.Clamp(-Player.Velocity.Y / maxSpeed, -1f, 1f);
		StateMachine.SetAnimParameter("parameters/air/blend_position", normalized);

		// Transition to Landing when grounded
		if (Player.IsOnFloor() && Player.Velocity.Y <= 0f)
		{
			StateMachine.TransitionTo("landing");
		}
	}
}
