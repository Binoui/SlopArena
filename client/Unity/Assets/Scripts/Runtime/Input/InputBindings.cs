using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SlopArena.Client.Input
{
    /// <summary>
    /// Remappable key-binding action (ADR-0016, issue #116). The two mouse-button slots
    /// (LMB/RMB) are not actions — they stay fixed to the mouse.
    /// </summary>
    public enum BindableAction : byte
    {
        MoveUp, MoveDown, MoveLeft, MoveRight,
        Jump, Dash, Burst,
        /// <summary>Dedicated "down" key (issue #116): drives the fast-fall / Down input bit.
        /// Deliberately NOT the backward-movement key — drifting backward must never fast-fall.</summary>
        FastFall,
        Slot1, Slot2, Slot3, Slot4, Slot5,
        SlotE, SlotR, SlotF,
        /// <summary>The "A" slot key. Layout-dependent: on AZERTY the physical A key sits at the
        /// QWERTY-Q position; on QWERTY it sits at the QWERTY-A position (which is the AZERTY
        /// left-movement key — hence the per-layout default).</summary>
        SlotA,
    }

    /// <summary>Physical keyboard layout — only affects the slot-A default key.</summary>
    public enum KeyboardLayout : byte { Azerty, Qwerty }

    /// <summary>
    /// Key-binding config (ScriptableObject). Assign an asset via CreateAssetMenu and drop it
    /// into Resources/InputBindings (or set the field on InputController); without an asset the
    /// layout-preset defaults apply. Defaults are QWERTY-position keys, which are the ZQSD
    /// physical keys on an AZERTY board — Unity's Key enum is position-based.
    /// </summary>
    [CreateAssetMenu(fileName = "InputBindings", menuName = "SlopArena/Input Bindings")]
    public class InputBindings : ScriptableObject
    {
        [SerializeField] private KeyboardLayout _layout = KeyboardLayout.Azerty;
        [SerializeField] private KeyBinding[] _overrides = Array.Empty<KeyBinding>();

        [Serializable]
        public struct KeyBinding
        {
            public BindableAction Action;
            public Key Key;
        }

        public KeyboardLayout Layout => _layout;

        public static Key DefaultKey(BindableAction action, KeyboardLayout layout = KeyboardLayout.Azerty)
        {
            return action switch
            {
                // W/A/S/D positions = ZQSD physical keys on AZERTY (Unity Key is position-based).
                BindableAction.MoveUp => Key.W,
                BindableAction.MoveDown => Key.S,
                BindableAction.MoveLeft => Key.A,
                BindableAction.MoveRight => Key.D,
                BindableAction.Jump => Key.Space,
                BindableAction.Dash => Key.LeftShift,
                BindableAction.Burst => Key.C,
                BindableAction.FastFall => Key.X, // issue #116: "maybe x for now"
                BindableAction.Slot1 => Key.Digit1,
                BindableAction.Slot2 => Key.Digit2,
                BindableAction.Slot3 => Key.Digit3,
                BindableAction.Slot4 => Key.Digit4,
                BindableAction.Slot5 => Key.Digit5,
                BindableAction.SlotE => Key.E,
                BindableAction.SlotR => Key.R,
                BindableAction.SlotF => Key.F,
                BindableAction.SlotA => layout == KeyboardLayout.Qwerty ? Key.A : Key.Q,
                _ => Key.None,
            };
        }

        /// <summary>Resolved key for an action: explicit override wins, else the layout default.</summary>
        public Key GetKey(BindableAction action)
        {
            foreach (var o in _overrides)
            {
                if (o.Action == action)
                    return o.Key;
            }
            return DefaultKey(action, _layout);
        }
    }
}
