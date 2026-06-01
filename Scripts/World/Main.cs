using Godot;
using System;
using SlopArena.Shared;

public partial class Main : Node3D
{
	private PlayerController? _player;
	private PlayerController?[] _npcs = new PlayerController?[5];
	private LocalSimulation? _simulation;
	private ProjectileManager? _projectileMgr;
	private Label? _label;
	private CanvasLayer? _canvasLayer;
	private ActionBarHUD? _actionBarHUD;
	private UnitFrames? _unitFrames;
	private EscapeMenuUI? _escapeMenu;
	
	// Cercle de ciblage (WoW-style ring under target)
	private MeshInstance3D? _targetRing;
	
	public override async void _Ready()
	{
		GD.Print("SlopArena 3D C# Client Started!");
		
		// Force fullscreen — UI elements size themselves to viewport on Build()
		DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
		
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
		
		// --- Crosshair (TPS aiming reticle) ---
		CreateCrosshair();
		
		// --- Projectile Manager (Object Pooling) ---
		_projectileMgr = new ProjectileManager();
		_projectileMgr.Name = "ProjectileManager";
		AddChild(_projectileMgr);
		
		// --- Local Simulation (pure C# combat logic) ---
		_simulation = new LocalSimulation();
		_simulation.Name = "LocalSimulation";
		_simulation.ProjectileVisuals = _projectileMgr;
		AddChild(_simulation);
		
		// --- NPC Enemies (5 PlayerController instances, not player controlled) ---
		Vector3[] npcPositions = new Vector3[]
		{
			new Vector3(60f, 1.5f, 60f),
			new Vector3(140f, 1.5f, 60f),
			new Vector3(100f, 1.5f, 80f),
			new Vector3(60f, 1.5f, 140f),
			new Vector3(140f, 1.5f, 140f),
		};
		
		for (int i = 0; i < 5; i++)
		{
			var npc = new PlayerController();
			npc.Name = $"NPC_{i}";
			npc.Position = npcPositions[i];
			// Cycle through classes
			npc.SetClass((CharacterClass)(i % 3));
			AddChild(npc);
			npc.SetNPC(true);
			npc.SetNpcSpawnPosition(npcPositions[i]);
			_npcs[i] = npc;
		}
		
		// --- Targeting Ring (WoW-style circle under target) ---
		_targetRing = CreateTargetRing();
		AddChild(_targetRing);
		_targetRing.Visible = false;
		
		// Register NPCs and player in simulation
		RegisterEntitiesInSimulation();
		
		// --- Player ---
		_player = new PlayerController();
		_player.Name = "Player";
		AddChild(_player);
		_player.Position = new Vector3(100f, 1.5f, 100f);
		
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
		
		// Register NPC combat components
		RegisterNpcCombatComponents();
		
		// Wire up HUD
		if (_player != null)
		{
			_player.OnStateUpdated += UpdateHUD;
			
			// Tab targeting: soft lock — raycast from camera center
			_player.OnTargetNextPressed += () =>
			{
				if (_unitFrames == null) return;
				
				var viewport = GetViewport();
				if (viewport == null) return;
				
				var camera = viewport.GetCamera3D();
				if (camera == null) return;
				
				var center = viewport.GetVisibleRect().Size / 2;
				var from = camera.ProjectRayOrigin(center);
				var dir = camera.ProjectRayNormal(center);
				var to = from + dir * 100f;
				
				var query = PhysicsRayQueryParameters3D.Create(from, to);
				query.CollisionMask = 2; // Layer 2 = entities (dummies)
				var result = GetWorld3D().DirectSpaceState.IntersectRay(query);
				
				if (result.Count > 0 && result.ContainsKey("collider"))
				{
					var collider = (Node?)result["collider"];
					while (collider != null)
					{
						if (collider is not CharacterBody3D)
						{
							collider = collider.GetParent();
							continue;
						}
						// Check if it's an NPC
						string name = collider.Name;
						if (name.StartsWith("NPC_") && int.TryParse(name.AsSpan("NPC_".Length), out int idx))
						{
							if (idx >= 0 && idx < _npcs.Length && _npcs[idx] != null && _npcs[idx]!.IsNpcAlive())
							{
								ulong targetId = (ulong)(100 + idx);
								_unitFrames.SetTarget(targetId);
								UpdateTargetRing(targetId);
							}
						}
						break;
					}
				}
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
		_unitFrames.Setup(_player!, _npcs);
		
		// --- Action Bar HUD (bottom bar with cooldowns) ---
		// Must be added to CanvasLayer (Control nodes need CanvasLayer)
		_actionBarHUD = new ActionBarHUD();
		_actionBarHUD.Name = "ActionBarHUD";
		_canvasLayer.AddChild(_actionBarHUD);
		
		// Connect to player for class abilities
		if (_player != null)
		{
			_actionBarHUD.Setup(_player);
			GD.Print("ActionBarHUD connected!");
			
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
			
			// --- Escape Menu (pause overlay) ---
			_escapeMenu = new EscapeMenuUI();
			_escapeMenu.Name = "EscapeMenuUI";
			_canvasLayer.AddChild(_escapeMenu);
			_escapeMenu.Build();

			// Connect escape menu events
			_escapeMenu.OnResumePressed += () => { };
			_escapeMenu.OnClassSelected += (CharacterClass pc) =>
			{
				if (_player != null)
				{
					_player.SetClass(pc);
					_actionBarHUD?.OnClassChanged();
					GD.Print($"Switched to {pc}");
				}
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
	
	private void RegisterEntitiesInSimulation()
	{
		if (_simulation == null) return;
		
		for (int i = 0; i < 5; i++)
		{
			ulong entityId = (ulong)(100 + i); // NPC IDs: 100-104
			if (_npcs[i] != null)
				_simulation.Entities[entityId] = (_npcs[i]!.GlobalPosition, 1.5f, true);
		}
		
		// Connect hit events from simulation to NPCs
		_simulation.OnEntityHit += (ulong entityId, float damage, float kbX, float kbY, float kbZ) =>
		{
			int npcIndex = (int)(entityId - 100);
			if (npcIndex >= 0 && npcIndex < 5 && _npcs[npcIndex] != null)
			{
				_npcs[npcIndex]!.NpcTakeDamage((int)damage, new Vector3(kbX, kbY, kbZ));
			}
			
			// Auto-target the first NPC that hits the player
			if (entityId == 1 && _unitFrames != null && !_unitFrames.HasTarget())
			{
				_unitFrames.SetTarget(100);
			}
		};
	}
	
	private void RegisterNpcCombatComponents()
	{
		if (_simulation == null) return;
		
		for (int i = 0; i < 5; i++)
		{
			if (_npcs[i] == null) continue;
			ulong entityId = (ulong)(100 + i);
			var combat = _npcs[i]!.GetCombatComponent();
			if (combat != null)
				_simulation.CombatComponents[entityId] = combat;
		}
		
		GD.Print("NPC CombatComponents registered.");
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
		
		if (entityId == 0 || entityId < 100 || entityId >= 105)
		{
			_targetRing.Visible = false;
			return;
		}
		
		// NPC IDs are 100-104
		int npcIndex = (int)(entityId - 100);
		if (npcIndex < 0 || npcIndex >= 5 || _npcs[npcIndex] == null || !_npcs[npcIndex]!.IsNpcAlive())
		{
			_targetRing.Visible = false;
			return;
		}
		
		Vector3 pos = _npcs[npcIndex]!.GlobalPosition;
		pos.Y = 0.1f;
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
				if (idx >= 0 && idx < 5 && _npcs[idx] != null)
				{
					if (_npcs[idx]!.IsNpcAlive())
					{
						Vector3 pos = _npcs[idx]!.GlobalPosition;
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
		for (int i = 0; i < 5; i++)
		{
			if (_npcs[i] != null)
			{
				string status = _npcs[i]!.IsNpcAlive() ? $"{_npcs[i]!.GetNpcHP()}/300" : "[DEAD]";
				dummyInfo += $"  NPC {i+1}: {status}\n";
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
	
	private void CreateCrosshair()
	{
		var crosshair = new ColorRect();
		crosshair.Name = "Crosshair";
		crosshair.MouseFilter = Control.MouseFilterEnum.Ignore;
		
		float center = 960f;
		float mid = 540f;
		float size = 8f;
		float gap = 4f;
		float thickness = 2f;
		var white = new Color(1f, 1f, 1f, 0.8f);
		
		var top = new ColorRect();
		top.Color = white;
		top.Position = new Vector2(center - thickness / 2, mid - gap - size);
		top.Size = new Vector2(thickness, size);
		top.MouseFilter = Control.MouseFilterEnum.Ignore;
		crosshair.AddChild(top);
		
		var bottom = new ColorRect();
		bottom.Color = white;
		bottom.Position = new Vector2(center - thickness / 2, mid + gap);
		bottom.Size = new Vector2(thickness, size);
		bottom.MouseFilter = Control.MouseFilterEnum.Ignore;
		crosshair.AddChild(bottom);
		
		var left = new ColorRect();
		left.Color = white;
		left.Position = new Vector2(center - gap - size, mid - thickness / 2);
		left.Size = new Vector2(size, thickness);
		left.MouseFilter = Control.MouseFilterEnum.Ignore;
		crosshair.AddChild(left);
		
		var right = new ColorRect();
		right.Color = white;
		right.Position = new Vector2(center + gap, mid - thickness / 2);
		right.Size = new Vector2(size, thickness);
		right.MouseFilter = Control.MouseFilterEnum.Ignore;
		crosshair.AddChild(right);
		
		var dot = new ColorRect();
		dot.Color = new Color(1f, 0f, 0f, 0.9f);
		dot.Position = new Vector2(center - 1.5f, mid - 1.5f);
		dot.Size = new Vector2(3f, 3f);
		dot.MouseFilter = Control.MouseFilterEnum.Ignore;
		crosshair.AddChild(dot);
		
		_canvasLayer?.AddChild(crosshair);
	}
}
