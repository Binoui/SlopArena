using System.Collections.Generic;
using SlopArena.Client.Animation;
using SlopArena.Client.Entities;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Resolves authoritative semantic timeline events into cosmetic prefabs.
    /// It owns no gameplay state and never queries physics.
    /// </summary>
    public sealed class TimelinePresentationDispatcher
    {
        private const int AerosolInfernoLifetimeTicks = 28;

        private readonly Dictionary<ulong, Entry> _entries = new();
        private readonly List<ActivePresentation> _active = new();
        private readonly HashSet<PresentationEventKey> _seenEvents = new();

        private sealed class Entry
        {
            public PlayerRenderer Renderer;
            public CharacterAnimationCatalog Catalog;
        }

        private sealed class ActivePresentation
        {
            public GameObject Instance;
            public int RemainingTicks;
        }

        public void Register(ulong entityId, PlayerRenderer renderer, CharacterAnimationCatalog catalog)
        {
            if (renderer == null || catalog == null)
            {
                _entries.Remove(entityId);
                return;
            }
            _entries[entityId] = new Entry { Renderer = renderer, Catalog = catalog };
        }

        public void Tick(IReadOnlyList<TimelinePresentationEvent> events)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                ActivePresentation active = _active[i];
                active.RemainingTicks--;
                if (active.RemainingTicks > 0) continue;
                if (active.Instance != null) DestroyInstance(active.Instance);
                _active.RemoveAt(i);
            }

            if (events == null) return;
            foreach (TimelinePresentationEvent presentationEvent in events)
            {
                if (!_seenEvents.Add(presentationEvent.Key)) continue;
                if (!_entries.TryGetValue(presentationEvent.EntityId, out Entry entry)) continue;
                if (entry.Renderer == null || entry.Catalog == null) continue;

                CharacterAnimationCatalog.PresentationEntry binding = FindBinding(
                    entry.Catalog.Presentations, presentationEvent.PresentationId);
                if (binding?.Prefab == null) continue;

                GameObject instance = Object.Instantiate(
                    binding.Prefab,
                    entry.Renderer.transform.position,
                    entry.Renderer.transform.rotation);
                _active.Add(new ActivePresentation
                {
                    Instance = instance,
                    RemainingTicks = AerosolInfernoLifetimeTicks,
                });
            }
        }

        public void Clear()
        {
            foreach (ActivePresentation active in _active)
                if (active.Instance != null) DestroyInstance(active.Instance);
            _active.Clear();
            _entries.Clear();
            _seenEvents.Clear();
        }

        private static CharacterAnimationCatalog.PresentationEntry FindBinding(
            CharacterAnimationCatalog.PresentationEntry[] bindings, string semanticId)
        {
            if (bindings == null) return null;
            foreach (CharacterAnimationCatalog.PresentationEntry binding in bindings)
                if (binding != null && binding.SemanticId == semanticId)
                    return binding;
            return null;
        }
        private static void DestroyInstance(GameObject instance)
        {
            if (Application.isPlaying)
                Object.Destroy(instance);
            else
                Object.DestroyImmediate(instance);
        }
    }
}
