using UnityEngine;

namespace SlopArena.Client.Combat
{
    /// <summary>
    /// Purely presentational destination marker. The prefab supplies the corner and wedge
    /// meshes; this component only places, or hides, those authored children.
    /// </summary>
    public sealed class GroundDestinationIndicator : MonoBehaviour
    {
        private const int CornerCount = 4;
        private const float MinimumVisibleWidth = 0.0001f;

        // TL/TR/BL/BR. The corner mesh's local L extends along +X and +Z.
        private static readonly Vector3[] CornerSigns =
        {
            new(-1f, 0f, 1f),
            new(1f, 0f, 1f),
            new(-1f, 0f, -1f),
            new(1f, 0f, -1f),
        };

        // Rotate the canonical inward-facing L into each footprint corner.
        private static readonly float[] CornerYawDegrees = { 90f, 180f, 0f, -90f };

        [SerializeField] private Transform[] _corners = new Transform[CornerCount];
        [SerializeField] private Transform _wedge;

        private MaterialPropertyBlock _properties;
        private Material _ringMaterial;
        private Renderer[] _markerRenderers = System.Array.Empty<Renderer>();
        private LineRenderer[] _tierRings = System.Array.Empty<LineRenderer>();
        private static readonly Color[] TierColors =
        {
            new(0.35f, 0.85f, 1f, 1f),
            new(1f, 0.8f, 0.15f, 1f),
            new(1f, 0.3f, 0.12f, 1f),
        };
        private void Awake() => Clear();

        private void EnsureChargeVisuals()
        {
            if (_tierRings.Length != 0) return;
            _properties = new MaterialPropertyBlock();
            _markerRenderers = GetComponentsInChildren<Renderer>(true);
            _ringMaterial = new Material(Shader.Find("Sprites/Default"));
            _tierRings = new LineRenderer[3];
            for (int tier = 0; tier < _tierRings.Length; tier++)
            {
                var ringObject = new GameObject($"ChargeTierRing{tier + 1}");
                ringObject.hideFlags = HideFlags.DontSave;
                ringObject.layer = gameObject.layer;
                ringObject.transform.SetParent(transform, false);
                var line = ringObject.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.loop = true;
                line.positionCount = 32;
                line.startWidth = line.endWidth = 0.025f;
                line.sharedMaterial = _ringMaterial;
                line.startColor = line.endColor = TierColors[tier];
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                float radius = 0.36f + tier * 0.2f;
                for (int i = 0; i < line.positionCount; i++)
                {
                    float angle = i * Mathf.PI * 2f / line.positionCount;
                    line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0.025f, Mathf.Sin(angle) * radius));
                }
                _tierRings[tier] = line;
            }
        }

        private void OnDestroy()
        {
            if (_ringMaterial == null) return;
            if (Application.isPlaying) Destroy(_ringMaterial);
            else DestroyImmediate(_ringMaterial);
        }
        private void OnEnable() => Clear();

        private void OnDisable() => Clear();

        /// <summary>
        /// Places the authored reticle at a destination and aims its open wedge along the
        /// horizontal travel direction. Width controls both corner spread and authored shape
        /// scale, so the short geometry remains proportionate at every footprint size.
        /// </summary>
        public void SetDestination(Vector3 position, Vector3 travelDirection, float width,
            byte? chargeTier = null, float endpointScale = 1f)
        {
            if (chargeTier.HasValue) EnsureChargeVisuals();
            float visualWidth = Mathf.Max(0f, width * endpointScale);
            bool visible = visualWidth > MinimumVisibleWidth;
            float halfWidth = visualWidth * 0.5f;

            transform.SetPositionAndRotation(position, Quaternion.identity);

            Vector3 planarDirection = new(travelDirection.x, 0f, travelDirection.z);
            float yawDegrees = planarDirection.sqrMagnitude > MinimumVisibleWidth * MinimumVisibleWidth
                ? Mathf.Atan2(planarDirection.x, planarDirection.z) * Mathf.Rad2Deg
                : 0f;

            if (_corners != null)
            {
                int count = Mathf.Min(_corners.Length, CornerCount);
                for (int i = 0; i < count; i++)
                {
                    Transform corner = _corners[i];
                    if (corner == null)
                        continue;

                    Vector3 sign = CornerSigns[i];
                    corner.localPosition = new Vector3(
                        sign.x * halfWidth,
                        0f,
                        sign.z * halfWidth);
                    corner.localRotation = Quaternion.Euler(0f, CornerYawDegrees[i], 0f);
                    corner.localScale = Vector3.one * visualWidth;
                    corner.gameObject.SetActive(visible);
                }
            }

            if (_wedge != null)
            {
                _wedge.localPosition = Vector3.zero;
                _wedge.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
                _wedge.localScale = Vector3.one * visualWidth;
                _wedge.gameObject.SetActive(visible);
            }
            if (chargeTier.HasValue)
            {
                Color color = chargeTier.HasValue && chargeTier.Value < TierColors.Length
                    ? TierColors[chargeTier.Value] : Color.white;
                if (_corners != null)
                    foreach (var corner in _corners)
                        if (corner != null)
                            SetColor(corner, color);
                if (_wedge != null) SetColor(_wedge, color);
            }
            for (int tier = 0; tier < _tierRings.Length; tier++)
                _tierRings[tier].gameObject.SetActive(
                    visible && chargeTier.HasValue && tier <= chargeTier.Value);
        }
        private void SetColor(Transform target, Color color)
        {
            foreach (var renderer in _markerRenderers)
            {
                if (renderer == null || !renderer.transform.IsChildOf(target)) continue;
                renderer.GetPropertyBlock(_properties);
                _properties.SetColor("_Color", color);
                _properties.SetColor("_BaseColor", color);
                renderer.SetPropertyBlock(_properties);
            }
        }

        /// <summary>Hides all authored marker children.</summary>
        public void Clear()
        {
            if (_corners != null)
            {
                for (int i = 0; i < _corners.Length; i++)
                {
                    Transform corner = _corners[i];
                    if (corner != null)
                        corner.gameObject.SetActive(false);
                }
            }

            if (_wedge != null)
                _wedge.gameObject.SetActive(false);
            if (_tierRings != null)
                foreach (var ring in _tierRings)
                    if (ring != null) ring.gameObject.SetActive(false);
        }
    }
}
