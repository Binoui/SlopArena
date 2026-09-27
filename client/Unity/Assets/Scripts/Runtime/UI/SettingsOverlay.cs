using System;
using System.Collections.Generic;
using SlopArena.Client.Input;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>Reusable frontend and in-match settings surface.</summary>
    public sealed class SettingsOverlay : MonoBehaviour
    {
        private const string Audio = "AUDIO";
        private const string Controls = "CONTROLS";
        private const string Gameplay = "GAMEPLAY";
        private static readonly Color OptionText = new(0.94f, 0.95f, 0.98f, 1f);
        private static readonly Color BindingText = new(0.76f, 0.80f, 0.88f, 1f);
        private static int _closedFrame = -1;
        private VisualElement? _surface;
        private VisualElement? _host;
        private VisualElement? _body;
        private VisualElement? _resetConfirmation;
        private VisualElement? _resetFocus;
        private VisualElement? _restoreFocus;
        private DropdownField? _device;
        private Label? _status;
        private Action? _onClose;
        private PickingMode _originalPickingMode;
        private bool _open;
        private string _deviceName = "Keyboard";
        private string _category = Audio;
        private FullScreenMode _displayRevertMode;
        private int _displayRevertWidth;
        private int _displayRevertHeight;
        private float _displayDeadline;
        private Label? _displayCountdown;

        private void Update()
        {
            if (_displayCountdown == null) return;
            if (!Application.isFocused || _surface == null || _surface.panel == null)
            {
                RevertDisplay();
                return;
            }
            int seconds = Mathf.CeilToInt(_displayDeadline - Time.unscaledTime);
            _displayCountdown.text = $"Keep these display settings? Reverting in {Mathf.Max(0, seconds)} seconds.";
            if (seconds <= 0) RevertDisplay();
        }

        public static SettingsOverlay? Active { get; private set; }
        public bool IsOpen => _open;
        public static bool ClosedThisFrame => _closedFrame == Time.frameCount;

        public void Open(VisualElement host, Action? onClose = null)
        {
            if (_open) return;
            Active = this;
            _host = host;
            _originalPickingMode = host.pickingMode;
            host.pickingMode = PickingMode.Position;
            _open = true;
            _onClose = onClose;
            _restoreFocus = host.panel?.focusController.focusedElement as VisualElement;
            FindFirstObjectByType<InputController>()?.RequireReleaseBeforeHumanInput();

            _surface = new VisualElement { name = "settings-overlay" };
            _surface.style.position = Position.Absolute;
            _surface.style.left = 0; _surface.style.right = 0; _surface.style.top = 0; _surface.style.bottom = 0;
            _surface.style.alignItems = Align.Center; _surface.style.justifyContent = Justify.Center;
            _surface.style.backgroundColor = new Color(0f, 0f, 0f, 0.78f);
            var panel = new VisualElement();
            panel.style.width = 760; panel.style.maxHeight = Length.Percent(92);
            panel.style.paddingLeft = 24; panel.style.paddingRight = 24; panel.style.paddingTop = 20; panel.style.paddingBottom = 20;
            panel.style.backgroundColor = new Color(0.09f, 0.09f, 0.11f, 1f);
            var title = new Label("SETTINGS"); title.style.fontSize = 28; title.style.unityFontStyleAndWeight = FontStyle.Bold; title.style.color = Color.white;
            panel.Add(title);

            var categories = new VisualElement();
            categories.style.flexDirection = FlexDirection.Row;
            categories.style.marginTop = 12; categories.style.marginBottom = 12;
            foreach (var name in new[] { Audio, Controls, "VIDEO", Gameplay, "ACCESSIBILITY" })
            {
                var category = new Button(() => SelectCategory(name)) { text = name };
                category.SetEnabled(true);
                StyleButton(category);
                category.style.marginRight = 5;
                categories.Add(category);
            }
            panel.Add(categories);
            var scroll = new ScrollView { name = "settings-scroll" };
            scroll.style.flexGrow = 1;
            _body = new VisualElement();
            scroll.Add(_body);
            panel.Add(scroll);
            _status = new Label(); _status.style.color = new Color(1f, 0.75f, 0.35f); _status.style.marginTop = 6;
            panel.Add(_status);
            var reset = new Button(AskReset) { text = "RESET ALL SETTINGS" };
            StyleButton(reset);
            reset.style.height = 38; reset.style.marginTop = 8;
            panel.Add(reset);
            var back = new Button(HandleBack) { text = "BACK" }; back.style.height = 42; back.style.marginTop = 10;
            StyleButton(back);
            panel.Add(back);
            _surface.RegisterCallback<ClickEvent>(_ => UISFX.PlayClick());
            _surface.RegisterCallback<NavigationCancelEvent>(OnNavigationCancel);
            _surface.Add(panel); host.Add(_surface);
            UiModalState.Push();
            SelectCategory(_category);
            back.Focus();
        }

        private void SelectCategory(string category)
        {
            if (HumanInputActions.IsCapturing && category != _category)
            {
                HumanInputActions.CancelCapture();
                return;
            }
            if (_body == null) return;
            _category = category;
            _body.Clear();
            _status!.text = "";
            if (category == Audio) BuildAudio(_body);
            else if (category == Controls) BuildControls(_body);
            else if (category == "VIDEO") BuildVideo(_body);
            else if (category == "ACCESSIBILITY") BuildAccessibility(_body);
            else BuildGameplay(_body);
            ApplyReadableContrast(_body);
        }

        private static void BuildAudio(VisualElement root)
        {
            var settings = ClientSettingsService.Instance;
            AddSlider(root, "Master Volume", settings.Master, 0f, 100f, settings.SetMaster);
            AddSlider(root, "Music Volume", settings.Music, 0f, 100f, settings.SetMusic);
            AddSlider(root, "SFX Volume", settings.Sfx, 0f, 100f, settings.SetSfx);
            AddSlider(root, "UI Volume", settings.Ui, 0f, 100f, settings.SetUi);
            var mute = new Toggle("Mute When Unfocused") { value = settings.MuteWhenUnfocused };
            var muteLabel = mute.Q<Label>();
            if (muteLabel != null) muteLabel.style.color = OptionText;
            mute.RegisterValueChangedCallback(evt => settings.SetMuteWhenUnfocused(evt.newValue));
            root.Add(mute);
        }

        private void BuildControls(VisualElement root)
        {
            var settings = ClientSettingsService.Instance;
            _device = new DropdownField("Device",
                new System.Collections.Generic.List<string> { "Keyboard", "Mouse", "Controller" },
                _deviceName == "Mouse" ? 1 : _deviceName == "Controller" ? 2 : 0);
            var deviceLabel = _device.Q<Label>();
            if (deviceLabel != null) deviceLabel.style.color = OptionText;
            _device.RegisterValueChangedCallback(evt => { _deviceName = evt.newValue; SelectCategory(Controls); });
            root.Add(_device);
            if (_deviceName == "Controller")
            {
                foreach (var action in new[]
                         {
                             "MoveStick", "Slot1", "Slot2", "Slot3", "Slot4", "SpecialModifier",
                             "Jump", "Down", "Dash", "Burst", "FaceToCamera", "ToggleLock", "Pause"
                         })
                    AddBindingRow(root, action, ActionLabel(action, controller: true));
                root.Add(new Label("Specials: hold Special Modifier + Normal button."));
            }
            else
            {
                foreach (var action in HumanInputActions.RebindableActions)
                    if (action != "SpecialModifier") AddBindingRow(root, action, ActionLabel(action));
            }
            AddSlider(root, "Stick Deadzone", settings.StickDeadzone, 5f, 50f, settings.SetStickDeadzone);
            var vibration = new Toggle("Vibration") { value = settings.Vibration };
            var vibrationLabel = vibration.Q<Label>();
            if (vibrationLabel != null) vibrationLabel.style.color = OptionText;
            vibration.RegisterValueChangedCallback(evt => settings.SetVibration(evt.newValue));
            root.Add(vibration);
            var reset = new Button(() =>
            {
                if (HumanInputActions.IsCapturing) HumanInputActions.CancelCapture();
                settings.ResetControls();
                SelectCategory(Controls);
            }) { text = "RESET CONTROLS" };
            root.Add(reset);
        }

        private void AddBindingRow(VisualElement root, string action, string label)
        {
            var group = SelectedGroup;
            int index = HumanInputActions.BindingIndex(action, group);
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center; row.style.marginBottom = 4;
            var name = new Label(label); name.style.flexGrow = 1; name.style.color = OptionText; row.Add(name);
            var current = new Label(HumanInputActions.BindingLabel(action, group));
            current.style.width = 110; current.style.color = BindingText; row.Add(current);
            var bind = new Button(() => BeginCapture(action, label)) { text = "REMAP" };
            bind.SetEnabled(index >= 0);
            row.Add(bind);
            root.Add(row);
        }

        private void BeginCapture(string action, string label)
        {
            string group = SelectedGroup;
            _status!.text = _deviceName == "Controller"
                ? $"Press a {group} control for {label}. Select/View/Back cancels."
                : $"Press a {group} control for {label}. Escape cancels.";
            HumanInputActions.Rebind(action, group, (path, error) =>
            {
                if (_open && _category == Controls) SelectCategory(Controls);
                if (!string.IsNullOrEmpty(error)) _status.text = error;
                else if (!string.IsNullOrEmpty(path))
                {
                    ClientSettingsService.Instance.SetBindingOverrides(HumanInputActions.SaveOverrides());
                    _status.text = $"{label} bound to {HumanInputActions.BindingLabel(action, group)}.";
                }
                else _status.text = "Rebind cancelled.";
            });
        }

        private string SelectedGroup => _device?.value switch
        {
            "Mouse" => HumanInputActions.MouseGroup,
            "Controller" => HumanInputActions.GamepadGroup,
            _ => HumanInputActions.KeyboardGroup,
        };

        private static string ActionLabel(string action, bool controller = false) => action switch
        {
            "MoveUp" => "Move Forward", "MoveDown" => "Move Back", "MoveLeft" => "Move Left", "MoveRight" => "Move Right",
            "MoveStick" => "Analog Move Stick",
            "Down" => "Down (Crouch / Slide / Fast Fall)", "FaceToCamera" => "Face to Camera",
            "ToggleLock" => "Target Lock", "Pause" => "Pause",
            "Slot1" => controller ? "Normal 1" : "Action 1",
            "Slot2" => controller ? "Normal 2" : "Action 2",
            "Slot3" => controller ? "Normal 3" : "Action 3",
            "Slot4" => controller ? "Normal 4" : "Action 4",
            "SpecialModifier" => "Special Modifier",
            "Burst" => "Burst", "Jump" => "Jump", "Dash" => "Dash",
            "SlotA" => "Action A", "SlotE" => "Action E", "SlotR" => "Action R", "SlotF" => "Action F",
            _ => action,
        };

        private static void BuildGameplay(VisualElement root)
        {
            var settings = ClientSettingsService.Instance;
            AddSlider(root, "Camera Sensitivity", settings.CameraSensitivity * 100f, 10f, 200f, settings.SetCameraSensitivity);
            var horizontal = new Toggle("Invert Horizontal Camera") { value = settings.InvertCameraHorizontal };
            horizontal.RegisterValueChangedCallback(evt => settings.SetInvertCameraHorizontal(evt.newValue));
            root.Add(horizontal);
            var vertical = new Toggle("Invert Vertical Camera") { value = settings.InvertCameraVertical };
            vertical.RegisterValueChangedCallback(evt => settings.SetInvertCameraVertical(evt.newValue));
            root.Add(vertical);
            AddSlider(root, "Target Indicator Opacity", settings.TargetOpacity * 100f, 20f, 100f, settings.SetTargetOpacity);
            var statsChoices = new List<string> { "Off", "Ping", "Detailed" };
            var stats = new DropdownField("Network Stats", statsChoices, statsChoices[settings.NetworkStats]);
            stats.RegisterValueChangedCallback(evt => settings.SetNetworkStats(statsChoices.IndexOf(evt.newValue)));
            root.Add(stats);
            var network = new Label("Network stats appear in the match HUD. Ping requires a live gameplay-server connection.");
            network.style.color = BindingText;
            root.Add(network);
            AddSlider(root, "Screen Shake", settings.ScreenShake * 100f, 0f, 100f, settings.SetScreenShake);
        }
        private static void AddSlider(VisualElement root, string label, float value, float min, float max, Action<float> setter)
        {
            var row = new VisualElement(); row.style.marginBottom = 12;
            var title = new Label($"{label}: {Mathf.RoundToInt(value)}%"); title.style.color = Color.white; row.Add(title);
            var slider = new Slider(min, max) { value = value };
            slider.RegisterValueChangedCallback(evt => { setter(evt.newValue); title.text = $"{label}: {Mathf.RoundToInt(evt.newValue)}%"; });
            row.Add(slider); root.Add(row);
        }
        private static void BuildAccessibility(VisualElement root)
        {
            var settings = ClientSettingsService.Instance;
            var choices = new List<string> { "80%", "90%", "100%", "110%", "120%", "130%", "140%" };
            string current = $"{settings.UiScale}%";
            var scale = new DropdownField("UI Scale", choices, current);
            scale.RegisterValueChangedCallback(evt => settings.SetUiScale(int.Parse(evt.newValue.TrimEnd('%'))));
            root.Add(scale);
            var reduced = new Toggle("Reduced Flashing") { value = settings.ReducedFlashing };
            reduced.RegisterValueChangedCallback(evt => settings.SetReducedFlashing(evt.newValue));
            root.Add(reduced);
            var explanation = new Label("Reduces transient graphic hit rings and rays. Other visual effects may still flash.");
            explanation.style.color = BindingText;
            root.Add(explanation);
        }
        private static void ApplyReadableContrast(VisualElement root)
        {
            root.Query<TextElement>().ForEach(text => text.style.color = OptionText);
            root.Query<DropdownField>().ForEach(field =>
            {
                field.style.color = OptionText;
                field.style.backgroundColor = new Color(0.16f, 0.17f, 0.20f, 1f);
                field.style.borderLeftWidth = 1;
                field.style.borderRightWidth = 1;
                field.style.borderTopWidth = 1;
                field.style.borderBottomWidth = 1;
                var border = new Color(0.48f, 0.50f, 0.56f, 1f);
                field.style.borderLeftColor = border;
                field.style.borderRightColor = border;
                field.style.borderTopColor = border;
                field.style.borderBottomColor = border;
            });
            root.Query<Toggle>().ForEach(toggle =>
            {
                toggle.style.color = OptionText;
                var label = toggle.Q<Label>();
                if (label != null) label.style.color = OptionText;
            });
            root.Query<Button>().ForEach(StyleButton);
        }

        private static void StyleButton(Button button)
        {
            button.style.color = OptionText;
            button.style.backgroundColor = new Color(0.20f, 0.21f, 0.25f, 1f);
            button.style.borderLeftWidth = 1;
            button.style.borderRightWidth = 1;
            button.style.borderTopWidth = 1;
            button.style.borderBottomWidth = 1;
            var border = new Color(0.48f, 0.50f, 0.56f, 1f);
            button.style.borderLeftColor = border;
            button.style.borderRightColor = border;
            button.style.borderTopColor = border;
            button.style.borderBottomColor = border;
            var label = button.Q<Label>();
            if (label != null) label.style.color = OptionText;
        }

        private void AskReset()
        {
            if (HumanInputActions.IsCapturing) HumanInputActions.CancelCapture();
            if (_resetConfirmation != null || _surface == null) return;
            _resetFocus = _surface.panel?.focusController.focusedElement as VisualElement;
            _resetConfirmation = new VisualElement();
            _resetConfirmation.style.position = Position.Absolute;
            _resetConfirmation.style.left = 0; _resetConfirmation.style.right = 0;
            _resetConfirmation.style.top = 0; _resetConfirmation.style.bottom = 0;
            _resetConfirmation.style.alignItems = Align.Center; _resetConfirmation.style.justifyContent = Justify.Center;
            _resetConfirmation.style.backgroundColor = new Color(0f, 0f, 0f, 0.8f);
            var box = new VisualElement();
            box.style.paddingLeft = 24; box.style.paddingRight = 24; box.style.paddingTop = 20; box.style.paddingBottom = 20;
            var label = new Label("Reset all settings to defaults?"); label.style.color = OptionText; box.Add(label);
            box.Add(new Button(ConfirmReset) { text = "RESET" });
            box.Add(new Button(CancelReset) { text = "CANCEL" });
            ApplyReadableContrast(box);
            _resetConfirmation.Add(box); _surface.Add(_resetConfirmation); box.Q<Button>()?.Focus();
        }

        private void CancelReset()
        {
            _resetConfirmation?.RemoveFromHierarchy();
            _resetConfirmation = null;
            _resetFocus?.Focus();
            _resetFocus = null;
        }

        private void BuildVideo(VisualElement root)
        {
            var settings = ClientSettingsService.Instance;
            var modes = new List<string> { "Windowed", "Borderless", "Fullscreen" };
            string currentMode = Screen.fullScreenMode == FullScreenMode.FullScreenWindow ? "Borderless" :
                Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen ? "Fullscreen" : "Windowed";
            var mode = new DropdownField("Display Mode", modes, currentMode);
            root.Add(mode);
            var resolutions = new List<Resolution>();
            foreach (var item in Screen.resolutions)
                if (!resolutions.Exists(r => r.width == item.width && r.height == item.height)) resolutions.Add(item);
            if (!resolutions.Exists(r => r.width == Screen.width && r.height == Screen.height))
                resolutions.Add(new Resolution { width = Screen.width, height = Screen.height });
            var labels = resolutions.ConvertAll(r => $"{r.width} × {r.height}");
            int selected = resolutions.FindIndex(r => r.width == Screen.width && r.height == Screen.height);
            var resolution = new DropdownField("Resolution", labels, labels[Mathf.Max(selected, 0)]);
            root.Add(resolution);
            var qualities = new List<string> { "Low", "Medium", "High" };
            var quality = new DropdownField("Quality", qualities, qualities[Mathf.Clamp(settings.Quality, 0, 2)]);
            quality.RegisterValueChangedCallback(evt => settings.SetVideo(qualities.IndexOf(evt.newValue), settings.VSync, settings.TargetFps));
            root.Add(quality);
            var vsync = new Toggle("VSync (controls pacing when enabled)") { value = settings.VSync };
            vsync.RegisterValueChangedCallback(evt => settings.SetVideo(settings.Quality, evt.newValue, settings.TargetFps));
            root.Add(vsync);
            var fpsChoices = new List<string> { "60", "120", "144", "240", "Unlimited" };
            int fpsIndex = settings.TargetFps switch { 60 => 0, 120 => 1, 144 => 2, 240 => 3, _ => 4 };
            var fps = new DropdownField("FPS Limit", fpsChoices, fpsChoices[fpsIndex]);
            fps.RegisterValueChangedCallback(evt =>
            {
                int index = fpsChoices.IndexOf(evt.newValue);
                settings.SetVideo(settings.Quality, settings.VSync, index == 4 ? -1 : int.Parse(evt.newValue));
            });
            root.Add(fps);
            root.Add(new Button(() => BeginDisplayChange(mode.value, resolutions[labels.IndexOf(resolution.value)])) { text = "APPLY DISPLAY" });
        }

        private void BeginDisplayChange(string mode, Resolution resolution)
        {
            _displayRevertMode = Screen.fullScreenMode;
            _displayRevertWidth = Screen.width;
            _displayRevertHeight = Screen.height;
            var targetMode = mode switch
            {
                "Borderless" => FullScreenMode.FullScreenWindow,
                "Fullscreen" => FullScreenMode.ExclusiveFullScreen,
                _ => FullScreenMode.Windowed
            };
            Screen.SetResolution(resolution.width, resolution.height, targetMode);
            var box = new VisualElement();
            box.style.position = Position.Absolute;
            box.style.left = 0; box.style.right = 0; box.style.top = 0; box.style.bottom = 0;
            box.style.alignItems = Align.Center; box.style.justifyContent = Justify.Center;
            box.style.backgroundColor = new Color(0f, 0f, 0f, 0.85f);
            var panel = new VisualElement();
            _displayCountdown = new Label();
            panel.Add(_displayCountdown);
            panel.Add(new Button(() => ConfirmDisplay(targetMode, resolution.width, resolution.height)) { text = "KEEP" });
            panel.Add(new Button(RevertDisplay) { text = "REVERT" });
            box.Add(panel);
            _resetConfirmation = box;
            _surface?.Add(box);
            _displayDeadline = Time.unscaledTime + 15f;
            panel.Q<Button>()?.Focus();
            ApplyReadableContrast(box);
        }

        private void ConfirmDisplay(FullScreenMode mode, int width, int height)
        {
            ClientSettingsService.Instance.SetDisplay(mode, width, height);
            _resetConfirmation?.RemoveFromHierarchy(); _resetConfirmation = null; _displayCountdown = null;
        }

        private void RevertDisplay()
        {
            Screen.SetResolution(_displayRevertWidth, _displayRevertHeight, _displayRevertMode);
            _resetConfirmation?.RemoveFromHierarchy(); _resetConfirmation = null; _displayCountdown = null;
        }

        private void ConfirmReset()
        {
            ClientSettingsService.Instance.ResetAll();
            SelectCategory(_category);
            CancelReset();
        }

        private void OnNavigationCancel(NavigationCancelEvent evt)
        {
            evt.StopImmediatePropagation();
            // UI cancel is buttonEast; do not steal it from a controller rebind capture.
            if (!HumanInputActions.IsCapturing) HandleBack();
        }

        public void HandleBack()
        {
            if (HumanInputActions.IsCapturing) { HumanInputActions.CancelCapture(); return; }
            if (_displayCountdown != null) { RevertDisplay(); return; }
            if (_resetConfirmation != null) CancelReset();
            else Close();
        }

        public void Close()
        {
            if (_displayCountdown != null) RevertDisplay();
            if (HumanInputActions.IsCapturing) HumanInputActions.CancelCapture();
            _closedFrame = Time.frameCount;
            _open = false;
            ClientSettingsService.Instance.Save();
            FindFirstObjectByType<InputController>()?.RequireReleaseBeforeHumanInput();
            if (_host != null) _host.pickingMode = _originalPickingMode;
            _host = null;
            _surface?.RemoveFromHierarchy(); _surface = null; _body = null;
            UiModalState.Pop();
            _restoreFocus?.Focus(); _restoreFocus = null;
            _onClose?.Invoke(); _onClose = null;
        }

        private void OnDestroy()
        {
            if (_open) Close();
            if (Active == this) Active = null;
        }
    }
}
