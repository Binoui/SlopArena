#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared;

/// <summary>
/// Action-MMO movement controller for Godot 4 C#.
/// - WoW-like camera managed by WowCamera (SpringArm3D)
/// - ZQSD/WASD relative to camera direction
/// - Left click: orbit camera
/// - Right click: rotate camera + character
/// - Realistic physics: meter-scaled, conserved jump momentum
/// </summary>
public partial class PlayerController : CharacterBody3D
{
	// ==========================================
	// EXPORTED VARIABLES (realistic scales)
	// ==========================================
	
	// ==========================================
	// GROUND MOVEMENT (acceleration-based)
	// ==========================================
	
	[Export] public float WalkSpeed = 10.0f;
	[Export] public float SprintSpeed = 14.0f;       // Faster but committed
	
	// ==========================================
	// SPRINT / DASH DANCE
	// ==========================================
	
	[Export] public float SprintThreshold = 0.2f;    // Hold direction this long to enter sprint
	[Export] public float TurnaroundLag = 0.1f;      // Lag when changing direction during sprint
	[Export] public float GroundFriction = 18.0f;    // Deceleration when no input
	
	// ==========================================
	// AIR MOVEMENT
	// ==========================================
	
	[Export] public float JumpVelocity = 16.0f;
	[Export] public float Gravity = 40.0f;
	[Export] public float FastFallKick = 25.0f;      // Instant Y velocity on fast fall
	[Export] public float FastFallGravityMult = 2.0f;
	[Export] public float MaxFallSpeed = 50.0f;
	[Export] public float AirAcceleration = 14.0f;
	[Export] public float AirDrag = 0.2f;            // Minimal drag in air
	
	// ==========================================
	// DASH (ground)
	// ==========================================
	
	[Export] public float DashSpeed = 30.0f;
	[Export] public float DashDuration = 0.18f;
	[Export] public float DashCooldown = 1.0f;
	
	// ==========================================
	// AIR DODGE (replaces roll)
	// ==========================================
	
	[Export] public float AirDodgeSpeed = 35.0f;     // Fast burst
	[Export] public float AirDodgeDuration = 0.12f;
	[Export] public int MaxAirDodges = 1;             // Reset on ground
	
	// ==========================================
	// KNOCKBACK
	// ==========================================
	
	// ==========================================
	// HP / HURTBOX
	// ==========================================
	
	private float _hp = 100f;
	private const float MaxHP = 100f;
	private Hurtbox? _hurtbox;
	
	// ==========================================
	// DASH / ROLL STATE
	// ==========================================
	
	private enum MoveState { Normal, Dashing, AirDodging }
	private MoveState _moveState = MoveState.Normal;
	
	private float _dashTimer = 0f;
	private float _dashCooldownTimer = 0f;
	private Vector3 _dashDirection = Vector3.Zero;
	
	private float _airDodgeTimer = 0f;
	private Vector3 _airDodgeDirection = Vector3.Zero;
	
	private int _airDodgesLeft = 0;
	
	// Sprint / dash dance state
	private float _dirHoldTime = 0f;
	private bool _isSprinting = false;
	private float _turnaroundTimer = 0f;
	private Vector3 _lastInputDir = Vector3.Zero;
	
	// Knockback state (applied on top of normal velocity)
	private Vector3 _knockbackVelocity = Vector3.Zero;
	
	// Tech roll & knockback tracking
	private bool _wasAirborneDuringKnockback = false;
	
	// Trinket
	private float _trinketCooldownTimer = 0f;
	[Export] public float TrinketCooldown = 120f;
	[Export] public float TrinketPushSpeed = 18.0f;
	
	// ==========================================
	// REFERENCES
	// ==========================================
	
	private WowCamera? _wowCamera;
	private AnimationPlayer? _animPlayer;
	private Node3D? _playerModel;
	private CombatComponent? _combatComponent;
	
	// ==========================================
	// UI STATE
	// ==========================================
	
	/// <summary>
	/// Set to true when the spell book is open, so we don't capture the mouse.
	/// </summary>
	public bool IsSpellBookOpen { get; set; } = false;

	/// <summary>
	/// Set to true when the escape menu is open, so we don't capture the mouse.
	/// </summary>
	public bool IsEscapeMenuOpen { get; set; } = false;
	
	// ==========================================
	// CLIC POUR CIBLER
	// ==========================================
	
	private float _leftClickDragDistance = 0f;
	private Vector2 _leftClickPressPosition = Vector2.Zero;
	private bool _leftClickIsDrag = false;
	private const float ClickThreshold = 5f; // max pixel movement to count as a click (not a drag)
	
	// ==========================================
	// EVENTS
	// ==========================================
	
	public event Action<float, float, float, float, float>? OnStateUpdated;
	
	/// <summary>
	/// Fired when Tab is pressed to cycle targets.
	/// </summary>
	public event Action? OnTargetNextPressed;
	
	/// <summary>
	/// Fired when left-clicking on a 3D entity to target it. Passes entity ID (0 = no entity hit).
	/// </summary>
	public event Action<ulong>? OnLeftClickEntity;
	
	// ==========================================
	// PUBLIC GETTERS
	// ==========================================
	
	public float GetVelZ() => Velocity.Y;
	
	public float GetHP() => _hp;
	public float GetMaxHP() => MaxHP;
	
	/// <summary>
	/// Get the combat component for spell hit detection.
	/// </summary>
	public CombatComponent? GetCombatComponent() => _combatComponent;
	
	/// <summary>
	/// Get dash cooldown remaining (0 = ready).
	/// </summary>
	public float GetDashCooldown() => _dashCooldownTimer;
	
	/// <summary>
	/// Setup combat component (called by Main.cs after creation).
	/// </summary>
	public void SetupCombat(LocalSimulation simulation)
	{
		_combatComponent = new CombatComponent();
		_combatComponent.Name = "CombatComponent";
		_combatComponent.Setup(this, simulation, 1);
		AddChild(_combatComponent);
		
		// Subscribe to spell cast events for animations
		_combatComponent.OnSpellCast += OnSpellCast;
	}
	
	// ==========================================
	// INITIALIZATION
	// ==========================================
	
	public override void _Ready()
	{
		// --- Input Map setup (AZERTY + QWERTY compatible) ---
		SetupInputMap();
		
		// --- CharacterBody3D setup ---
		UpDirection = Vector3.Up;
		CollisionLayer = 1;
		CollisionMask = 1;
		FloorStopOnSlope = true;
		FloorMaxAngle = 45.0f;
		
		// --- Collision shape ---
		var collisionShape = new CollisionShape3D();
		var capsule = new CapsuleShape3D();
		capsule.Radius = 1.5f;
		capsule.Height = 3f;
		collisionShape.Shape = capsule;
		AddChild(collisionShape);
		
		// --- Pro Magic Pack character (FBX model) ---
		_playerModel = LoadPlayerModel();
		
		// --- Load animations from Pro Magic Pack ---
		_animPlayer = new AnimationPlayer();
		_animPlayer.Name = "AnimationPlayer";
		
		if (_playerModel != null)
			_playerModel.AddChild(_animPlayer);
		else
			AddChild(_animPlayer);
		
		var skeleton = _playerModel != null ? FindSkeleton(_playerModel) : null;
		if (skeleton != null)
		{
			_animPlayer.RootNode = skeleton.GetPath();
			GD.Print($"AnimationPlayer RootNode set to: {skeleton.GetPath()}");
		}
		
		var animLib = new AnimationLibrary();
		_animPlayer.AddAnimationLibrary("default", animLib);
		
		// Load all animations from the Pro Magic Pack directory
		LoadAllAnimations(animLib);
		
		// Log which animations we loaded
		var loadedAnims = animLib.GetAnimationList();
		GD.Print($"Loaded {loadedAnims.Count} animations:");
		foreach (var animName in loadedAnims)
			GD.Print($"  - {animName}");
		
		// Play idle if available
		if (animLib.HasAnimation("idle"))
		{
			animLib.GetAnimation("idle").LoopMode = Animation.LoopModeEnum.Linear;
			_animPlayer.Play("default/idle");
		}
		else if (loadedAnims.Count > 0)
		{
			string firstAnim = loadedAnims[0];
			GD.Print($"Playing first available animation: {firstAnim}");
			var first = animLib.GetAnimation(firstAnim);
			if (first != null) first.LoopMode = Animation.LoopModeEnum.Linear;
			_animPlayer.Play("default/" + firstAnim);
		}
		else
		{
			GD.Print("WARNING: No animations loaded at all!");
		}
		
		// --- Hurtbox (to receive damage) ---
		_hurtbox = new Hurtbox();
		_hurtbox.Name = "Hurtbox";
		_hurtbox.OwnerEntity = this;
		
		// Hurtbox collision shape (sphere around the player)
		var hurtboxShape = new CollisionShape3D();
		var hurtboxSphere = new SphereShape3D();
		hurtboxSphere.Radius = 2.0f;
		hurtboxShape.Shape = hurtboxSphere;
		_hurtbox.AddChild(hurtboxShape);
		
		AddChild(_hurtbox);
		
		// Quand on prend un coup
		_hurtbox.OnHit += (Vector3 attackerPos, float damage, Vector3 knockbackForce) =>
		{
			_hp -= damage;
			ApplyKnockback(knockbackForce);
			GD.Print($"Player took {damage} damage! HP: {_hp}/{MaxHP}");
			
			if (_hp <= 0)
			{
				GD.Print("Player defeated! Respawning...");
				_hp = MaxHP;
				Position = new Vector3(100f, 50f, 100f);
				Velocity = Vector3.Zero;
			}
		};
		
		// --- WoW-style camera (SpringArm3D) ---
		// WowCamera creates its own SpringArm3D and Camera3D in _Ready
		_wowCamera = new WowCamera();
		_wowCamera.Name = "WowCamera";
		_wowCamera.Target = this;
		AddChild(_wowCamera);
		
		// Spawn at arena center
		Position = new Vector3(100f, 10f, 100f);
	}
	
	// ==========================================
	// INPUT MAP SETUP (AZERTY + QWERTY compatible)
	// ==========================================
	
	private void SetupInputMap()
	{
		// Movement actions (PhysicalKeycode for layout independence)
		AddInputAction("move_forward", new InputEventKey { PhysicalKeycode = Key.Z });
		AddInputAction("move_forward", new InputEventKey { PhysicalKeycode = Key.W });
		AddInputAction("move_back",    new InputEventKey { PhysicalKeycode = Key.S });
		AddInputAction("move_left",    new InputEventKey { PhysicalKeycode = Key.Q });
		AddInputAction("move_left",    new InputEventKey { PhysicalKeycode = Key.A });
		AddInputAction("move_right",   new InputEventKey { PhysicalKeycode = Key.D });
		AddInputAction("jump",         new InputEventKey { PhysicalKeycode = Key.Space });
		AddInputAction("dash",         new InputEventKey { PhysicalKeycode = Key.Shift });
		AddInputAction("crouch",       new InputEventKey { PhysicalKeycode = Key.Ctrl });
		AddInputAction("crouch",       new InputEventKey { PhysicalKeycode = Key.C });
		
		// Spell actions (Keycode for layout-aware letter matching)
		AddInputAction("spell_slot1", new InputEventKey { PhysicalKeycode = Key.Key1 });
		AddInputAction("spell_slot2", new InputEventKey { PhysicalKeycode = Key.Key2 });
		AddInputAction("spell_slot3", new InputEventKey { PhysicalKeycode = Key.Key3 });
		AddInputAction("spell_slot4", new InputEventKey { PhysicalKeycode = Key.Key4 });
		AddInputAction("spell_slotA", new InputEventKey { Keycode = Key.A });
		AddInputAction("spell_slotE", new InputEventKey { Keycode = Key.E });
		AddInputAction("spell_slotR", new InputEventKey { Keycode = Key.R });
		
		// UI actions
		AddInputAction("spellbook_toggle", new InputEventKey { Keycode = Key.B });
		AddInputAction("ui_cancel",         new InputEventKey { Keycode = Key.Escape });
		AddInputAction("target_next",       new InputEventKey { PhysicalKeycode = Key.Tab });
		AddInputAction("trinket",           new InputEventKey { Keycode = Key.G });
		AddInputAction("tech",             new InputEventKey { Keycode = Key.F });
		
		GD.Print("InputMap setup complete (AZERTY + QWERTY compatible)");
	}
	
	private void AddInputAction(string actionName, InputEventKey keyEvent)
	{
		if (!InputMap.HasAction(actionName))
			InputMap.AddAction(actionName);
		InputMap.ActionAddEvent(actionName, keyEvent);
	}
	
	// ==========================================
	// INPUTS
	// ==========================================
	
	public override void _UnhandledInput(InputEvent @event)
	{
		// Si le spellbook ou le menu echap est ouvert, on ignore les clics souris
		// so drag & drop works without camera moving
		if (IsSpellBookOpen || IsEscapeMenuOpen)
		{
			if (Input.IsActionJustPressed("ui_cancel"))
			{
				Input.MouseMode = Input.MouseModeEnum.Visible;
			}
			// On ignore les events souris
			return;
		}
		
		// 1. Mouse state management (Visible vs Captured)
		if (@event is InputEventMouseButton mouseBtn)
		{
			if (mouseBtn.Pressed)
			{
				if (mouseBtn.ButtonIndex == MouseButton.Left)
				{
					// Clic gauche : NE PAS capturer tout de suite.
					// On attend de voir si le joueur drag (pour orbite) ou clique (pour cibler).
					_leftClickDragDistance = 0f;
					_leftClickIsDrag = false;
					_leftClickPressPosition = GetViewport().GetMousePosition();
				}
				else if (mouseBtn.ButtonIndex == MouseButton.Right)
				{
					// Right click: immediate capture (as before)
					if (Input.MouseMode != Input.MouseModeEnum.Captured)
					{
						Input.MouseMode = Input.MouseModeEnum.Captured;
					}
					
					// Align character with camera direction
					if (_wowCamera != null)
					{
						float cameraYaw = _wowCamera.GetCameraYaw();
						GlobalRotation = new Vector3(0f, cameraYaw, 0f);
						_wowCamera.SetYaw(cameraYaw);
					}
				}
				else if (mouseBtn.ButtonIndex == MouseButton.WheelUp)
				{
					_wowCamera?.ZoomCamera(-1f);
				}
				else if (mouseBtn.ButtonIndex == MouseButton.WheelDown)
				{
					_wowCamera?.ZoomCamera(1f);
				}
			}
			else
			{
				// Left click released
				if (mouseBtn.ButtonIndex == MouseButton.Left)
				{
					if (!_leftClickIsDrag)
					{
						// Clic rapide sans drag → raycast pour cibler
						DoClickRaycast();
					}
					// If it was a drag, mouse is Captured → la liberer
					if (Input.MouseMode == Input.MouseModeEnum.Captured)
					{
						Input.MouseMode = Input.MouseModeEnum.Visible;
					}
				}
				
				// Clic droit relache
				if (mouseBtn.ButtonIndex == MouseButton.Right)
				{
					if (!Input.IsMouseButtonPressed(MouseButton.Left))
					{
						Input.MouseMode = Input.MouseModeEnum.Visible;
					}
				}
			}
		}
		
		// 2. Mouse movement → orbite camera (quand capturee)
		if (@event is InputEventMouseMotion mouseMotion)
		{
			// Drag detection for left click (avant capture)
			if (Input.IsMouseButtonPressed(MouseButton.Left) && Input.MouseMode != Input.MouseModeEnum.Captured)
			{
				_leftClickDragDistance += mouseMotion.Relative.Length();
				if (_leftClickDragDistance > ClickThreshold && !_leftClickIsDrag)
				{
					// Player moved enough → c'est un drag, pas un clic → capturer
					_leftClickIsDrag = true;
					Input.MouseMode = Input.MouseModeEnum.Captured;
				}
			}
			
			// Camera rotation (once captured)
			if (Input.MouseMode == Input.MouseModeEnum.Captured)
			{
				bool isLeftDown = Input.IsMouseButtonPressed(MouseButton.Left) || _leftClickIsDrag;
				bool isRightDown = Input.IsMouseButtonPressed(MouseButton.Right);
				_wowCamera?.RotateCamera(mouseMotion.Relative, isLeftDown, isRightDown);
			}
		}

		// 3. Sorts via SpellSystem (via InputMap actions)
		if (Input.IsActionJustPressed("spell_slot1"))     TriggerSpellSlot(SlotType.Slot1);
		else if (Input.IsActionJustPressed("spell_slot2")) TriggerSpellSlot(SlotType.Slot2);
		else if (Input.IsActionJustPressed("spell_slot3")) TriggerSpellSlot(SlotType.Slot3);
		else if (Input.IsActionJustPressed("spell_slot4")) TriggerSpellSlot(SlotType.Slot4);
		else if (Input.IsActionJustPressed("spell_slotA")) TriggerSpellSlot(SlotType.Slot5);
		else if (Input.IsActionJustPressed("spell_slotE")) TriggerSpellSlot(SlotType.Slot6);
		else if (Input.IsActionJustPressed("spell_slotR")) TriggerSpellSlot(SlotType.Slot8);
		else if (Input.IsActionJustPressed("dash"))        TryDash();
		else if (Input.IsActionJustPressed("crouch"))     { /* Air dodge now in _PhysicsProcess */ }
		else if (Input.IsActionJustPressed("trinket"))    UseTrinket();
		else if (Input.IsActionJustPressed("ui_cancel"))   Input.MouseMode = Input.MouseModeEnum.Visible;
		else if (Input.IsActionJustPressed("target_next"))
		{
			// Cycle target — handled by Main.cs via event
			OnTargetNextPressed?.Invoke();
		}
		else if (Input.IsActionJustPressed("spellbook_toggle"))
		{
			var spellBook = GetNodeOrNull<SpellBookUI>("../CanvasLayer/SpellBookUI");
			if (spellBook != null)
			{
				spellBook.Toggle();
			}
		}
	}
	
	private void TriggerSpellSlot(SlotType slot)
	{
		if (_combatComponent != null)
		{
			_combatComponent.TriggerSlot(slot);
		}
	}
	
	// ==========================================
	// TIMERS (ticked every frame)
	// ==========================================
	
	public override void _Process(double delta)
	{
		float dt = (float)delta;
		
		if (_dashCooldownTimer > 0f)
			_dashCooldownTimer -= dt;
		
		if (_dashTimer > 0f)
			_dashTimer -= dt;
		
		if (_airDodgeTimer > 0f)
			_airDodgeTimer -= dt;
		
		if (_turnaroundTimer > 0f)
			_turnaroundTimer -= dt;
		
		if (_trinketCooldownTimer > 0f)
			_trinketCooldownTimer -= dt;
		
		if (_castAnimTimer > 0f)
			_castAnimTimer -= dt;
	}
	
	/// <summary>
	/// Attempt a ground dash. Ground only — short burst, cancelable into jump/attack.
	/// </summary>
	private void TryDash()
	{
		if (_dashCooldownTimer > 0f) return;
		if (_moveState != MoveState.Normal) return;
		if (!IsOnFloor()) return;
		if (_knockbackVelocity.LengthSquared() > 0.001f) return;
		
		// Direction: input or forward
		Vector3 inputDir = GetInputDirection();
		if (inputDir.LengthSquared() < 0.01f)
		{
			inputDir = -Transform.Basis.Z;
			inputDir.Y = 0;
			inputDir = inputDir.Normalized();
		}
		
		_dashDirection = inputDir;
		_dashTimer = DashDuration;
		_dashCooldownTimer = DashCooldown;
		_moveState = MoveState.Dashing;
		
		// Apply dash burst (set horizontal, preserve vertical momentum)
		Velocity = new Vector3(
			_dashDirection.X * DashSpeed,
			Mathf.Max(Velocity.Y, 0f),
			_dashDirection.Z * DashSpeed
		);
		
		GD.Print($"Dash!");
	}
	
	/// <summary>
	/// Attempt an air dodge. Directional, once per jump, short burst.
	/// Replaces both the old roll and air dash.
	/// </summary>
	private void TryAirDodge()
	{
		if (_airDodgesLeft <= 0) return;
		if (_moveState != MoveState.Normal) return;
		if (IsOnFloor()) return; // Air only
		if (_knockbackVelocity.LengthSquared() > 0.001f) return;
		
		// Direction: input or forward
		Vector3 inputDir = GetInputDirection();
		if (inputDir.LengthSquared() < 0.01f)
		{
			// No input = spot dodge (barely move)
			inputDir = -Transform.Basis.Z;
			inputDir.Y = 0;
			inputDir = inputDir.Normalized();
		}
		
		_airDodgeDirection = inputDir;
		_airDodgeTimer = AirDodgeDuration;
		_moveState = MoveState.AirDodging;
		_airDodgesLeft--;
		
		// Burst in direction, preserve Y (fall still happens)
		Velocity = new Vector3(
			_airDodgeDirection.X * AirDodgeSpeed,
			Velocity.Y,
			_airDodgeDirection.Z * AirDodgeSpeed
		);
		
		GD.Print($"Air dodge! ({_airDodgesLeft} left this jump)");
	}
	
	/// <summary>
	/// Tech roll: press crouch/trinket right before landing during knockback.
	/// Clears knockback, gives iframes, and lets you act immediately.
	/// </summary>
	private void DoTechRoll()
	{
		_knockbackVelocity = Vector3.Zero;
		_moveState = MoveState.Normal;
		
		// Small roll in input direction (or forward if no input)
		Vector3 rollDir = GetInputDirection();
		if (rollDir.LengthSquared() < 0.01f)
		{
			rollDir = -Transform.Basis.Z;
			rollDir.Y = 0;
			rollDir = rollDir.Normalized();
		}
		Velocity = new Vector3(rollDir.X * 10f, 0f, rollDir.Z * 10f);
		
		GD.Print("Tech roll!");
	}
	
	/// <summary>
	/// Trinket: G key, long cooldown, breaks combos.
	/// Clears knockback and bursts backward (no hitstun — resets to neutral).
	/// </summary>
	private void UseTrinket()
	{
		if (_trinketCooldownTimer > 0f) return;
		
		_knockbackVelocity = Vector3.Zero;
		_moveState = MoveState.Normal;
		
		// Push backward (away from facing direction)
		Vector3 backDir = Transform.Basis.Z; // +Z = backward in Godot
		backDir.Y = 0;
		backDir = backDir.Normalized();
		Velocity = new Vector3(backDir.X * TrinketPushSpeed, JumpVelocity * 0.5f, backDir.Z * TrinketPushSpeed);
		
		_trinketCooldownTimer = TrinketCooldown;
		
		GD.Print($"Trinket! ({TrinketCooldown}s cooldown)");
	}
	
	/// <summary>
	/// Get the input direction relative to the player's facing.
	/// </summary>
	private Vector3 GetInputDirection()
	{
		Vector3 dir = Vector3.Zero;
		Vector3 playerForward = -Transform.Basis.Z;
		playerForward.Y = 0;
		playerForward = playerForward.Normalized();
		Vector3 playerRight = Transform.Basis.X;
		playerRight.Y = 0;
		playerRight = playerRight.Normalized();
		
		if (Input.IsActionPressed("move_forward"))  dir += playerForward;
		if (Input.IsActionPressed("move_back"))     dir -= playerForward;
		if (Input.IsActionPressed("move_left"))     dir -= playerRight;
		if (Input.IsActionPressed("move_right"))    dir += playerRight;
		
		if (dir.LengthSquared() > 0f)
			dir = dir.Normalized();
		
		return dir;
	}
	
	// ==========================================
	// PHYSICS
	// ==========================================
	
	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		
		// --- Knockback ---
		if (_knockbackVelocity.LengthSquared() > 0.001f)
		{
			float decay = 8.0f;
			_knockbackVelocity = _knockbackVelocity.Lerp(Vector3.Zero, decay * dt);
			Velocity = _knockbackVelocity;
			ApplyGravity(dt);
			
			bool wasAirborne = !IsOnFloor();
			MoveAndSlide();
			
			// Tech roll on landing: press F (tech) to avoid knockdown
			if (wasAirborne && IsOnFloor())
			{
				if (Input.IsActionJustPressed("tech"))
					DoTechRoll();
				else
				{
					// Normal knockdown landing
					_knockbackVelocity = Vector3.Zero;
					_moveState = MoveState.Normal;
				}
			}
			
			PostMove();
			return;
		}
		
		// --- Dashing (maintain velocity while timer active) ---
		if (_moveState == MoveState.Dashing)
		{
			if (_dashTimer > 0f)
			{
				// Dash can be canceled by jump
				if (Input.IsActionPressed("jump") && IsOnFloor())
				{
					_moveState = MoveState.Normal;
					Velocity = new Vector3(Velocity.X, JumpVelocity, Velocity.Z);
				}
				else
				{
					Velocity = new Vector3(
						_dashDirection.X * DashSpeed,
						Mathf.Max(Velocity.Y, 0f),
						_dashDirection.Z * DashSpeed
					);
				}
			}
			else
			{
				_moveState = MoveState.Normal;
			}
		}
		
		// --- Air dodge (maintain velocity while timer active) ---
		if (_moveState == MoveState.AirDodging)
		{
			if (_airDodgeTimer > 0f)
			{
				Velocity = new Vector3(
					_airDodgeDirection.X * AirDodgeSpeed,
					Velocity.Y,
					_airDodgeDirection.Z * AirDodgeSpeed
				);
			}
			else
			{
				_moveState = MoveState.Normal;
			}
		}
		
		// --- Normal movement (acceleration-based) ---
		if (_moveState == MoveState.Normal)
		{
			bool isGrounded = IsOnFloor();
			bool inputJump = Input.IsActionPressed("jump");
			Vector3 inputDir = GetInputDirection();
			
			if (isGrounded)
			{
				// Reset air dodges on landing
				_airDodgesLeft = MaxAirDodges;
				
				bool hasInput = inputDir.LengthSquared() > 0.01f;
				
				if (hasInput)
				{
					// Detect direction change (dot < 0.5 means significant change)
					bool dirChanged = _lastInputDir.LengthSquared() > 0.01f
						&& inputDir.Dot(_lastInputDir) < 0.5f;
					
					if (dirChanged)
					{
						_dirHoldTime = 0f;
						if (_isSprinting)
						{
							// Sprint → turnaround lag (can't move for a moment)
							_turnaroundTimer = TurnaroundLag;
							_isSprinting = false;
						}
						// else: walk → instant turn (no lag)
					}
					else
					{
						// Same direction or initial press
						_dirHoldTime += dt;
						if (_dirHoldTime >= SprintThreshold && !_isSprinting)
							_isSprinting = true;
					}
					
					_lastInputDir = inputDir;
					
					if (_turnaroundTimer > 0f)
					{
						// Turnaround lag: decelerate, can't move in new direction yet
						float friction = GroundFriction * dt;
						Velocity = new Vector3(
							Mathf.MoveToward(Velocity.X, 0f, Mathf.Abs(Velocity.X) * friction),
							Velocity.Y,
							Mathf.MoveToward(Velocity.Z, 0f, Mathf.Abs(Velocity.Z) * friction)
						);
					}
					else
					{
						// Walk or sprint: instant speed
						float speed = _isSprinting ? SprintSpeed : WalkSpeed;
						Velocity = new Vector3(inputDir.X * speed, Velocity.Y, inputDir.Z * speed);
					}
					
					// Jump
					if (inputJump)
						Velocity = new Vector3(Velocity.X, JumpVelocity, Velocity.Z);
				}
				else
				{
					// No input: reset sprint, decelerate fast
					_dirHoldTime = 0f;
					_isSprinting = false;
					_lastInputDir = Vector3.Zero;
					
					float friction = GroundFriction * dt;
					Velocity = new Vector3(
						Mathf.MoveToward(Velocity.X, 0f, Mathf.Abs(Velocity.X) * friction),
						Velocity.Y,
						Mathf.MoveToward(Velocity.Z, 0f, Mathf.Abs(Velocity.Z) * friction)
					);
				}
			}
			else
			{
				// AIR: reduced acceleration, some drag
				Vector3 targetVel = inputDir * WalkSpeed;
				float airAccel = AirAcceleration * dt;
				float newX = Mathf.MoveToward(Velocity.X, targetVel.X, airAccel);
				float newZ = Mathf.MoveToward(Velocity.Z, targetVel.Z, airAccel);
				Velocity = new Vector3(newX, Velocity.Y, newZ);
				
				// Air drag (gentle slowdown)
				float drag = AirDrag * dt;
				Velocity = new Vector3(
					Velocity.X * (1f - drag),
					Velocity.Y,
					Velocity.Z * (1f - drag)
				);
				
				// Air dodge trigger (physics-synchronous)
				if (Input.IsActionJustPressed("crouch") && _airDodgesLeft > 0)
				{
					TryAirDodge();
				}
			}
		}
		
		ApplyGravity(dt);
		MoveAndSlide();
		
		// Air dodge landing: just go back to normal (no slide)
		if (_moveState == MoveState.AirDodging && IsOnFloor())
		{
			_moveState = MoveState.Normal;
			_airDodgeTimer = 0f;
		}
		
		PostMove();
	}
	
	private void ApplyGravity(float dt)
	{
		if (!IsOnFloor())
		{
			bool inputDown = Input.IsActionPressed("move_back") || Input.IsActionPressed("crouch");
			
			if (inputDown && _moveState != MoveState.AirDodging)
			{
				// Fast fall: instant kick downward (Melee-style)
				if (Velocity.Y > -FastFallKick)
					Velocity = new Vector3(Velocity.X, -FastFallKick, Velocity.Z);
				
				// Maintain speed with extra gravity
				Velocity -= new Vector3(0f, Gravity * FastFallGravityMult * dt, 0f);
			}
			else
			{
				Velocity -= new Vector3(0f, Gravity * dt, 0f);
			}
			
			// Hard cap on fall speed
			if (Velocity.Y < -MaxFallSpeed)
				Velocity = new Vector3(Velocity.X, -MaxFallSpeed, Velocity.Z);
		}
	}
	
	private void PostMove()
	{
		// Safety: under the floor
		if (GlobalPosition.Y < 0f && IsOnFloor())
		{
			GlobalPosition = new Vector3(GlobalPosition.X, 1f, GlobalPosition.Z);
			Velocity = new Vector3(Velocity.X, 0f, Velocity.Z);
		}
		
		// Reset air dashes on landing (if not already done in normal movement)
		if (IsOnFloor() && _moveState == MoveState.Dashing)
		{
			_airDodgesLeft = MaxAirDodges;
		}
		
		UpdateAnimations();
		
		if (GlobalPosition.Y < -50f)
		{
			GD.Print("Player fell through the floor! Respawning...");
			Position = new Vector3(100f, 10f, 100f);
			Velocity = Vector3.Zero;
		}
		
		Vector3 pos = GlobalPosition;
		Vector3 vel = Velocity;
		OnStateUpdated?.Invoke(pos.X, pos.Z, pos.Y, vel.X, vel.Z);
	}
	
	// ==========================================
	// KNOCKBACK
	// ==========================================
	
	public void ApplyKnockback(Vector3 force)
	{
		_knockbackVelocity = force;
		_moveState = MoveState.Normal;
		_dashTimer = 0f;
		_airDodgeTimer = 0f;
	}
	
	// ==========================================
	// CLICK → RAYCAST → TARGETING
	// ==========================================
	
	/// <summary>
/// Casts a ray from camera to mouse position to detect clicks on units.
	/// Dummies are on collision layer 2, player on layer 1.
	/// </summary>
	private void DoClickRaycast()
	{
		if (_wowCamera == null) return;
		
		var camera = _wowCamera.GetCamera();
		if (camera == null) return;
		
		var spaceState = GetWorld3D().DirectSpaceState;
		Vector2 mousePos = _leftClickPressPosition;
		
		Vector3 from = camera.ProjectRayOrigin(mousePos);
		Vector3 to = from + camera.ProjectRayNormal(mousePos) * 2000f;
		
		var query = new PhysicsRayQueryParameters3D();
		query.From = from;
		query.To = to;
		query.CollisionMask = 2; // Layer 2 = dummies / entities
		
		var result = spaceState.IntersectRay(query);
		
		ulong hitEntityId = 0;
		
		if (result.Count > 0)
		{
			Node? collider = result["collider"].AsGodotObject() as Node;
			
			if (collider != null)
			{
				// Walk up parents jusqu'a trouver un CharacterBody3D
				Node? body = collider;
				while (body != null && body is not CharacterBody3D)
				{
					body = body.GetParent();
				}
				
				if (body is CharacterBody3D character)
				{
					// Les dummies ont des noms comme "DummyBody_0", "DummyBody_1", etc.
					string name = character.Name;
					if (name.StartsWith("DummyBody_") && int.TryParse(name.AsSpan("DummyBody_".Length), out int idx))
					{
						hitEntityId = (ulong)(100 + idx);
					}
				}
			}
		}
		
		OnLeftClickEntity?.Invoke(hitEntityId);
	}
	
	// ==========================================
	// SPELLS (using player direction)
	// ==========================================
	
	public Vector3 GetPlayerForward()
	{
		Vector3 forward = -Transform.Basis.Z;
		forward.Y = 0;
		return forward.Normalized();
	}
	
	public Vector3 GetCameraForward()
	{
		if (_wowCamera != null)
			return _wowCamera.GetForwardDirection();
		return GetPlayerForward();
	}
	
	private void CreateFallbackMesh()
	{
		var mesh = new MeshInstance3D();
		var capsuleMesh = new CapsuleMesh();
		capsuleMesh.Radius = 1.5f;
		capsuleMesh.Height = 3f;
		var mat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0f, 0.75f, 1f, 1f),
			EmissionEnabled = true,
			Emission = new Color(0f, 0.5f, 0.8f, 1f),
			EmissionEnergyMultiplier = 2f
		};
		mesh.Mesh = capsuleMesh;
		mesh.MaterialOverride = mat;
		AddChild(mesh);
	}
	
	// ==========================================
	// MODEL LOADING
	// ==========================================
	
	private Node3D? LoadPlayerModel()
	{
		var playerModelPath = "res://assets/characters/Model/characterMedium.fbx";
		if (!ResourceLoader.Exists(playerModelPath))
		{
			GD.Print("Player model not found at " + playerModelPath + ", using fallback capsule");
			CreateFallbackMesh();
			return null;
		}
		
		var playerModel = GD.Load<PackedScene>(playerModelPath);
		if (playerModel == null)
		{
			GD.PrintErr("Failed to load player model");
			CreateFallbackMesh();
			return null;
		}
		
		var playerInstance = playerModel.Instantiate<Node3D>();
		playerInstance.Name = "PlayerModel";
		AddChild(playerInstance);
		
		// Apply skin to ALL MeshInstance3D nodes in the model
		var skinTex = GD.Load<Texture2D>("res://assets/characters/Skins/skaterMaleA.png");
		ApplySkinRecursive(playerInstance, skinTex);
		
		// Mixamo models face +Z but Godot uses -Z as forward.
		// Rotate the model 180° around Y so it faces away from camera.
		playerInstance.RotateY(Mathf.Pi);
		
		// Adjust model position and scale
		playerInstance.Scale = new Vector3(0.5f, 0.5f, 0.5f);
		playerInstance.Position = new Vector3(0f, -1.5f, 0f);
		
		return playerInstance;
	}
	
	private void ApplySkinRecursive(Node node, Texture2D? skinTex)
	{
		if (node is MeshInstance3D mi)
		{
			if (skinTex != null)
			{
				var mat2 = new StandardMaterial3D();
				mat2.AlbedoTexture = skinTex;
				mat2.TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear;
				mi.MaterialOverride = mat2;
				GD.Print($"Applied skin to MeshInstance3D: {mi.Name}");
			}
			else
			{
				// If no skin, at least use a visible material
				var mat2 = new StandardMaterial3D();
				mat2.AlbedoColor = new Color(0.7f, 0.7f, 0.7f, 1f);
				mi.MaterialOverride = mat2;
			}
		}
		
		// Continue recursion for children
		foreach (var child in node.GetChildren())
		{
			ApplySkinRecursive(child, skinTex);
		}
	}
	
	// ==========================================
	// ANIMATION LOADING
	// ==========================================
	
	// ==========================================
	// ANIMATION LOADING
	// ==========================================
	
	/// <summary>
	/// Load ALL FBX animation files from the Pro Magic Pack directory.
	/// Each FBX file contains one animation clip. We load it and name it
	/// by a standardized key derived from the filename.
	/// </summary>
	private void LoadAllAnimations(AnimationLibrary animLib)
	{
		string animDir = "res://assets/characters/ProMagicPack/";
		
		// Use Godot's Directory API to list FBX files
		var dir = DirAccess.Open(animDir);
		if (dir == null)
		{
			GD.PrintErr($"Cannot open animation directory: {animDir}");
			return;
		}
		
		dir.ListDirBegin();
		int loadedCount = 0;
		
		while (true)
		{
			string fileName = dir.GetNext();
			if (string.IsNullOrEmpty(fileName))
				break;
			
			// Only process FBX files (skip .import, directories, etc.)
			if (!fileName.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
				continue;
			
			if (fileName.Equals("characterMedium.fbx", StringComparison.OrdinalIgnoreCase))
				continue; // Skip the model file itself
			
			string fbxPath = animDir + fileName;
			string animKey = NormalizeAnimationName(fileName);
			
			if (LoadSingleAnimation(animLib, fbxPath, animKey))
				loadedCount++;
		}
		
		dir.ListDirEnd();
		GD.Print($"Loaded {loadedCount} animations from {animDir}");
	}
	
	/// <summary>
	/// Convert a filename like "Standing Run Forward.fbx" to "run_forward"
	/// </summary>
	private string NormalizeAnimationName(string fileName)
	{
		// Remove .fbx extension
		string name = fileName;
		int extIdx = name.LastIndexOf(".fbx", StringComparison.OrdinalIgnoreCase);
		if (extIdx > 0)
			name = name.Substring(0, extIdx);
		
		// Remove common prefixes
		if (name.StartsWith("Standing ", StringComparison.OrdinalIgnoreCase))
			name = name.Substring("Standing ".Length);
		else if (name.StartsWith("standing ", StringComparison.OrdinalIgnoreCase))
			name = name.Substring("standing ".Length);
		else if (name.StartsWith("Crouch ", StringComparison.OrdinalIgnoreCase))
			name = "crouch_" + name.Substring("Crouch ".Length);
		
		// Replace spaces with underscores, lowercase
		name = name.Replace(" ", "_").ToLower();
		
		// Remove leading whitespace only (not digits — "1h" and "2h" matter!)
		name = name.TrimStart();
		
		// Specific renames for common animations
		var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			// Idle variants → single "idle"
			{ "idle", "idle" },
			{ "idle_02", "idle" },
			{ "idle_03", "idle" },
			{ "idle_04", "idle" },
			
			// Movement
			{ "run_forward", "run" },
			{ "walk_forward", "walk" },
			{ "sprint_forward", "sprint" },
			{ "jump", "jump" },
			{ "jump_running", "jump_run" },
			{ "land_to_standing_idle", "land" },
			{ "jump_running_landing", "land_run" },
			{ "idle_to_crouch", "crouch_transition" },
			{ "crouch_idle", "crouch_idle" },
			{ "crouch_walk_forward", "crouch_walk" },
			{ "crouch_to_standing_idle", "stand_transition" },
			
			// Cast animations
			{ "1h_cast_spell_01", "cast_1h" },
			{ "2h_cast_spell_01", "cast_2h" },
			
			// Attack animations
			{ "1h_magic_attack_01", "attack_1h" },
			{ "1h_magic_attack_02", "attack_1h_b" },
			{ "1h_magic_attack_03", "attack_1h_c" },
			{ "2h_magic_attack_01", "attack_2h" },
			{ "2h_magic_attack_02", "attack_2h_b" },
			{ "2h_magic_attack_03", "attack_2h_c" },
			{ "2h_magic_attack_04", "attack_2h_d" },
			{ "2h_magic_attack_05", "attack_2h_e" },
			{ "2h_magic_area_attack_01", "attack_area_2h" },
			{ "2h_magic_area_attack_02", "attack_area_2h_b" },
			
			// Block
			{ "block_idle", "block_idle" },
			{ "block_start", "block_start" },
			{ "block_end", "block_end" },
			{ "block_react_large", "block_react" },
			
			// Hit reactions
			{ "react_small_from_front", "hit_small_front" },
			{ "react_small_from_back", "hit_small_back" },
			{ "react_small_from_left", "hit_small_left" },
			{ "react_small_from_right", "hit_small_right" },
			{ "react_large_from_front", "hit_large_front" },
			{ "react_large_from_back", "hit_large_back" },
			{ "react_large_from_left", "hit_large_left" },
			{ "react_large_from_right", "hit_large_right" },
			
			// Death
			{ "react_death_forward", "death_forward" },
			{ "react_death_backward", "death_backward" },
			{ "react_death_left", "death_left" },
			{ "react_death_right", "death_right" },
			
			// Turn
			{ "turn_left_90", "turn_left" },
			{ "turn_right_90", "turn_right" },
		};
		
		if (renames.ContainsKey(name))
			return renames[name];
		
		return name;
	}
	
	/// <summary>
	/// Load a single animation from an FBX file and add it to the library.
	/// Handles both scene-based FBX (with AnimationPlayer) and direct Animation resources.
	/// </summary>
	private bool LoadSingleAnimation(AnimationLibrary animLib, string fbxPath, string animKey)
	{
		if (!ResourceLoader.Exists(fbxPath))
		{
			GD.Print($"Animation file not found: {fbxPath}");
			return false;
		}
		
		// Try loading as PackedScene first (most common for FBX animations)
		var scene = ResourceLoader.Load<PackedScene>(fbxPath);
		if (scene != null)
		{
			var tempInstance = scene.Instantiate<Node>();
			if (tempInstance == null) return false;
			
			var animPlayerInScene = FindAnimationPlayer(tempInstance);
			if (animPlayerInScene != null)
			{
				foreach (var libName in animPlayerInScene.GetAnimationLibraryList())
				{
					var lib = animPlayerInScene.GetAnimationLibrary(libName);
					if (lib == null) continue;
					
					foreach (var animNameInLib in lib.GetAnimationList())
					{
						var anim = lib.GetAnimation(animNameInLib);
						if (anim == null) continue;
						
						// Remap bone paths: handle both "Root|" (Kenney) and bare bones (Mixamo)
						anim = RemapAnimationPaths(anim);
						if (anim != null)
						{
							if (animLib.HasAnimation(animKey))
							{
								GD.Print($"  Skipping duplicate animation key: {animKey} (from {fbxPath})");
							}
							else
							{
								animLib.AddAnimation(animKey, anim);
								GD.Print($"  Loaded: {animKey} <- {fbxPath}");
							}
						}
					}
				}
			}
			else
			{
				// Try loading as direct Animation resource
				LoadDirectAnimation(animLib, fbxPath, animKey);
			}
			
			tempInstance.QueueFree();
			return true;
		}
		
		// Fallback: try direct Animation resource
		return LoadDirectAnimation(animLib, fbxPath, animKey);
	}
	
	private bool LoadDirectAnimation(AnimationLibrary animLib, string fbxPath, string animKey)
	{
		try
		{
			var directAnim = ResourceLoader.Load<Animation>(fbxPath);
			if (directAnim != null)
			{
				directAnim = RemapAnimationPaths(directAnim);
				if (directAnim != null)
				{
					animLib.AddAnimation(animKey, directAnim);
					GD.Print($"  Loaded direct: {animKey} <- {fbxPath}");
					return true;
				}
			}
		}
		catch (Exception ex)
		{
			GD.Print($"  Could not load animation from {fbxPath}: {ex.Message}");
		}
		return false;
	}
	
	/// <summary>
	/// Remap bone paths in animation tracks to match our skeleton.
	/// Our AnimationPlayer's RootNode is set to the skeleton node.
	/// Mixamo FBX animations have tracks referencing "Root/Skeleton3D" or "Root/Skeleton"
	/// which need to be stripped so paths resolve relative to the skeleton.
	/// Kenney animations use "Root|" prefix which also gets stripped.
	/// </summary>
	private Animation RemapAnimationPaths(Animation anim)
	{
		var animCopy = (Animation)anim.Duplicate();
		int trackCount = animCopy.GetTrackCount();
		
		// Detect the skeleton path prefix used in this animation
		// Mixamo: "Root/Skeleton3D" or "Root/Skeleton"
		// Kenney: "Root|"
		string? prefixToStrip = null;
		
		for (int i = 0; i < trackCount && prefixToStrip == null; i++)
		{
			string path = animCopy.TrackGetPath(i);
			if (path.Contains("Root/Skeleton3D"))
				prefixToStrip = "Root/Skeleton3D";
			else if (path.Contains("Root/Skeleton"))
				prefixToStrip = "Root/Skeleton";
			else if (path.Contains("Root|"))
				prefixToStrip = "Root|";
		}
		
		if (prefixToStrip == null)
			return animCopy; // No remapping needed
		
		GD.Print($"  Remapping paths: stripping '{prefixToStrip}' prefix");
		
		for (int i = 0; i < trackCount; i++)
		{
			string trackPath = animCopy.TrackGetPath(i);
			
			if (trackPath.Contains(prefixToStrip))
			{
				string newPath = trackPath.Replace(prefixToStrip, "");
				animCopy.TrackSetPath(i, new NodePath(newPath));
			}
		}
		
		return animCopy;
	}
	
	/// <summary>
	/// Recursively finds a Skeleton3D in the node tree.
	/// </summary>
	private Skeleton3D? FindSkeleton(Node node)
	{
		if (node is Skeleton3D sk)
			return sk;
		foreach (var child in node.GetChildren())
		{
			var result = FindSkeleton(child);
			if (result != null)
				return result;
		}
		return null;
	}
	
	/// <summary>
	/// Recursively finds an AnimationPlayer in the node tree.
	/// </summary>
	private AnimationPlayer? FindAnimationPlayer(Node node)
	{
		if (node is AnimationPlayer ap)
			return ap;
		foreach (var child in node.GetChildren())
		{
			var result = FindAnimationPlayer(child);
			if (result != null)
				return result;
		}
		return null;
	}
	
	// ==========================================
	// ANIMATION STATE
	// ==========================================
	
	private string _currentAnim = "";
	private float _castAnimTimer = 0f;
	
	/// <summary>
	/// Called when a spell is cast. Plays an appropriate cast/attack animation.
	/// </summary>
	private void OnSpellCast(SlotType slot)
	{
		if (_animPlayer == null) return;
		
		// Try cast animations first, fall back to attack animations
		string[] castAnims;
		if (slot == SlotType.Slot8)
		{
			// Elite/ultimate spells: dramatic 2H cast
			castAnims = new[] { "cast_2h", "attack_area_2h", "attack_area_2h_b" };
		}
		else if (slot == SlotType.Slot6 || slot == SlotType.Slot7)
		{
			// Utility/defense: 1H cast
			castAnims = new[] { "cast_1h", "attack_1h" };
		}
		else
		{
			// Regular spells: mix of attacks and casts
			castAnims = new[] { "attack_1h", "attack_1h_b", "attack_1h_c", "attack_2h", "attack_2h_b", "cast_1h", "cast_2h" };
		}
		
		// Pick the first available animation from the list
		foreach (string anim in castAnims)
		{
			string fullPath = "default/" + anim;
			if (_animPlayer.HasAnimation(fullPath))
			{
				_animPlayer.Play(fullPath);
				_currentAnim = fullPath;
				_castAnimTimer = 2.0f;
				GD.Print($"Playing cast anim: {anim} for slot {slot}");
				return;
			}
		}
		
		GD.Print($"No cast/attack animation found for slot {slot}");
	}
	
	private void UpdateAnimations()
	{
		if (_animPlayer == null) return;
		
		// Cast animation takes priority
		if (_castAnimTimer > 0f)
		{
			if (!_animPlayer.IsPlaying())
				_castAnimTimer = 0f;
			return;
		}
		
		string targetAnim;
		
		// Movement state → animation
		switch (_moveState)
		{
			case MoveState.Dashing:
				targetAnim = _dashTimer > 0f ? "run" : "run";
				break;
				
			case MoveState.AirDodging:
				targetAnim = "jump";
				break;
				
			default:
				bool isGrounded = IsOnFloor();
				float hSpeed = new Vector3(Velocity.X, 0f, Velocity.Z).Length();
				bool isWalking = hSpeed > 1f && hSpeed < 11f;
				bool isRunning = hSpeed >= 11f;
				
				if (!isGrounded)
					targetAnim = "jump";
				else if (isRunning)
					targetAnim = "run";
				else if (isWalking)
					targetAnim = "walk";
				else
					targetAnim = "idle";
				break;
		}
		
		PlayAnimWithFallback(targetAnim);
	}
	
	private void PlayAnimWithFallback(string animName)
	{
		if (_animPlayer == null) return;
		string fullPath = "default/" + animName;
		
		if (_animPlayer.HasAnimation(fullPath) && _currentAnim != fullPath)
		{
			SetLoopMode(fullPath, animName);
			_animPlayer.Play(fullPath, 0.2f);
			_currentAnim = fullPath;
		}
		else if (!_animPlayer.HasAnimation(fullPath))
		{
			// Fallback: find any animation that starts with the same prefix
			foreach (var available in _animPlayer.GetAnimationList())
			{
				if (available.StartsWith(animName) || animName.StartsWith(available))
				{
					string fallbackPath = "default/" + available;
					if (_currentAnim != fallbackPath)
					{
						SetLoopMode(fallbackPath, available);
						_animPlayer.Play(fallbackPath, 0.2f);
						_currentAnim = fallbackPath;
						return;
					}
				}
			}
		}
	}
	
	private void SetLoopMode(string fullPath, string animName)
	{
		if (_animPlayer == null) return;
		// fullPath is "default/animName", GetAnimation might need bare name
		var anim = _animPlayer.GetAnimation(animName);
		if (anim == null) return;
		
		// Loop movement / idle animations, not attacks/casts/reacts
		bool shouldLoop = animName == "idle" || animName == "run" || animName == "walk"
			|| animName.StartsWith("idle") || animName.StartsWith("run") || animName.StartsWith("walk")
			|| animName.StartsWith("crouch");
		anim.LoopMode = shouldLoop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
	}
	
}
