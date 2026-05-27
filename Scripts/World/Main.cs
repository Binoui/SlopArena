using Godot;
using System;
using SlopArena.Shared;

public partial class Main : Node3D
{
	private PlayerController? _player;
	private DummyManager? _dummyMgr;
	private LocalSimulation? _simulation;
	private ProjectileManager? _projectileMgr;
	private Label? _label;
	private CanvasLayer? _canvasLayer;
	private ActionBarHUD? _actionBarHUD;
	private SpellBookUI? _spellBookUI;
	private UnitFrames? _unitFrames;
	private EscapeMenuUI? _escapeMenu;
	
	// Cercle de ciblage (WoW-style ring under target)
	private MeshInstance3D? _targetRing;
	
	public override async void _Ready()
	{
		GD.Print("SlopArena 3D C# Client Started!");
		
		_canvasLayer = GetNodeOrNull<CanvasLayer>("CanvasLayer");
		if (_canvasLayer == null)
		{
			_canvasLayer = new CanvasLayer();
			_canvasLayer.Name = "CanvasLayer";
			AddChild(_canvasLayer);
		}
		
		_label = _canvasLayer.GetNodeOrNull<Label>("Label");
		if (_label == null)
		{
			_label = new Label();
			_label.Name = "Label";
			_label.Size = new Vector2(600f, 200f);
			_canvasLayer.AddChild(_label);
		}
		
		// Toujours appliquer le positionnement quoi qu'il arrive
		_label.AddThemeFontSizeOverride("font_size", 18);
		_label.HorizontalAlignment = HorizontalAlignment.Right;
		
		// --- Projectile Manager (Object Pooling) ---
		_projectileMgr = new ProjectileManager();
		_projectileMgr.Name = "ProjectileManager";
		AddChild(_projectileMgr);
		
		// --- Local Simulation (pure C# combat logic) ---
		_simulation = new LocalSimulation();
		_simulation.Name = "LocalSimulation";
		_simulation.ProjectileVisuals = _projectileMgr;
		AddChild(_simulation);
		
		// --- Dummy Manager ---
		_dummyMgr = new DummyManager();
		AddChild(_dummyMgr);
		
		// --- Targeting Ring (WoW-style circle under target) ---
		_targetRing = CreateTargetRing();
		AddChild(_targetRing);
		_targetRing.Visible = false;
		
		// Register dummies in the simulation
		RegisterDummiesInSimulation();
		
		// --- Player ---
		_player = new PlayerController();
		_player.Name = "Player";
		AddChild(_player);
		
		// Setup combat component (for spell hit detection)
		if (_simulation != null)
			_player.SetupCombat(_simulation);
		
		// Register player in the simulation's combat component map
		var playerCombat = _player.GetCombatComponent();
		if (playerCombat != null && _simulation != null)
		{
			_simulation.CombatComponents[1] = playerCombat;
		}
		
		// Register player in the simulation
		RegisterPlayerInSimulation();
		
		// Give dummies CombatComponents for status testing
		RegisterDummyCombatComponents();
		
		// Wire up HUD
		if (_player != null)
		{
			_player.OnStateUpdated += UpdateHUD;
			
			// Tab targeting: cycle through dummies
			_player.OnTargetNextPressed += () =>
			{
				if (_unitFrames == null) return;
				
				ulong currentTarget = _unitFrames.GetTarget();
				ulong nextTarget = 0;
				
				if (currentTarget == 0)
				{
					// No target — target the first alive dummy
					nextTarget = 100;
				}
				else
				{
					// Cycle to next alive dummy
					int currentIndex = (int)(currentTarget - 100);
					for (int i = 1; i <= 5; i++)
					{
						int nextIndex = (currentIndex + i) % 5;
						if (_dummyMgr != null && _dummyMgr.IsAlive(nextIndex))
						{
							nextTarget = (ulong)(100 + nextIndex);
							break;
						}
					}
				}
				
				_unitFrames.SetTarget(nextTarget);
				UpdateTargetRing(nextTarget);
			};
			
			// Click targeting: left-click on a dummy to target it
			_player.OnLeftClickEntity += (ulong entityId) =>
			{
				if (_unitFrames == null) return;
				_unitFrames.SetTarget(entityId);
				UpdateTargetRing(entityId);
			};
		}
		
		// --- Unit Frames (top-left player + target HP bars) ---
		_unitFrames = new UnitFrames();
		_unitFrames.Name = "UnitFrames";
		_canvasLayer.AddChild(_unitFrames);
		_unitFrames.Setup(_player!, _dummyMgr);
		
		// --- Action Bar HUD (bottom bar with cooldowns) ---
		// Must be added to CanvasLayer (Control nodes need CanvasLayer)
		_actionBarHUD = new ActionBarHUD();
		_actionBarHUD.Name = "ActionBarHUD";
		_canvasLayer.AddChild(_actionBarHUD);
		
		// Connecter au SpellSystem du joueur (via CombatComponent)
		if (_player != null)
		{
			var combat = _player.GetCombatComponent();
			if (combat != null)
			{
				var spellSystem = combat.GetSpellSystem();
				if (spellSystem != null)
				{
					_actionBarHUD.Setup(_player, spellSystem);
					GD.Print("ActionBarHUD connected to SpellSystem!");
					
					// --- Spell Book UI (full-screen spell browser) ---
					_spellBookUI = new SpellBookUI();
					_spellBookUI.Name = "SpellBookUI";
					_canvasLayer.AddChild(_spellBookUI);
					_spellBookUI.Setup(_player, spellSystem);
					
					// --- Status routing ---
					// Route status application requests through the simulation
					if (_simulation != null)
					{
						_simulation.OnStatusApply += (ulong targetId, StatusType type, float duration, ulong sourceId) =>
						{
							if (_simulation.CombatComponents.TryGetValue(targetId, out var targetCombat))
							{
								targetCombat.ApplyStatus(type, duration, sourceId);
							}
						};
						
						_simulation.OnStatusConsume += (ulong targetId, StatusType type) =>
						{
							if (_simulation.CombatComponents.TryGetValue(targetId, out var targetCombat))
							{
								return targetCombat.ConsumeStatus(type);
							}
							return false;
						};
					}
					
					GD.Print("SpellBookUI initialized!");

					// --- Escape Menu (pause overlay) ---
					_escapeMenu = new EscapeMenuUI();
					_escapeMenu.Name = "EscapeMenuUI";
					_canvasLayer.AddChild(_escapeMenu);
					_escapeMenu.Build();

					// Connect escape menu events
					_escapeMenu.OnResumePressed += () => { };
					_escapeMenu.OnSpellbookRequested += () =>
					{
						_spellBookUI?.Toggle();
					};
					_escapeMenu.OnExitLobby += () =>
					{
						GD.Print("Exit Lobby - would return to main menu");
						GetTree().ChangeSceneToFile("res://main.tscn");
					};
					_escapeMenu.OnExitGame += () =>
					{
						GD.Print("Exiting game...");
						GetTree().Quit();
					};
				}
			}
		}
		
		// Wait for physics sync then generate heightmap
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		
		try
		{
			HeightmapGenerator.Generate(GetWorld3D());
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Heightmap generation failed: {ex.Message}");
		}
	}
	
	private void RegisterDummiesInSimulation()
	{
		if (_simulation == null || _dummyMgr == null) return;
		
		for (int i = 0; i < 5; i++)
		{
			ulong entityId = (ulong)(100 + i); // Dummy IDs: 100-104
			Vector3 pos = DummyManager.DummyPositions[i];
			_simulation.Entities[entityId] = (pos, DummyManager.DummyHitRadius, true);
		}
		
		// Connect hit events from simulation to dummy manager
		_simulation.OnEntityHit += (ulong entityId, float damage, float kbX, float kbY, float kbZ) =>
		{
			int dummyIndex = (int)(entityId - 100);
			if (dummyIndex >= 0 && dummyIndex < 5)
			{
				_dummyMgr.DamageDummy(dummyIndex, (int)damage);
			}
			
			// Auto-target the first dummy that hits the player
			if (entityId == 1 && _unitFrames != null && !_unitFrames.HasTarget())
			{
				// Find the attacker (whoever dealt the damage)
				// For now, auto-target dummy 0 as a test
				_unitFrames.SetTarget(100);
			}
		};
	}
	
	private void RegisterDummyCombatComponents()
	{
		if (_simulation == null || _dummyMgr == null) return;
		
		for (int i = 0; i < 5; i++)
		{
			ulong entityId = (ulong)(100 + i);
			var dummyCombat = new CombatComponent();
			dummyCombat.Name = $"DummyCombat_{i}";
			dummyCombat.Setup(_dummyMgr, _simulation, entityId);
			_dummyMgr.AddChild(dummyCombat);
			_simulation.CombatComponents[entityId] = dummyCombat;
		}
		
		GD.Print("Dummy CombatComponents registered for status testing.");
	}
	
	private void RegisterPlayerInSimulation()
	{
		if (_simulation == null || _player == null) return;
		
		ulong playerId = 1;
		_simulation.Entities[playerId] = (_player.GlobalPosition, 2.0f, true);
		
		// Update player position in simulation each frame
		_player.OnStateUpdated += (float posX, float posZ, float posY, float velX, float velZ) =>
		{
			_simulation.Entities[playerId] = (
				new Vector3(posX, posY, posZ),
				2.0f,
				true
			);
		};
	}
	
	/// <summary>
	/// Get the simulation instance (for spells to use).
	/// </summary>
	public LocalSimulation? GetSimulation() => _simulation;
	
	// ==========================================
	// TARGETING RING (WoW-style)
	// ==========================================
	
	/// <summary>
	/// Create a flat ring mesh under the target (yellow-ish, emissive).
	/// </summary>
	private MeshInstance3D CreateTargetRing()
	{
		var ring = new MeshInstance3D();
		
		// Create a flat ring with SurfaceTool
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		
		float innerRadius = 0.8f;
		float outerRadius = 2.2f;
		int segments = 32;
		
		for (int i = 0; i < segments; i++)
		{
			float a1 = (float)i / segments * Mathf.Tau;
			float a2 = (float)(i + 1) / segments * Mathf.Tau;
			
			float c1 = Mathf.Cos(a1), s1 = Mathf.Sin(a1);
			float c2 = Mathf.Cos(a2), s2 = Mathf.Sin(a2);
			
			Vector3 in1 = new Vector3(c1 * innerRadius, 0, s1 * innerRadius);
			Vector3 in2 = new Vector3(c2 * innerRadius, 0, s2 * innerRadius);
			Vector3 out1 = new Vector3(c1 * outerRadius, 0, s1 * outerRadius);
			Vector3 out2 = new Vector3(c2 * outerRadius, 0, s2 * outerRadius);
			
			st.AddVertex(in1);
			st.AddVertex(out1);
			st.AddVertex(in2);
			st.AddVertex(in2);
			st.AddVertex(out1);
			st.AddVertex(out2);
		}
		
		st.GenerateNormals();
		ring.Mesh = st.Commit();
		
		// Yellowish emissive material
		var mat = new StandardMaterial3D
		{
			AlbedoColor = new Color(1f, 0.85f, 0.2f),
			EmissionEnabled = true,
			Emission = new Color(1f, 0.8f, 0.1f),
			EmissionEnergyMultiplier = 3f,
			Metallic = 0.3f,
			Roughness = 0.5f,
		};
		ring.MaterialOverride = mat;
		
		return ring;
	}
	
	/// <summary>
	/// Show/hide and position the targeting ring under the given entity.
	/// </summary>
	private void UpdateTargetRing(ulong entityId)
	{
		if (_targetRing == null) return;
		
		if (entityId == 0 || _dummyMgr == null)
		{
			_targetRing.Visible = false;
			return;
		}
		
		// Dummy IDs are 100-104
		int dummyIndex = (int)(entityId - 100);
		if (dummyIndex < 0 || dummyIndex >= 5 || !_dummyMgr.IsAlive(dummyIndex))
		{
			_targetRing.Visible = false;
			return;
		}
		
		Vector3 pos = DummyManager.DummyPositions[dummyIndex];
		pos.Y = 0.1f; // Juste au-dessus du sol
		_targetRing.Position = pos;
		_targetRing.Visible = true;
	}
	
	public override void _Process(double delta)
	{
		// Keep targeting ring on the target (in case dummy respawns or target changes via click)
		if (_unitFrames != null && _targetRing != null)
		{
			ulong targetId = _unitFrames.GetTarget();
			if (targetId > 0)
			{
				int idx = (int)(targetId - 100);
				if (idx >= 0 && idx < 5 && _dummyMgr != null)
				{
					if (_dummyMgr.IsAlive(idx))
					{
						Vector3 pos = DummyManager.DummyPositions[idx];
						pos.Y = 0.1f;
						_targetRing.Position = pos;
						_targetRing.Visible = true;
					}
					else
					{
						_targetRing.Visible = false;
					}
				}
			}
			else
			{
				_targetRing.Visible = false;
			}
		}
	}
	
	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey key && key.Pressed && !key.Echo)
		{
			if (key.Keycode == Key.Escape || key.PhysicalKeycode == Key.Escape)
			{
				if (_escapeMenu != null && _player != null)
				{
					_escapeMenu.Toggle(_player);
					if (_escapeMenu.IsOpen())
						_player.IsEscapeMenuOpen = true;
					else
						_player.IsEscapeMenuOpen = false;
					GetViewport().SetInputAsHandled();
				}
			}
		}
	}

	private void UpdateHUD(float posX, float posY, float posZ, float velX, float velY)
	{
		if (_label == null) return;
		
		float speed2D = MathF.Sqrt(velX * velX + velY * velY);
		
		string dummyInfo = "";
		if (_dummyMgr != null)
		{
			for (int i = 0; i < 5; i++)
			{
				string status = _dummyMgr.IsAlive(i) ? $"{_dummyMgr.GetHP(i)}/{_dummyMgr.GetMaxHP()}" : "[DEAD]";
				dummyInfo += $"  D{i+1}: {status}\n";
			}
		}
		
_label.Text = $"SlopArena Arena Sandbox\n" +
					  $"---------------------------------\n" +
					  $"Speed: {speed2D:F1}\n" +
					  $"Position: ({posX:F1}, {posY:F1}, {posZ:F1})\n" +
					  $"\n" +
					  $"--- CONTROLS ---\n" +
					  $"Souris : Viser / Tourner\n" +
					  $"ZQSD : Movement\n" +
					  $"Space : Saut\n" +
					  $"Shift : Dash (air dash si en l'air)\n" +
					  $"Ctrl : Roll (au sol, distance fixe)\n" +
					  $"1-4, A, E, R : Sorts\n" +
					  $"B : Spellbook (assigner des sorts)\n" +
					  $"Escape: Release mouse";
	}
}