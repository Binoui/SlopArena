#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using SlopArena.Shared;

/// <summary>
/// Thin orchestrator: delegates movement to MovementComponent and animation to AnimationController.
/// Retains combat logic, NPC mode, input handling, and public API.
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
	// HP / HURTBOX
	// ==========================================

	private Hurtbox? _hurtbox;

	public float GetHP() => _movementComponent.State.HP;
	public float GetMaxHP() => _movementComponent.State.MaxHP;

	// ==========================================
	// COMBAT STATE (now stored in CharacterState via MovementComponent)
	// ==========================================

	private bool _heavyHeld = false;

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
	// CLICK TARGETING
	// ==========================================

	private Vector2 _leftClickPressPosition = Vector2.Zero;

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

		// Model + AnimationPlayer
		_playerModel = LoadPlayerModel();
		var animPlayer = new AnimationPlayer { Name = "AnimationPlayer" };
		(_playerModel ?? this).AddChild(animPlayer);

		var skeleton = _playerModel != null ? _animationController.FindSkeleton(_playerModel) : null;
		if (skeleton != null) animPlayer.RootNode = skeleton.GetPath();

		_animationController.Setup(animPlayer, skeleton, _playerModel);
		_animationController.Initialize();

		// Hurtbox
		_hurtbox = new Hurtbox { Name = "Hurtbox", OwnerEntity = this };
		var hurtboxSphere = new SphereShape3D { Radius = 2.0f };
		_hurtbox.AddChild(new CollisionShape3D { Shape = hurtboxSphere });
		AddChild(_hurtbox);

		_hurtbox.OnHit += (Vector3 attackerPos, float damage, Vector3 knockbackForce) =>
		{
			var s = _movementComponent.State;
			s.HP -= damage;
			if (s.HP <= 0f)
			{
				s.HP = s.MaxHP;
				Position = new Vector3(100f, 50f, 100f);
				Velocity = Vector3.Zero;
				s.VX = s.VY = s.VZ = 0f;
				s.KVX = s.KVY = s.KVZ = 0f;
			}
			_movementComponent.ApplyKnockback(knockbackForce.X, knockbackForce.Y, knockbackForce.Z);
		};

		if (_isPlayerControlled)
		{
			_wowCamera = new WowCamera { Name = "WowCamera", Target = this };
			AddChild(_wowCamera);
			Input.MouseMode = Input.MouseModeEnum.Captured;
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

		// NPCs don't read keyboard input — they have their own AI (or stand still)
		if (!_isPlayerControlled)
		{
			_movementComponent.Tick(default);
			OnStateUpdated?.Invoke(GlobalPosition.X, GlobalPosition.Z, GlobalPosition.Y, Velocity.X, Velocity.Z);
			return;
		}

		var input = BuildInputState();

		// Ground dash (air dodge handled by MovementComponent/Simulation internally)
		if (input.Dash && IsOnFloor() && _movementComponent.State.AnimLockTicks <= 0)
			_movementComponent.StartDash(_moveDirection.X, _moveDirection.Z);

		bool wasKnocked = _movementComponent.IsInKnockback();
		_movementComponent.Tick(input);

		// Tech roll on knockback landing
		if (wasKnocked && _movementComponent.IsGrounded && !_movementComponent.IsInKnockback() && Input.IsActionJustPressed("tech"))
			_movementComponent.DoTechRoll();

		// Face movement direction (Z is forward) — use CharacterState's facing
		Vector3 hVel = new Vector3(Velocity.X, 0f, Velocity.Z);
		if (hVel.LengthSquared() > 0.01f)
			GlobalRotation = new Vector3(0f, Mathf.Atan2(hVel.X, hVel.Z), 0f);

		// Animation
		_animationController.ProcessAnimation(dt, new AnimationController.AnimationState
		{
			IsOnFloor = _movementComponent.IsGrounded,
			HorizontalSpeed = hVel.Length(),
			IsDashing = _movementComponent.CurrentState == ActionState.Dashing,
			IsAirDodging = _movementComponent.CurrentState == ActionState.AirDodging,
			IsInKnockback = _movementComponent.IsInKnockback(),
			CastTimerRemaining = _animationController.GetCastTimer(),
		});

		OnStateUpdated?.Invoke(GlobalPosition.X, GlobalPosition.Z, GlobalPosition.Y, Velocity.X, Velocity.Z);
	}

	// ==========================================
	// INPUT STATE BUILDER
	// ==========================================

	private SlopArena.Shared.InputState BuildInputState()
	{
		var input = new SlopArena.Shared.InputState();

		// World-space movement: ZQSD = fixed directions, camera doesn't matter
		Vector3 cf = new Vector3(0f, 0f, 1f);  // Z = forward (+Z)
		Vector3 cr = new Vector3(1f, 0f, 0f);  // D = right (+X)

		_moveDirection = Vector3.Zero;
		if (Input.IsActionPressed("move_forward"))  _moveDirection += cf;
		if (Input.IsActionPressed("move_back"))     _moveDirection -= cf;
		if (Input.IsActionPressed("move_left"))     _moveDirection -= cr;
		if (Input.IsActionPressed("move_right"))    _moveDirection += cr;
		if (_moveDirection.LengthSquared() > 0f) _moveDirection = _moveDirection.Normalized();

		input.Up = _moveDirection.Z < -0.3f;
		input.Down = _moveDirection.Z > 0.3f;
		input.Left = _moveDirection.X < -0.3f;
		input.Right = _moveDirection.X > 0.3f;
		input.MoveX = _moveDirection.X;
		input.MoveY = _moveDirection.Z;
		input.Jump = Input.IsActionPressed("jump");
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
	/// All 6 slots use the same AbilityData struct with:
	///   1. Stage resolution (SpellResolver for melee/AoE/beam)
	///   2. Special effects after stages (status, delayed AoE, self-buff, projectile visuals)
	/// Slots 2-5 are no longer special — same system as LMB/RMB.
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

		// ── Step 2: Special effects (status, visuals, complex behavior) ──
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
	/// Handles combo tracking, lunge, anim locks, and hit application.
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
		Vector3 fwd = (-Transform.Basis.Z with { Y = 0 }).Normalized();
		Vector3 pos = GlobalPosition;

		// Lunge
		if (stage.LungeForce > 0f)
		{
			float upBoost = airborne && slotIndex == 1 ? -8f : Velocity.Y + 2f;
			Velocity = new Vector3(fwd.X * stage.LungeForce * 3f, upBoost, fwd.Z * stage.LungeForce * 3f);
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
				// Beam uses cone with narrow angle (hitscan approximation)
				results = SpellResolver.ResolveConeHit(
					pos.X, pos.Y, pos.Z,
					fwd.X, fwd.Z,
					3f * MathF.PI / 180f, // ~3 degrees half-angle
					stage.Range,
					stage.Damage, stage.KnockbackForce, stage.KnockbackUpward,
					pid, entities);
				break;

			case AttackShape.Projectile:
				// Projectile spawning is handled by the special effect (client-side visuals).
				// For immediate hit detection in the shared sim, use a cone check.
				// The special effect method creates the Godot projectile visual.
				results = SpellResolver.ResolveConeHit(
					pos.X, pos.Y, pos.Z,
					fwd.X, fwd.Z,
					15f * MathF.PI / 180f, // wide cone as projectile approximation
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

		// Apply hits — special effects can access via CombatComponent.GetTargetsFromLastHit()
		foreach (var hit in results)
		{
			float kbUp = (airborne && slotIndex == 1) ? -stage.KnockbackUpward : stage.KnockbackUpward;
			float kbMul = (airborne && slotIndex == 1) ? 0.5f : 1f;
			sim.OnEntityHit?.Invoke(hit.TargetEntityId, hit.Damage,
				hit.KnockbackX * kbMul,
				kbUp,
				hit.KnockbackZ * kbMul);
		}

		// Update CharacterState combo/animation ticks
		_movementComponent.SetComboState(newComboStage, stage.ChainWindowTicks, stage.SelfLockTicks);
	}

	// ==========================================
	// CLASS ABILITIES
	// ==========================================

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
		if (!ResourceLoader.Exists("res://assets/characters/Model/characterMedium.fbx"))
		{ GD.Print("Model not found, using fallback capsule"); CreateFallbackMesh(); return null; }

		var pm = GD.Load<PackedScene>("res://assets/characters/Model/characterMedium.fbx")?.Instantiate<Node3D>();
		if (pm == null) { CreateFallbackMesh(); return null; }

		pm.Name = "PlayerModel";
		AddChild(pm);
		ApplySkinRecursive(pm, GD.Load<Texture2D>("res://assets/characters/Skins/skaterMaleA.png"));
		pm.Scale = new Vector3(0.5f, 0.5f, 0.5f);
		pm.Position = new Vector3(0f, -1.5f, 0f);
		return pm;
	}

	private void ApplySkinRecursive(Node node, Texture2D? tex)
	{
		if (node is MeshInstance3D mi)
		{
			var mat = new StandardMaterial3D();
			if (tex != null) { mat.AlbedoTexture = tex; mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear; }
			else mat.AlbedoColor = new Color(0.7f, 0.7f, 0.7f);
			mi.MaterialOverride = mat;
			if (_isNPC && _firstMesh == null) { _firstMesh = mi; mat.EmissionEnabled = true; mat.Emission = new Color(0.8f, 0f, 0f); _npcOriginalEmission = mat.EmissionEnergyMultiplier; }
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
}
