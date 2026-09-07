using UnityEngine;
using SlopArena.Client.Entities;
using UnityEngine.UIElements;

namespace SlopArena.Client.Combat
{
    /// <summary>
    /// Stable screen-space locked-target readout. It uses the same character-center anchor
    /// as the overhead HUD and performs no selection, range, physics, or gameplay queries.
    /// </summary>
    public sealed class TargetLockIndicator : MonoBehaviour
    {
        private const string VisibleClass = "visible";

        [Header("Framing")]
        [SerializeField] private float _paddingPx = 55f;
        [SerializeField] private float _readoutWidthPx = 60f;
        [SerializeField] private float _readoutHeightPx = 44f;

        private VisualElement _root;
        private Label _damage;
        private UnityEngine.Camera _camera;
        private PlayerRenderer _target;
        private readonly Vector3[] _frameSamples = new Vector3[4];

        public void Init(VisualElement root, UnityEngine.Camera camera)
        {
            _root = root;
            _damage = root?.Q<Label>("target-lock-damage");
            _camera = camera;
            HideImmediate();
        }

        /// <summary>Show the resolved target's arrow and authoritative damage percent.</summary>
        public void SetTarget(PlayerRenderer target, bool locked, ushort damagePercent)
        {
            if (_root == null) return;
            _target = target;
            if (target == null || !locked)
            {
                HideImmediate();
                return;
            }

            _damage.text = $"{damagePercent}%";
            _root.EnableInClassList(VisibleClass, true);
        }
        /// <summary>Clear the presentation during lifecycle reset or lock loss.</summary>
        public void Clear(bool immediate = false)
        {
            _target = null;
            HideImmediate();
        }

        private void LateUpdate()
        {
            if (_target == null || !_root.ClassListContains(VisibleClass)) return;

            var cam = _camera != null ? _camera : FindRenderCamera();
            if (cam == null || _root.panel == null) return;

            Vector3 world = _target.transform.position;
            Vector3 screenPoint = cam.WorldToScreenPoint(world);
            if (screenPoint.z <= 0.001f || !float.IsFinite(screenPoint.x) || !float.IsFinite(screenPoint.y))
                return;


            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(screenPoint.x, screenPoint.y));
            float panelHeight = _root.panel.visualTree.layout.height;
            _root.style.left = panelPoint.x - _readoutWidthPx * 0.5f;
            _root.style.top = panelHeight - panelPoint.y - _paddingPx - _readoutHeightPx;
        }

        private void HideImmediate()
        {
            if (_root == null) return;
            _root.EnableInClassList(VisibleClass, false);
        }

        /// <summary>Same camera resolution as HUDManager.FindRenderCamera.</summary>
        private static UnityEngine.Camera FindRenderCamera()
        {
            var main = UnityEngine.Camera.main;
            if (main != null) return main;
            foreach (var c in UnityEngine.Camera.allCameras)
                if (c != null && c.enabled && c.gameObject.activeInHierarchy)
                    return c;
            return null;
        }

        private void OnEnable() => HideImmediate();

        private void OnDisable() => HideImmediate();
    }
}
