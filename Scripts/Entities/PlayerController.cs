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

	public void SetClass(CharacterClass pc)
	{
		_playerClass = pc;
		_charDef = CharacterRegistry.Get(pc);
	}

	public CharacterClass GetClass() => _playerClass;

	// ==========================================
	// HP / HURTBOX
	// ==========================================

	private float _hp = 100f;
	private const float MaxHP = 100f;
	private Hurtbox? _hurtbox;

	public float GetHP() => _hp;
	public float GetMaxHP() => MaxHP;

	// ==========================================
	// MELEE COMBO (LMB)
	// ==========================================

	private int _comboStage = 0;
	private float _comboTimer = 0f;
	private float _comboAnimLock = 0f;
	private float _heavyHoldTimer = 0f;
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

	public void SetupCombat(LocalSimulation simulation)
	{
		_combatComponent = new CombatComponent();
		_combatComponent.Name = "CombatComponent";
		_combatComponent.Setup(this, simulation, 1);
		AddChild(_combatComponent);
		_combatComponent.OnSpellCast += (slot) => _animationController.OnSpellCast(slot);
	}

	// ==========================================
	// INITIALIZATION
	// ==========================================

	public override void _Ready()
	{
		_charDef = CharacterRegistry.Get(_playerClass);
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
			_hp -= damage;
			_movementComponent.ApplyKnockback(knockbackForce);
			if (_hp <= 0) { _hp = MaxHP; Position = new Vector3(100f, 50f, 100f); Velocity = Vector3.Zero; }
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

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		if (_trinketCooldownTimer > 0f) _trinketCooldownTimer -= dt;

		if (_comboTimer > 0f) { _comboTimer -= dt; if (_comboTimer <= 0f) _comboStage = 0; }
		if (_comboAnimLock > 0f) _comboAnimLock -= dt;

		if (Input.IsMouseButtonPressed(MouseButton.Right) && _combatComponent != null)
		{
			_heavyHoldTimer += dt;
			if (_heavyHoldTimer > 0.3f && !_heavyHeld)
			{
				_heavyHeld = true;
				ExecuteSlot(1, true, !IsOnFloor());
			}
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
			_movementComponent.Tick(dt, _charDef, default);
			OnStateUpdated?.Invoke(GlobalPosition.X, GlobalPosition.Z, GlobalPosition.Y, Velocity.X, Velocity.Z);
			return;
		}

		var input = BuildInputState();

		// Ground dash (air dodge handled by MovementComponent internally)
		if (input.Dash && IsOnFloor() && _comboAnimLock <= 0f)
			_movementComponent.StartDash(_moveDirection, _charDef.Movement);

		bool wasKnocked = _movementComponent.IsInKnockback();
		_movementComponent.Tick(dt, _charDef, input);

		// Tech roll on knockback landing
		if (wasKnocked && _movementComponent.IsGrounded && !_movementComponent.IsInKnockback() && Input.IsActionJustPressed("tech"))
			_movementComponent.DoTechRoll();

		// Face movement direction (Z is forward)
		Vector3 hVel = new Vector3(Velocity.X, 0f, Velocity.Z);
		if (hVel.LengthSquared() > 0.01f)
			GlobalRotation = new Vector3(0f, Mathf.Atan2(hVel.X, hVel.Z), 0f);

		// Animation
		_animationController.ProcessAnimation(dt, new AnimationController.AnimationState
		{
			IsOnFloor = _movementComponent.IsGrounded,
			HorizontalSpeed = hVel.Length(),
			IsDashing = _movementComponent.CurrentState == MovementComponent.MoveState.Dashing,
			IsAirDodging = _movementComponent.CurrentState == MovementComponent.MoveState.AirDodging,
			IsInKnockback = _movementComponent.IsInKnockback(),
			CastTimerRemaining = _animationController.GetCastTimer(),
		});

		OnStateUpdated?.Invoke(GlobalPosition.X, GlobalPosition.Z, GlobalPosition.Y, Velocity.X, Velocity.Z);
	}

	// ==========================================
	// INPUT STATE BUILDER
	// ==========================================

	private InputState BuildInputState()
	{
		var input = new InputState();

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
	/// Execute an ability slot (0-5). Slots 0-1 (LMB/RMB) use AttackStage data.
	/// Slots 2-5 (Q/E/R/F) delegate to AbilityRegistry.
	/// </summary>
	private void ExecuteSlot(int slotIndex, bool charged, bool airborne)
	{
		if (_combatComponent == null) return;
		if (_movementComponent.IsInKnockback() || _comboAnimLock > 0f) return;

		// Slots 2-5: class abilities (delegate to registry)
		if (slotIndex >= 2)
		{
			int keyIndex = slotIndex - 2;
			if (keyIndex < _charDef.ClassAbilityKeys.Length)
				AbilityRegistry.Execute(_charDef.ClassAbilityKeys[keyIndex], _combatComponent);
			return;
		}

		// Slots 0-1: LMB / RMB — read from CharacterDefinition
		var ability = slotIndex == 0 ? _charDef.LMB : _charDef.RMB;
		var stages = charged && ability.ChargedStages != null ? ability.ChargedStages : ability.Stages;
		if (stages == null || stages.Length == 0) return;

		// LMB combo stage tracking (ground only; air uses stage 0)
		int stageIndex;
		if (slotIndex == 0 && !airborne)
		{
			if (_comboStage == 0 || _comboTimer <= 0f) _comboStage = 1;
			else if (_comboStage < stages.Length) _comboStage++;
			else { _comboStage = 0; return; }
			stageIndex = _comboStage - 1;
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

		// Hit detection — direct against simulation entities
		var sim = _combatComponent.GetSimulation();
		if (sim == null) return;
		ulong pid = _combatComponent.GetEntityId();

		foreach (var kvp in sim.Entities)
		{
			ulong eid = kvp.Key; var (ep, er, ea) = kvp.Value;
			if (!ea || eid == pid) continue;

			float dx = ep.X - pos.X, dz = ep.Z - pos.Z, dist = MathF.Sqrt(dx * dx + dz * dz);

			switch (stage.Shape)
			{
				case AttackShape.CircleAOE:
					if (dist > stage.Radius + er) continue;
					break;
				case AttackShape.MeleeCone:
					if (dist > stage.Range + er) continue;
					float a = MathF.Atan2(fwd.Z, fwd.X);
					Vector3 tt = new Vector3(dx, 0, dz).Normalized();
					float ta = MathF.Atan2(tt.Z, tt.X);
					if (MathF.Abs(Mathf.AngleDifference(a, ta)) > stage.HitAngleDeg * Mathf.Pi / 180f) continue;
					break;
				default:
					if (dist > stage.Range + er) continue;
					break;
			}

			// Airborne RMB: downward spike
			float kbUp = (airborne && slotIndex == 1) ? -stage.KnockbackUpward : stage.KnockbackUpward;
			float kbMul = (airborne && slotIndex == 1) ? 0.5f : 1f;
			sim.OnEntityHit?.Invoke(eid, stage.Damage,
				fwd.X * stage.KnockbackForce * kbMul,
				kbUp,
				fwd.Z * stage.KnockbackForce * kbMul);
		}

		_comboAnimLock = stage.SelfLockTicks / 60f;
		if (slotIndex == 0 && !airborne)
			_comboTimer = stage.ChainWindowTicks / 60f;
	}

	// ==========================================
	// CLASS ABILITIES
	// ==========================================

	// ==========================================
	// KNOCKBACK
	// ==========================================

	public void ApplyKnockback(Vector3 force) => _movementComponent.ApplyKnockback(force);

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
