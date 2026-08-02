using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using SlopArena.Client.Camera;
using SlopArena.Client.Input;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// In-match pause menu (issue #77): Esc toggles a small centered panel with
    /// Resume and Quit Game. While paused the simulation is frozen (Time.timeScale
    /// stops FixedUpdate, and the sim ticks only there), the cursor is released,
    /// and camera mouse input is suppressed via CameraMount.FreeCursor. Shared by
    /// TrainingMatch and PvPMatch through MatchBase. The panel is built into the
    /// match scene's existing UIDocument at runtime — no scene or asset edits.
    /// </summary>
    public class MatchPauseMenu : MonoBehaviour
    {
        private bool _paused;
        private CameraMount? _cameraMount;
        private InputController? _inputController;
        private VisualElement? _panel;

        /// <summary>True while the pause menu is open (gameplay frozen).</summary>
        public bool IsPaused => _paused;

        public void Init(CameraMount? cameraMount, InputController? inputController)
        {
            _cameraMount = cameraMount;
            _inputController = inputController;
            var doc = FindFirstObjectByType<UIDocument>();
            if (doc == null)
            {
                Debug.LogWarning("[PauseMenu] No UIDocument in scene — pause menu unavailable.");
                return;
            }
            BuildPanel(doc.rootVisualElement);
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                SetPaused(!_paused);
        }

        public void SetPaused(bool paused)
        {
            if (_paused == paused) return;
            _paused = paused;

            // Freeze the sim: it ticks only in FixedUpdate, which timeScale=0 stops.
            Time.timeScale = paused ? 0f : 1f;

            // Discard buffered input so nothing fires on the first frame after resume.
            if (paused) _inputController?.ClearPendingFrameState();

            if (_cameraMount != null)
            {
                if (paused) _cameraMount.FreezeAtCurrentAngles();
                // FreeCursor releases the cursor; Normal re-locks it (issue #77).
                _cameraMount.SetMode(paused ? CameraMode.FreeCursor : CameraMode.Normal);
            }
            else
            {
                UnityEngine.Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
                UnityEngine.Cursor.visible = paused;
            }

            if (_panel != null)
                _panel.style.display = paused ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void BuildPanel(VisualElement root)
        {
            _panel = new VisualElement();
            _panel.style.position = Position.Absolute;
            _panel.style.left = 0;
            _panel.style.right = 0;
            _panel.style.top = 0;
            _panel.style.bottom = 0;
            _panel.style.alignItems = Align.Center;
            _panel.style.justifyContent = Justify.Center;
            _panel.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);

            var box = new VisualElement();
            box.style.width = 300;
            box.style.backgroundColor = new Color(0.09f, 0.09f, 0.11f, 0.96f);
            box.style.borderTopLeftRadius = 10;
            box.style.borderTopRightRadius = 10;
            box.style.borderBottomLeftRadius = 10;
            box.style.borderBottomRightRadius = 10;
            box.style.paddingTop = 24;
            box.style.paddingBottom = 24;
            box.style.paddingLeft = 24;
            box.style.paddingRight = 24;

            var title = new Label("PAUSED");
            title.style.fontSize = 30;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = Color.white;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.marginBottom = 20;
            box.Add(title);

            box.Add(MakeButton("RESUME", () => SetPaused(false)));
            box.Add(MakeButton("QUIT GAME", QuitGame));

            _panel.Add(box);
            root.Add(_panel);
            _panel.style.display = DisplayStyle.None;
        }

        private static Button MakeButton(string label, Action onClick)
        {
            var btn = new Button(onClick) { text = label };
            btn.style.width = 240;
            btn.style.height = 46;
            btn.style.fontSize = 20;
            btn.style.marginBottom = 10;
            return btn;
        }

        private void QuitGame()
        {
            Debug.Log("[PauseMenu] Quit Game.");
            SetPaused(false); // restore timeScale + cursor before exiting
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnDestroy()
        {
            // Never leave the sim frozen if the match scene unloads mid-pause.
            Time.timeScale = 1f;
        }
    }
}
