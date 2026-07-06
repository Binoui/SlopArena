using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.Combat
{
    public class AimIndicator : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private float _maxRange = 12f;
        [SerializeField] private float _minRange = 1f;

        private Transform _groundRing;
        private LineRenderer _arcLine;
        private Vector3 _aimTarget;
        private bool _isAiming;
        private Transform _character;
        private float _capsuleHeight = 1.3f;
        private bool _visualsCreated;
        private UnityEngine.Camera _camera;

        public float AimYawRad { get; private set; }
        public float AimDistance { get; private set; }

        private struct AbilityArcParams
        {
            public float Gravity;
            public float LaunchAngleDeg;
            public float LaunchOffsetY;
        }
        private AbilityArcParams _arcParams = new() { Gravity = 30f, LaunchAngleDeg = 30f, LaunchOffsetY = 1.2f };

        private void Awake() => EnsureVisuals();

        private void EnsureVisuals()
        {
            if (_visualsCreated) return;
            _visualsCreated = true;
            if (_groundRing != null && _arcLine != null) return;

            var ringGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ringGO.name = "GroundMarker";
            ringGO.transform.SetParent(transform, false);
            ringGO.transform.localScale = new Vector3(1f, 0.05f, 1f);
            ringGO.transform.localPosition = Vector3.zero;
            Destroy(ringGO.GetComponent<CapsuleCollider>());
            var mr = ringGO.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = new Color(1f, 0.4f, 0.1f, 0.6f);
            mr.sharedMaterial = mat;
            mr.receiveShadows = false;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _groundRing = ringGO.transform;
            _groundRing.gameObject.SetActive(false);

            var arcGO = new GameObject("ArcLine");
            arcGO.transform.SetParent(transform, false);
            _arcLine = arcGO.AddComponent<LineRenderer>();
            _arcLine.startWidth = 0.1f;
            _arcLine.endWidth = 0.05f;
            _arcLine.positionCount = 0;
            var lineMat = new Material(Shader.Find("Sprites/Default"));
            lineMat.color = new Color(1f, 0.6f, 0.1f, 0.5f);
            _arcLine.sharedMaterial = lineMat;
            _arcLine.enabled = false;
        }

        public void SetCharacter(Transform character, float capsuleHeight)
        {
            _character = character;
            _capsuleHeight = capsuleHeight;
        }

        public void SetMaxRange(float range) => _maxRange = range;
        public void SetCamera(UnityEngine.Camera camera) => _camera = camera;
        public bool IsAiming => _isAiming;

        public void SetAbilityParams(float gravity, float launchAngleDeg, float launchOffsetY)
        {
            _arcParams.Gravity = gravity;
            _arcParams.LaunchAngleDeg = launchAngleDeg;
            _arcParams.LaunchOffsetY = launchOffsetY;
        }

        public void SetAiming(bool aiming)
        {
            EnsureVisuals();
            if (_isAiming == aiming) return;
            _isAiming = aiming;
            if (_groundRing != null)
                _groundRing.gameObject.SetActive(aiming);
            if (_arcLine != null)
                _arcLine.enabled = aiming;
        }

        public void UpdateAim()
        {
            EnsureVisuals();
            if (!_isAiming || _character == null) return;

            var unityCam = _camera != null ? _camera : UnityEngine.Camera.main;
            if (unityCam == null) return;

            var mouseRay = unityCam.ScreenPointToRay(UnityEngine.Input.mousePosition);

            float groundY = 0f;
            bool foundGround = false;
            if (Physics.Raycast(mouseRay, out var hit, 200f))
            {
                if (hit.point.y < 1.0f && hit.point.y > -0.5f)
                {
                    groundY = hit.point.y;
                    _aimTarget = hit.point;
                    foundGround = true;
                }
            }

            if (!foundGround && mouseRay.direction.y < 0f)
            {
                float t = -mouseRay.origin.y / mouseRay.direction.y;
                _aimTarget = mouseRay.origin + mouseRay.direction * t;
                _aimTarget.y = 0f;
            }
            else if (!foundGround)
            {
                return;
            }

            Vector3 toTarget = _aimTarget - _character.position;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;
            if (dist < _minRange)
            {
                toTarget = toTarget.normalized * _minRange;
                dist = _minRange;
            }
            else if (dist > _maxRange)
            {
                toTarget = toTarget.normalized * _maxRange;
                dist = _maxRange;
            }

            _aimTarget = _character.position + toTarget;
            _aimTarget.y = groundY + 0.05f; // slight offset to avoid z-fight with floor

            AimYawRad = Mathf.Atan2(toTarget.x, toTarget.z);
            AimDistance = dist;

            _groundRing.position = _aimTarget;
            _groundRing.localScale = new Vector3(0.8f, 0.05f, 0.8f);
            _groundRing.rotation = Quaternion.Euler(0f, 0f, 0f);

            UpdateArcLine(dist);
        }

        private void UpdateArcLine(float distance)
        {
            if (_character == null) return;

            float g = _arcParams.Gravity;
            float launchRad = _arcParams.LaunchAngleDeg * Mathf.Deg2Rad;
            float dY = -_capsuleHeight * 0.5f - _arcParams.LaunchOffsetY;

            CombatMath.ComputeProjectileLaunch(distance, launchRad, g, dY,
                out float _, out float hSpeed, out float vSpeed);

            float aimCos = Mathf.Cos(AimYawRad);
            float aimSin = Mathf.Sin(AimYawRad);

            int segments = 30;
            var points = new Vector3[segments + 1];
            float startY = _character.position.y + _arcParams.LaunchOffsetY;
            float totalTime = distance / Mathf.Max(hSpeed, 0.01f);

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments * totalTime;
                points[i] = new Vector3(
                    _character.position.x + hSpeed * aimSin * t,
                    Mathf.Max(startY + vSpeed * t - 0.5f * g * t * t, 0f),
                    _character.position.z + hSpeed * aimCos * t
                );
            }

            _arcLine.positionCount = points.Length;
            _arcLine.SetPositions(points);
        }

        public (float? aimYawRad, ushort? aimDistanceCm) GetAimInput()
        {
            if (!_isAiming || _character == null)
                return (null, null);
            ushort distCm = (ushort)Mathf.Clamp(AimDistance * 100f, 0f, 6500f);
            return (AimYawRad, distCm);
        }
    }
}
