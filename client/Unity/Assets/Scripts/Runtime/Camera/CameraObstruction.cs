using Unity.Cinemachine;
using UnityEngine;

namespace SlopArena.Client.Camera
{
    /// <summary>Constrain the orbit or aim camera to the visible side of baked stage geometry.</summary>
    [DisallowMultipleComponent]
    public sealed class CameraObstruction : CinemachineExtension
    {
        // Fits inside a fighter's wall clearance while enclosing the cameras' near planes.
        private const float Radius = 0.25f;
        private const float ReturnTime = 0.15f;
        private float _distance;
        private Transform _target;

        protected override void PostPipelineStageCallback(
            CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage,
            ref CameraState state, float deltaTime)
        {
            if (stage != CinemachineCore.Stage.Body || vcam.Follow == null) return;

            var target = vcam.Follow;
            Vector3 origin = TargetPositionCache.GetTargetPosition(target);
            Vector3 offset = state.GetCorrectedPosition() - origin;
            float desiredDistance = offset.magnitude;
            if (desiredDistance < 0.001f) return;
            Vector3 direction = offset / desiredDistance;
            float safeDistance = desiredDistance;
            // The stage proxy is excluded from ordinary aim raycasts and contains no fighters.
            if (Physics.SphereCast(origin, Radius, direction, out var hit, desiredDistance,
                    Physics.IgnoreRaycastLayer, QueryTriggerInteraction.Ignore))
                safeDistance = Mathf.Max(0f, hit.distance - 0.01f);

            bool reset = !vcam.PreviousStateIsValid || deltaTime < 0f || _target != target;
            _target = target;
            // Never damp into a wall. Only ease back out, capped by this frame's clear segment.
            _distance = reset ? safeDistance : Mathf.Min(safeDistance,
                Mathf.Lerp(_distance, desiredDistance, 1f - Mathf.Exp(-deltaTime / ReturnTime)));
            state.PositionCorrection += origin + direction * _distance - state.GetCorrectedPosition();
            // Leave aim rotation alone; the orbital camera's Aim stage composes its look target.
        }
    }
}
