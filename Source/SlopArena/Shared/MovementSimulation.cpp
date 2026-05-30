// Copyright SlopArena Contributors. MIT License.

#include "MovementSimulation.h"
#include "CombatMath.h"
#include "Math/UnrealMathUtility.h"
#include <cmath>

void FMovementSimulation::SimulateStep(
	FCharacterSimState& State,
	const FCharacterInputState& Input,
	const FMovementProfile& Profile,
	float DeltaTime,
	float GroundHeight,
	float Radius)
{
	// Parse input flags
	const bool bInputUp     = (Input.MovementFlags & 0x01) != 0;
	const bool bInputLeft   = (Input.MovementFlags & 0x02) != 0;
	const bool bInputDown   = (Input.MovementFlags & 0x04) != 0;
	const bool bInputRight  = (Input.MovementFlags & 0x08) != 0;
	const bool bInputJump   = (Input.MovementFlags & 0x10) != 0;
	const bool bInputDash   = (Input.MovementFlags & 0x20) != 0;
	const bool bInputCrouch = (Input.MovementFlags & 0x80) != 0;
	const bool bInputAttack = (Input.ActionFlags & 0x01) != 0;
	const bool bInputRespawn= (Input.MovementFlags & 0x40) != 0;

	// Respawn
	if (bInputRespawn)
	{
		State.Position = FVector(2500.0f, 2500.0f, GroundHeight + 10.0f);
		State.Velocity = FVector::ZeroVector;
		State.ActionState = EActionState::Idle;
		State.StateTicksRemaining = 0;
		State.DashCooldownTicks = 0;
		State.CombatLockoutTicks = 0;
		State.bSlideMomentumActive = false;
		return;
	}

	if (State.CombatLockoutTicks > 0) State.CombatLockoutTicks--;

	float Speed2D = FMath::Sqrt(State.Velocity.X * State.Velocity.X + State.Velocity.Y * State.Velocity.Y);

	// Resolve movement input (direction-relative)
	FVector2D MoveDir(0.0f);
	float MoveMaxSpeed = Profile.WalkSpeed;
	ResolveMovementInput(Input, Input.Yaw, MoveDir, MoveMaxSpeed);

	if (State.DashCooldownTicks > 0) State.DashCooldownTicks--;

	const float EpsilonZ = 0.1f;
	float GroundZ = GroundHeight;

	// Grounded check (simple heightmap version)
	const bool bOnGround = State.Position.Z <= GroundZ + EpsilonZ;
	State.bIsGrounded = bOnGround;

	const float Dt = DeltaTime;

	// =========== STATE MACHINE ===========

	if (State.ActionState == EActionState::Hitstun)
	{
		// Knockback decay
		const float KnockbackDecay = 10.0f;
		State.Velocity.X -= State.Velocity.X * KnockbackDecay * Dt;
		State.Velocity.Y -= State.Velocity.Y * KnockbackDecay * Dt;
		State.Velocity.Z -= State.Velocity.Z * KnockbackDecay * Dt;

		// Directional Influence (DI)
		if (bInputUp || bInputDown || bInputLeft || bInputRight)
		{
			float DiX = 0.0f, DiY = 0.0f;
			if (bInputUp)    DiY -= 1.0f;
			if (bInputDown)  DiY += 1.0f;
			if (bInputLeft)  DiX -= 1.0f;
			if (bInputRight) DiX += 1.0f;

			float DiLen = FMath::Sqrt(DiX * DiX + DiY * DiY);
			if (DiLen > 0.0f)
			{
				DiX /= DiLen; DiY /= DiLen;
				float CurrentSpeed = FMath::Sqrt(State.Velocity.X * State.Velocity.X + State.Velocity.Y * State.Velocity.Y);
				float DiForce = CurrentSpeed * 0.15f + 50.0f;
				State.Velocity.X += DiX * DiForce * Dt;
				State.Velocity.Y += DiY * DiForce * Dt;
			}
		}

		if (State.StateTicksRemaining > 0) State.StateTicksRemaining--;
		if (State.StateTicksRemaining == 0) State.ActionState = EActionState::Idle;
	}
	else if (State.ActionState == EActionState::Attacking)
	{
		State.Velocity.X *= 0.94f;
		State.Velocity.Y *= 0.94f;
		if (State.StateTicksRemaining > 0) State.StateTicksRemaining--;
		if (State.StateTicksRemaining == 0)
		{
			State.ActionState = EActionState::Idle;
			State.CombatLockoutTicks = Profile.PostAttackSlideLockoutTicks;
			State.bSlideMomentumActive = false;
		}
	}
	else if (State.ActionState == EActionState::Dashing)
	{
		if (bInputAttack)
		{
			State.ActionState = EActionState::Attacking;
			State.StateTicksRemaining = Profile.AttackDurationTicks;
			State.bSlideMomentumActive = false;
		}
		else if (bInputCrouch && bOnGround)
		{
			BeginSlide(State.ActionState, State.StateTicksRemaining, State.bSlideMomentumActive,
				CanMomentumSlide(Profile, State.CombatLockoutTicks, Speed2D));
		}
		else
		{
			float DashSpeed = Profile.DashSpeed;
			if (MoveDir.Length() > 0.1f)
			{
				State.Velocity.X = MoveDir.X * DashSpeed;
				State.Velocity.Y = MoveDir.Y * DashSpeed;
			}
			State.Velocity.Z = 0.0f;
			if (bOnGround) State.Position.Z = GroundZ;

			if (State.StateTicksRemaining > 0) State.StateTicksRemaining--;
			if (State.StateTicksRemaining == 0) State.ActionState = EActionState::Idle;
		}
	}
	else if (State.ActionState == EActionState::Sliding)
	{
		if (bInputAttack)
		{
			State.ActionState = EActionState::Attacking;
			State.StateTicksRemaining = Profile.AttackDurationTicks;
			State.bSlideMomentumActive = false;
		}
		else if (bInputDash && State.DashCooldownTicks == 0 && MoveDir.Length() > 0.1f)
		{
			State.ActionState = EActionState::Dashing;
			State.StateTicksRemaining = Profile.DashDurationTicks;
			State.DashCooldownTicks = Profile.DashCooldownTicks;
			State.Velocity.X = MoveDir.X * Profile.DashSpeed;
			State.Velocity.Y = MoveDir.Y * Profile.DashSpeed;
			State.Velocity.Z = 0.0f;
			State.bSlideMomentumActive = false;
		}
		else
		{
			float Drag = State.bSlideMomentumActive ? Profile.SlideMomentumDrag : Profile.SlideNormalDrag;
			State.Velocity.X -= State.Velocity.X * Drag * Dt;
			State.Velocity.Y -= State.Velocity.Y * Drag * Dt;
			ClampSlideSpeed(State.Velocity, Profile, State.bSlideMomentumActive);

			if (MoveDir.Length() > 0.1f)
			{
				float Steer = State.bSlideMomentumActive ? 220.0f : 300.0f;
				State.Velocity.X += MoveDir.X * Steer * Dt;
				State.Velocity.Y += MoveDir.Y * Steer * Dt;
				ClampSlideSpeed(State.Velocity, Profile, State.bSlideMomentumActive);
			}

			if (bInputJump && bOnGround)
			{
				State.Velocity.Z = Profile.JumpForce;
				State.bIsGrounded = false;
				State.ActionState = EActionState::Idle;
				State.bSlideMomentumActive = false;
			}
			else if (!bInputCrouch)
			{
				Speed2D = FMath::Sqrt(State.Velocity.X * State.Velocity.X + State.Velocity.Y * State.Velocity.Y);
				if (!State.bSlideMomentumActive || Speed2D < Profile.SlideMomentumMinSpeed * 0.45f)
				{
					State.ActionState = EActionState::Idle;
					State.bSlideMomentumActive = false;
				}
			}
			else if (!State.bSlideMomentumActive && Speed2D < 90.0f)
			{
				State.ActionState = EActionState::Idle;
			}
		}
	}
	else if (State.ActionState == EActionState::AirDodging)
	{
		State.Velocity.X *= 0.94f;
		State.Velocity.Y *= 0.94f;
		if (State.StateTicksRemaining > 0) State.StateTicksRemaining--;
		if (State.StateTicksRemaining == 0) State.ActionState = EActionState::Idle;
	}
	else // Idle / Jogging
	{
		if (bInputAttack && bOnGround)
		{
			State.ActionState = EActionState::Attacking;
			State.StateTicksRemaining = Profile.AttackDurationTicks;
			State.bSlideMomentumActive = false;
		}
		else if (bInputDash && State.DashCooldownTicks == 0 && MoveDir.Length() > 0.1f)
		{
			State.ActionState = EActionState::Dashing;
			State.StateTicksRemaining = Profile.DashDurationTicks;
			State.DashCooldownTicks = Profile.DashCooldownTicks;
			State.Velocity.X = MoveDir.X * Profile.DashSpeed;
			State.Velocity.Y = MoveDir.Y * Profile.DashSpeed;
			State.Velocity.Z = 0.0f;
		}
		else
		{
			// Jogging physics
			float Accel = bOnGround ? Profile.Acceleration : Profile.AirAcceleration;
			float Drag = bOnGround
				? ((MoveDir.Length() < 0.1f) ? Profile.DragWhenStopped : Profile.DragWhenMoving)
				: 0.5f;

			State.Velocity.X -= State.Velocity.X * Drag * Dt;
			State.Velocity.Y -= State.Velocity.Y * Drag * Dt;

			if (MoveDir.Length() > 0.1f)
			{
				float ProjSpeed = State.Velocity.X * MoveDir.X + State.Velocity.Y * MoveDir.Y;
				if (ProjSpeed < MoveMaxSpeed)
				{
					float AddedSpeed = Accel * Dt;
					if (ProjSpeed + AddedSpeed > MoveMaxSpeed)
						AddedSpeed = MoveMaxSpeed - ProjSpeed;
					State.Velocity.X += MoveDir.X * AddedSpeed;
					State.Velocity.Y += MoveDir.Y * AddedSpeed;
				}
			}

			// Jump
			if (bInputJump && bOnGround)
			{
				State.Velocity.Z = Profile.JumpForce;
				State.bIsGrounded = false;
			}

			// Air dodge
			if (!bOnGround && bInputCrouch)
			{
				const uint8 AirDodgeDurationTicks = 6;
				State.ActionState = EActionState::AirDodging;
				State.StateTicksRemaining = AirDodgeDurationTicks;
				if (MoveDir.Length() > 0.1f)
				{
					State.Velocity.X = MoveDir.X * Profile.DashSpeed;
					State.Velocity.Y = MoveDir.Y * Profile.DashSpeed;
				}
				else
				{
					State.Velocity.Y = -Profile.DashSpeed * 0.5f;
				}
				State.Velocity.Z = 0.0f;
			}

			// Slide from idle
			if (bOnGround && bInputCrouch)
			{
				Speed2D = FMath::Sqrt(State.Velocity.X * State.Velocity.X + State.Velocity.Y * State.Velocity.Y);
				if (Speed2D < 100.0f && MoveDir.Length() > 0.1f && !CanMomentumSlide(Profile, State.CombatLockoutTicks, Speed2D))
				{
					State.Velocity.X = MoveDir.X * 300.0f;
					State.Velocity.Y = MoveDir.Y * 300.0f;
				}
				BeginSlide(State.ActionState, State.StateTicksRemaining, State.bSlideMomentumActive,
					CanMomentumSlide(Profile, State.CombatLockoutTicks, Speed2D));
			}
		}
	}

	// Gravity
	if (!bOnGround && State.ActionState != EActionState::Dashing && State.ActionState != EActionState::Attacking)
	{
		float GravMult = (bInputDown || bInputCrouch) ? 1.5f : 1.0f;
		if (State.ActionState == EActionState::AirDodging)
			GravMult = 1.0f;
		State.Velocity.Z -= Profile.Gravity * GravMult * Dt;
	}

	// Position update with ground collision
	FVector NewPos;
	NewPos.X = State.Position.X + State.Velocity.X * Dt;
	NewPos.Y = State.Position.Y + State.Velocity.Y * Dt;
	float NextZ = State.Position.Z + State.Velocity.Z * Dt;

	// Ground snap: check new Z, not old Z (otherwise jump is cancelled)
	if (NextZ <= GroundZ + EpsilonZ && !(State.Velocity.Z > 0.0f))
	{
		NewPos.Z = GroundZ;
		State.Velocity.Z = 0.0f;
		State.bIsGrounded = true;
	}
	else
	{
		NewPos.Z = NextZ;
		if (NewPos.Z <= GroundZ)
		{
			NewPos.Z = GroundZ;
			State.Velocity.Z = 0.0f;
			State.bIsGrounded = true;
		}
	}

	State.Position = NewPos;
	State.GroundHeight = GroundZ;
}

void FMovementSimulation::ResolveMovementInput(
	const FCharacterInputState& Input,
	float Yaw,
	FVector2D& OutDirection,
	float& OutMaxSpeed)
{
	bool bUp    = (Input.MovementFlags & 0x01) != 0;
	bool bLeft  = (Input.MovementFlags & 0x02) != 0;
	bool bDown  = (Input.MovementFlags & 0x04) != 0;
	bool bRight = (Input.MovementFlags & 0x08) != 0;

	FVector2D MoveInput(0.0f);
	if (bUp)    MoveInput.Y -= 1.0f;
	if (bDown)  MoveInput.Y += 1.0f;
	if (bLeft)  MoveInput.X -= 1.0f;
	if (bRight) MoveInput.X += 1.0f;

	// Normalize
	float Len = MoveInput.Length();
	if (Len > 1.0f)
		MoveInput /= Len;

	// Rotate by yaw (camera-relative movement)
	float CosYaw = FMath::Cos(Yaw);
	float SinYaw = FMath::Sin(Yaw);
	OutDirection.X = MoveInput.X * CosYaw - MoveInput.Y * SinYaw;
	OutDirection.Y = MoveInput.X * SinYaw + MoveInput.Y * CosYaw;

	OutMaxSpeed = 600.0f; // WalkSpeed default
}

void FMovementSimulation::BeginSlide(EActionState& ActionState, uint8& StateTicksRemaining, bool& bSlideMomentum, bool CanMomentum)
{
	ActionState = EActionState::Sliding;
	StateTicksRemaining = 0;
	bSlideMomentum = CanMomentum;
}

bool FMovementSimulation::CanMomentumSlide(const FMovementProfile& Profile, uint8 CombatLockout, float Speed2D)
{
	if (CombatLockout > 0) return false;
	return Speed2D >= Profile.SlideMomentumMinSpeed;
}

void FMovementSimulation::ClampSlideSpeed(FVector& Velocity, const FMovementProfile& Profile, bool bMomentum)
{
	float MaxSpeed = bMomentum ? 1200.0f : 900.0f;
	float Speed2D = FMath::Sqrt(Velocity.X * Velocity.X + Velocity.Y * Velocity.Y);
	if (Speed2D > MaxSpeed)
	{
		float Scale = MaxSpeed / Speed2D;
		Velocity.X *= Scale;
		Velocity.Y *= Scale;
	}
}
