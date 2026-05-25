#nullable enable
using Godot;
using System;
using MoveBox.Shared;

/// <summary>
/// Ranged-type spells (fireballs, AoE, zone control).
/// 
/// PURELY VISUAL: These methods only create Godot visual effects.
/// All hit detection logic is delegated to CombatComponent → SpellResolver (Shared).
/// 
/// Each method takes a CombatComponent (generic: works for player, dummy, AI bot).
/// </summary>
public static class RangedSpells
{
	// ==========================================
	// SLOT 1 : Fire Bolt (Fireball toward camera direction)
	// ==========================================
	
	public static void FireBolt(CombatComponent combat)
	{
		combat.FireProjectile(1, combat.GetOwnerPosition() + combat.GetCameraForward() * 2f + new Vector3(0f, 1f, 0f), combat.GetCameraForward());
	}
	
	// ==========================================
	// SLOT 2 : Flame (fast fireball, short CD)
	// ==========================================
	
	public static void Flame(CombatComponent combat)
	{
		combat.FireProjectile(1, combat.GetOwnerPosition() + combat.GetCameraForward() * 2f + new Vector3(0f, 1f, 0f), combat.GetCameraForward());
	}
	
	// ==========================================
	// SLOT 3 : Flame Shock (delayed AoE at 12m)
	// ==========================================
	
	public static void FlameShock(CombatComponent combat)
	{
		Vector3 camForward = combat.GetCameraForward();
		float distance = 12f;
		float radius = 5f;
		float delay = 0.5f;
		Vector3 playerPos = combat.GetOwnerPosition();
		
		Vector3 impactPos = playerPos + camForward * distance;
		impactPos.Y = 0.5f;
		
		// Visual indicator
		var indicator = CreateAoEIndicator(impactPos, radius, new Color(1f, 0.3f, 0f, 0.4f));
		AddToScene(combat, indicator);
		
		var delayTimer = combat.GetTree().CreateTimer(delay);
		delayTimer.Timeout += () =>
		{
			if (!GodotObject.IsInstanceValid(indicator))
				return;
			
			// Flash effect
			if (indicator.GetChild(0) is MeshInstance3D mi && mi.MaterialOverride is StandardMaterial3D mat)
			{
				mat.AlbedoColor = new Color(1f, 1f, 0.5f, 0.6f);
				mat.Emission = new Color(1f, 0.5f, 0f);
				mat.EmissionEnergyMultiplier = 4f;
			}
			
			// Hit check via CombatComponent (uses Shared/SpellResolver)
			combat.CheckCircleHit(impactPos, radius, 20f, 20f, 8f);
			
			var cleanup = combat.GetTree().CreateTimer(0.5f);
			cleanup.Timeout += () => indicator.QueueFree();
		};
	}
	
	// ==========================================
	// SLOT 4 : Fire Wall (barrier of flames)
	// ==========================================
	
	public static void FireWall(CombatComponent combat)
	{
		Vector3 forward = combat.GetOwnerForward();
		Vector3 pos = combat.GetOwnerPosition();
		float wallWidth = 8f;
		float wallDuration = 4f;
		
		int segments = 5;
		for (int s = 0; s < segments; s++)
		{
			float offset = (s - segments / 2) * (wallWidth / segments);
			Vector3 wallPos = pos + forward * 4f + new Vector3(
				-forward.Z * offset / wallWidth * wallWidth,
				0.5f,
				forward.X * offset / wallWidth * wallWidth
			);
			
			var pillar = new MeshInstance3D();
			var cyl = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 3f };
			var mat = new StandardMaterial3D
			{
				AlbedoColor = new Color(1f, 0.3f, 0f, 0.5f),
				EmissionEnabled = true,
				Emission = new Color(1f, 0.2f, 0f),
				EmissionEnergyMultiplier = 3f,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				NoDepthTest = true
			};
			cyl.Material = mat;
			pillar.Mesh = cyl;
			pillar.GlobalPosition = wallPos;
			AddToScene(combat, pillar);
			
			var timer = combat.GetTree().CreateTimer(wallDuration);
			timer.Timeout += () => pillar.QueueFree();
		}
		
		GD.Print("FireWall!");
	}
	
	// ==========================================
	// SLOT A : Poison Flask (ground zone)
	// ==========================================
	
	public static void PoisonFlask(CombatComponent combat)
	{
		Vector3 forward = combat.GetOwnerForward();
		float fixedDist = 5f;
		float radius = 4f;
		float duration = 3f;
		Vector3 pos = combat.GetOwnerPosition();
		
		float impactX = pos.X + forward.X * fixedDist;
		float impactZ = pos.Z + forward.Z * fixedDist;
		Vector3 impactPos = new Vector3(impactX, 0.5f, impactZ);
		
		// Visual
		var cyl = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 1f };
		var mat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0f, 0.8f, 0f, 0.4f),
			EmissionEnabled = true,
			Emission = new Color(0f, 0.5f, 0f),
			EmissionEnergyMultiplier = 1.5f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			NoDepthTest = true
		};
		var zoneMesh = new MeshInstance3D { Mesh = cyl, MaterialOverride = mat };
		AddToScene(combat, zoneMesh);
		zoneMesh.GlobalPosition = impactPos;
		
		// Hit check via CombatComponent
		combat.CheckCircleHit(impactPos, radius, 8f, 5f, 2f);
		
		var timer = combat.GetTree().CreateTimer(duration);
		timer.Timeout += () => zoneMesh.QueueFree();
		
		GD.Print("Cast Poison Flask!");
	}
	
	// ==========================================
	// SLOT E : Blaze (powerful fireball)
	// ==========================================
	
	public static void Blaze(CombatComponent combat)
	{
		combat.FireProjectile(1, combat.GetOwnerPosition() + combat.GetCameraForward() * 2f + new Vector3(0f, 1f, 0f), combat.GetCameraForward());
	}
	
	// ==========================================
	// SHIFT : Blink (teleport)
	// ==========================================
	
	public static void Blink(CombatComponent combat)
	{
		// Blink is a movement spell - teleport forward
		Vector3 forward = combat.GetOwnerForward();
		float blinkDist = 15f;
		
		Vector3 newPos = combat.GetOwnerPosition() + forward * blinkDist;
		newPos.Y = Math.Max(newPos.Y, 2f);
		
		// Apply via the owner's Node3D
		var owner = combat.GetOwnerNode();
		if (owner != null)
		{
			owner.GlobalPosition = newPos;
			if (owner is CharacterBody3D cb)
				cb.Velocity = Vector3.Zero;
		}
		
		GD.Print($"Blink ! Nouvelle position: {newPos}");
	}
	
	// ==========================================
	// ELITE (R) : Meteor (large AoE with delay)
	// ==========================================
	
	public static void Meteor(CombatComponent combat)
	{
		Vector3 camForward = combat.GetCameraForward();
		float distance = 15f;
		float radius = 8f;
		float delay = 1.0f;
		Vector3 playerPos = combat.GetOwnerPosition();
		
		Vector3 impactPos = playerPos + camForward * distance;
		impactPos.Y = 0.5f;
		
		// Indicator
		var indicator = CreateAoEIndicator(impactPos, radius, new Color(1f, 0f, 0f, 0.3f));
		AddToScene(combat, indicator);
		
		// Meteor visual (falling from sky)
		var meteor = new MeshInstance3D();
		var sphere = new SphereMesh();
		sphere.Radius = 1.5f;
		sphere.Height = 3f;
		var meteorMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(1f, 0.3f, 0f),
			EmissionEnabled = true,
			Emission = new Color(1f, 0.2f, 0f),
			EmissionEnergyMultiplier = 3f,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
		};
		sphere.Material = meteorMat;
		meteor.Mesh = sphere;
		meteor.GlobalPosition = impactPos + new Vector3(0f, 20f, 0f);
		AddToScene(combat, meteor);
		
		// Fall animation
		float fallDuration = delay;
		float startY = 20f;
		var fallTimer = combat.GetTree().CreateTimer(0.016f, false);
		float elapsed = 0f;
		System.Action fallUpdate = () => {};
		fallUpdate = () =>
		{
			if (!GodotObject.IsInstanceValid(meteor))
				return;
			
			elapsed += 0.016f;
			float t = elapsed / fallDuration;
			if (t >= 1f) t = 1f;
			
			meteor.GlobalPosition = new Vector3(impactPos.X, startY * (1f - t), impactPos.Z);
			
			if (t < 1f)
			{
				var t2 = combat.GetTree().CreateTimer(0.016f, false);
				t2.Timeout += fallUpdate;
			}
		};
		fallTimer.Timeout += fallUpdate;
		
		// Impact
		var delayTimer = combat.GetTree().CreateTimer(delay);
		delayTimer.Timeout += () =>
		{
			if (!GodotObject.IsInstanceValid(indicator) || !GodotObject.IsInstanceValid(meteor))
				return;
			
			// Flash
			if (indicator.GetChild(0) is MeshInstance3D mi && mi.MaterialOverride is StandardMaterial3D mat)
			{
				mat.AlbedoColor = new Color(1f, 1f, 1f, 0.8f);
				mat.Emission = new Color(1f, 1f, 1f);
				mat.EmissionEnergyMultiplier = 5f;
			}
			
			meteor.QueueFree();
			
			// Hit check via CombatComponent
			combat.CheckCircleHit(impactPos, radius, 40f, 40f, 20f);
			
			var cleanup = combat.GetTree().CreateTimer(0.5f);
			cleanup.Timeout += () => indicator.QueueFree();
		};
	}
	
	/// <summary>
	/// Nova: huge burst AoE around self.
	/// </summary>
	public static void NovaSpell(CombatComponent combat)
	{
		Vector3 pos = combat.GetOwnerPosition();
		float radius = 7f;
		
		// Expanding ring visual
		var indicator = CreateAoEIndicator(pos, radius, new Color(0.8f, 0.5f, 1f, 0.4f));
		AddToScene(combat, indicator);
		
		// Hit check
		combat.CheckCircleHit(pos, radius, 50f, 40f, 15f);
		
		var cleanup = combat.GetTree().CreateTimer(0.5f);
		cleanup.Timeout += () => indicator.QueueFree();
		
		GD.Print("Nova!");
	}
	
	// ==========================================
	// HELPERS
	// ==========================================
	
	private static MeshInstance3D CreateAoEIndicator(Vector3 position, float radius, Color color)
	{
		var mesh = new MeshInstance3D();
		var cyl = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.3f };
		var mat = new StandardMaterial3D
		{
			AlbedoColor = color,
			EmissionEnabled = true,
			Emission = new Color(color.R, color.G, color.B),
			EmissionEnergyMultiplier = 2f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			NoDepthTest = true
		};
		cyl.Material = mat;
		mesh.Mesh = cyl;
		mesh.GlobalPosition = position;
		return mesh;
	}
	
	private static void AddToScene(CombatComponent combat, Node3D node)
	{
		var tree = combat.GetTree();
		if (tree?.CurrentScene != null)
			tree.CurrentScene.AddChild(node);
	}
}
