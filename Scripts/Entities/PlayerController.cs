#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using SlopArena.Shared;

/// <summary>
/// Thin orchestrator: delegates movement to MovementComponent and animation to AnimationController.
/// Retains combat logic, NPC mode, input handling, and public API.
///
/// Movement is camera-relative: Z/S moves forward/backward relative to camera facing,
/// Q/D strafes left/right. Input snaps to 8 directions.
/// Dash works on ground and in air (1s duration, invincibility).
/// Uses Smash-style % system: damage increases %, knockback scales with %.
/// </summary>
public partial class PlayerController : CharacterBody3D
{
	// ==========================================
	// CLASS / CHARACTER DEFINITION
	// ==========================================

	private CharacterDefinition _charDef;
	private CharacterClass _playerClass = CharacterClass.Vanguard;
	private ArenaDefinition _arenaDef;

	public void SetClass(CharacterClass pc)
	{
		_playerClass = pc;
		_charDef = CharacterRegistry.Get(pc);
	}

	public CharacterClass GetClass() => _playerClass;

	// ==========================================
	// HURTBOX / %
	// ==========================================

	private Hurtbox? _hurtbox;

	public ushort GetDamagePercent() => _movementComponent.DamagePercent;

	// ==========================================
	// COMPONENTS
	// ==========================================

	private MovementComponent _movementComponent = null!;
	private AnimationController _animationController = null!;
	private WowCamera? _wowCamera;
	private CombatComponent? _combatComponent;
	private MeshInstance3D? _firstMesh;
	private Vector3 _moveDirection = Vector3.Zero;
	private Node3D? _playerModel;
	private bool _heavyHeld = false;

	// Ground arrow indicator
	private MeshInstance3D? _groundArrow;
	private Vector2 _snappedInputDirection = Vector2.Zero; // camera-relative (X=camRight, Y=camForward)

	// ==========================================
	// NPC STATE
	// ==========================================

	private bool _isPlayerControlled = true;
	private bool _isNPC = false;
	private int _npcHP = 300;
	private const int NpcMaxHP = 300;
	private float _npcRespawnTimer = 0f;
	private const float NpcRespawnDelay = 3.0f;
	private Vector3 _npcSpawnPosition;
	private float _npcHitFlashTimer = 0f;
	private MeshInstance3D? _npcMesh;
	private float _npcOriginalEmission = 1.5f;

	public void SetNPC(bool isNpc)
	{
		_isNPC = isNpc;
		_isPlayerControlled = !isNpc;
		if (isNpc) _npcSpawnPosition = GlobalPosition;
	}

	public bool IsNPC() => _isNPC;
	public int GetNpcHP() => _npcHP;
	public bool IsNpcAlive() => _npcRespawnTimer <= 0f && _npcHP > 0;

	// ==========================================
	// UI STATE
	// ==========================================

	public bool IsSpellBookOpen { get; set; } = false;
	public bool IsEscapeMenuOpen { get; set; } = false;

	// ==========================================
	// EVENTS
	// ==========================================

	public event Action<float, float, float, float, float>? OnStateUpdated;
	public event Action? OnTargetNextPressed;
	public event Action<ulong>? OnLeftClickEntity;

	// ==========================================
	// PUBLIC GETTERS
	// ==========================================

	public float GetVelZ() => Velocity.Y;
	public CombatComponent? GetCombatComponent() => _combatComponent;
	public float GetDashCooldown() => _movementComponent.DashCooldownRemaining;

	public void SetupCombat(LocalSimulation simulation, ArenaDefinition arenaDef)
	{
		_arenaDef = arenaDef;
		_combatComponent = new CombatComponent();
		_combatComponent.Name = "CombatComponent";
		_combatComponent.Setup(this, simulation, 1);
		AddChild(_combatComponent);
	}

	// ==========================================
	// INITIALIZATION
	// ==========================================

	public override void _Ready()
	{
		_charDef = CharacterRegistry.Get(_playerClass);
		_arenaDef = ArenaRegistry.Get("pit");
		SetupInputMap();

		UpDirection = Vector3.Up;
		CollisionLayer = 1;
		CollisionMask = 1;
		FloorStopOnSlope = true;
		FloorMaxAngle = 45.0f;

		var capsule = new CapsuleShape3D { Radius = 1.5f, Height = 3f };
		AddChild(new CollisionShape3D { Shape = capsule });

		// Components
		_movementComponent = new MovementComponent(this);
		_movementComponent.Setup(_charDef, _arenaDef);

		_animationController = new AnimationController { Name = "AnimationController" };
		AddChild(_animationController);

		// Model + AnimationPlayer (KayKit MovementBasic rig — has mesh + working anims)
		_playerModel = LoadPlayerModel();
		AnimationPlayer? animPlayer = null;
		Skeleton3D? skeleton = null;

		if (_playerModel != null)
		{
			// Find the MovementBasic rig's AnimationPlayer and Skeleton (they work natively)
		    animPlayer = _animationController.FindAnimationPlayer(_playerModel);
		    skeleton = _animationController.FindSkeleton(_playerModel);

		    if (animPlayer != null)
		    {
		        animPlayer.Name = "AnimationPlayer";
		        if (skeleton != null)
		            animPlayer.RootNode = skeleton.GetPath();

				// Ensure there's a "default" library with normalized animation names
				var lib = animPlayer.HasAnimationLibrary("default")
					? animPlayer.GetAnimationLibrary("default")
					: new AnimationLibrary();

				// Copy all existing animations into default library with normalized names
				foreach (var libName in animPlayer.GetAnimationLibraryList())
				{
					if (libName == "default") continue;
					var sourceLib = animPlayer.GetAnimationLibrary(libName);
					if (sourceLib == null) continue;
					foreach (var animName in sourceLib.GetAnimationList())
					{
						var anim = sourceLib.GetAnimation(animName);
						if (anim == null) continue;
						string normalized = NormalizeKayKitAnimName(animName);
						if (!lib.HasAnimation(normalized))
							lib.AddAnimation(normalized, (Animation)anim.Duplicate());
					}
				}

				if (!animPlayer.HasAnimationLibrary("default"))
					animPlayer.AddAnimationLibrary("default", lib);
			}
		}

		if (animPlayer == null)
		{
			// Fallback: create our own AnimationPlayer
			animPlayer = new AnimationPlayer { Name = "AnimationPlayer" };
			(_playerModel ?? this).AddChild(animPlayer);
			if (skeleton != null) animPlayer.RootNode = skeleton.GetPath();
		}

		_animationController.Setup(animPlayer, skeleton, _playerModel);

		// Load General rig animations (idle, hit, death) and merge into our library
		LoadKayKitGeneralAnims(_animationController, animPlayer);

		_animationController.Initialize();

		// Hurtbox
		_hurtbox = new Hurtbox { Name = "Hurtbox", OwnerEntity = this };
		var hurtboxSphere = new SphereShape3D { Radius = 2.0f };
		_hurtbox.AddChild(new CollisionShape3D { Shape = hurtboxSphere });
		AddChild(_hurtbox);

		// Hit handler: Smash-style % system
		_hurtbox.OnHit += (Vector3 attackerPos, float damage, Vector3 knockbackForce) =>
		{
			// Can't be hit while invincible (dash)
			if (_movementComponent.IsInvincible) return;

			// Increase damage percentage
			_movementComponent.ApplyDamage(damage);

			// Scale knockback by damage% and apply
			_movementComponent.ApplyKnockback(knockbackForce.X, knockbackForce.Y, knockbackForce.Z);

			_animationController.CancelAttack();
		};

		// Ground arrow indicator
		_groundArrow = CreateGroundArrow();
		AddChild(_groundArrow);

		if (_isPlayerControlled)
		{
			_wowCamera = new WowCamera { Name = "WowCamera", Target = this };
			AddChild(_wowCamera);
			Input.MouseMode = Input.MouseModeEnum.Captured;
		}
	}

	// ==========================================
	// GROUND ARROW (indicates input direction)
	// ==========================================

	private MeshInstance3D CreateGroundArrow()
	{
		var arrow = new MeshInstance3D();
		arrow.Name = "GroundArrow";

		// Build a chevron/triangle pointing in +Z (forward)
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);

		// Arrow shape: triangle pointing forward (Z+)
		float size = 0.8f;
		float shaftLen = 0.5f;
		float shaftWid = 0.15f;

		// Tip
		st.AddVertex(new Vector3(0f, 0f, size));
		// Left
		st.AddVertex(new Vector3(-size * 0.5f, 0f, 0f));
		// Right
		st.AddVertex(new Vector3(size * 0.5f, 0f, 0f));

		st.GenerateNormals();
		arrow.Mesh = st.Commit();

		// Semi-transparent white material
		var mat = new StandardMaterial3D
		{
			AlbedoColor = new Color(1f, 1f, 1f, 0.6f),
			EmissionEnabled = true,
			Emission = new Color(0.8f, 0.8f, 1f),
			EmissionEnergyMultiplier = 1.5f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelDither,
			DistanceFadeMaxDistance = 50f,
		};
		arrow.MaterialOverride = mat;

		arrow.Visible = false;
		return arrow;
	}

	private void UpdateGroundArrow(bool hasInput)
	{
		if (_groundArrow == null) return;

		if (!hasInput || _isNPC)
		{
			_groundArrow.Visible = false;
			return;
		}

		_groundArrow.Visible = true;

		// Position at character's feet on the ground
		Vector3 pos = GlobalPosition;
		pos.Y = 0.05f;
		_groundArrow.Position = pos;

		// Rotate to face the input direction (camera-relative snapped direction)
		if (_snappedInputDirection.LengthSquared() > 0.001f)
		{
			float angle = MathF.Atan2(_snappedInputDirection.X, _snappedInputDirection.Y);
			_groundArrow.Rotation = new Vector3(0f, angle, 0f);
		}
	}

	// ==========================================
	// INPUT MAP SETUP
	// ==========================================

	private void SetupInputMap()
	{
		void Add(string n, InputEventKey k) { if (!InputMap.HasAction(n)) InputMap.AddAction(n); InputMap.ActionAddEvent(n, k); }
		Add("move_forward",  new InputEventKey { PhysicalKeycode = Key.Z });
		Add("move_forward",  new InputEventKey { PhysicalKeycode = Key.W });
		Add("move_back",     new InputEventKey { PhysicalKeycode = Key.S });
		Add("move_left",     new InputEventKey { PhysicalKeycode = Key.Q });
		Add("move_left",     new InputEventKey { PhysicalKeycode = Key.A });
		Add("move_right",    new InputEventKey { PhysicalKeycode = Key.D });
		Add("jump",          new InputEventKey { PhysicalKeycode = Key.Space });
		Add("dash",          new InputEventKey { PhysicalKeycode = Key.Shift });
		Add("crouch",        new InputEventKey { PhysicalKeycode = Key.C });
		Add("spell_slot1",   new InputEventKey { PhysicalKeycode = Key.Key1 });
		Add("spell_slot2",   new InputEventKey { PhysicalKeycode = Key.Key2 });
		Add("spell_slot3",   new InputEventKey { PhysicalKeycode = Key.Key3 });
		Add("spell_slot4",   new InputEventKey { PhysicalKeycode = Key.Key4 });
		Add("spell_slotA",   new InputEventKey { Keycode = Key.A });
		Add("spell_slotE",   new InputEventKey { Keycode = Key.E });
		Add("spell_slotR",   new InputEventKey { Keycode = Key.R });
		Add("spellbook_toggle", new InputEventKey { Keycode = Key.B });
		Add("ui_cancel",     new InputEventKey { Keycode = Key.Escape });
		Add("trinket",       new InputEventKey { Keycode = Key.G });
		Add("tech",          new InputEventKey { Keycode = Key.F });
		SettingsUI.LoadBindings();
	}

	// ==========================================
	// UNHANDLED INPUT
	// ==========================================

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!_isPlayerControlled) return;
		if (IsEscapeMenuOpen) { if (Input.IsActionJustPressed("ui_cancel")) Input.MouseMode = Input.MouseModeEnum.Visible; return; }

		if (@event is InputEventMouseButton mb)
		{
			if (mb.Pressed)
			{
				if (mb.ButtonIndex == MouseButton.WheelUp) { _wowCamera?.ZoomCamera(-1f); return; }
				if (mb.ButtonIndex == MouseButton.WheelDown) { _wowCamera?.ZoomCamera(1f); return; }
				if (Input.MouseMode != Input.MouseModeEnum.Captured) Input.MouseMode = Input.MouseModeEnum.Captured;
			}

			if (mb.ButtonIndex == MouseButton.Left && mb.Pressed && _combatComponent != null)
			{
				ExecuteSlot(0, false, !IsOnFloor());
				GetViewport().SetInputAsHandled(); return;
			}

			if (mb.ButtonIndex == MouseButton.Right && _combatComponent != null)
			{
				if (mb.Pressed) { _heavyHoldTimer = 0f; _heavyHeld = false; }
				else if (!_heavyHeld)
				{
					ExecuteSlot(1, false, !IsOnFloor());
					GetViewport().SetInputAsHandled(); return;
				}
			}
		}

		if (@event is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured)
			_wowCamera?.RotateCamera(mm.Relative);

		if (Input.IsActionJustPressed("spell_slot1")) ExecuteSlot(2, false, false);
		if (Input.IsActionJustPressed("spell_slotE")) ExecuteSlot(3, false, false);
		if (Input.IsActionJustPressed("spell_slotR")) ExecuteSlot(4, false, false);
		if (Input.IsActionJustPressed("spell_slot3")) ExecuteSlot(5, false, false);
		if (Input.IsActionJustPressed("ui_cancel")) Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	// ==========================================
	// PROCESS (timers, NPC)
	// ==========================================

	private float _trinketCooldownTimer = 0f;
	[Export] public float TrinketCooldown = 120f;
	[Export] public float TrinketPushSpeed = 18.0f;
	private float _heavyHoldTimer = 0f;

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		if (_trinketCooldownTimer > 0f) _trinketCooldownTimer -= dt;

		if (Input.IsMouseButtonPressed(MouseButton.Right) && _combatComponent != null)
		{
			_heavyHoldTimer += dt;
			if (_heavyHoldTimer > 0.3f && !_heavyHeld)
			{
				_heavyHeld = true;
				ExecuteSlot(1, true, !IsOnFloor());
			}
		}
		else
		{
			_heavyHoldTimer = 0f;
		}

		if (_isNPC)
		{
			if (_npcRespawnTimer > 0f) { _npcRespawnTimer -= dt; if (_npcRespawnTimer <= 0f) NpcRespawn(); }
			if (_npcHitFlashTimer > 0f)
			{
				_npcHitFlashTimer -= dt;
				if (_npcMesh?.MaterialOverride is StandardMaterial3D m) m.EmissionEnergyMultiplier = 8f;
			}
			else if (_npcMesh?.MaterialOverride is StandardMaterial3D m && !Mathf.IsEqualApprox(m.EmissionEnergyMultiplier, _npcOriginalEmission))
				m.EmissionEnergyMultiplier = _npcOriginalEmission;
			Visible = _npcRespawnTimer <= 0f;
		}
	}

	// ==========================================
	// PHYSICS PROCESS
	// ==========================================

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

		// NPCs don't read keyboard input
		if (!_isPlayerControlled)
		{
			_movementComponent.Tick(default);
			OnStateUpdated?.Invoke(GlobalPosition.X, GlobalPosition.Z, GlobalPosition.Y, Velocity.X, Velocity.Z);
			return;
		}

		var input = BuildInputState();

		// Dash (ground OR air)
		if (input.Dash && _movementComponent.State.AnimLockTicks <= 0)
		{
			_animationController.CancelAttack();
			_movementComponent.StartDash(_moveDirection.X, _moveDirection.Z);
		}

		bool wasKnocked = _movementComponent.IsInKnockback();
		_movementComponent.Tick(input);

		// Tech roll on knockback landing
		if (wasKnocked && _movementComponent.IsGrounded && !_movementComponent.IsInKnockback() && Input.IsActionJustPressed("tech"))
			_movementComponent.DoTechRoll();

		// Face movement direction
		Vector3 hVel = new Vector3(Velocity.X, 0f, Velocity.Z);
		if (hVel.LengthSquared() > 0.01f)
			GlobalRotation = new Vector3(0f, Mathf.Atan2(hVel.X, hVel.Z), 0f);

		// Update ground arrow
		UpdateGroundArrow(_snappedInputDirection.LengthSquared() > 0.001f);

		// Animation
		_animationController.ProcessAnimation(dt, new AnimationController.AnimationState
		{
			IsOnFloor = _movementComponent.IsGrounded,
			HorizontalSpeed = hVel.Length(),
			IsDashing = _movementComponent.CurrentState == ActionState.Dashing,
			IsAirDodging = _movementComponent.CurrentState == ActionState.AirDodging,
			IsInKnockback = _movementComponent.IsInKnockback(),
		});

		OnStateUpdated?.Invoke(GlobalPosition.X, GlobalPosition.Z, GlobalPosition.Y, Velocity.X, Velocity.Z);
	}

	// ==========================================
	// INPUT STATE BUILDER (camera-relative, 8-direction)
	// ==========================================

	private SlopArena.Shared.InputState BuildInputState()
	{
		var input = new SlopArena.Shared.InputState();

		// Get camera-relative forward/right (default to world if no camera)
		Vector3 camForward = Vector3.Forward;
		Vector3 camRight = Vector3.Right;
		if (_wowCamera != null)
		{
			// Z = direction où la caméra regarde = -Basis.Z = vers le centre de l'écran
			camForward = _wowCamera.GetForwardDirection();
			camRight = _wowCamera.GetRightDirection();
		}

		// Build raw camera-relative direction
		Vector3 rawDir = Vector3.Zero;
		if (Input.IsActionPressed("move_forward"))  rawDir += camForward;
		if (Input.IsActionPressed("move_back"))     rawDir -= camForward;
		if (Input.IsActionPressed("move_left"))     rawDir -= camRight;
		if (Input.IsActionPressed("move_right"))    rawDir += camRight;

		// 8-direction snap
		_moveDirection = Vector3.Zero;
		_snappedInputDirection = Vector2.Zero;

		if (rawDir.LengthSquared() > 0.001f)
		{
			// Get angle in camera-relative space (camRight=X, camForward=Z)
			// Convert to camera-relative 2D coordinates
			float rawForward = rawDir.Dot(camForward);
			float rawRight = rawDir.Dot(camRight);

			// Snap to 8 directions: compute angle and round to nearest 45°
			float angle = MathF.Atan2(rawRight, rawForward);
			const float snapStep = MathF.PI / 4f; // 45°
			float snappedAngle = MathF.Round(angle / snapStep) * snapStep;

			// Convert snapped angle back to camera-relative direction
			float fwd = MathF.Cos(snappedAngle);
			float rgt = MathF.Sin(snappedAngle);

			// Store in snapped direction (for ground arrow)
			_snappedInputDirection = new Vector2(rgt, fwd);

			// Convert from camera-relative to world space
			_moveDirection = camForward * fwd + camRight * rgt;
			_moveDirection = _moveDirection.Normalized();
		}

		input.MoveX = _moveDirection.X;
		input.MoveY = _moveDirection.Z;
		input.Up = _moveDirection.Z < -0.3f;      // forward in world +Z
		input.Down = _moveDirection.Z > 0.3f;     // backward
		input.Left = _moveDirection.X < -0.3f;    // left
		input.Right = _moveDirection.X > 0.3f;    // right
		input.Jump = Input.IsActionJustPressed("jump");
		input.Dash = Input.IsActionJustPressed("dash");
		input.Crouch = Input.IsActionPressed("crouch");
		input.Attack = Input.IsMouseButtonPressed(MouseButton.Left);
		return input;
	}

	// ==========================================
	// COMBAT: unified ability execution
	// ==========================================

	/// <summary>
	/// Execute any ability slot (0-5) uniformly.
	/// </summary>
	private void ExecuteSlot(int slotIndex, bool charged, bool airborne)
	{
		if (_combatComponent == null) return;
		if (_movementComponent.IsInKnockback()) return;
		if (_movementComponent.State.AnimLockTicks > 0) return;

		var ability = _charDef.GetSlotAbility(slotIndex);

		// Cooldown check
		ushort slotCd = slotIndex switch
		{
			0 => _movementComponent.State.Cooldown0,
			1 => _movementComponent.State.Cooldown1,
			2 => _movementComponent.State.Cooldown2,
			3 => _movementComponent.State.Cooldown3,
			4 => _movementComponent.State.Cooldown4,
			5 => _movementComponent.State.Cooldown5,
			_ => 0
		};
		if (slotCd > 0) return;
		var stages = charged && ability.ChargedStages != null ? ability.ChargedStages : ability.Stages;

		// ── Step 1: Resolve stages (hit detection) ──
		if (stages != null && stages.Length > 0)
		{
			ResolveAbilityStages(ability, stages, slotIndex, charged, airborne);
		}

		// Play attack animation
		int animStage = (stages != null && stages.Length > 0)
			? Math.Clamp(_movementComponent.State.ComboStage - 1, 0, stages.Length - 1)
			: 0;
		_animationController.PlayAttack(ability.AnimationNames, animStage, slotIndex);

		// ── Step 2: Special effects ──
		if (ability.SpecialEffectKeys != null)
		{
			foreach (var key in ability.SpecialEffectKeys)
			{
				AbilityRegistry.Execute(key, _combatComponent);
			}
		}

		// ── Step 3: Set cooldown ──
		if (ability.CooldownTicks > 0)
		{
			switch (slotIndex)
			{
				case 0: _movementComponent.State.Cooldown0 = ability.CooldownTicks; break;
				case 1: _movementComponent.State.Cooldown1 = ability.CooldownTicks; break;
				case 2: _movementComponent.State.Cooldown2 = ability.CooldownTicks; break;
				case 3: _movementComponent.State.Cooldown3 = ability.CooldownTicks; break;
				case 4: _movementComponent.State.Cooldown4 = ability.CooldownTicks; break;
				case 5: _movementComponent.State.Cooldown5 = ability.CooldownTicks; break;
			}
		}
	}

	/// <summary>
	/// Resolve attack stages against simulation entities via SpellResolver.
	/// </summary>
	private void ResolveAbilityStages(AbilityData ability, AttackStage[] stages, int slotIndex, bool charged, bool airborne)
	{
		var sim = _combatComponent?.GetSimulation();
		if (sim == null) return;
		ulong pid = _combatComponent.GetEntityId();

		// LMB combo stage tracking via CharacterState (ground only; air uses stage 0)
		int stageIndex;
		byte newComboStage = _movementComponent.State.ComboStage;
		if (slotIndex == 0 && !airborne)
		{
			if (_movementComponent.State.ComboStage == 0 || _movementComponent.State.ComboTimerTicks <= 0)
				newComboStage = 1;
			else if (_movementComponent.State.ComboStage < stages.Length)
				newComboStage++;
			else
				return;
			stageIndex = newComboStage - 1;
		}
		else
		{
			stageIndex = 0;
		}

		var stage = stages[stageIndex];
		// Use camera-relative input direction for lunge, fall back to character facing
		Vector3 fwd = (-Transform.Basis.Z with { Y = 0 }).Normalized();
		Vector3 lungeDir = _moveDirection.LengthSquared() > 0.001f ? _moveDirection : fwd;
		Vector3 pos = GlobalPosition;

		// Lunge (dans la direction de l'input caméra, pas le facing du perso)
		if (stage.LungeForce > 0f)
		{
			float upBoost = airborne && slotIndex == 1 ? -8f : Velocity.Y + 2f;
			Velocity = new Vector3(lungeDir.X * stage.LungeForce, upBoost, lungeDir.Z * stage.LungeForce);
		}

		// Build entity list for SpellResolver
		var entities = new System.Collections.Generic.List<SpellResolver.EntityData>();
		foreach (var kvp in sim.Entities)
		{
			entities.Add(new SpellResolver.EntityData
			{
				Id = kvp.Key,
				PosX = kvp.Value.pos.X,
				PosY = kvp.Value.pos.Y,
				PosZ = kvp.Value.pos.Z,
				Radius = kvp.Value.radius,
				Active = kvp.Value.active
			});
		}

		System.Collections.Generic.List<SpellResolver.HitResult> results;

		switch (stage.Shape)
		{
			case AttackShape.CircleAOE:
				results = SpellResolver.ResolveCircleHit(
					pos.X, pos.Y, pos.Z,
					stage.Radius,
					stage.Damage, stage.KnockbackForce, stage.KnockbackUpward,
					pid, entities);
				break;

			case AttackShape.MeleeCone:
				results = SpellResolver.ResolveConeHit(
					pos.X, pos.Y, pos.Z,
					fwd.X, fwd.Z,
					stage.HitAngleDeg * MathF.PI / 180f,
					stage.Range,
					stage.Damage, stage.KnockbackForce, stage.KnockbackUpward,
					pid, entities);
				break;

			case AttackShape.Beam:
				results = SpellResolver.ResolveConeHit(
					pos.X, pos.Y, pos.Z,
					fwd.X, fwd.Z,
					3f * MathF.PI / 180f,
					stage.Range,
					stage.Damage, stage.KnockbackForce, stage.KnockbackUpward,
					pid, entities);
				break;

			case AttackShape.Projectile:
				results = SpellResolver.ResolveConeHit(
					pos.X, pos.Y, pos.Z,
					fwd.X, fwd.Z,
					15f * MathF.PI / 180f,
					stage.Range,
					stage.Damage, stage.KnockbackForce, stage.KnockbackUpward,
					pid, entities);
				break;

			default:
				results = SpellResolver.ResolveCircleHit(
					pos.X, pos.Y, pos.Z,
					stage.Range,
					stage.Damage, stage.KnockbackForce, stage.KnockbackUpward,
					pid, entities);
				break;
		}

		// Apply hits — knockback is scaled by damage% in Simulation.ApplyKnockback
		foreach (var hit in results)
		{
			float kbUp = (airborne && slotIndex == 1) ? -stage.KnockbackUpward : stage.KnockbackUpward;
			float kbMul = (airborne && slotIndex == 1) ? 0.5f : 1f;

			// Don't hit invincible targets (dashing)
			ulong targetId = hit.TargetEntityId;
			bool targetInvincible = false;
			if (_movementComponent.State.InvincibilityTicks > 0 && targetId == _movementComponent.State.EntityId)
				targetInvincible = true;
			if (targetInvincible) continue;

			sim.OnEntityHit?.Invoke(targetId, hit.Damage,
				hit.KnockbackX * kbMul,
				kbUp,
				hit.KnockbackZ * kbMul);
		}

		// Update CharacterState combo/animation ticks
		_movementComponent.SetComboState(newComboStage, stage.ChainWindowTicks, stage.SelfLockTicks);
	}

	// ==========================================
	// KNOCKBACK
	// ==========================================

	public void ApplyKnockback(Vector3 force) => _movementComponent.ApplyKnockback(force.X, force.Y, force.Z);

	// ==========================================
	// CLICK → RAYCAST
	// ==========================================

	private void DoClickRaycast()
	{
		if (_wowCamera?.GetCamera() is not Camera3D cam) return;
		var from = cam.ProjectRayOrigin(_leftClickPressPosition);
		var query = new PhysicsRayQueryParameters3D { From = from, To = from + cam.ProjectRayNormal(_leftClickPressPosition) * 2000f, CollisionMask = 2 };
		var result = GetWorld3D().DirectSpaceState.IntersectRay(query);

		ulong id = 0;
		if (result.Count > 0)
		{
			Node? n = result["collider"].AsGodotObject() as Node;
			while (n != null && n is not CharacterBody3D) n = n.GetParent();
			if (n is CharacterBody3D cb && cb.Name.ToString().StartsWith("DummyBody_") && int.TryParse(cb.Name.ToString().AsSpan("DummyBody_".Length), out int idx))
				id = (ulong)(100 + idx);
		}
		OnLeftClickEntity?.Invoke(id);
	}

	// ==========================================
	// DIRECTION HELPERS
	// ==========================================

	public Vector3 GetPlayerForward() => (-Transform.Basis.Z with { Y = 0 }).Normalized();
	public Vector3 GetCameraForward() => _wowCamera?.GetForwardDirection() ?? GetPlayerForward();
	public CharacterDefinition GetCharacterDef() => _charDef;

	// ==========================================
	// MODEL LOADING
	// ==========================================

	private void CreateFallbackMesh()
	{
		var cm = new CapsuleMesh { Radius = 1.5f, Height = 3f };
		cm.SurfaceSetMaterial(0, new StandardMaterial3D { AlbedoColor = new Color(0f, 0.75f, 1f), EmissionEnabled = true, Emission = new Color(0f, 0.5f, 0.8f), EmissionEnergyMultiplier = 2f });
		AddChild(new MeshInstance3D { Mesh = cm });
	}

	private Node3D? LoadPlayerModel()
	{
		// Use MovementBasic as the base model (has mesh + skeleton + working movement anims)
		if (!ResourceLoader.Exists("res://assets/characters/kaykit/knight/Rig_Medium_MovementBasic.fbx"))
		{ GD.Print("KayKit rig not found, using fallback capsule"); CreateFallbackMesh(); return null; }

		var pm = GD.Load<PackedScene>("res://assets/characters/kaykit/knight/Rig_Medium_MovementBasic.fbx")?.Instantiate<Node3D>();
		if (pm == null) { CreateFallbackMesh(); return null; }

		pm.Name = "PlayerModel";
		AddChild(pm);

		// Use default FBX materials (they have correct UV setup)
		// Skins can be changed by swapping the FBX embedded texture or modifying materials
		GD.Print($"PlayerModel loaded, using default FBX materials");

		// Scale: KayKit models are ~1 unit tall, scale up to match game scale
		pm.Scale = new Vector3(2.0f, 2.0f, 2.0f);
		pm.Position = new Vector3(0f, -1.0f, 0f);

		// Attach 2-handed sword to handslot.r
		AttachWeaponToHand(pm);

		return pm;
	}

	/// <summary>
	/// Find the handslot.r bone in the skeleton and attach a 2-handed sword via BoneAttachment3D.
	/// </summary>
	private void AttachWeaponToHand(Node3D model)
	{
		var skeleton = _animationController?.FindSkeleton(model);
		if (skeleton == null) return;

		// Find the weapon hand bone
		string[] possibleBones = { "hand.r", "handslot.r", "hand_r", "Hand_Right", "weapon_bone" };
		int boneIdx = -1;
		string foundBone = "";
		for (int i = 0; i < skeleton.GetBoneCount(); i++)
		{
			string boneName = skeleton.GetBoneName(i);
			foreach (var candidate in possibleBones)
			{
				if (boneName == candidate)
				{
					boneIdx = i;
					foundBone = boneName;
					break;
				}
			}
			if (boneIdx >= 0) break;
		}

		if (boneIdx < 0)
		{
			GD.Print("KayKit: no weapon bone found, skipping sword attachment");
			return;
		}

		// Load the 2-handed sword
		if (!ResourceLoader.Exists("res://assets/characters/weapons/sword_2handed_color.fbx"))
		{
			// Try the non-colored variant
			if (!ResourceLoader.Exists("res://assets/characters/weapons/sword_2handed.fbx")) return;
		}

		var sword = GD.Load<PackedScene>("res://assets/characters/weapons/sword_2handed_color.fbx")?.Instantiate<Node3D>();
		if (sword == null) return;

		sword.Name = "Weapon_Sword2H";
		sword.Scale = new Vector3(2.0f, 2.0f, 2.0f);

		// Use BoneAttachment3D to follow the bone
		var attachment = new BoneAttachment3D();
		attachment.Name = "WeaponAttachment_" + foundBone;
		attachment.BoneName = foundBone;
		skeleton.AddChild(attachment);
		attachment.AddChild(sword);

		// Adjust sword position relative to the hand (fine-tune later)
		sword.Position = new Vector3(0f, 0f, 0f);
		sword.Rotation = new Vector3(0f, 0f, 0f);

		GD.Print($"KayKit: attached sword to bone '{foundBone}'");
	}

	/// <summary>
	/// Load general animations (idle, hit, death) from the KayKit General rig file.
	/// </summary>
	private void LoadKayKitGeneralAnims(AnimationController controller, AnimationPlayer? mainAnimPlayer)
	{
		if (mainAnimPlayer == null) return;

		string generalPath = "res://assets/characters/kaykit/knight/Rig_Medium_General.fbx";
		if (!ResourceLoader.Exists(generalPath)) return;

		var genScene = GD.Load<PackedScene>(generalPath)?.Instantiate<Node3D>();
		if (genScene == null) return;

		var genAnimPlayer = controller.FindAnimationPlayer(genScene);
		if (genAnimPlayer == null) { genScene.QueueFree(); return; }

		// Copy animations from General rig to main animation library
		var lib = mainAnimPlayer.GetAnimationLibrary("default");
		if (lib == null)
		{
			lib = new AnimationLibrary();
			mainAnimPlayer.AddAnimationLibrary("default", lib);
		}

		foreach (var libName in genAnimPlayer.GetAnimationLibraryList())
		{
			var sourceLib = genAnimPlayer.GetAnimationLibrary(libName);
			if (sourceLib == null) continue;

			foreach (var animName in sourceLib.GetAnimationList())
			{
				var anim = sourceLib.GetAnimation(animName);
				if (anim == null) continue;

				string key = NormalizeKayKitAnimName(animName);
				if (!lib.HasAnimation(key))
					lib.AddAnimation(key, (Animation)anim.Duplicate());
			}
		}

		genScene.QueueFree();
	}

	/// <summary>
	/// Normalize KayKit animation names to our internal naming.
	/// Idle_A → idle, Walking_A → walk, Running_A → run, etc.
	/// </summary>
	private string NormalizeKayKitAnimName(string name)
	{
		var map = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			{ "Idle_A", "idle" },
			{ "Idle_B", "idle" },
			{ "Walking_A", "walk" },
			{ "Walking_B", "walk" },
			{ "Walking_C", "walk" },
			{ "Running_A", "run" },
			{ "Running_B", "run" },
			{ "Jump_Full_Long", "jump" },
			{ "Jump_Full_Short", "jump" },
			{ "Jump_Idle", "jump_idle" },
			{ "Jump_Land", "land" },
			{ "Jump_Start", "jump_start" },
			{ "Hit_A", "hit_small_front" },
			{ "Hit_B", "hit_large_front" },
			{ "Death_A", "death_forward" },
			{ "Death_B", "death_backward" },
			{ "T-Pose", "tpose" },
		};

		if (map.TryGetValue(name, out string? mapped))
			return mapped;
		return name;
	}

	/// <summary>
	/// Strip scene root prefixes from KayKit animation bone paths.
	/// KayKit FBX imports with paths like "Rig_Medium/hips" but we need "hips".
	/// </summary>
	[Obsolete("No longer needed — animations come from the same rig they play on")]
	private Animation? RemapKayKitAnimation(Animation anim) { return anim; }

	private void ApplySkinRecursive(Node node, Texture2D? tex)
	{
		if (node is MeshInstance3D mi)
		{
			var mat = new StandardMaterial3D();
			mat.VertexColorUseAsAlbedo = false;
			mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear;
			if (tex != null) { mat.AlbedoTexture = tex; }
			else mat.AlbedoColor = new Color(0.7f, 0.7f, 0.7f);
			mi.MaterialOverride = mat;
			if (_isNPC && _firstMesh == null) { _firstMesh = mi; mat.EmissionEnabled = true; mat.Emission = new Color(0.8f, 0f, 0f); _npcOriginalEmission = mat.EmissionEnergyMultiplier; }
			GD.Print($"Skin: applied to {mi.Name} (tex={tex != null})");
		}
		foreach (var c in node.GetChildren()) ApplySkinRecursive(c, tex);
	}

	// ==========================================
	// NPC METHODS
	// ==========================================

	public void NpcTakeDamage(int damage, Vector3 knockbackForce)
	{
		if (_npcRespawnTimer > 0f) return;
		_npcHP -= damage;
		_npcHitFlashTimer = 0.3f;
		_npcMesh = _firstMesh;
		Velocity = knockbackForce;
		if (_npcHP <= 0) { _npcHP = 0; _npcRespawnTimer = NpcRespawnDelay; }
	}

	private void NpcRespawn()
	{
		_npcHP = NpcMaxHP;
		_npcRespawnTimer = 0f;
		GlobalPosition = _npcSpawnPosition;
		Velocity = Vector3.Zero;
	}

	public void SetNpcSpawnPosition(Vector3 pos) => _npcSpawnPosition = pos;

	// ==========================================
	// CLICK TARGETING STATE
	// ==========================================

	private Vector2 _leftClickPressPosition = Vector2.Zero;
}
