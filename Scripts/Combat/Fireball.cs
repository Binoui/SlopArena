#nullable enable
using Godot;
using System;
using SlopArena.Shared;

/// <summary>
/// Visual-only Fireball projectile.
/// No collision logic - just renders mesh, particles, and light.
/// Position is updated by ProjectileManager based on simulation data.
/// 
/// Uses Object Pooling: created once, reused via Reset().
/// </summary>
public partial class Fireball : Node3D
{
	private MeshInstance3D? _coreMesh;
	private GpuParticles3D? _trailParticles;
	private OmniLight3D? _light;
	
	public override void _Ready()
	{
		CreateVisuals();
	}
	
	private void CreateVisuals()
	{
		// Cœur de la boule de feu
		_coreMesh = new MeshInstance3D();
		var sphere = new SphereMesh();
		sphere.Radius = 0.4f;
		sphere.Height = 0.8f;
		sphere.RadialSegments = 12;
		sphere.Rings = 8;
		var coreMat = new StandardMaterial3D
		{
			EmissionEnabled = true,
			Emission = new Color(1f, 0.4f, 0f),
			EmissionEnergyMultiplier = 4.0f,
			AlbedoColor = new Color(1f, 0.5f, 0f),
			Metallic = 0.3f,
			Roughness = 0.2f
		};
		_coreMesh.Mesh = sphere;
		_coreMesh.MaterialOverride = coreMat;
		_coreMesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		AddChild(_coreMesh);
		
		// Trainee de particules
		_trailParticles = new GpuParticles3D();
		_trailParticles.Name = "FireTrail";
		_trailParticles.Emitting = true;
		_trailParticles.Amount = 25;
		_trailParticles.Lifetime = 0.4f;
		_trailParticles.OneShot = false;
		_trailParticles.Preprocess = 0.2f;
		_trailParticles.Explosiveness = 0.0f;
		_trailParticles.Randomness = 0.3f;
		_trailParticles.FixedFps = 0;
		_trailParticles.Interpolate = true;
		_trailParticles.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		
		var procMat = new ParticleProcessMaterial();
		procMat.Gravity = Vector3.Zero;
		procMat.Direction = new Vector3(0, 0, 1);
		procMat.Spread = 40.0f;
		procMat.InitialVelocity = new Vector2(3.0f, 8.0f);
		procMat.Scale = new Vector2(0.2f, 0.5f);
		procMat.Color = new Color(1f, 0.4f, 0f, 0.8f);
		procMat.Angle = new Vector2(0f, 360f);
		procMat.AngularVelocity = new Vector2(0f, 180f);
		_trailParticles.ProcessMaterial = procMat;
		
		var particleSphere = new SphereMesh();
		particleSphere.Radius = 0.12f;
		particleSphere.Height = 0.24f;
		particleSphere.RadialSegments = 6;
		particleSphere.Rings = 4;
		var particleMat = new StandardMaterial3D
		{
			EmissionEnabled = true,
			Emission = new Color(1f, 0.3f, 0f),
			EmissionEnergyMultiplier = 3.0f,
			AlbedoColor = new Color(1f, 0.4f, 0f),
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
		};
		particleSphere.Material = particleMat;
		_trailParticles.DrawPass1 = particleSphere;
		
		AddChild(_trailParticles);
		_trailParticles.Position = new Vector3(0f, 0f, -0.3f);
		
		// Lumiere
		_light = new OmniLight3D();
		_light.LightColor = new Color(1f, 0.3f, 0f);
		_light.LightEnergy = 2.0f;
		_light.OmniRange = 8.0f;
		_light.ShadowEnabled = false;
		AddChild(_light);
	}
	
	/// <summary>
	/// Reset and position this projectile visual.
	/// Called by ProjectileManager when recycling from pool.
	/// </summary>
	public void Reset(Vector3 position, Vector3 direction)
	{
		GlobalPosition = position;
		LookAt(position + direction, Vector3.Up);
		Visible = true;
		
		// Restart particles
		if (_trailParticles != null)
		{
			_trailParticles.Emitting = true;
			_trailParticles.Restart();
			
			if (_trailParticles.ProcessMaterial is ParticleProcessMaterial ppm)
			{
				ppm.Direction = -direction;
			}
		}
	}
	
	/// <summary>
	/// Hide and stop particles. Called when projectile expires.
	/// </summary>
	public void Deactivate()
	{
		Visible = false;
		if (_trailParticles != null)
			_trailParticles.Emitting = false;
	}
}
