using System;
using System.Collections.Generic;
using SlopArena.Client.Animation;
using SlopArena.Client.Entities;
using SlopArena.Shared;
using SlopArena.Client.World;
using UnityEngine;

namespace SlopArena.Client.Tools;

/// <summary>Scrub-safe presentation preview using package-local catalog bindings.</summary>
public sealed class AbilityLabPresentationPreviewer
{
    private readonly Dictionary<PresentationEventKey, GameObject> _instances = new();
    private CharacterAssetCatalog.PresentationBinding[] _bindings = Array.Empty<CharacterAssetCatalog.PresentationBinding>();
    private CharacterStageSource _stage;

    public int ActiveInstanceCount => _instances.Count;
    public IReadOnlyCollection<GameObject> ActiveInstances => _instances.Values;
    public void SetBindings(CharacterAssetCatalog.PresentationBinding[] bindings)
    {
        var next = bindings ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>();
        if (ReferenceEquals(_bindings, next)) return;
        _bindings = next;
        ClearInstances();
    }

    public void SetFrame(
        CharacterStageSource stage,
        ushort tick,
        CharacterAssetCatalog.PresentationBinding[] bindings,
        Transform origin,
        PlayerRenderer renderer = null)
    {
        SetBindings(bindings);
        if (stage == null || origin == null)
        {
            Clear();
            return;
        }

        if (!ReferenceEquals(_stage, stage))
        {
            _stage = stage;
            ClearInstances();
        }

        var active = new HashSet<PresentationEventKey>();
        var operations = stage.Operations ?? Array.Empty<CharacterTimelineOperationSource>();
        for (int index = 0; index < operations.Count; index++)
        {
            if (operations[index] is not EmitPresentationOperationSource operation ||
                tick < operation.Tick ||
                tick >= operation.Tick + operation.Placement.DurationTicks)
                continue;

            var key = new PresentationEventKey(0, 0, 0, PresentationEventSource.Timeline, index);
            active.Add(key);
            if (_instances.ContainsKey(key)) continue;
            var binding = FindBinding(operation.PresentationId);
            if (binding?.Prefab == null) continue;
            _instances[key] = PresentationPlacementResolver.Instantiate(
                binding,
                operation.Placement,
                renderer,
                origin,
                origin.position,
                origin.rotation);
        }
        RemoveInactive(active);
    }

    public void SetSimulationFrame(
        IReadOnlyList<TimelinePresentationEvent> events,
        uint currentTick,
        CharacterAssetCatalog.PresentationBinding[] bindings,
        PlayerRenderer renderer,
        Transform origin)
    {
        SetBindings(bindings);
        _stage = null;
        var active = new HashSet<PresentationEventKey>();
        foreach (var presentationEvent in events ?? Array.Empty<TimelinePresentationEvent>())
        {
            int lifetime = TimelinePresentationDispatcher.LifetimeTicks(presentationEvent);
            if (currentTick < presentationEvent.MatchTick ||
                currentTick >= presentationEvent.MatchTick + (uint)lifetime)
                continue;

            PresentationEventKey key = presentationEvent.Key;
            active.Add(key);
            if (_instances.ContainsKey(key)) continue;
            var binding = FindBinding(presentationEvent.PresentationId);
            if (binding?.Prefab == null) continue;
            _instances[key] = PresentationPlacementResolver.Instantiate(
                binding,
                presentationEvent.Placement,
                renderer,
                origin,
                new Vector3(presentationEvent.WorldX, presentationEvent.WorldY, presentationEvent.WorldZ),
                Quaternion.Euler(0f, presentationEvent.WorldYaw * Mathf.Rad2Deg, 0f));
        }
        RemoveInactive(active);
    }


    public void Clear()
    {
        ClearInstances();
        _stage = null;
    }

    private void RemoveInactive(HashSet<PresentationEventKey> active)
    {
        foreach (var key in new List<PresentationEventKey>(_instances.Keys))
        {
            if (active.Contains(key)) continue;
            DestroyInstance(_instances[key]);
            _instances.Remove(key);
        }
    }

    private void ClearInstances()
    {
        foreach (GameObject instance in _instances.Values)
            DestroyInstance(instance);
        _instances.Clear();
    }

    private CharacterAssetCatalog.PresentationBinding FindBinding(string semanticId)
    {
        foreach (var binding in _bindings)
            if (binding != null && binding.SemanticId == semanticId)
                return binding;
        return null;
    }

    private static void DestroyInstance(GameObject instance)
    {
        if (instance == null) return;
        if (Application.isPlaying)
            UnityEngine.Object.Destroy(instance);
        else
            UnityEngine.Object.DestroyImmediate(instance);
    }
}
