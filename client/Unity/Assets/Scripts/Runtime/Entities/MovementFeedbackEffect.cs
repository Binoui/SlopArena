using System.Collections.Generic;
using UnityEngine;

namespace SlopArena.Client.Entities
{
    internal enum MovementFeedbackKind : byte
    {
        Dash,
        Jump,
        DoubleJump,
        Landing,
        HeavyLanding,
        FastFall,
        Launch,
        AirRing,
        GroundRing,
    }

    /// <summary>
    /// Pooled, presentation-only movement effects adapted from Cartoon FX prefabs. Events are
    /// raised from successive authoritative CharacterState snapshots; this component never
    /// drives gameplay state.
    /// </summary>
    internal sealed class MovementFeedbackEffect : MonoBehaviour
    {
        private const int PrewarmCount = 3;
        private static readonly Dictionary<MovementFeedbackKind, Stack<MovementFeedbackEffect>> Pools = new();
        private static readonly Dictionary<MovementFeedbackKind, GameObject> Prefabs = new();
        private static bool _prewarmed;

        private ParticleSystem[] _particles;
        private MovementFeedbackKind _kind;
        private float _releaseAt;
        private bool _continuous;
        private bool _active;

        public static void Prewarm()
        {
            if (_prewarmed)
                return;
            _prewarmed = true;

            foreach (MovementFeedbackKind kind in SourceKinds())
            {
                GameObject prefab = LoadPrefab(kind);
                if (prefab == null)
                    continue;

                Stack<MovementFeedbackEffect> pool = PoolFor(kind);
                for (int i = 0; i < PrewarmCount; i++)
                {
                    MovementFeedbackEffect effect = CreateInstance(kind, prefab);
                    effect.gameObject.SetActive(false);
                    pool.Push(effect);
                }
            }
        }

        public static void Spawn(Vector3 position, Vector3 direction, MovementFeedbackKind kind)
        {
            Prewarm();
            if (kind == MovementFeedbackKind.DoubleJump)
            {
                SpawnOne(position, direction, MovementFeedbackKind.DoubleJump);
                SpawnOne(position, direction, MovementFeedbackKind.AirRing);
                return;
            }
            if (kind == MovementFeedbackKind.HeavyLanding)
            {
                SpawnOne(position, direction, MovementFeedbackKind.HeavyLanding);
                SpawnOne(position, direction, MovementFeedbackKind.GroundRing);
                return;
            }


            SpawnOne(position, direction, kind);
        }
        public static MovementFeedbackEffect BeginDashTrail(Vector3 position, Vector3 direction)
        {
            Prewarm();
            GameObject prefab = LoadPrefab(MovementFeedbackKind.Dash);
            if (prefab == null)
                return null;

            Stack<MovementFeedbackEffect> pool = PoolFor(MovementFeedbackKind.Dash);
            MovementFeedbackEffect effect = null;
            while (pool.Count > 0 && effect == null)
                effect = pool.Pop();
            effect ??= CreateInstance(MovementFeedbackKind.Dash, prefab);
            effect.ShowContinuousDash(position, direction);
            return effect;
        }

        public void FollowDashTrail(Vector3 position, Vector3 direction)
        {
            if (!_continuous)
                return;
            transform.position = position;
            transform.rotation = RotationFor(direction, MovementFeedbackKind.Dash);
        }

        public void EndDashTrail()
        {
            if (!_continuous)
                return;
            _continuous = false;
            foreach (ParticleSystem particle in _particles)
                particle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            _releaseAt = Time.unscaledTime + 0.55f;
        }


        private static void SpawnOne(Vector3 position, Vector3 direction, MovementFeedbackKind kind)
        {
            GameObject prefab = LoadPrefab(kind);
            if (prefab == null)
                return;

            Stack<MovementFeedbackEffect> pool = PoolFor(kind);
            MovementFeedbackEffect effect = null;
            while (pool.Count > 0 && effect == null)
                effect = pool.Pop();
            effect ??= CreateInstance(kind, prefab);
            effect.Show(position, direction);
        }

        private static MovementFeedbackEffect CreateInstance(MovementFeedbackKind kind, GameObject prefab)
        {
            GameObject root = new($"Movement Feedback ({kind})")
            {
                hideFlags = HideFlags.DontSave,
            };
            DontDestroyOnLoad(root);

            GameObject visual = Instantiate(prefab, root.transform);
            visual.name = prefab.name;
            visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            MovementFeedbackEffect effect = root.AddComponent<MovementFeedbackEffect>();
            effect._kind = kind;
            effect._particles = visual.GetComponentsInChildren<ParticleSystem>(true);
            effect.ConfigureParticles();
            return effect;
        }

        private void ConfigureParticles()
        {
            foreach (ParticleSystem particle in _particles)
            {
                var main = particle.main;
                main.loop = false;
                if (_kind is MovementFeedbackKind.Dash or MovementFeedbackKind.FastFall or MovementFeedbackKind.Launch)
                    main.duration = Mathf.Min(main.duration, 0.18f);
                main.stopAction = ParticleSystemStopAction.None;
            }
        }

        private void Show(Vector3 position, Vector3 direction)
        {
            gameObject.SetActive(true);
            transform.position = position;
            transform.rotation = RotationFor(direction, _kind);
            transform.localScale = Vector3.one * ScaleFor(_kind);

            foreach (ParticleSystem particle in _particles)
            {
                particle.gameObject.SetActive(true);
                particle.Clear(true);
                particle.Play(true);
            }

            _active = true;
            _releaseAt = Time.unscaledTime + LifetimeFor(_kind);
        }

        private void ShowContinuousDash(Vector3 position, Vector3 direction)
        {
            gameObject.SetActive(true);
            transform.position = position;
            transform.rotation = RotationFor(direction, MovementFeedbackKind.Dash);
            transform.localScale = Vector3.one * ScaleFor(MovementFeedbackKind.Dash);

            foreach (ParticleSystem particle in _particles)
            {
                particle.gameObject.SetActive(true);
                particle.Clear(true);
                var main = particle.main;
                main.loop = true;
                main.duration = 1f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 96;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
                var emission = particle.emission;
                emission.rateOverTime = 45f;
                var trails = particle.trails;
                if (trails.enabled)
                {
                    trails.worldSpace = true;
                    trails.lifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.38f);
                }
                particle.Play(true);
            }

            _active = true;
            _continuous = true;
            _releaseAt = float.PositiveInfinity;
        }

        private void Update()
        {
            if (!_active || _continuous || Time.unscaledTime < _releaseAt)
                return;

            foreach (ParticleSystem particle in _particles)
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            _active = false;
            gameObject.SetActive(false);
            PoolFor(_kind).Push(this);
        }

        private static Quaternion RotationFor(Vector3 direction, MovementFeedbackKind kind)
        {
            if (kind == MovementFeedbackKind.AirRing)
                return Quaternion.Euler(90f, 0f, 0f);
            if (kind is MovementFeedbackKind.Jump or MovementFeedbackKind.DoubleJump
                or MovementFeedbackKind.Landing or MovementFeedbackKind.HeavyLanding
                or MovementFeedbackKind.GroundRing)
                return Quaternion.identity;
            if (direction.sqrMagnitude < 0.001f)
                return Quaternion.identity;
            return Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        private static float ScaleFor(MovementFeedbackKind kind) => kind switch
        {
            MovementFeedbackKind.Dash => 1.2f,
            MovementFeedbackKind.Jump => 0.75f,
            MovementFeedbackKind.DoubleJump => 0.48f,
            MovementFeedbackKind.AirRing => 0.5f,
            MovementFeedbackKind.GroundRing => 0.72f,
            MovementFeedbackKind.Landing => 0.34f,
            MovementFeedbackKind.HeavyLanding => 0.54f,
            MovementFeedbackKind.FastFall => 0.55f,
            MovementFeedbackKind.Launch => 0.65f,
            _ => 0.5f,
        };

        private static float LifetimeFor(MovementFeedbackKind kind) => kind switch
        {
            MovementFeedbackKind.Dash or MovementFeedbackKind.FastFall => 0.45f,
            MovementFeedbackKind.AirRing => 0.8f,
            MovementFeedbackKind.GroundRing => 0.55f,
            MovementFeedbackKind.Launch => 0.65f,
            _ => 1.1f,
        };

        private static string ResourcePath(MovementFeedbackKind kind) => kind switch
        {
            MovementFeedbackKind.Dash or MovementFeedbackKind.FastFall or MovementFeedbackKind.Launch
                => "VFX/SlopArena/MovementWindTrails",
            MovementFeedbackKind.Jump or MovementFeedbackKind.DoubleJump
                or MovementFeedbackKind.Landing or MovementFeedbackKind.HeavyLanding
                => "VFX/SlopArena/MovementMagicPoof",
            MovementFeedbackKind.AirRing => "VFX/SlopArena/MovementAirRing",
            MovementFeedbackKind.GroundRing => "VFX/SlopArena/MovementGroundHit",
            _ => null,
        };

        private static GameObject LoadPrefab(MovementFeedbackKind kind)
        {
            if (Prefabs.TryGetValue(kind, out GameObject prefab))
                return prefab;
            prefab = Resources.Load<GameObject>(ResourcePath(kind));
            Prefabs[kind] = prefab;
            return prefab;
        }

        private static Stack<MovementFeedbackEffect> PoolFor(MovementFeedbackKind kind)
        {
            if (Pools.TryGetValue(kind, out Stack<MovementFeedbackEffect> pool))
                return pool;
            pool = new Stack<MovementFeedbackEffect>();
            Pools.Add(kind, pool);
            return pool;
        }

        private static MovementFeedbackKind[] SourceKinds() => new[]
        {
            MovementFeedbackKind.Dash,
            MovementFeedbackKind.Jump,
            MovementFeedbackKind.DoubleJump,
            MovementFeedbackKind.Landing,
            MovementFeedbackKind.HeavyLanding,
            MovementFeedbackKind.FastFall,
            MovementFeedbackKind.Launch,
            MovementFeedbackKind.AirRing,
            MovementFeedbackKind.GroundRing,
        };
    }
}
