using SlopArena.Client.UI;
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
        private const string DamageHiddenClass = "damage-hidden";

        [Header("Framing")]
        [SerializeField] private float _paddingPx = 55f;

        private VisualElement _root;
        private Label? _damage;
        private ushort _damagePercent = ushort.MaxValue;
        private UnityEngine.Camera _camera;
        private PlayerRenderer _target;
        private float _authoredOpacity = 1f;
        private ClientSettingsService _settings;

        public void Init(VisualElement root, UnityEngine.Camera camera)
        {
            if (_settings != null) _settings.Changed -= ApplySettings;
            _root = root;
            _damage = root?.Q<Label>("target-lock-damage");
            _damagePercent = ushort.MaxValue;
            _camera = camera;
            _settings = ClientSettingsService.Instance;
            _authoredOpacity = root != null ? root.resolvedStyle.opacity : 1f;
            _settings.Changed += ApplySettings;
            ApplySettings();
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

            if (_damage != null && damagePercent != _damagePercent)
            {
                _damage.text = $"{damagePercent}%";
                _damage.style.color = HUDManager.DamageColor(damagePercent);
                _damagePercent = damagePercent;
            }

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
            if (_root == null || _target == null || !_root.ClassListContains(VisibleClass)) return;

            var cam = _camera != null ? _camera : FindRenderCamera();
            if (cam == null || _root.panel == null) return;

            Vector3 world = _target.transform.position;
            Vector3 screenPoint = cam.WorldToScreenPoint(world);
            if (screenPoint.z <= 0.001f || !float.IsFinite(screenPoint.x) || !float.IsFinite(screenPoint.y))
                return;

            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(screenPoint.x, screenPoint.y));
            float panelHeight = _root.panel.visualTree.layout.height;
            float rootWidth = _root.resolvedStyle.width;
            float rootHeight = _root.resolvedStyle.height;
            if (!float.IsFinite(rootWidth) || !float.IsFinite(rootHeight) || rootWidth <= 0f || rootHeight <= 0f) return;
            _root.style.left = panelPoint.x - rootWidth * 0.5f;
            _root.style.top = panelHeight - panelPoint.y - _paddingPx - rootHeight;
        }

        private void ApplySettings()
        {
            if (_root == null) return;
            _root.style.opacity = _authoredOpacity * (_settings != null ? _settings.TargetOpacity : 1f);
            _root.EnableInClassList(DamageHiddenClass, _settings != null && !_settings.ShowOverheadDamage);
        }

        private void HideImmediate()
        {
            if (_root == null) return;
            _root.EnableInClassList(VisibleClass, false);
        }

        /// <summary>Resolve the live gameplay camera without selecting a target.</summary>
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
        private void OnDestroy()
        {
            if (_settings != null) _settings.Changed -= ApplySettings;
        }
    }
}
