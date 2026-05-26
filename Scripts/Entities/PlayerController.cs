#nullable enable
using Godot;
using System;
using System.Collections.Generic;
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
	
	[Export] public float Speed = 35.0f;
	[Export] public float BackwardSpeed = 22.0f;  // Reduced backward speed
	[Export] public float JumpForce = 14.0f;
	[Export] public float Gravity = 45.0f;       // Heavy gravity for fast landing
	
	// ==========================================
	// KNOCKBACK
	// ==========================================
	
	private Vector3 _knockbackVelocity = Vector3.Zero;
	private float _knockbackDecay = 8.0f;
	
	// ==========================================
	// HP / HURTBOX
	// ==========================================
	
	private float _hp = 100f;
	private const float MaxHP = 100f;
	private Hurtbox? _hurtbox;
	
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
	/// Setup combat component (called by Main.cs after creation).
	/// </summary>
	public void SetupCombat(LocalSimulation simulation)
	{
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
		
		// --- Kenney character (FBX model) ---
		_playerModel = LoadPlayerModel();
		
		// --- Animations Kenney ---
		// Create AnimationPlayer as child of MODEL (pas du PlayerController)
		// pour que les chemins relatifs des animations fonctionnent correctement.
		_animPlayer = new AnimationPlayer();
		_animPlayer.Name = "AnimationPlayer";
		
		// Add AnimationPlayer to model (or PlayerController if no model)
		if (_playerModel != null)
		{
			_playerModel.AddChild(_animPlayer);
		}
		else
		{
			AddChild(_animPlayer);
		}
		
		// Set RootNode on the skeleton so relative paths
		// like "Hips", "Spine", etc. resolve relative to the skeleton
		var skeleton = _playerModel != null ? FindSkeleton(_playerModel) : null;
		if (skeleton != null)
		{
			_animPlayer.RootNode = skeleton.GetPath();
			GD.Print($"AnimationPlayer RootNode set to: {skeleton.GetPath()}");
		}
		
		var animLib = new AnimationLibrary();
		_animPlayer.AddAnimationLibrary("default", animLib);
		
		// Charger les animations depuis les fichiers FBX
		// Different approach : on clone l'animation et on remplace
		// the "Root|" prefix with "" (empty) since RootNode is already on the skeleton.
		// Les os du squelette s'appellent "Hips", "Spine", etc. directement.
		LoadAnimationsFromFbx(animLib, "res://assets/characters/Animations/idle.fbx", "idle");
		LoadAnimationsFromFbx(animLib, "res://assets/characters/Animations/run.fbx", "run");
		LoadAnimationsFromFbx(animLib, "res://assets/characters/Animations/jump.fbx", "jump");
		
		// Fallback: try loading from model
		if (animLib.GetAnimationList().Count == 0)
		{
			GD.Print("No animations loaded from separate FBX files, trying model's embedded animations...");
			LoadAnimationsFromModel(animLib);
		}
		
		// Jouer l'animation disponible
		if (animLib.HasAnimation("idle"))
			_animPlayer.Play("default/idle");
		else if (animLib.GetAnimationList().Count > 0)
		{
			string firstAnim = animLib.GetAnimationList()[0];
			GD.Print($"Playing first available animation: {firstAnim}");
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
		
		// Spell actions (Keycode for layout-aware letter matching)
		AddInputAction("spell_slot1", new InputEventKey { PhysicalKeycode = Key.Key1 });
		AddInputAction("spell_slot2", new InputEventKey { PhysicalKeycode = Key.Key2 });
		AddInputAction("spell_slot3", new InputEventKey { PhysicalKeycode = Key.Key3 });
		AddInputAction("spell_slot4", new InputEventKey { PhysicalKeycode = Key.Key4 });
		AddInputAction("spell_slotA", new InputEventKey { Keycode = Key.A });
		AddInputAction("spell_slotE", new InputEventKey { Keycode = Key.E });
		AddInputAction("spell_shift", new InputEventKey { Keycode = Key.Shift });
		AddInputAction("spell_elite", new InputEventKey { Keycode = Key.R });
		
		// UI actions
		AddInputAction("spellbook_toggle", new InputEventKey { Keycode = Key.B });
		AddInputAction("ui_cancel",         new InputEventKey { Keycode = Key.Escape });
		AddInputAction("target_next",       new InputEventKey { PhysicalKeycode = Key.Tab });
		
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
		// Si le spellbook est ouvert, on ignore les clics souris
		// so drag & drop works without camera moving
		if (IsSpellBookOpen)
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
		else if (Input.IsActionJustPressed("spell_slotA")) TriggerSpellSlot(SlotType.SlotA);
		else if (Input.IsActionJustPressed("spell_slotE")) TriggerSpellSlot(SlotType.SlotE);
		else if (Input.IsActionJustPressed("spell_shift")) TriggerSpellSlot(SlotType.Shift);
		else if (Input.IsActionJustPressed("spell_elite")) TriggerSpellSlot(SlotType.Elite);
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
	// PHYSICS
	// ==========================================
	
	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		
		if (_knockbackVelocity.LengthSquared() > 0.001f)
		{
			_knockbackVelocity = _knockbackVelocity.Lerp(Vector3.Zero, _knockbackDecay * dt);
			Velocity = _knockbackVelocity;
		}
		else
		{
			_knockbackVelocity = Vector3.Zero;
			
			bool isGrounded = IsOnFloor();
			bool inputJump = Input.IsActionPressed("jump");
			
			if (isGrounded)
			{
				// --- ON GROUND: player-relative direction (pas a la camera) ---
				// Left click must NOT influence la direction de deplacement.
				Vector3 inputDir = Vector3.Zero;
				
				// Use player direction (Transform.Basis.Z) qui n'est changee
				// que par le clic droit.
				Vector3 playerForward = -Transform.Basis.Z;
				playerForward.Y = 0;
				playerForward = playerForward.Normalized();
				Vector3 playerRight = Transform.Basis.X;
				playerRight.Y = 0;
				playerRight = playerRight.Normalized();
				
				if (Input.IsActionPressed("move_forward"))  inputDir += playerForward;
				if (Input.IsActionPressed("move_back"))     inputDir -= playerForward;
				if (Input.IsActionPressed("move_left"))     inputDir -= playerRight;
				if (Input.IsActionPressed("move_right"))    inputDir += playerRight;
				
				if (inputDir.LengthSquared() > 0f)
					inputDir = inputDir.Normalized();
				
				// Apply ground speed
				float speed = Speed;
				Velocity = new Vector3(inputDir.X * speed, Velocity.Y, inputDir.Z * speed);
				
				// FIXED JUMP (WoW-style) : on garde la Velocity.X/Z calculee a cette frame
				if (inputJump)
				{
					Velocity = new Vector3(Velocity.X, JumpForce, Velocity.Z);
				}
			}
			else
			{
				// --- IN AIR: strict WoW behavior ---
				// Do NOT touch Velocity.X or Velocity.Z!
				// Keyboard and camera are ignored pour la trajectoire.
				// Character continues on its trajectory XZ initiale.
			}
		}
		
		// Gravity (applied even if knockback active)
		if (!IsOnFloor())
		{
			Velocity -= new Vector3(0f, Gravity * dt, 0f);
			
			// Clamp vertical velocity pour eviter de traverser le sol
			if (Velocity.Y < -100f)
				Velocity = new Vector3(Velocity.X, -100f, Velocity.Z);
		}
		
		MoveAndSlide();
		
		// Safety check : si on est sous le sol, on remonte
		if (GlobalPosition.Y < 0f && IsOnFloor())
		{
			GlobalPosition = new Vector3(GlobalPosition.X, 1f, GlobalPosition.Z);
			Velocity = new Vector3(Velocity.X, 0f, Velocity.Z);
		}
		
		// --- Animations ---
		UpdateAnimations();
		
		// Fall safety net
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
		
		// Adjust model scale and position
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
	
	private void LoadAnimationsFromFbx(AnimationLibrary animLib, string fbxPath, string animName)
	{
		if (!ResourceLoader.Exists(fbxPath))
		{
			GD.Print($"Animation file not found: {fbxPath}");
			return;
		}
		
		var scene = GD.Load<PackedScene>(fbxPath);
		if (scene == null)
		{
			GD.Print($"Failed to load animation scene: {fbxPath}");
			return;
		}
		
		var tempInstance = scene.Instantiate<Node>();
		if (tempInstance == null)
		{
			GD.Print($"Failed to instantiate animation scene: {fbxPath}");
			return;
		}
		
		// Chercher un AnimationPlayer dans l'instance
		var animPlayerInScene = FindAnimationPlayer(tempInstance);
		if (animPlayerInScene != null)
		{
			// Copier toutes les animations de l'AnimationLibrary
			foreach (var libName in animPlayerInScene.GetAnimationLibraryList())
			{
				var lib = animPlayerInScene.GetAnimationLibrary(libName);
				if (lib != null)
				{
					foreach (var animNameInLib in lib.GetAnimationList())
					{
						var anim = lib.GetAnimation(animNameInLib);
						if (anim != null)
						{
							// IMPORTANT: Les animations Kenney utilisent des chemins comme "Root|Hips"
							// but in our scene the skeleton is at a different path.
							// We must remap paths pour qu'ils pointent vers le squelette du modele.
							anim = RemapAnimationPaths(anim, animNameInLib);
							if (anim != null)
							{
								animLib.AddAnimation(animName, anim);
								GD.Print($"Loaded animation: {animName} (from {animNameInLib} in {fbxPath})");
							}
						}
					}
				}
			}
		}
		else
		{
			GD.Print($"No AnimationPlayer found in {fbxPath}, trying direct Animation resource...");
			
			try
			{
				var directAnim = ResourceLoader.Load<Animation>(fbxPath);
				if (directAnim != null)
				{
					directAnim = RemapAnimationPaths(directAnim, animName);
					if (directAnim != null)
					{
						animLib.AddAnimation(animName, directAnim);
						GD.Print($"Loaded animation directly: {animName} from {fbxPath}");
					}
				}
			}
			catch (Exception ex)
			{
				GD.Print($"Could not load animation directly from {fbxPath}: {ex.Message}");
			}
		}
		
		tempInstance.QueueFree();
	}
	
	/// <summary>
/// Remaps Kenney animation paths (which use "Root|...")
	/// to the actual skeleton paths in our scene.
	/// Kenney animations target nodes like "Root|Hips", "Root|Spine", etc.
	/// "Root|" is the root node prefix in the FBX animation file.
	/// Since our AnimationPlayer's RootNode is set to the skeleton,
	/// we replace "Root|" with "" (empty) so paths become
	/// just "Hips", "Spine", etc. (relative to the skeleton).
	/// We Duplicate to avoid modifying the shared original animation.
	/// </summary>
	private Animation? RemapAnimationPaths(Animation anim, string animName)
	{
		// Make a copy to avoid modifying the shared original
		var animCopy = (Animation)anim.Duplicate();
		
		// Remap paths in the copied animation
		int trackCount = animCopy.GetTrackCount();
		for (int i = 0; i < trackCount; i++)
		{
			string trackPath = animCopy.TrackGetPath(i);
			// Les chemins Kenney sont comme "Root|Hips:position" ou "Root|Hips:rotation_quaternion"
			// On remplace "Root|" par "" (vide) car le RootNode de l'AnimationPlayer
			// is already set to the skeleton
			if (trackPath.Contains("Root|"))
			{
				string newPath = trackPath.Replace("Root|", "");
				animCopy.TrackSetPath(i, new NodePath(newPath));
				GD.Print($"  Remapped track: {trackPath} -> {newPath}");
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
	
	private void LoadAnimationsFromModel(AnimationLibrary animLib)
	{
		if (_playerModel == null) return;
		
		var animPlayerInModel = FindAnimationPlayer(_playerModel);
		if (animPlayerInModel != null)
		{
			GD.Print("Found AnimationPlayer in model!");
			foreach (var libName in animPlayerInModel.GetAnimationLibraryList())
			{
				var lib = animPlayerInModel.GetAnimationLibrary(libName);
				if (lib != null)
				{
					foreach (var animName in lib.GetAnimationList())
					{
						var anim = lib.GetAnimation(animName);
						if (anim != null)
						{
							animLib.AddAnimation(animName, anim);
							GD.Print($"Loaded animation from model: {animName}");
						}
					}
				}
			}
		}
		else
		{
			GD.Print("No AnimationPlayer found in model either.");
		}
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
	
	private void UpdateAnimations()
	{
		if (_animPlayer == null) return;
		
		bool isGrounded = IsOnFloor();
		float hSpeed = new Vector3(Velocity.X, 0f, Velocity.Z).Length();
		bool isMoving = hSpeed > 1f;
		
		string targetAnim;
		
		if (!isGrounded)
		{
			targetAnim = "default/jump";
		}
		else if (isMoving)
		{
			targetAnim = "default/run";
		}
		else
		{
			targetAnim = "default/idle";
		}
		
		if (_animPlayer.HasAnimation(targetAnim) && _animPlayer.CurrentAnimation != targetAnim)
		{
			_animPlayer.Play(targetAnim, 0.2f); // Crossfade de 0.2s
		}
	}
	
}
