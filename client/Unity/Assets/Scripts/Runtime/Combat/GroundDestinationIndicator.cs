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

        private void Awake() => Clear();

        private void OnEnable() => Clear();

        private void OnDisable() => Clear();

        /// <summary>
        /// Places the authored reticle at a destination and aims its open wedge along the
        /// horizontal travel direction. Width controls both corner spread and authored shape
        /// scale, so the short geometry remains proportionate at every footprint size.
        /// </summary>
        public void SetDestination(Vector3 position, Vector3 travelDirection, float width)
        {
            float visualWidth = Mathf.Max(0f, width);
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
        }
    }
}
