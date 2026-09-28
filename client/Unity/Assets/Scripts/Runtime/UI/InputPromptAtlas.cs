using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using UnityEngine;

namespace SlopArena.Client.UI
{
    // Kenney Input Prompts 1.5: the two PNG sheets and XML maps are local, ignored assets.
    internal sealed class InputPromptAtlas
    {
        internal readonly struct Glyph
        {
            public readonly Texture2D Texture;
            public readonly Rect Uv;

            public Glyph(Texture2D texture, Rect uv)
            {
                Texture = texture;
                Uv = uv;
            }
        }

        private readonly Dictionary<string, Glyph> _keyboard = Load("keyboard");
        private readonly Dictionary<string, Glyph> _xbox = Load("xbox");

        // Number-row binds are physical keys: AZERTY reports &/é/"/' as their
        // legends, but the HUD's canonical 1–4 prompts should keep their digits.
        internal static string KeyboardDisplayLabel(string path, string label)
        {
            const string prefix = "<Keyboard>/";
            if (path == null || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                path.Length != prefix.Length + 1) return label;
            return path[prefix.Length] switch
            {
                '0' => "0", '1' => "1", '2' => "2", '3' => "3", '4' => "4",
                '5' => "5", '6' => "6", '7' => "7", '8' => "8", '9' => "9",
                _ => label
            };
        }

        public bool TryKeyboard(string effectivePath, string displayName, out Glyph glyph)
        {
            string name = KeyboardName(effectivePath, displayName);
            if (name != null && _keyboard.TryGetValue("keyboard_" + name, out glyph)) return true;
            glyph = default;
            return false;
        }

        public bool TryXbox(string effectivePath, out Glyph glyph)
        {
            string name = XboxName(effectivePath);
            if (name != null && _xbox.TryGetValue(name, out glyph)) return true;
            glyph = default;
            return false;
        }

        private static string KeyboardName(string path, string label)
        {
            // For number-row keys the caller has normalized the physical digit;
            // other keys use the Input System's layout-aware displayed legend.
            if (label.Length == 1)
            {
                char key = char.ToLowerInvariant(label[0]);
                if (key is >= 'a' and <= 'z' or >= '0' and <= '9')
                    return key.ToString();
                return key switch
                {
                    '-' => "minus", '=' => "equals", '+' => "plus", ',' => "comma",
                    '.' => "period", ';' => "semicolon", ':' => "colon", '"' => "quote",
                    '\'' => "apostrophe", '/' => "slash_forward", '\\' => "slash_back",
                    '[' => "bracket_open", ']' => "bracket_close", '`' => "tilde",
                    '*' => "asterisk", '_' => "underscore", '?' => "question",
                    '!' => "exclamation", _ => null
                };
            }
            if (string.IsNullOrEmpty(path)) return null;
            string control = path.Substring(path.LastIndexOf('/') + 1);
            if (control.Length is 2 or 3 && control[0] == 'f' &&
                int.TryParse(control.Substring(1), out int function) && function is >= 1 and <= 12)
                return control;
            return control switch
            {
                "space" => "space", "tab" => "tab", "enter" => "enter",
                "escape" => "escape", "backspace" => "backspace", "delete" => "delete",
                "leftShift" or "rightShift" => "shift",
                "leftCtrl" or "rightCtrl" => "ctrl",
                "leftAlt" or "rightAlt" => "alt",
                "leftMeta" or "rightMeta" => "win",
                "upArrow" => "arrow_up", "downArrow" => "arrow_down",
                "leftArrow" => "arrow_left", "rightArrow" => "arrow_right",
                "home" => "home", "end" => "end", "pageUp" => "page_up",
                "pageDown" => "page_down", "insert" => "insert",
                "capsLock" => "capslock", "numLock" => "numlock",
                "numpadEnter" => "numpad_enter", _ => null
            };
        }

        private static string XboxName(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (path.EndsWith("/dpad/up", StringComparison.OrdinalIgnoreCase)) return "xbox_dpad_up";
            if (path.EndsWith("/dpad/down", StringComparison.OrdinalIgnoreCase)) return "xbox_dpad_down";
            if (path.EndsWith("/dpad/left", StringComparison.OrdinalIgnoreCase)) return "xbox_dpad_left";
            if (path.EndsWith("/dpad/right", StringComparison.OrdinalIgnoreCase)) return "xbox_dpad_right";
            return path.Substring(path.LastIndexOf('/') + 1) switch
            {
                "buttonSouth" => "xbox_button_color_a", "buttonEast" => "xbox_button_color_b",
                "buttonWest" => "xbox_button_color_x", "buttonNorth" => "xbox_button_color_y",
                "leftShoulder" => "xbox_lb", "rightShoulder" => "xbox_rb",
                "leftTrigger" => "xbox_lt", "rightTrigger" => "xbox_rt",
                "leftStickPress" => "xbox_ls", "rightStickPress" => "xbox_rs",
                "start" => "xbox_button_menu", "select" => "xbox_button_view",
                _ => null
            };
        }

        private static Dictionary<string, Glyph> Load(string sheet)
        {
            var result = new Dictionary<string, Glyph>(StringComparer.Ordinal);
            var texture = Resources.Load<Texture2D>($"InputPrompts/{sheet}");
            var xml = Resources.Load<TextAsset>($"InputPrompts/{sheet}");
            if (texture == null || xml == null) return result;

            var document = new XmlDocument();
            document.LoadXml(xml.text);
            var cells = document.DocumentElement.ChildNodes;
            float sourceWidth = 0f;
            float sourceHeight = 0f;
            foreach (XmlNode node in cells)
            {
                var attributes = node.Attributes;
                float x = float.Parse(attributes["x"].Value, CultureInfo.InvariantCulture);
                float y = float.Parse(attributes["y"].Value, CultureInfo.InvariantCulture);
                float width = float.Parse(attributes["width"].Value, CultureInfo.InvariantCulture);
                float height = float.Parse(attributes["height"].Value, CultureInfo.InvariantCulture);
                sourceWidth = Mathf.Max(sourceWidth, x + width);
                sourceHeight = Mathf.Max(sourceHeight, y + height);
            }
            // The supplied sheets are vertically inverted relative to their XML rows;
            // Unity UVs start at the bottom, so XML y is already the UV row.
            foreach (XmlNode node in cells)
            {
                var attributes = node.Attributes;
                string name = attributes["name"].Value;
                float x = float.Parse(attributes["x"].Value, CultureInfo.InvariantCulture);
                float y = float.Parse(attributes["y"].Value, CultureInfo.InvariantCulture);
                float width = float.Parse(attributes["width"].Value, CultureInfo.InvariantCulture);
                float height = float.Parse(attributes["height"].Value, CultureInfo.InvariantCulture);
                result.Add(name, new Glyph(texture, new Rect(x / sourceWidth,
                    y / sourceHeight, width / sourceWidth, height / sourceHeight)));
            }
            return result;
        }
    }
}
