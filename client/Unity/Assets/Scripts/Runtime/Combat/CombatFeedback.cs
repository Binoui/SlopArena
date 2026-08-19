using SlopArena.Client.Simulation;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.Combat
{
    /// <summary>
    /// Converts accepted simulation hits into the shared light/medium/heavy/launch grammar.
    /// Character-specific particles layer over this component rather than redefining strength.
    /// </summary>
    public sealed class CombatFeedback : MonoBehaviour
    {
        private const float MediumDamage = 6f;
        private const float HeavyDamage = 11f;
        private const float LaunchForce = 12f;

        private ISimulationBridge _bridge;

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
                GraphicHitEffect.Spawn(in hit, Classify(in hit));
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
