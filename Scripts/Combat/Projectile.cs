using Godot;
using System;
using SlopArena.Shared;

/// <summary>
/// Types de projectiles avec leurs proprietes visuelles et de hitbox.
/// </summary>
public enum ProjectileType
{
	Fireball,    // Boule de feu qui avance vite, petite hitbox
	IceShard,    // Tranchant gele, etroit et rapide
	ArcaneBlast, // Onde large qui s'elargit
	EarthSpike,  // Pic qui monte du sol
	HolyLight,   // Onde circulaire qui s'etend
}

/// <summary>
/// Un projectile actif dans le monde, avec hitbox mobile et duree de vie.
/// </summary>
public partial class Projectile : Node3D
{
	public ProjectileType Type;
	public float PosX, PosY, PosZ;
	public float VelX, VelY, VelZ;
	public float Lifetime; // Seconds restants
	public float MaxLifetime;
	public float Range; // Distance max parcourue
	public float Radius; // Hitbox radius
	public float Damage; // Knockback force
	public ushort HitstunTicks;
	public float ArcCos; // Pour les AoE directionnels
	public bool IsAoE; // Si true, explose/impacte a la fin
	public bool HasHit; // A deja touche quelque chose
	
	private MeshInstance3D? _mesh;
	private float _distTraveled;
	
	/// <summary>
	/// Cree un projectile a partir d'un sort, visant vers une position cible (reticule).
	/// </summary>
	public static Projectile Create(SpellData spell, ProjectileType type, float startX, float startY, float startZ, float aimX, float aimY)
	{
		var proj = new Projectile();
		proj.Type = type;
		proj.PosX = startX;
		proj.PosY = startY;
		proj.PosZ = startZ;
		proj.Radius = 4f; // Valeur par defaut
		proj.Damage = 400f; // Valeur par defaut
		proj.HitstunTicks = 25;
		proj.ArcCos = MathF.Cos(30f * MathF.PI / 180f);
		proj.Range = 60f;
		
		// Direction du joueur vers le reticule
		float dx = aimX - startX;
		float dy = aimY - startY;
		float dist = MathF.Sqrt(dx * dx + dy * dy);
		float dirX = dist > 0.001f ? dx / dist : 0f;
		float dirY = dist > 0.001f ? dy / dist : 1f;
		
		// Duree de vie et velocite selon le type
		switch (type)
		{
			case ProjectileType.Fireball:
				proj.MaxLifetime = 3.0f;
				proj.VelX = dirX * 400f;
				proj.VelY = dirY * 400f;
				proj.VelZ = 0f;
				proj.IsAoE = true;
				break;
				
			case ProjectileType.IceShard:
				proj.MaxLifetime = 2.5f;
				proj.VelX = dirX * 600f;
				proj.VelY = dirY * 600f;
				proj.VelZ = 0f;
				proj.IsAoE = false;
				break;
				
			case ProjectileType.ArcaneBlast:
				proj.MaxLifetime = 2.0f;
				proj.VelX = dirX * 300f;
				proj.VelY = dirY * 300f;
				proj.VelZ = 0f;
				proj.IsAoE = true;
				break;
				
			case ProjectileType.EarthSpike:
				proj.MaxLifetime = 1.5f;
				proj.VelX = 0f;
				proj.VelY = 0f;
				proj.VelZ = 0f;
				proj.PosX = startX + dirX * 40f;
				proj.PosY = startY + dirY * 40f;
				proj.IsAoE = true;
				break;
				
			case ProjectileType.HolyLight:
				proj.MaxLifetime = 1.2f;
				proj.VelX = 0f;
				proj.VelY = 0f;
				proj.VelZ = 0f;
				proj.IsAoE = true;
				break;
		}
		
		proj.Lifetime = proj.MaxLifetime;
		return proj;
	}
	
	/// <summary>
	/// Met a jour la position et la hitbox. Retourne true si encore actif.
	/// </summary>
	public bool Update(float dt, out float hitX, out float hitY, out float hitRadius)
	{
		// Mouvement (avant le check lifetime pour que le dernier frame bouge aussi)
		PosX += VelX * dt;
		PosY += VelY * dt;
		
		hitX = PosX;
		hitY = PosY;
		hitRadius = Radius;
		
		// Collision avec le decor
		// 1. Verifier si on touche le sol (heightmap)
		float groundZ = PhysicsConfig.GetGroundHeight(PosX, PosY);
		if (PosZ <= groundZ + 1f)
		{
			// Le projectile touche le sol - le faire disparaitre
			return false;
		}
		
		// 2. Verifier les limites de l'arene (murs exterieurs)
		float arenaSize = 3000f; // Meme taille que les murs CSG
		float halfArena = arenaSize * 0.5f;
		if (MathF.Abs(PosX - 2500f) > halfArena || MathF.Abs(PosY - 2500f) > halfArena)
		{
			return false; // Sorti de l'arene
		}
		
		Lifetime -= dt;
		if (Lifetime <= 0) return false;
		
		// Gravite sur le projectile (sauf Earth Spike et Holy Light)
		if (Type != ProjectileType.EarthSpike && Type != ProjectileType.HolyLight)
		{
			PosZ += VelZ * dt;
			VelZ -= 200f * dt; // Legere gravite
		}
		
		// Mise a jour du rayon pour certains sorts
		switch (Type)
		{
			case ProjectileType.ArcaneBlast:
				// S'elargit avec le temps
				float t = 1f - (Lifetime / MaxLifetime);
				hitRadius = Radius * (1f + t * 2f);
				break;
			case ProjectileType.HolyLight:
				// S'etend en cercle
				float expand = (1f - Lifetime / MaxLifetime) * 2f;
				hitRadius = Radius * (1f + expand);
				break;
			case ProjectileType.EarthSpike:
				hitRadius = Radius * (1f + (1f - Lifetime / MaxLifetime));
				break;
		}
		
		// Mettre a jour le mesh visuel
		UpdateMesh();
		
		return true;
	}
	
	private void UpdateMesh()
	{
		if (_mesh == null) return;
		
		float gy = PhysicsConfig.GetGroundHeight(PosX, PosY);
		_mesh.GlobalPosition = new Vector3(PosX, gy + 2f, PosY);
		
		// Ajuster la taille selon le rayon actuel
		if (_mesh.Mesh is SphereMesh sphere)
		{
			sphere.Radius = Radius;
			sphere.Height = Radius * 2f;
		}
		else if (_mesh.Mesh is CylinderMesh cyl)
		{
			cyl.TopRadius = Radius;
			cyl.BottomRadius = Radius;
		}
	}
	
	public void SetMesh(MeshInstance3D mesh)
	{
		_mesh = mesh;
	}
}
