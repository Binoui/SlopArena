using UnityEngine;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Plays authored Cartoon FX particle prefabs behind the authoritative UI text.
    /// Each message can later map to its own static prefab without runtime glyph generation.
    /// </summary>
    public sealed class MatchTextVFX : MonoBehaviour
    {
        [SerializeField] private UnityEngine.Object _dynamicPrefab;
        [SerializeField] private UnityEngine.Object _staticPrefab;
        [SerializeField] private UnityEngine.Camera _camera;
        [SerializeField] private float _depth = 6f;
        private bool _reportedMissingPrefab;
        private bool _reportedMissingCamera;

        public void Show(string text, Vector2 screenPosition, float size = 1f,
            Color? color1 = null, Color? color2 = null, float lifetimeMultiplier = 1f)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            string normalizedText = text.Trim();
            bool isSmash = normalizedText.Equals("SMASH", System.StringComparison.OrdinalIgnoreCase);
            bool isFight = normalizedText.Equals("FIGHT!", System.StringComparison.OrdinalIgnoreCase);
            bool useImpactPrefab = isSmash || isFight;

            var assignedDynamic = _dynamicPrefab;
            var assignedStatic = _staticPrefab;

            GameObject prefab;
            if (useImpactPrefab)
            {
                prefab = (assignedStatic as GameObject ?? (assignedStatic as Component)?.gameObject)
                    ?? Resources.Load<GameObject>("MatchTextVFX/MatchTextSmash")
                    ?? Resources.Load<GameObject>("VFX/SlopArena/MatchTextSmash")
                    ?? (assignedDynamic as GameObject ?? (assignedDynamic as Component)?.gameObject)
                    ?? Resources.Load<GameObject>("MatchTextVFX/CFXR Dynamic Text");
            }
            else
            {
                prefab = (assignedDynamic as GameObject ?? (assignedDynamic as Component)?.gameObject)
                    ?? Resources.Load<GameObject>("MatchTextVFX/CFXR Dynamic Text")
                    ?? (assignedStatic as GameObject ?? (assignedStatic as Component)?.gameObject)
                    ?? Resources.Load<GameObject>("MatchTextVFX/MatchTextSmash")
                    ?? Resources.Load<GameObject>("VFX/SlopArena/MatchTextSmash");
            }

            if (prefab == null)
            {
                if (!_reportedMissingPrefab)
                {
                    Debug.LogWarning("[MatchTextVFX] Particle text prefab is not assigned or available.");
                    _reportedMissingPrefab = true;
                }
                return;
            }
            _reportedMissingPrefab = false;

            if (_camera == null)
                _camera = UnityEngine.Camera.main ?? FindFirstObjectByType<UnityEngine.Camera>();
            if (_camera == null)
            {
                if (!_reportedMissingCamera)
                {
                    Debug.LogWarning("[MatchTextVFX] No camera available for particle text.");
                    _reportedMissingCamera = true;
                }
                return;
            }

            float spawnDepth = _depth > 0.5f ? _depth : 6f;
            Vector3 world = (screenPosition.x > 0f && screenPosition.y > 0f)
                ? _camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, spawnDepth))
                : _camera.transform.position + _camera.transform.forward * spawnDepth;

            var effect = Instantiate(prefab, world, _camera.transform.rotation);
            effect.SetActive(true);
            foreach (var behaviour in effect.GetComponentsInChildren<MonoBehaviour>(true))
            {
                string typeName = behaviour.GetType().Name;
                if (typeName == "CFXR_Effect" || typeName == "CFXR_Demo_RandomText")
                    behaviour.enabled = false;
            }

            Transform disabledTextRoot = null;
            // The authored impact prefab contains its own SMASH glyphs. FIGHT! keeps
            // the same burst but uses the HUD's authoritative FIGHT! label instead.
            if (isFight)
            {
                disabledTextRoot = effect.transform.Find("CFXR _SMASH_");
                if (disabledTextRoot != null)
                    disabledTextRoot.gameObject.SetActive(false);
            }

            var pText = effect.GetComponent("CFXR_ParticleText");
            if (pText != null && !useImpactPrefab)
            {
                var method = pText.GetType().GetMethod("UpdateText",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                    null,
                    new[] { typeof(string), typeof(float?), typeof(Color?), typeof(Color?), typeof(Color?), typeof(float?) },
                    null);
                method?.Invoke(pText, new object[] { text, (float?)size, color1, color2, (Color?)Color.black, (float?)lifetimeMultiplier });
            }
            var particles = effect.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var particle in particles)
            {
                if ((disabledTextRoot != null && particle.transform.IsChildOf(disabledTextRoot))
                    || particle.gameObject.name == "MODEL"
                    || particle.gameObject.name == "Model")
                {
                    particle.gameObject.SetActive(false);
                    continue;
                }

                var main = particle.main;
                main.useUnscaledTime = true;
                main.stopAction = ParticleSystemStopAction.None;
                main.loop = false;
                particle.gameObject.SetActive(true);
                particle.Clear(false);
                particle.Play(false);
            }

            Destroy(effect, Mathf.Max(0.5f, 1.5f * lifetimeMultiplier));
        }

        public void SetCamera(UnityEngine.Camera camera) => _camera = camera;
    }
}
