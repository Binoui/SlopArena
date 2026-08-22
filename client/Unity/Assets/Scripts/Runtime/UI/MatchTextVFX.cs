using System.Collections;
using UnityEngine;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Plays authored Cartoon FX particle text for match broadcasts.
    /// Scene-authored variants are preferred when present; dynamic particle text
    /// remains the fallback for phrases that have not been authored yet.
    /// </summary>
    public sealed class MatchTextVFX : MonoBehaviour
    {
        [SerializeField] private UnityEngine.Object _dynamicPrefab;
        [SerializeField] private UnityEngine.Object _staticPrefab;
        [SerializeField] private GameObject _sceneImpactObject;
        [SerializeField] private UnityEngine.Camera _camera;
        [SerializeField] private float _depth = 6f;
        private bool _reportedMissingPrefab;
        private bool _reportedMissingCamera;

        public void Show(string text, Vector2 screenPosition, float size = 1f,
            Color? color1 = null, Color? color2 = null, float lifetimeMultiplier = 1f,
            bool centerInCamera = false)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            string normalizedText = text.Trim();
            GameObject authoredPrefab = LoadAuthoredPrefab(normalizedText);
            GameObject sceneTextVariant = authoredPrefab == null
                ? FindSceneTextVariant(normalizedText)
                : null;
            bool useSceneImpact = sceneTextVariant != null;
            bool useAuthoredImpact = authoredPrefab != null || useSceneImpact;

            var assignedDynamic = _dynamicPrefab;
            var assignedStatic = _staticPrefab;
            GameObject prefab = useSceneImpact
                ? _sceneImpactObject
                : authoredPrefab
                    ?? (assignedDynamic as GameObject ?? (assignedDynamic as Component)?.gameObject)
                    ?? Resources.Load<GameObject>("CFXR Dynamic Text")
                    ?? Resources.Load<GameObject>("MatchTextVFX/CFXR Dynamic Text")
                    ?? (normalizedText.Equals("SMASH", System.StringComparison.OrdinalIgnoreCase)
                        ? (assignedStatic as GameObject ?? (assignedStatic as Component)?.gameObject)
                            ?? Resources.Load<GameObject>("MatchTextVFX/MatchTextSmash")
                            ?? Resources.Load<GameObject>("VFX/SlopArena/MatchTextSmash")
                        : null);

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
            Vector3 viewportPoint = _camera.ScreenToViewportPoint(
                new Vector3(screenPosition.x, screenPosition.y, 0f));
            float viewportX = centerInCamera ? 0.5f : viewportPoint.x;
            float viewportY = centerInCamera ? 0.5f : viewportPoint.y;
            Vector3 world = _camera.ViewportToWorldPoint(
                new Vector3(viewportX, viewportY, spawnDepth));

            GameObject effect;
            if (useSceneImpact)
            {
                effect = _sceneImpactObject;
                effect.SetActive(true);
                sceneTextVariant.SetActive(true);
            }
            else
            {
                effect = Instantiate(prefab);
                effect.SetActive(true);
            }
            effect.transform.SetParent(_camera.transform, false);
            effect.transform.localPosition = _camera.transform.InverseTransformPoint(world);
            effect.transform.localRotation = Quaternion.identity;

            var hiddenTextRoots = new System.Collections.Generic.List<Transform>();
            foreach (var behaviour in effect.GetComponentsInChildren<MonoBehaviour>(true))
            {
                string typeName = behaviour.GetType().Name;
                if (typeName == "CFXR_Effect" || typeName == "CFXR_Demo_RandomText")
                    behaviour.enabled = false;
                else if (useSceneImpact && typeName == "CFXR_ParticleText"
                    && behaviour.gameObject != sceneTextVariant)
                {
                    behaviour.gameObject.SetActive(false);
                    hiddenTextRoots.Add(behaviour.transform);
                }
            }

            var pText = effect.GetComponent("CFXR_ParticleText");
            if (pText != null && !useAuthoredImpact)
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
                bool hidden = false;
                for (int i = 0; i < hiddenTextRoots.Count; i++)
                {
                    if (particle.transform.IsChildOf(hiddenTextRoots[i]))
                    {
                        hidden = true;
                        break;
                    }
                }

                if (hidden || particle.gameObject.name == "MODEL"
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

            float lifetime = Mathf.Max(0.5f, 1.5f * lifetimeMultiplier);
            if (useSceneImpact)
                StartCoroutine(DeactivateSceneImpact(effect, lifetime));
            else
                Destroy(effect, lifetime);
        }

        private static GameObject LoadAuthoredPrefab(string text)
        {
            string key = text.Replace(" ", string.Empty).ToUpperInvariant();
            if (key != "READY" && key != "1" && key != "2" && key != "3" && key != "SLOPITOUT")
                return null;
            return Resources.Load<GameObject>($"MatchTextVFX/MatchText{key}");
        }

        private GameObject FindSceneTextVariant(string text)
        {
            if (_sceneImpactObject == null)
                return null;

            foreach (var behaviour in _sceneImpactObject.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour.GetType().Name != "CFXR_ParticleText")
                    continue;

                var field = behaviour.GetType().GetField("text",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic);
                if (field?.GetValue(behaviour) is string authored
                    && authored.Trim().Equals(text, System.StringComparison.OrdinalIgnoreCase))
                    return behaviour.gameObject;
            }

            return null;
        }

        private IEnumerator DeactivateSceneImpact(GameObject effect, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            if (effect != null)
                effect.SetActive(false);
        }
        public void SetCamera(UnityEngine.Camera camera)
        {
            _camera = camera;
        }
    }
}
