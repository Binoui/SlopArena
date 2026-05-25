#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using MoveBox.Shared;

/// <summary>
/// Contrôleur de mouvement Action-MMO pour Godot 4 C#.
/// - Caméra WoW-like gérée par WowCamera (SpringArm3D)
/// - ZQSD relatif à la direction de la caméra
/// - Clic gauche : tourner la caméra
/// - Clic droit : tourner caméra + personnage
/// - Physique réaliste : échelles en mètres, inertie du saut conservée
/// </summary>
public partial class PlayerController : CharacterBody3D
{
	// ==========================================
	// VARIABLES EXPORTÉES (échelles réalistes)
	// ==========================================
	
	[Export] public float Vitesse = 35.0f;       // Course rapide et nerveuse
	[Export] public float VitesseRecul = 22.0f;  // Recul bridé
	[Export] public float ForceSaut = 14.0f;     // Impulsion verticale proportionnelle
	[Export] public float Gravite = 45.0f;       // Gravité lourde pour retomber vite au sol
	
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
	// RÉFÉRENCES
	// ==========================================
	
	private WowCamera? _wowCamera;
	private AnimationPlayer? _animPlayer;
	private Node3D? _playerModel;
	private CombatComponent? _combatComponent;
	
	// ==========================================
	// ÉTAT UI
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
	private const float ClickThreshold = 5f; // pixels max de mouvement pour considérer comme un clic (pas un drag)
	
	// ==========================================
	// ÉVÉNEMENTS
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
	// GETTERS PUBLICS
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
	// INITIALISATION
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
		
		// --- Personnage Kenney (modèle FBX) ---
		_playerModel = LoadPlayerModel();
		
		// --- Animations Kenney ---
		// On crée l'AnimationPlayer comme enfant du MODÈLE (pas du PlayerController)
		// pour que les chemins relatifs des animations fonctionnent correctement.
		_animPlayer = new AnimationPlayer();
		_animPlayer.Name = "AnimationPlayer";
		
		// Ajouter l'AnimationPlayer au modèle (ou au PlayerController si pas de modèle)
		if (_playerModel != null)
		{
			_playerModel.AddChild(_animPlayer);
		}
		else
		{
			AddChild(_animPlayer);
		}
		
		// Définir le RootNode sur le squelette pour que les chemins relatifs
		// comme "Hips", "Spine", etc. soient résolus par rapport au squelette
		var skeleton = _playerModel != null ? FindSkeleton(_playerModel) : null;
		if (skeleton != null)
		{
			_animPlayer.RootNode = skeleton.GetPath();
			GD.Print($"AnimationPlayer RootNode set to: {skeleton.GetPath()}");
		}
		
		var animLib = new AnimationLibrary();
		_animPlayer.AddAnimationLibrary("default", animLib);
		
		// Charger les animations depuis les fichiers FBX
		// On utilise une approche différente : on clone l'animation et on remplace
		// le préfixe "Root|" par "" (vide) puisque le RootNode est déjà sur le squelette.
		// Les os du squelette s'appellent "Hips", "Spine", etc. directement.
		LoadAnimationsFromFbx(animLib, "res://assets/characters/Animations/idle.fbx", "idle");
		LoadAnimationsFromFbx(animLib, "res://assets/characters/Animations/run.fbx", "run");
		LoadAnimationsFromFbx(animLib, "res://assets/characters/Animations/jump.fbx", "jump");
		
		// Fallback: essayer de charger depuis le modèle
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
		
		// --- Hurtbox (pour recevoir les dégâts) ---
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
		
		// --- Caméra WoW-like (SpringArm3D) ---
		// WowCamera crée son propre SpringArm3D et Camera3D dans _Ready
		_wowCamera = new WowCamera();
		_wowCamera.Name = "WowCamera";
		_wowCamera.Target = this;
		AddChild(_wowCamera);
		
		// Spawn au centre de l'arène
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
		// pour permettre le drag & drop sans que la caméra bouge
		if (IsSpellBookOpen)
		{
			if (Input.IsActionJustPressed("ui_cancel"))
			{
				Input.MouseMode = Input.MouseModeEnum.Visible;
			}
			// On ignore les events souris
			return;
		}
		
		// 1. Gestion de l'état de la souris (Visible vs Capturée)
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
					// Clic droit : capture immédiate (comme avant)
					if (Input.MouseMode != Input.MouseModeEnum.Captured)
					{
						Input.MouseMode = Input.MouseModeEnum.Captured;
					}
					
					// Aligner le personnage sur la direction de la caméra
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
				// Clic gauche relâché
				if (mouseBtn.ButtonIndex == MouseButton.Left)
				{
					if (!_leftClickIsDrag)
					{
						// Clic rapide sans drag → raycast pour cibler
						DoClickRaycast();
					}
					// Si c'était un drag, la souris est Capturée → la libérer
					if (Input.MouseMode == Input.MouseModeEnum.Captured)
					{
						Input.MouseMode = Input.MouseModeEnum.Visible;
					}
				}
				
				// Clic droit relâché
				if (mouseBtn.ButtonIndex == MouseButton.Right)
				{
					if (!Input.IsMouseButtonPressed(MouseButton.Left))
					{
						Input.MouseMode = Input.MouseModeEnum.Visible;
					}
				}
			}
		}
		
		// 2. Mouvement de la souris → orbite caméra (quand capturée)
		if (@event is InputEventMouseMotion mouseMotion)
		{
			// Détection de drag pour le clic gauche (avant capture)
			if (Input.IsMouseButtonPressed(MouseButton.Left) && Input.MouseMode != Input.MouseModeEnum.Captured)
			{
				_leftClickDragDistance += mouseMotion.Relative.Length();
				if (_leftClickDragDistance > ClickThreshold && !_leftClickIsDrag)
				{
					// Le joueur a assez bougé → c'est un drag, pas un clic → capturer
					_leftClickIsDrag = true;
					Input.MouseMode = Input.MouseModeEnum.Captured;
				}
			}
			
			// Rotation caméra (une fois capturé)
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
	// PHYSIQUE
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
				// --- AU SOL : direction relative au JOUEUR (pas à la caméra) ---
				// Le clic gauche ne doit PAS influencer la direction de déplacement.
				Vector3 inputDir = Vector3.Zero;
				
				// Utiliser la direction du joueur (Transform.Basis.Z) qui n'est changée
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
				
				// Application de la vitesse au sol
				float speed = Vitesse;
				Velocity = new Vector3(inputDir.X * speed, Velocity.Y, inputDir.Z * speed);
				
				// SAUT FIXE (style WoW) : on garde la Velocity.X/Z calculée à cette frame
				if (inputJump)
				{
					Velocity = new Vector3(Velocity.X, ForceSaut, Velocity.Z);
				}
			}
			else
			{
				// --- EN L'AIR : comportement WoW strict ---
				// On ne touche PAS à Velocity.X ni Velocity.Z !
				// Le clavier et la caméra sont ignorés pour la trajectoire.
				// Le personnage continue sur sa lancée XZ initiale.
			}
		}
		
		// Gravité (appliquée même si knockback actif)
		if (!IsOnFloor())
		{
			Velocity -= new Vector3(0f, Gravite * dt, 0f);
			
			// Limiter la vélocité verticale pour éviter de traverser le sol
			if (Velocity.Y < -100f)
				Velocity = new Vector3(Velocity.X, -100f, Velocity.Z);
		}
		
		MoveAndSlide();
		
		// Vérification supplémentaire : si on est sous le sol, on remonte
		if (GlobalPosition.Y < 0f && IsOnFloor())
		{
			GlobalPosition = new Vector3(GlobalPosition.X, 1f, GlobalPosition.Z);
			Velocity = new Vector3(Velocity.X, 0f, Velocity.Z);
		}
		
		// --- Animations ---
		UpdateAnimations();
		
		// Filet de sécurité anti-chute
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
	// CLIC → RAYCAST → CIBLAGE
	// ==========================================
	
	/// <summary>
	/// Lance un raycast depuis la caméra vers la position de la souris pour détecter un clic sur une unité.
	/// Les dummies sont sur le layer de collision 2, le joueur sur le layer 1.
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
				// Remonter dans le parent jusqu'à trouver un CharacterBody3D
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
	// SORTS (utilisant la direction du joueur)
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
	// CHARGEMENT DU MODÈLE
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
		
		// Appliquer un skin à TOUS les MeshInstance3D du modèle
		var skinTex = GD.Load<Texture2D>("res://assets/characters/Skins/skaterMaleA.png");
		ApplySkinRecursive(playerInstance, skinTex);
		
		// Ajuster l'échelle et la position du modèle
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
				// Si pas de skin, au moins mettre un matériau visible
				var mat2 = new StandardMaterial3D();
				mat2.AlbedoColor = new Color(0.7f, 0.7f, 0.7f, 1f);
				mi.MaterialOverride = mat2;
			}
		}
		
		// Continuer la récursion pour les enfants
		foreach (var child in node.GetChildren())
		{
			ApplySkinRecursive(child, skinTex);
		}
	}
	
	// ==========================================
	// CHARGEMENT DES ANIMATIONS
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
							// mais dans notre scène le squelette est à un chemin différent.
							// On doit remapper les chemins pour qu'ils pointent vers le squelette du modèle.
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
	/// Remappe les chemins des animations Kenney (qui utilisent "Root|...") 
	/// vers les chemins réels du squelette dans notre scène.
	/// Les animations Kenney ciblent des noeuds comme "Root|Hips", "Root|Spine", etc.
	/// Le "Root|" est le préfixe du noeud racine dans le fichier FBX d'animation.
	/// Puisque notre AnimationPlayer a son RootNode défini sur le squelette,
	/// on doit remplacer "Root|" par "" (vide) pour que les chemins deviennent
	/// juste "Hips", "Spine", etc. (relatifs au squelette).
	/// On fait une copie (Duplicate) pour ne pas modifier l'animation originale partagée.
	/// </summary>
	private Animation? RemapAnimationPaths(Animation anim, string animName)
	{
		// Faire une copie pour ne pas modifier l'original partagé
		var animCopy = (Animation)anim.Duplicate();
		
		// Remapper les chemins dans l'animation copiée
		int trackCount = animCopy.GetTrackCount();
		for (int i = 0; i < trackCount; i++)
		{
			string trackPath = animCopy.TrackGetPath(i);
			// Les chemins Kenney sont comme "Root|Hips:position" ou "Root|Hips:rotation_quaternion"
			// On remplace "Root|" par "" (vide) car le RootNode de l'AnimationPlayer
			// est déjà défini sur le squelette
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
	/// Trouve récursivement un Skeleton3D dans l'arbre de noeuds.
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
	/// Recherche récursivement un AnimationPlayer dans l'arbre de noeuds.
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
