using System.Collections.Generic;
using SlopArena.Client.Entities;
using SlopArena.Client.Simulation;
using SlopArena.Shared;
using SlopArena.Client.UI;
using UnityEngine;

namespace SlopArena.Client.Combat
{
    /// <summary>
    /// Converts accepted simulation hits into the shared light/medium/heavy/launch grammar.
    /// Character-specific impact sounds layer over this component without changing gameplay.
    /// </summary>
    public sealed class CombatFeedback : MonoBehaviour
    {
        private const float MediumDamage = 6f;
        private const float HeavyDamage = 11f;
        private const float LaunchForce = 12f;

        private ISimulationBridge _bridge;
        private CombatSFX _sfx;
        private readonly Dictionary<ulong, CharacterClass> _characters = new();

        private void Awake()
        {
            _sfx = GetComponent<CombatSFX>() ?? gameObject.AddComponent<CombatSFX>();
        }

        public void RegisterRenderer(PlayerRenderer renderer)
        {
            if (renderer?.CharacterDef != null)
                _characters[renderer.EntityId] = renderer.CharacterDef.Class;
        }

        public void SetSimulation(ISimulationBridge bridge)
        {
            _bridge = bridge;
            GraphicHitEffect.Prewarm();
        }

        /// <summary>Call once after the bridge advances its simulation tick.</summary>
        public void OnTick()
        {
            if (_bridge == null)
                return;

            foreach (var hit in _bridge.LastTickHits)
            {
                if (hit.Blocked) continue; // block feedback arrives once via its authoritative semantic event
                ImpactTier tier = Classify(in hit);
                GraphicHitEffect.Spawn(in hit, tier);
                _sfx.Play(tier, _characters.TryGetValue(
                    hit.OwnerEntityId, out var character)
                    ? character
                    : CharacterClass.FightGuy);
                ClientSettingsService.Instance.PlayConfirmedImpact();
            }
        }

        public static ImpactTier Classify(in SpellResolver.HitResult hit)
        {
            if (hit.ImpactForce >= LaunchForce)
                return ImpactTier.Launch;
            if (hit.Damage >= HeavyDamage)
                return ImpactTier.Heavy;
            if (hit.Damage >= MediumDamage)
                return ImpactTier.Medium;
            return ImpactTier.Light;
        }
    }
}
