using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SlopArena.Client.Input
{
    /// <summary>One action map for human gameplay, camera, utilities, and rebinding.</summary>
    public static class HumanInputActions
    {
        public const string KeyboardGroup = "Keyboard";
        public const string MouseGroup = "Mouse";
        public const string GamepadGroup = "Gamepad";

        private static readonly Dictionary<string, InputAction> Actions = new();
        private static InputActionAsset? _asset;
        private static InputActionMap? _map;
        private static InputActionRebindingExtensions.RebindingOperation? _operation;
        private static float _defaultDeadzone = -1f;
        private static bool _capturing;

        [Serializable]
        private sealed class BindingOverridesJson { public BindingOverrideJson[] bindings; }
        [Serializable]
        private sealed class BindingOverrideJson
        {
            public string action;
            public string id;
            public string path;
            public string interactions;
            public string processors;
        }

        public static float DefaultDeadzone { get { EnsureCreated(); return _defaultDeadzone; } }
        public static bool IsCapturing => _capturing;
        public static InputAction MouseLook => Get("MouseLook");
        public static InputAction StickLook => Get("StickLook");
        public static InputAction Zoom => Get("Zoom");
        public static IReadOnlyList<string> RebindableActions { get; } = new[]
        {
            "MoveUp", "MoveDown", "MoveLeft", "MoveRight", "MoveStick", "Jump", "Dash", "Burst", "Down",
            "Slot1", "Slot2", "Slot3", "Slot4", "SlotA", "SlotE", "SlotR", "SlotF",
            "FaceToCamera", "ToggleLock", "Pause"
        };

        public static void Initialize(string overridesJson)
        {
            EnsureCreated();
            if (_map!.enabled || _capturing) return;
            ApplyOverrides(overridesJson);
            _map.Enable();
        }

        public static InputAction Get(string name) { EnsureCreated(); return Actions[name]; }
        public static string SaveOverrides() { EnsureCreated(); return _asset!.SaveBindingOverridesAsJson(); }
        public static void CancelCapture() => _operation?.Cancel();

        public static void ApplyOverrides(string json)
        {
            EnsureCreated();
            bool wasEnabled = _map!.enabled;
            _map.Disable();
            _asset!.RemoveAllBindingOverrides();
            if (!string.IsNullOrEmpty(json))
            {
                try { _asset.LoadBindingOverridesFromJson(ResolveOverrideIds(json)); }
                catch (Exception ex) { Debug.LogWarning($"[Input] Ignoring invalid binding overrides: {ex.Message}"); }
            }
            if (wasEnabled) _map.Enable();
        }

        public static void ResetOverrides()
        {
            EnsureCreated();
            bool wasEnabled = _map!.enabled;
            _map.Disable();
            _asset!.RemoveAllBindingOverrides();
            if (wasEnabled) _map.Enable();
        }

        public static int BindingIndex(string actionName, string group)
        {
            var action = Get(actionName);
            for (int i = 0; i < action.bindings.Count; i++)
                if (action.bindings[i].groups?.Contains(group, StringComparison.OrdinalIgnoreCase) == true)
                    return i;
            return -1;
        }

        public static string BindingLabel(string actionName, string group)
        {
            int index = BindingIndex(actionName, group);
            return index < 0 ? "Unbound" : Get(actionName).GetBindingDisplayString(index);
        }

        public static void Rebind(string actionName, string group, Action<string, string> completed)
        {
            if (_capturing) return;
            int bindingIndex = BindingIndex(actionName, group);
            if (bindingIndex < 0) { completed(null, "This action has no binding for that device."); return; }

            var action = Get(actionName);
            string priorOverride = action.bindings[bindingIndex].overridePath;
            _capturing = true;
            _map!.Disable();
            string devicePath = group == GamepadGroup ? "<Gamepad>" : group == MouseGroup ? "<Mouse>" : "<Keyboard>";
            string cancelPath = group == GamepadGroup ? "<Gamepad>/buttonEast" : "<Keyboard>/escape";
            _operation = action.PerformInteractiveRebinding(bindingIndex)
                .WithControlsHavingToMatchPath(devicePath)
                .WithCancelingThrough(cancelPath)
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/scroll")
                .OnComplete(op =>
                {
                    string path = action.bindings[bindingIndex].overridePath ?? action.bindings[bindingIndex].path;
                    string conflict = FindConflict(actionName, path, group);
                    if (conflict != null)
                    {
                        action.ApplyBindingOverride(bindingIndex, new InputBinding { overridePath = priorOverride });
                        completed(null, $"Already bound to {conflict}; choose another control.");
                    }
                    else completed(path, null);
                    FinishCapture(op);
                })
                .OnCancel(op => { completed(null, null); FinishCapture(op); });
            _operation.Start();
        }

        private static string ResolveOverrideIds(string json)
        {
            var data = JsonUtility.FromJson<BindingOverridesJson>(json);
            if (data?.bindings == null) return json;
            foreach (var entry in data.bindings)
            {
                if (string.IsNullOrEmpty(entry.action) || string.IsNullOrEmpty(entry.path)) continue;
                int slash = entry.action.LastIndexOf('/');
                string name = slash >= 0 ? entry.action.Substring(slash + 1) : entry.action;
                if (!Actions.TryGetValue(name, out var action)) continue;
                string group = entry.path.StartsWith("<Gamepad>", StringComparison.OrdinalIgnoreCase)
                    ? GamepadGroup
                    : entry.path.StartsWith("<Mouse>", StringComparison.OrdinalIgnoreCase) ? MouseGroup : KeyboardGroup;
                for (int i = 0; i < action.bindings.Count; i++)
                    if (action.bindings[i].groups?.Contains(group, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        entry.id = action.bindings[i].id.ToString();
                        break;
                    }
            }
            return JsonUtility.ToJson(data);
        }

        private static string FindConflict(string actionName, string path, string group)
        {
            foreach (var pair in Actions)
            {
                if (pair.Key == actionName) continue;
                foreach (var binding in pair.Value.bindings)
                    if (binding.groups?.Contains(group, StringComparison.OrdinalIgnoreCase) == true &&
                        string.Equals(binding.effectivePath, path, StringComparison.OrdinalIgnoreCase))
                        return pair.Key;
            }
            return null;
        }

        private static void FinishCapture(InputActionRebindingExtensions.RebindingOperation operation)
        {
            operation.Dispose();
            _operation = null;
            _capturing = false;
            _map!.Enable();
        }

        private static void EnsureCreated()
        {
            if (_asset != null) return;
            _defaultDeadzone = Mathf.Clamp(InputSystem.settings.defaultDeadzoneMin, 0.05f, 0.5f);
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            _map = new InputActionMap("Gameplay");
            _asset.AddActionMap(_map);
            AddButton("MoveUp", "<Keyboard>/w", "<Gamepad>/leftStick/up");
            AddButton("MoveDown", "<Keyboard>/s", "<Gamepad>/leftStick/down");
            AddButton("MoveLeft", "<Keyboard>/a", "<Gamepad>/leftStick/left");
            AddButton("MoveRight", "<Keyboard>/d", "<Gamepad>/leftStick/right");
            AddButton("Jump", "<Keyboard>/space", "<Gamepad>/dpad/up");
            AddButton("Dash", "<Keyboard>/leftShift", "<Gamepad>/dpad/right");
            AddButton("Burst", "<Keyboard>/c", "<Gamepad>/dpad/left");
            AddButton("Down", "<Keyboard>/x", "<Gamepad>/dpad/down");
            AddButton("Slot1", "<Keyboard>/1", "<Gamepad>/buttonSouth");
            AddButton("Slot2", "<Keyboard>/2", "<Gamepad>/buttonEast");
            AddButton("Slot3", "<Keyboard>/3", "<Gamepad>/buttonWest");
            AddButton("Slot4", "<Keyboard>/4", "<Gamepad>/buttonNorth");
            var keyboard = Keyboard.current;
            string slotAKey = keyboard != null ? keyboard.FindKeyOnCurrentKeyboardLayout("A").name : "q";
            AddButton("SlotA", $"<Keyboard>/{slotAKey}", "<Gamepad>/leftShoulder");
            AddButton("SlotE", "<Keyboard>/e", "<Gamepad>/rightShoulder");
            AddButton("SlotR", "<Keyboard>/r", "<Gamepad>/leftTrigger");
            AddButton("SlotF", "<Keyboard>/f", "<Gamepad>/rightTrigger");
            AddButton("FaceToCamera", "<Mouse>/leftButton", "<Gamepad>/leftStickPress");
            AddButton("ToggleLock", "<Mouse>/rightButton", "<Gamepad>/rightStickPress");
            AddButton("Pause", "<Keyboard>/escape", "<Gamepad>/start");
            AddValue("MoveStick", "<Gamepad>/leftStick", "Vector2");
            AddValue("MouseLook", "<Mouse>/delta", "Vector2");
            AddValue("StickLook", "<Gamepad>/rightStick", "Vector2");
            AddValue("Zoom", "<Mouse>/scroll/y", "Axis");
        }

        private static void AddButton(string name, string keyboardOrMouse, string gamepad)
        {
            var action = _map!.AddAction(name, InputActionType.Button);
            string group = keyboardOrMouse.StartsWith("<Mouse>", StringComparison.Ordinal) ? MouseGroup : KeyboardGroup;
            action.AddBinding(keyboardOrMouse).WithGroup(group);
            action.AddBinding(gamepad).WithGroup(GamepadGroup);
            Actions.Add(name, action);
        }

        private static void AddValue(string name, string path, string controlType)
        {
            var action = _map!.AddAction(name, InputActionType.Value, expectedControlLayout: controlType);
            action.AddBinding(path).WithGroup(path.StartsWith("<Gamepad>", StringComparison.Ordinal) ? GamepadGroup : MouseGroup);
            Actions.Add(name, action);
        }
    }
}
