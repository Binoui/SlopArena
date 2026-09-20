using SlopArena.Client.Animation;
using SlopArena.Client.Entities;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.World;

/// <summary>Instantiates timeline presentation assets using the authored placement contract.</summary>
public static class PresentationPlacementResolver
{
    public static bool TryResolveWorldTransform(
        PresentationPlacement placement,
        PlayerRenderer renderer,
        Transform root,
        Vector3 eventWorldPosition,
        Quaternion eventWorldRotation,
        out Vector3 position,
        out Quaternion rotation,
        out Vector3 scale)
    {
        placement ??= new PresentationPlacement();
        Transform parent = placement.AttachmentMode == AuthoringPresentationAttachmentMode.Bone
            ? renderer?.ResolvePresentationBone(placement.BoneId)
            : null;
        if (placement.AttachmentMode == AuthoringPresentationAttachmentMode.Bone && parent == null)
            Debug.LogWarning($"[Presentation] Attachment bone '{placement.BoneId}' was not found; using event world transform.");
        if (parent != null)
        {
            position = parent.TransformPoint(LocalPosition(placement));
            rotation = parent.rotation * Quaternion.Euler(LocalRotation(placement));
            scale = Vector3.Scale(parent.lossyScale, LocalScale(placement));
            return true;
        }
        position = eventWorldPosition + eventWorldRotation * LocalPosition(placement);
        rotation = eventWorldRotation * Quaternion.Euler(LocalRotation(placement));
        scale = LocalScale(placement);
        return root != null;
    }

    public static GameObject Instantiate(
        CharacterAnimationCatalog.PresentationEntry binding,
        PresentationPlacement placement,
        PlayerRenderer renderer,
        Transform root,
        Vector3 eventWorldPosition,
        Quaternion eventWorldRotation)
    {
        if (binding?.Prefab == null || root == null)
            return null;

        placement ??= new PresentationPlacement();
        Transform parent = placement.AttachmentMode == AuthoringPresentationAttachmentMode.Bone
            ? renderer?.ResolvePresentationBone(placement.BoneId)
            : null;
        if (placement.AttachmentMode == AuthoringPresentationAttachmentMode.Bone && parent == null)
            Debug.LogWarning($"[Presentation] Attachment bone '{placement.BoneId}' was not found; using event world transform.");

        GameObject instance = Object.Instantiate(binding.Prefab);
        Transform transform = instance.transform;
        if (parent != null)
        {
            transform.SetParent(parent, false);
            ApplyLocal(transform, placement);
        }
        else
        {
            transform.SetParent(null, true);
            transform.SetPositionAndRotation(
                eventWorldPosition + eventWorldRotation * LocalPosition(placement),
                eventWorldRotation * Quaternion.Euler(LocalRotation(placement)));
            transform.localScale = Vector3.Scale(transform.localScale, LocalScale(placement));
        }
        return instance;
    }
    public static GameObject Instantiate(
        CharacterAssetCatalog.PresentationBinding binding,
        PresentationPlacement placement,
        PlayerRenderer renderer,
        Transform root,
        Vector3 eventWorldPosition,
        Quaternion eventWorldRotation)
        => Instantiate(binding == null ? null : new CharacterAnimationCatalog.PresentationEntry
        {
            SemanticId = binding.SemanticId,
            Prefab = binding.Prefab,
        }, placement, renderer, root, eventWorldPosition, eventWorldRotation);

    private static void ApplyLocal(Transform transform, PresentationPlacement placement)
    {
        transform.localPosition = LocalPosition(placement);
        transform.localRotation = Quaternion.Euler(LocalRotation(placement));
        transform.localScale = LocalScale(placement);
    }

    private static Vector3 LocalPosition(PresentationPlacement placement)
        => new(placement.LocalPositionX, placement.LocalPositionY, placement.LocalPositionZ);

    private static Vector3 LocalRotation(PresentationPlacement placement)
        => new(placement.LocalRotationX, placement.LocalRotationY, placement.LocalRotationZ);

    private static Vector3 LocalScale(PresentationPlacement placement)
        => new(placement.LocalScaleX, placement.LocalScaleY, placement.LocalScaleZ);
}
