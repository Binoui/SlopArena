#nullable enable
using Godot;

/// <summary>
/// Hit reaction state — plays small/medium/hard hit animation.
/// PlayerController sets HitAnimName before TransitionTo("hit_reaction").
/// Visual: white flash → hitstun gradient (damage%-based).
/// </summary>
public sealed partial class HitReactionState : State
{
    public string HitAnimName { get; set; } = "hit_small";

    private float _flashTimer = 0f;
    private const float FlashDuration = 0.2f;

    public HitReactionState()
    {
        AnimationName = "hit_small";
        CanMove = false;
    }

    public override void Enter()
    {
        AnimationName = HitAnimName;
        base.Enter();
        // Brief white flash on impact
        _flashTimer = FlashDuration;
        Player.SetModelEmission(new Color(1.5f, 1.5f, 1.5f), 2.5f); // Bright white
    }

    public override void Exit()
    {
        Player.ClearModelEmission();
        base.Exit();
    }

    public override void OnProcess(float delta)
    {
        // White flash → transition to hitstun gradient
        if (_flashTimer > 0f)
        {
            _flashTimer -= delta;
            if (_flashTimer <= 0f)
            {
                // Flash ended — switch to hitstun color
                Player.SetModelEmission(Player.GetHitstunColor());
            }
        }

        // Stay locked while animation plays (AnimLockTicks set by OnHit handler)
        if (Movement.State.AnimLockTicks > 0)
            return;

        if (Movement.IsGrounded)
            StateMachine.TransitionTo("idle");
        else
            StateMachine.TransitionTo("air");
    }
}
