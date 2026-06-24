#nullable enable
using System;
using Godot;
using SlopArena.Shared;

/// <summary>
/// Special effects for Manki (Mad Bomber Monkey) abilities.
/// Called AFTER stage resolution by AbilityRegistry.
/// Each method maps to a key in CharacterDefinition's SpecialEffectKeys.
/// </summary>
public static partial class MankiAbilities
{
    /// <summary>
    /// RMB — Aerosol + Lighter: homemade flamethrower
    /// </summary>
    public static void AerosolFlame(CombatComponent combat)
    {
        // TEMP: negate forward — GLB model faces +Z (Mixamo), not Godot -Z
        Vector3 forward = -combat.GetOwnerForward();
        Vector3 pos = combat.GetOwnerPosition() + (Vector3.Up * 1.2f);

        var vfxManager = combat.GetSpellVFX();
        if (vfxManager != null)
        {
            var flame = vfxManager.SpawnFlamethrower(pos, forward);
            if (flame != null)
            {
                var tree = combat.GetTree();
                if (tree != null)
                    tree.CreateTimer(0.4).Timeout += () => flame.StopFlame();
            }
        }
        else
        {
            StatusSpells.CreateConeVisual(combat, pos, forward, 5f, 3f, new Color(1f, 0.6f, 0f, 0.3f), 0.4f);
            StatusSpells.CreateImpactVisual(combat, pos + (forward * 4f), 1.5f, new Color(1f, 0.8f, 0.2f));
        }
    }

    /// <summary>
    /// Q — Round Bomb: spawn a projectile that follows the EXACT same parabolic arc
    /// as the server hitbox. Uses CombatMath.ComputeProjectileLaunch — same code
    /// as ServerSimulation and ArcDrawer, so arc, hitbox, and bomb model all match.
    /// </summary>
    public static void RoundBomb(CombatComponent combat)
    {
        SceneTree? tree = combat.GetTree();
        if (tree == null) return;

        // ── Gather the same params the server uses ──
        float aimYaw = 0f, targetDist = 8f, launchAngleDeg = 30f, gravity = 30f, launchOffsetY = 1.2f;
        float capsuleHalf = 0.65f;
        float hitboxRadius = 0.6f, explosionRadius = 2.5f;
        float maxRange = 12f;

        if (combat.GetOwnerNode() is PlayerController player)
        {
            ref var state = ref player.GetState();
            aimYaw = state.AimYaw;

            var def = player.GetCharacterDef();
            capsuleHalf = def.CapsuleHeight * 0.5f;

			// Read params from AbilitySpec.Params (same source as server)
			if (def.Q?.Params != null)
			{
				if (def.Q.Params.TryGetValue("max_range", out float mr)) maxRange = mr;
				if (def.Q.Params.TryGetValue("launch_angle", out float la)) launchAngleDeg = la;
				if (def.Q.Params.TryGetValue("gravity", out float gv)) gravity = gv;
				if (def.Q.Params.TryGetValue("launch_offset_y", out float lo)) launchOffsetY = lo;
				if (def.Q.Params.TryGetValue("hitbox_radius", out float hr)) hitboxRadius = hr;
				if (def.Q.Params.TryGetValue("explosion_radius", out float er)) explosionRadius = er;
			}
			targetDist = Mathf.Clamp(state.AimTargetDistance, 0.5f, maxRange);
		}

		// Compute velocity — SAME as ServerSimulation.cs lines 233-248
		float D = targetDist;
		float launchRad = launchAngleDeg * (Mathf.Pi / 180f);
		float g = gravity;
		float dY = -capsuleHalf - launchOffsetY;

		CombatMath.ComputeProjectileLaunch(D, launchRad, g, dY,
			out float _, out float hSpeed, out float vSpeed);

		float aimSin = Mathf.Sin(aimYaw);
		float aimCos = Mathf.Cos(aimYaw);

		Vector3 launchPos = combat.GetOwnerPosition() + (Vector3.Up * launchOffsetY);

        // ── Spawn the visual mesh ──
        var proj = new MeshInstance3D();
        var sphere = new SphereMesh { Radius = hitboxRadius, Height = hitboxRadius * 2f, RadialSegments = 12, Rings = 8 };
		var mat = new StandardMaterial3D
		{
			AlbedoColor = new Color(1f, 0.7f, 0f),
			EmissionEnabled = true,
			Emission = new Color(1f, 0.5f, 0f),
			EmissionEnergyMultiplier = 4f,
		};
		proj.Mesh = sphere;
		proj.MaterialOverride = mat;
		tree.CurrentScene?.AddChild(proj);
		proj.GlobalPosition = launchPos;

		// ── Fly on the same parabolic arc ──
		var flyer = new ParabolicProjectile(
			proj, launchPos,
			hSpeed * aimSin, vSpeed, -hSpeed * aimCos,
			gravity, 0.05f,
			() =>
			{
							if (GodotObject.IsInstanceValid(proj))
							{
								StatusSpells.CreateImpactVisual(combat, proj.GlobalPosition, explosionRadius, new Color(1f, 0.7f, 0f));
								StatusSpells.CreateCircleVisual(combat, proj.GlobalPosition, explosionRadius, new Color(1f, 0.5f, 0f, 0.3f), 0.3f);
					proj.QueueFree();
				}
			});
		tree.CurrentScene?.AddChild(flyer);
	}

	/// <summary>
	/// E — Dynamite Mine: place or detonate a mine on the ground.
	/// The first press places a mine (visual sphere at ground level).
	/// A second press or 3s timer detonates it with explosion VFX.
	/// Server handles the actual hitbox/damage via ExplosiveMineSpec.
	/// </summary>
	public static void PlaceMine(CombatComponent combat)
	{
	    SceneTree? tree = combat.GetTree();
	    if (tree == null) return;

	    // Read mine data from character definition
	    float mineRadius = 0.3f;
	    float explosionRadius = 2.5f;
	    float autoDetonateSec = 3f;
	    if (combat.GetOwnerNode() is PlayerController player)
	    {
	        var def = player.GetCharacterDef();
	        if (def.E is ExplosiveMineSpec mineSpec)
	        {
	            mineRadius = mineSpec.MineRadius;
	            explosionRadius = mineSpec.ExplosionConfig.Radius;
	            autoDetonateSec = mineSpec.MineDurationTicks / 60f;
	        }
	    }

	    Vector3 pos = combat.GetOwnerPosition();
	    ulong ownerId = combat.GetOwnerNode()?.GetInstanceId() ?? 0;

	    // Check if this owner already has a mine mesh
	    if (_mineMeshes.TryGetValue(ownerId, out var existing) && GodotObject.IsInstanceValid(existing))
	    {
	        // Detonate: remove mine, show explosion
	        existing.QueueFree();
	        _mineMeshes.Remove(ownerId);
	        StatusSpells.CreateImpactVisual(combat, existing.GlobalPosition, explosionRadius * 0.8f, new Color(1f, 0.6f, 0f));
	        StatusSpells.CreateCircleVisual(combat, existing.GlobalPosition, explosionRadius, new Color(1f, 0.4f, 0f, 0.25f), 0.3f);
	    }
	    else
	    {
	        // Place: spawn mine mesh at feet using the data-driven radius
	        pos.Y = 0.05f;
	        var mine = new MeshInstance3D();
	        var sphere = new SphereMesh { Radius = mineRadius, Height = mineRadius * 2f, RadialSegments = 12, Rings = 8 };
	        var mat = new StandardMaterial3D
	        {
	            AlbedoColor = new Color(0.8f, 0.2f, 0.1f),
	            EmissionEnabled = true,
	            Emission = new Color(1f, 0.3f, 0f),
	            EmissionEnergyMultiplier = 3f,
	        };
	        mine.Mesh = sphere;
	        mine.MaterialOverride = mat;
	        tree.CurrentScene?.AddChild(mine);
	        mine.GlobalPosition = pos;
	        _mineMeshes[ownerId] = mine;

	        // Auto-remove after data-defined duration
	        tree.CreateTimer(autoDetonateSec).Timeout += () =>
	        {
	            if (_mineMeshes.TryGetValue(ownerId, out var m) && m == mine && GodotObject.IsInstanceValid(mine))
	            {
	                StatusSpells.CreateImpactVisual(combat, mine.GlobalPosition, explosionRadius * 0.8f, new Color(1f, 0.6f, 0f));
	                StatusSpells.CreateCircleVisual(combat, mine.GlobalPosition, explosionRadius, new Color(1f, 0.4f, 0f, 0.25f), 0.3f);
	                mine.QueueFree();
	                _mineMeshes.Remove(ownerId);
	            }
	        };
	    }
	}

	private static readonly System.Collections.Generic.Dictionary<ulong, MeshInstance3D> _mineMeshes = new();

	public static void DiveBomb(CombatComponent combat)
	{
		Vector3 pos = combat.GetOwnerPosition();
		StatusSpells.CreateImpactVisual(combat, pos + (Vector3.Down * 0.5f), 3f, new Color(1f, 0.7f, 0f));
		StatusSpells.CreateCircleVisual(combat, pos, 3.5f, new Color(1f, 0.5f, 0f, 0.3f), 0.4f);
	}

	public static void BigBoom(CombatComponent combat)
	{
		Vector3 pos = combat.GetOwnerPosition();
		StatusSpells.CreateImpactVisual(combat, pos, 4f, new Color(1f, 0.9f, 0.2f));
		StatusSpells.CreateCircleVisual(combat, pos, 5f, new Color(1f, 0.6f, 0f, 0.4f), 0.6f);
	}

	/// <summary>
	/// Drives a MeshInstance3D along a parabolic arc using the same
	/// velocity + gravity math as the server's SpellResolver.
    /// </summary>
    private partial class ParabolicProjectile : Node
    {
        private readonly MeshInstance3D _mesh;
        private float _vx, _vy, _vz;
        private readonly float _gravity;
        private readonly float _groundY;
        private readonly Action _onFinished;
        private float _age;

        private const float MaxAge = 10f;

        public ParabolicProjectile(
            MeshInstance3D mesh, Vector3 launchPos,
            float vx, float vy, float vz,
            float gravity, float groundY, Action onFinished)
        {
            _mesh = mesh;
            _vx = vx;
            _vy = vy;
            _vz = vz;
            _gravity = gravity;
            _groundY = groundY;
            _onFinished = onFinished;
            _age = 0f;
            Name = "ParabolicProjectile";
        }

        public override void _Process(double delta)
        {
            float dt = (float)delta;
            _age += dt;

            // Euler integration — same as SpellResolver.Tick
            _mesh.GlobalPosition += new Vector3(_vx * dt, _vy * dt, _vz * dt);
            _vy -= _gravity * dt;

            if (_mesh.GlobalPosition.Y <= _groundY || _age >= MaxAge)
            {
                _onFinished();
                _mesh.QueueFree();
                QueueFree();
            }
        }
    }
}
