using Godot;
using System;

/// <summary>
/// Manages training dummies: positions, HP, visual feedback (flash on hit), auto-respawn.
/// Each dummy has a CharacterBody3D (like a real player/entity) with a Hurtbox child.
/// This makes them compatible with projectile collision detection (BodyEntered + AreaEntered).
/// </summary>
public partial class DummyManager : Node3D
{
	// Training dummies positions (must match the arena layout)
	public static readonly Vector3[] DummyPositions = new Vector3[]
	{
		new Vector3(60f, 0.1f, 60f),
		new Vector3(140f, 0.1f, 60f),
		new Vector3(100f, 0.1f, 100f),
		new Vector3(60f, 0.1f, 140f),
		new Vector3(140f, 0.1f, 140f),
	};
	public const float DummyHitRadius = 20f;
	public const float DummyContactRadius = 30f;
	
	// HP system
	private int[] _hp = new int[5];
	private const int MaxHP = 100;
	
	// Respawn timers (seconds)
	private float[] _respawnTimers = new float[5];
	private const float RespawnDelay = 3.0f;
	
	// Visual feedback timers
	private float[] _hitFlashTimers = new float[5];
	
	// Original emission energy (to restore after flash)
	private float[] _originalEmissionEnergy = new float[5];
	
	// Mesh references
	private MeshInstance3D[] _dummyMeshes = new MeshInstance3D[5];
	
	// Physics bodies (CharacterBody3D, one per dummy)
	private CharacterBody3D[] _dummyBodies = new CharacterBody3D[5];
	
	// Hurtbox references (one per dummy, child of CharacterBody)
	private Hurtbox[] _dummyHurtboxes = new Hurtbox[5];
	
	public override void _Ready()
	{
		// Initialize HP
		for (int i = 0; i < _hp.Length; i++)
			_hp[i] = MaxHP;
		
		// Create physics bodies + hurtboxes + meshes for each dummy
		for (int i = 0; i < 5; i++)
		{
			CreateDummy(i);
		}
	}
	
	private void CreateDummy(int index)
	{
		Vector3 pos = DummyPositions[index];
		
		// --- CharacterBody3D (like a real player/entity) ---
		var body = new CharacterBody3D();
		body.Name = $"DummyBody_{index}";
		
		// Collision layers: Layer 2 (entities)
		body.CollisionLayer = 2;
		body.CollisionMask = 0; // Dummies don't need to detect anything
		
		// Collision shape
		var collisionShape = new CollisionShape3D();
		var cylinder = new CylinderShape3D();
		cylinder.Radius = DummyHitRadius;
		cylinder.Height = 4.0f;
		collisionShape.Shape = cylinder;
		body.AddChild(collisionShape);
		
		body.Position = pos;
		
		AddChild(body);
		_dummyBodies[index] = body;
		
		// --- Hurtbox (child of CharacterBody) ---
		var hurtbox = new Hurtbox();
		hurtbox.Name = $"DummyHurtbox_{index}";
		hurtbox.OwnerEntity = this;
		
		int dummyIndex = index;
		
		var hurtboxShape = new CollisionShape3D();
		var sphere = new SphereShape3D();
		sphere.Radius = DummyHitRadius * 1.2f;
		hurtboxShape.Shape = sphere;
		hurtbox.AddChild(hurtboxShape);
		
		body.AddChild(hurtbox);
		_dummyHurtboxes[index] = hurtbox;
		
		hurtbox.OnHit += (Vector3 attackerPos, float damage, Vector3 knockbackForce) =>
		{
			DamageDummy(dummyIndex, (int)damage);
		};
		
		// --- Mesh (CapsuleMesh like the original scene) ---
		var mesh = new MeshInstance3D();
		mesh.Name = $"DummyMesh_{index}";
		
		var capsule = new CapsuleMesh();
		capsule.Radius = 6.0f;
		capsule.Height = 18.0f;
		mesh.Mesh = capsule;
		
		var mat = new StandardMaterial3D
		{
			AlbedoColor = new Color(1f, 0.2f, 0.2f),
			Metallic = 0.5f,
			Roughness = 0.3f,
			EmissionEnabled = true,
			Emission = new Color(0.8f, 0f, 0f),
			EmissionEnergyMultiplier = 1.5f
		};
		mesh.MaterialOverride = mat;
		
		// Position the mesh relative to the body (centered)
		mesh.Position = new Vector3(0f, 0f, 0f);
		
		body.AddChild(mesh);
		_dummyMeshes[index] = mesh;
		
		// Save original emission energy for flash restore
		_originalEmissionEnergy[index] = mat.EmissionEnergyMultiplier;
	}
	
	public override void _Process(double delta)
	{
		float dt = (float)delta;
		for (int i = 0; i < 5; i++)
		{
			// Update hit flash (visuel : émission boostée)
			if (_hitFlashTimers[i] > 0)
			{
				_hitFlashTimers[i] -= dt;
				
				if (_dummyMeshes[i] != null)
				{
					var mat = _dummyMeshes[i].MaterialOverride as StandardMaterial3D;
					if (mat != null)
					{
						mat.EmissionEnergyMultiplier = 8.0f;
					}
				}
			}
			else
			{
				if (_dummyMeshes[i] != null)
				{
					var mat = _dummyMeshes[i].MaterialOverride as StandardMaterial3D;
					if (mat != null && !Mathf.IsEqualApprox(mat.EmissionEnergyMultiplier, _originalEmissionEnergy[i]))
					{
						mat.EmissionEnergyMultiplier = _originalEmissionEnergy[i];
					}
				}
			}
			
			// Update respawn timer
			if (_respawnTimers[i] > 0)
			{
				_respawnTimers[i] -= dt;
				if (_respawnTimers[i] <= 0)
				{
					RespawnDummy(i);
				}
			}
			
			// Update visibility (hidden during respawn)
			bool alive = _respawnTimers[i] <= 0;
			if (_dummyBodies[i] != null)
				_dummyBodies[i].Visible = alive;
			if (_dummyHurtboxes[i] != null)
				_dummyHurtboxes[i].Visible = alive;
		}
	}
	
	/// <summary>
	/// Deal damage to a dummy. Returns true if the dummy was hit (was alive).
	/// </summary>
	public bool DamageDummy(int index, int damage)
	{
		if (index < 0 || index >= _hp.Length) return false;
		if (_respawnTimers[index] > 0) return false;
		
		_hp[index] -= damage;
		FlashDummy(index);
		
		if (_hp[index] <= 0)
		{
			_hp[index] = 0;
			_respawnTimers[index] = RespawnDelay;
			GD.Print($"Dummy {index+1} DEFEATED! Respawning in {RespawnDelay}s");
			return true;
		}
		
		GD.Print($"Dummy {index+1} took {damage} damage! HP: {_hp[index]}/{MaxHP}");
		return true;
	}
	
	public int GetHP(int index)
	{
		if (index < 0 || index >= _hp.Length) return 0;
		return _hp[index];
	}
	
	public int GetMaxHP() => MaxHP;
	
	public bool IsAlive(int index)
	{
		if (index < 0 || index >= _hp.Length) return false;
		return _respawnTimers[index] <= 0 && _hp[index] > 0;
	}
	
	private void RespawnDummy(int index)
	{
		_hp[index] = MaxHP;
		_respawnTimers[index] = 0;
		GD.Print($"Dummy {index+1} respawned!");
	}
	
	public void FlashDummy(int index, float duration = 0.3f)
	{
		if (index >= 0 && index < _hitFlashTimers.Length)
			_hitFlashTimers[index] = duration;
	}
	
	public float GetContactRadius() => DummyContactRadius;
}
