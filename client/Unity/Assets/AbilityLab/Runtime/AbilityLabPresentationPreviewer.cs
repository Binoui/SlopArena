using System;
using System.Collections.Generic;
using SlopArena.Client.Animation;
using SlopArena.Client.World;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.Tools;

/// <summary>Scrub-safe presentation preview using package-local catalog bindings.</summary>
public sealed class AbilityLabPresentationPreviewer
{
    private readonly Dictionary<int, GameObject> _instances = new();
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

    public void SetFrame(CharacterStageSource stage, ushort tick, CharacterAssetCatalog.PresentationBinding[] bindings, Transform origin)
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

        var active = new HashSet<int>();
        var operations = stage.Operations ?? Array.Empty<CharacterTimelineOperationSource>();
        for (int index = 0; index < operations.Count; index++)
        {
            if (operations[index] is not EmitPresentationOperationSource operation ||
                tick < operation.Tick || tick >= operation.Tick + TimelinePresentationDispatcher.DefaultPresentationLifetimeTicks)
                continue;

            active.Add(index);
            if (_instances.ContainsKey(index)) continue;
            var binding = FindBinding(operation.PresentationId);
            if (binding?.Prefab == null) continue;
            _instances[index] = UnityEngine.Object.Instantiate(binding.Prefab, origin.position, origin.rotation);
        }

        foreach (int index in new List<int>(_instances.Keys))
        {
            if (active.Contains(index)) continue;
            DestroyInstance(_instances[index]);
            _instances.Remove(index);
        }
    }

    public void Clear()
    {
        ClearInstances();
        _stage = null;
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
