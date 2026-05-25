#nullable enable
using Godot;
using System;
using MoveBox.Shared;

/// <summary>
/// Melee-type spells (knockbacks, charges, area control).
/// 
/// PURELY VISUAL: These methods only create Godot visual effects.
/// All hit detection logic is delegated to CombatComponent → SpellResolver (Shared).
/// 
/// Each method takes a CombatComponent (generic: works for player, dummy, AI bot).
/// </summary>
public static partial class MeleeSpells
{
	// ==========================================
	// SLOT 1 : Sword Slash (90° cone in front)
	// ==========================================
	
	public static void SwordSlash(CombatComponent combat)
	{
		Vector3 forward = combat.GetOwnerForward();
		Vector3 pos = combat.GetOwnerPosition();
		float range = 6f;
		float radius = 3f;
		
		// Visual only
		CreateConeVisual(combat, pos, forward, range, radius, new Color(1f, 1f, 1f, 0.3f), 0.3f);
		
		// Hit check via CombatComponent (uses Shared/SpellResolver)
		combat.CheckMeleeCone(pos, forward, range, 90f, 10f, 15f, 5f);
	}
	
	// ==========================================
	// SLOT 2 : Power Strike (60° cone, strong knockback)
	// ==========================================
	
	public static void PowerStrike(CombatComponent combat)
	{
		Vector3 forward = combat.GetOwnerForward();
		Vector3 pos = combat.GetOwnerPosition();
		float range = 7f;
		float radius = 4f;
		
		// Visual only
		CreateConeVisual(combat, pos, forward, range, radius, new Color(1f, 0.8f, 0.2f, 0.4f), 0.4f);
		
		// Hit check via CombatComponent
		combat.CheckMeleeCone(pos, forward, range, 60f, 15f, 30f, 10f);
	}
	
	// ==========================================
	// SLOT 3 : Shockwave (90° cone, horizontal knockback)
	// ==========================================
	
	public static void Shockwave(CombatComponent combat)
	{
		Vector3 forward = combat.GetOwnerForward();
		Vector3 pos = combat.GetOwnerPosition();
		float range = 8f;
		float radius = 5f;
		
		// Visual only
		CreateConeVisual(combat, pos, forward, range, radius, new Color(0.6f, 0.8f, 1f, 0.3f), 0.5f);
		
		// Hit check via CombatComponent
		combat.CheckMeleeCone(pos, forward, range, 90f, 12f, 25f, 0f);
	}
	
	// ==========================================
	// SLOT A : Chain Pull (attracts enemy toward player)
	// ==========================================
	
	private partial class ChainProjectile : Area3D
	{
		private Vector3 _direction;
		private float _speed;
		private float _maxTravel;
		private float _traveled = 0f;
		private CombatComponent _combat = null!;
		private Vector3 _startPos;
		private LocalSimulation? _simulation;
		
		public void Setup(Vector3 direction, float speed, float lifetime, CombatComponent combat)
		{
			_direction = direction;
			_speed = speed;
			_maxTravel = speed * lifetime;
			_combat = combat;
			_startPos = combat.GetOwnerPosition();
			_simulation = combat.GetSimulation();
			
			var collisionShape = new CollisionShape3D();
			var sphere = new SphereShape3D();
			sphere.Radius = 1.5f;
			collisionShape.Shape = sphere;
			AddChild(collisionShape);
			
			var mesh = new MeshInstance3D();
			var sphereMesh = new SphereMesh();
			sphereMesh.Radius = 0.3f;
			sphereMesh.Height = 0.6f;
			var mat = new StandardMaterial3D
			{
				EmissionEnabled = true,
				Emission = new Color(0.3f, 0.8f, 1f),
				EmissionEnergyMultiplier = 3f,
				AlbedoColor = new Color(0.3f, 0.8f, 1f),
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
			};
			sphereMesh.Material = mat;
			mesh.Mesh = sphereMesh;
			AddChild(mesh);
		}
		
		public override void _PhysicsProcess(double delta)
		{
			float dt = (float)delta;
			GlobalPosition += _direction * _speed * dt;
			_traveled += _speed * dt;
			
			if (_traveled >= _maxTravel)
			{
				QueueFree();
				return;
			}
			
			// Check hit via simulation entities
			if (_simulation != null)
			{
				foreach (var kvp in _simulation.Entities)
				{
					ulong targetId = kvp.Key;
					var (targetPos, radius, active) = kvp.Value;
					
					if (!active || targetId == _combat.GetEntityId()) continue;
					
					float dx = targetPos.X - GlobalPosition.X;
					float dz = targetPos.Z - GlobalPosition.Z;
					float dist = MathF.Sqrt(dx * dx + dz * dz);
					
					if (dist < 1.5f + radius)
					{
						GD.Print($"ChainPull hit entity {targetId}!");
						
						// INVERTED knockback: pulls enemy toward player
						Vector3 pullDir = (_startPos - GlobalPosition).Normalized();
						pullDir.Y = 0f;
						
						_simulation.OnEntityHit?.Invoke(targetId, 5f, pullDir.X * 40f, 5f, pullDir.Z * 40f);
						
						QueueFree();
						return;
					}
				}
			}
		}
	}
	
	public static void ChainPull(CombatComponent combat)
	{
		Vector3 forward = combat.GetOwnerForward();
		Vector3 pos = combat.GetOwnerPosition();
		
		var chainProj = new ChainProjectile();
		chainProj.Name = "ChainProjectile";
		chainProj.Setup(forward, 60f, 1.5f, combat);
		
		AddToScene(combat, chainProj);
		chainProj.GlobalPosition = pos + forward * 2f + new Vector3(0f, 1f, 0f);
	}
	
	// ==========================================
	// SLOT E : Iron Wall (defensive zone)
	// ==========================================
	
	public static void IronWall(CombatComponent combat)
	{
		Vector3 pos = combat.GetOwnerPosition();
		float radius = 4f;
		float duration = 3f;
		
		var mesh = new MeshInstance3D();
		var cylinder = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.5f };
		var mat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.5f, 0.5f, 1f, 0.3f),
			EmissionEnabled = true,
			Emission = new Color(0.3f, 0.3f, 1f),
			EmissionEnergyMultiplier = 1.5f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			NoDepthTest = true
		};
		mesh.Mesh = cylinder;
		mesh.MaterialOverride = mat;
		
		var owner = combat.GetOwnerNode();
		if (owner != null)
		{
			owner.AddChild(mesh);
			mesh.GlobalPosition = new Vector3(pos.X, 0.25f, pos.Z);
		}
		
		var timer = combat.GetTree().CreateTimer(duration);
		timer.Timeout += () => mesh.QueueFree();
		
		GD.Print("IronWall!");
	}
	
	// ==========================================
	// SHIFT : Charge (forward velocity, ignores inputs)
	// ==========================================
	
	public static void Charge(CombatComponent combat)
	{
		Vector3 forward = combat.GetOwnerForward();
		float chargeSpeed = 80f;
		float chargeDuration = 0.4f;
		
		combat.ApplyKnockback(forward * chargeSpeed);
		
		Vector3 pos = combat.GetOwnerPosition();
		var mesh = new MeshInstance3D();
		var cylinder = new CylinderMesh { TopRadius = 1.5f, BottomRadius = 1.5f, Height = 3f };
		var mat = new StandardMaterial3D
		{
			AlbedoColor = new Color(1f, 1f, 0.5f, 0.2f),
			EmissionEnabled = true,
			Emission = new Color(1f, 1f, 0.5f),
			EmissionEnergyMultiplier = 2f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			NoDepthTest = true
		};
		mesh.Mesh = cylinder;
		mesh.MaterialOverride = mat;
		
		var owner = combat.GetOwnerNode();
		if (owner != null)
		{
			owner.AddChild(mesh);
			mesh.GlobalPosition = new Vector3(pos.X, 1.5f, pos.Z);
		}
		
		var timer = combat.GetTree().CreateTimer(chargeDuration);
		timer.Timeout += () => mesh.QueueFree();
		
		GD.Print("Charge!");
	}
	
	// ==========================================
	// ELITE (R) : Ground Pound (jump + AoE on landing)
	// ==========================================
	
	public static void GroundPound(CombatComponent combat)
	{
		var owner = combat.GetOwnerNode();
		if (owner is CharacterBody3D cb)
		{
			cb.Velocity = new Vector3(cb.Velocity.X, 15f, cb.Velocity.Z);
		}
		
		Vector3 pos = combat.GetOwnerPosition();
		
		var indicator = new MeshInstance3D();
		var cyl = new CylinderMesh { TopRadius = 8f, BottomRadius = 8f, Height = 0.3f };
		var mat = new StandardMaterial3D
		{
			AlbedoColor = new Color(1f, 0.2f, 0.2f, 0.3f),
			EmissionEnabled = true,
			Emission = new Color(1f, 0f, 0f),
			EmissionEnergyMultiplier = 2f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			NoDepthTest = true
		};
		cyl.Material = mat;
		indicator.Mesh = cyl;
		indicator.GlobalPosition = new Vector3(pos.X, 0.15f, pos.Z);
		AddToScene(combat, indicator);
		
		var checkTimer = combat.GetTree().CreateTimer(0.05f, false);
		System.Action checkLanding = () => {};
		checkLanding = () =>
		{
			if (!GodotObject.IsInstanceValid(owner) || !GodotObject.IsInstanceValid(indicator))
				return;
			
			if (owner is CharacterBody3D cb2 && cb2.IsOnFloor())
			{
				Vector3 impactPos = cb2.GlobalPosition;
				indicator.GlobalPosition = new Vector3(impactPos.X, 0.15f, impactPos.Z);
				
				mat.AlbedoColor = new Color(1f, 1f, 1f, 0.6f);
				mat.Emission = new Color(1f, 1f, 1f);
				mat.EmissionEnergyMultiplier = 4f;
				
				// Hit check via CombatComponent
				combat.CheckCircleHit(impactPos, 8f, 30f, 50f, 15f);
				
				var cleanup = combat.GetTree().CreateTimer(0.5f);
				cleanup.Timeout += () => indicator.QueueFree();
				
				GD.Print("GroundPound!");
				return;
			}
			
			if (owner != null)
				indicator.GlobalPosition = new Vector3(owner.GlobalPosition.X, 0.15f, owner.GlobalPosition.Z);
			
			var t = combat.GetTree().CreateTimer(0.05f, false);
			t.Timeout += checkLanding;
		};
		
		checkTimer.Timeout += checkLanding;
	}
	
	// ==========================================
	// BLADESTORM: spin attack, AoE around self, multiple hits
	// ==========================================
	
	/// <summary>
	/// Bladestorm: spin attack, AoE around self, multiple hits.
	/// </summary>
	public static void Bladestorm(CombatComponent combat)
	{
		Vector3 pos = combat.GetOwnerPosition();
		float radius = 4f;
		int hitCount = 5;
		float interval = 0.3f;
		
		// Visual: initial spin
		CreateConeVisual(combat, pos, combat.GetOwnerForward(), 4f, 4f, new Color(0.8f, 0.8f, 0.8f, 0.4f), hitCount * interval);
		
		// Multiple tick hits around the player
		for (int i = 0; i < hitCount; i++)
		{
			var timer = combat.GetTree().CreateTimer(i * interval);
			timer.Timeout += () =>
			{
				combat.CheckCircleHit(pos, radius, 18f, 8f, 3f);
			};
		}
		
		GD.Print("Bladestorm!");
	}
	
	// ==========================================
	// HELPERS
	// ==========================================
	
	private static void CreateConeVisual(CombatComponent combat, Vector3 pos, Vector3 forward, 
	                                      float range, float radius, Color color, float duration)
	{
		var mesh = new MeshInstance3D();
		var cylinder = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = range };
		var mat = new StandardMaterial3D
		{
			AlbedoColor = color,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			NoDepthTest = true
		};
		mesh.Mesh = cylinder;
		mesh.MaterialOverride = mat;
		float midX = pos.X + forward.X * range * 0.5f;
		float midZ = pos.Z + forward.Z * range * 0.5f;
		float yaw = MathF.Atan2(-forward.X, -forward.Z);
		mesh.Rotation = new Vector3(0f, yaw, 0f);
		
		var owner = combat.GetOwnerNode();
		if (owner != null)
		{
			owner.AddChild(mesh);
			mesh.GlobalPosition = new Vector3(midX, 2f, midZ);
		}
		
		var timer = combat.GetTree().CreateTimer(duration);
		timer.Timeout += () => mesh.QueueFree();
	}
	
	private static void AddToScene(CombatComponent combat, Node3D node)
	{
		var tree = combat.GetTree();
		if (tree?.CurrentScene != null)
			tree.CurrentScene.AddChild(node);
	}
}
