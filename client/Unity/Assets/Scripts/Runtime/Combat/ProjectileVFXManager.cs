using System.Collections.Generic;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopArena.Client.Combat
{
    /// <summary>
    /// Reads server hitboxes from SpellResolver each tick and spawns/manages
    /// projectile VFX (KiShot, etc.). Moving hitboxes (|V| > 0) get a visual
    /// GameObject with a glowing sphere + trail + light that follows the hitbox.
    /// On hitbox removal, spawns an impact burst.
    /// </summary>
    public class ProjectileVFXManager : MonoBehaviour
    {
        [Header("Projectile Visuals")]
        [SerializeField] private Color _projectileColor = new(0f, 0.7f, 1f);  // cyan energy
        [SerializeField] private float _sphereRadius = 0.25f;
        [SerializeField] private float _lightIntensity = 2f;
        [SerializeField] private float _lightRange = 5f;
        [SerializeField] private float _trailDuration = 0.2f;
        [SerializeField] private Color _emissionColor = new(0.3f, 0.8f, 1f);  // brighter emission

        [Header("Impact VFX")]
        [SerializeField] private float _impactLifetime = 1f;
        [SerializeField] private Color _impactColor = new(0f, 0.7f, 1f, 1f);

        private ServerSimulation _sim;
        private SpellResolver _resolver;
        private ProjectileVFXConfig _config;

        // Active projectile visuals keyed by stable hash from (ownerId + spawn origin)
        private readonly Dictionary<int, GameObject> _activeVisuals = new();
        private readonly Dictionary<int, CharacterClass> _activeProjectileClasses = new();

        public void SetSimulation(ServerSimulation sim)
        {
            _sim = sim;
            _config = Resources.Load<ProjectileVFXConfig>("VFXConfigs/ProjectileVisuals");
            _resolver = sim.Resolver;
            if (_resolver != null)
                _resolver.OnHitboxRemoved += OnHitboxRemoved;
        }

        /// <summary>Call after _sim.Tick() each FixedUpdate.</summary>
        public void OnTick()
        {
            if (_resolver == null) return;

            var hitboxes = _resolver.GetActiveHitboxes();
            var matched = new HashSet<int>();

            for (int i = 0; i < hitboxes.Count; i++)
            {
                var hb = hitboxes[i];
                // Projectile discriminator: non-zero velocity (not a static melee hitbox)
                float speedSq = hb.VX * hb.VX + hb.VY * hb.VY + hb.VZ * hb.VZ;
                if (speedSq <= 0.0001f) continue;

                int key = ComputeHitboxKey(hb);
                matched.Add(key);

                if (!_activeVisuals.TryGetValue(key, out var vis))
                {
                    vis = CreateProjectileVisual(hb);
                    _activeVisuals[key] = vis;
                    _activeProjectileClasses[key] =
                        _sim.GetDefinition(hb.OwnerId)?.Class ?? CharacterClass.None;
                }

                vis.transform.position = new Vector3(hb.X, hb.Y, hb.Z);
            }

            // Remove visuals for hitboxes that disappeared (hit, expired, ground-collided)
            // Impact VFX is handled by OnHitboxRemoved callback with the correct removal position.
            List<int> gone = null;
            foreach (var kv in _activeVisuals)
            {
                if (matched.Contains(kv.Key)) continue;
                (gone ??= new List<int>()).Add(kv.Key);
                Destroy(kv.Value);
                _activeProjectileClasses.Remove(kv.Key);
            }
            if (gone != null)
                foreach (var id in gone) _activeVisuals.Remove(id);
        }

        /// <summary>
        /// Stable hash key for a hitbox across ticks.
        /// Computes approximate spawn origin by reversing velocity * age.
        /// </summary>
        private static int ComputeHitboxKey(in Hitbox hb)
        {
            float tickDt = SlopArena.Shared.Simulation.TickDt;
            float ox = hb.X - hb.VX * hb.AgeTicks * tickDt;
            float oy = hb.Y - hb.VY * hb.AgeTicks * tickDt;
            float oz = hb.Z - hb.VZ * hb.AgeTicks * tickDt;
            // Manual hash combine (System.HashCode unavailable in Unity profile)
            int hash = 17;
            hash = hash * 31 + (int)hb.OwnerId;
            hash = hash * 31 + Mathf.RoundToInt(ox * 10f);
            hash = hash * 31 + Mathf.RoundToInt(oy * 10f);
            hash = hash * 31 + Mathf.RoundToInt(oz * 10f);
            return hash;
        }

        private GameObject CreateProjectileVisual(Hitbox hb)
        {
            if (_config != null)
            {
                var owner = _sim.GetState(hb.OwnerId);
                var def = _sim.GetDefinition(hb.OwnerId);
                var entry = FindProjectileEntry(def?.Class ?? CharacterClass.None, owner.AttackSlot, !owner.IsGrounded);
                if (entry != null && entry.Prefab != null)
                {
                    var go = Instantiate(entry.Prefab);
                    go.transform.localScale = Vector3.one * entry.Scale;
                    return go;
                }
            }
            return BuildProjectileVisual();
        }

        private ProjectileVisualEntry FindProjectileEntry(CharacterClass character, byte attackSlot, bool airborne)
        {
            if (_config.ProjectileEntries == null) return null;
            for (int i = 0; i < _config.ProjectileEntries.Length; i++)
            {
                var e = _config.ProjectileEntries[i];
                if (e.Character == character && e.AttackSlot == attackSlot && e.Airborne == airborne)
                    return e;
            }
            return null;
        }

        /// <summary>
        /// Build a glowing projectile visual GameObject.
        /// Composition: glowing sphere mesh + light + trail renderer.
        /// </summary>
        private GameObject BuildProjectileVisual()
        {
            var root = new GameObject("ProjectileVisual");
            root.transform.localScale = Vector3.one * _sphereRadius * 2f;

            // ── Glowing Sphere ──
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Core";
            sphere.transform.SetParent(root.transform, false);
            // Remove the auto-added collider (not needed for VFX)
            DestroyImmediate(sphere.GetComponent<SphereCollider>());

            var mr = sphere.GetComponent<MeshRenderer>();
            var coreMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            coreMat.SetFloat("_Surface", 1f);               // Transparent
            coreMat.SetOverrideTag("RenderType", "Transparent");
            coreMat.SetInt("_ZWrite", 0);
            coreMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            coreMat.SetFloat("_DstBlend", (float)BlendMode.One);  // Additive
            coreMat.SetFloat("_AlphaClip", 0f);
            coreMat.renderQueue = 3000;
            coreMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            coreMat.color = Color.white;
            coreMat.SetColor("_EmissionColor", _emissionColor * 2f);
            coreMat.EnableKeyword("_EMISSION");
            mr.material = coreMat;

            // ── Light ──
            var light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = _projectileColor;
            light.intensity = _lightIntensity;
            light.range = _lightRange;

            // ── Trail ──
            var trail = root.AddComponent<TrailRenderer>();
            trail.time = _trailDuration;
            trail.startWidth = 1f;
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.1f;
            var trailMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            trailMat.SetFloat("_Surface", 1f);
            trailMat.SetOverrideTag("RenderType", "Transparent");
            trailMat.SetInt("_ZWrite", 0);
            trailMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            trailMat.SetFloat("_DstBlend", (float)BlendMode.One);
            trailMat.renderQueue = 3000;
            trailMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            trailMat.color = _projectileColor;
            trailMat.SetColor("_EmissionColor", _projectileColor * 1.5f);
            trailMat.EnableKeyword("_EMISSION");
            trail.material = trailMat;

            return root;
        }

        private void OnHitboxRemoved(Hitbox hb, float lastX, float lastY, float lastZ)
        {
            if (hb.Explosion.HasValue)
            {
                SpawnExplosion(hb, new Vector3(lastX, lastY, lastZ));
                return;
            }
            float speedSq = hb.VX * hb.VX + hb.VY * hb.VY + hb.VZ * hb.VZ;
            if (speedSq <= 0.0001f) return;
            SpawnImpact(new Vector3(lastX, lastY, lastZ));
        }

        private void SpawnExplosion(Hitbox hb, Vector3 position)
        {
            GameObject prefab = _config != null ? _config.ExplosionPrefab : null;
            float scale = _config != null ? _config.ExplosionScale : 1f;
            float visualScale = hb.Explosion.Value.Radius * scale;
            var cls = _activeProjectileClasses.TryGetValue(ComputeHitboxKey(hb), out var cachedClass)
                ? cachedClass
                : (_sim.GetDefinition(hb.OwnerId)?.Class ?? CharacterClass.None);
            if (_config != null && _config.ExplosionOverrides != null)
            {
                for (int i = 0; i < _config.ExplosionOverrides.Length; i++)
                {
                    var overrideEntry = _config.ExplosionOverrides[i];
                    if (overrideEntry.Character == cls
                        && (overrideEntry.AttackSlot == 0 || overrideEntry.AttackSlot == hb.AttackSlot))
                    {
                        prefab = overrideEntry.Prefab;
                        visualScale = overrideEntry.Scale;
                        break;
                    }
                }
            }
            if (prefab == null) { SpawnImpact(position); return; }
            var go = Instantiate(prefab);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * visualScale;
            Destroy(go, 2.5f);
        }

        private void SpawnImpact(Vector3 position)
        {
            // Procedural impact burst — no prefab dependency
            var go = new GameObject("KiShotImpact")
            {
                transform =
                {
                    position = position
                }
            };

            var ps = go.AddComponent<ParticleSystem>();
            // AddComponent on an active GameObject auto-plays the system (playOnAwake
            // defaults to true), so configuring main.* — including duration — while it is
            // playing throws "Setting the duration while system is still playing is not
            // supported". Stop + clear before configuring (the error's own suggested remedy);
            // playOnAwake is disabled below and Play() is called explicitly at the end.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = 0.3f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
            main.startColor = _impactColor;
            main.maxParticles = 20;
            main.duration = 0.2f;
            main.loop = false;
            main.playOnAwake = false;

            var burst = ps.emission;
            burst.SetBurst(0, new ParticleSystem.Burst(0f, 12));

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f;

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            // All three orbital axes must share the same curve mode or Unity throws
            // "Particle Orbital Velocity curves must all be in the same mode".
            vel.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.orbitalY = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.orbitalZ = new ParticleSystem.MinMaxCurve(-90f, 90f);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var mat = renderer.material;
            mat.SetFloat("_Surface", 1f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_ZWrite", 0);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.One);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.color = _impactColor;
            mat.SetColor("_EmissionColor", _impactColor * 2f);
            mat.EnableKeyword("_EMISSION");
            mat.renderQueue = 3000;

            ps.Play();
            Destroy(go, 1f);
        }

        private void OnDestroy()
        {
            if (_resolver != null)
                _resolver.OnHitboxRemoved -= OnHitboxRemoved;
        }
    }
}
