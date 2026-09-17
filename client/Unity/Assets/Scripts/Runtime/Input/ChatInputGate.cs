using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;

namespace SlopArena.Client.Input
{
    /// <summary>
    /// Shared input boundary for the chat composer.
    ///
    /// Chat does not pause the match. While the composer owns keyboard focus, gameplay
    /// consumers must ignore human input, camera axes, and background shortcuts. End also
    /// keeps the boundary active until every control that was held during composition is
    /// released, so a typed key cannot become an action on the first gameplay frame after
    /// the composer closes.
    /// </summary>
    public static class ChatInputGate
    {
        private static bool _isComposing;
        private static bool _releaseRequired;
        private static int _suppressThroughFrame = -1;

        public static bool IsComposing => _isComposing;

        /// <summary>True while human gameplay and camera input must be ignored.</summary>
        public static bool SuppressGameplay => IsInputSuppressed;

        /// <summary>True while menu/pause and other background shortcuts must be ignored.</summary>
        public static bool SuppressShortcuts => IsInputSuppressed;

        /// <summary>
        /// True for the actual match scenes. Arena_Offline hosts both Training and Solo;
        /// Arena_PvP hosts online fights. The component fallback keeps direct/test-loaded
        /// gameplay scenes working without treating menu scenes as gameplay.
        /// </summary>
        public static bool IsGameplayScene
        {
            get
            {
                string sceneName = SceneManager.GetActiveScene().name;
                if (sceneName == "Arena_Offline" || sceneName == "Arena_PvP")
                    return true;

                return Object.FindFirstObjectByType<SlopArena.Client.World.MatchBase>() != null;
            }
        }

        private static bool IsInputSuppressed
        {
            get
            {
                if (_isComposing || Time.frameCount <= _suppressThroughFrame)
                    return true;

                // ChatOverlay opens on Enter during gameplay and focuses the field through
                // UI Toolkit scheduling. Consume that opening frame even before FocusIn
                // calls Begin(), so Enter cannot also reach gameplay.
                if (IsChatOpenPressedThisFrame())
                    return true;

                if (_releaseRequired)
                {
                    if (AnyHumanInputHeld())
                        return true;
                    _releaseRequired = false;
                }

                return false;
            }
        }
        private static bool IsChatOpenPressedThisFrame()
        {
            if (!IsGameplayScene)
                return false;

            var keyboard = Keyboard.current;
            return (keyboard != null &&
                    (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) ||
                   UnityEngine.Input.GetKeyDown(KeyCode.Return) ||
                   UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter);
        }

        /// <summary>Begin composing. Idempotent so focus callbacks can safely repeat it.</summary>
        public static void Begin()
        {
            _isComposing = true;
            _releaseRequired = true;
            _suppressThroughFrame = Mathf.Max(_suppressThroughFrame, Time.frameCount);
        }

        /// <summary>
        /// End composing without pausing simulation. The current frame is consumed and all
        /// controls held during typing must be released before gameplay input is accepted.
        /// </summary>
        public static void End()
        {
            _isComposing = false;
            _releaseRequired = true;
            _suppressThroughFrame = Mathf.Max(_suppressThroughFrame, Time.frameCount);
        }

        private static bool AnyHumanInputHeld()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                foreach (var key in keyboard.allKeys)
                {
                    if (key.isPressed)
                        return true;
                }
            }

            var mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed ||
                                  mouse.middleButton.isPressed))
                return true;

            foreach (var gamepad in Gamepad.all)
            {
                foreach (var control in gamepad.allControls)
                {
                    if (control is ButtonControl button && button.isPressed)
                        return true;
                }

                if (gamepad.leftStick.ReadValue().sqrMagnitude > 0.0001f ||
                    gamepad.rightStick.ReadValue().sqrMagnitude > 0.0001f ||
                    Mathf.Abs(gamepad.leftTrigger.ReadValue()) > 0.0001f ||
                    Mathf.Abs(gamepad.rightTrigger.ReadValue()) > 0.0001f)
                    return true;
            }

            return false;
        }
    }
}
