using System.Collections.Generic;

using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.Combat
{
    /// <summary>
    /// Reads hit results from ServerSimulation.LastTickHits after each tick
    /// and triggers client-side combat feedback (VFX, etc.).
    /// </summary>
    public class CombatFeedback : MonoBehaviour
    {
        [Header("VFX")]
        [SerializeField] private GameObject _hitSparkPrefab;
        [SerializeField] private float _sparkLifetime = 1f;
        private readonly HashSet<ulong> _alreadyTriggered = new(); // per-tick dedup
        private ServerSimulation _sim;

        public void SetSimulation(ServerSimulation sim) => _sim = sim;

        /// <summary>Call after _localSim.Tick() each FixedUpdate.</summary>
        public void OnTick()
        {
            if (_sim == null) return;
            _alreadyTriggered.Clear();

            foreach (var hit in _sim.LastTickHits)
            {
                if (!_alreadyTriggered.Add(hit.TargetEntityId)) continue; // one VFX per entity per tick

                if (_hitSparkPrefab != null)
                {
                    var targetState = _sim.GetState(hit.TargetEntityId);
                    var sparkPos = new Vector3(targetState.PX, targetState.PY, targetState.PZ);
                    var spark = Instantiate(_hitSparkPrefab, sparkPos, Quaternion.identity);
                    Destroy(spark, _sparkLifetime);
                }
            }
        }
    }
}
