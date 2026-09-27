using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>Session-owned confirmation and passive notice surfaces for Steam room joins.</summary>
    public sealed class SteamRoomJoinPrompt
    {
        private static SteamRoomJoinPrompt? _active;
        public static bool IsConfirming => _active != null;
        public static bool CancelActive()
        {
            if (_active == null) return false;
            _active.Close(invokeCancel: true);
            return true;
        }
        private VisualElement? _surface;
        public bool IsPresented => _active == this && _surface?.panel != null;
        private VisualElement? _host;
        private VisualElement? _restoreFocus;
        private PickingMode _originalPickingMode;
        private Button? _acceptButton;
        private Button? _cancelButton;
        private MatchPauseMenu? _pauseMenu;
        private bool _wasPaused;
        private bool _ownsModal;
        private Action? _accept;
        private Action? _cancel;
        private EventCallback<NavigationCancelEvent>? _cancelHandler;
        private string? _pendingNotice;
        private bool _pendingGameplay;

        /// <summary>Shows one confirmation, or returns false without changing UI when unavailable.</summary>
        public bool TryConfirm(string title, string message, Action accept, Action cancel, bool training)
        {
            if (_surface != null || accept == null || cancel == null)
                return false;

            VisualElement? host;
            if (training)
            {
                _pauseMenu = MatchPauseMenu.Active;
                var document = UnityEngine.Object.FindFirstObjectByType<HUDManager>()?.Document;
                if (_pauseMenu == null || document == null || !document.isActiveAndEnabled
                    || document.rootVisualElement == null || document.rootVisualElement.panel == null)
                {
                    _pauseMenu = null;
                    return false;
                }
                host = document.rootVisualElement;
                _wasPaused = _pauseMenu.IsPaused;
                _restoreFocus = host.panel?.focusController.focusedElement as VisualElement;
                if (!_wasPaused)
                    _pauseMenu.SetPaused(true);
            }
            else
            {
                var shell = FrontendController.Shell;
                host = shell?.ModalHost;
                if (!FrontendController.IsFrontendActive || host == null || host.panel == null
                    || UiModalState.Presented)
                    return false;
                _restoreFocus = host.panel?.focusController.focusedElement as VisualElement;
                _originalPickingMode = host.pickingMode;
                host.pickingMode = PickingMode.Position;
            }

            _host = host;
            _accept = accept;
            _cancel = cancel;
            _surface = BuildSurface(title, message, training);
            _cancelHandler = OnNavigationCancel;
            _surface.RegisterCallback(_cancelHandler, TrickleDown.TrickleDown);
            host.Add(_surface);
            UiModalState.Push();
            _ownsModal = true;
            _active = this;
            _acceptButton?.Focus();
            return true;
        }

        /// <summary>Shows a non-modal notice on the active frontend or gameplay HUD.</summary>
        public void Notice(string message, bool gameplay)
        {
            Clear();
            if (gameplay)
            {
                var document = UnityEngine.Object.FindFirstObjectByType<HUDManager>()?.Document;
                _host = document != null ? document.rootVisualElement : null;
            }
            else
            {
                _host = FrontendController.IsFrontendActive ? FrontendController.Shell?.Root : null;
            }
            if (_host?.panel == null)
            {
                _pendingNotice = message;
                _pendingGameplay = gameplay;
                return;
            }

            var notice = new Label(message) { name = "steam-room-join-notice" };
            notice.AddToClassList("shell-modal-help");
            notice.style.position = Position.Absolute;
            notice.style.left = 12;
            notice.style.right = 12;
            notice.style.top = 12;
            notice.style.paddingLeft = 12;
            notice.style.paddingRight = 12;
            notice.style.paddingTop = 8;
            notice.style.paddingBottom = 8;
            notice.style.backgroundColor = new Color(0.09f, 0.09f, 0.11f, 0.94f);
            notice.pickingMode = PickingMode.Ignore;
            _surface = notice;
            _host.Add(notice);
            notice.schedule.Execute(() =>
            {
                if (ReferenceEquals(_surface, notice)) Clear();
            }).StartingIn(8000);
        }

        /// <summary>Removes owned UI and restores any Training pause state.</summary>
        public void Clear()
        {
            _pendingNotice = null;
            Close();
        }

        public void Tick()
        {
            if (_pendingNotice == null) return;
            bool gameplay = _pendingGameplay && !FrontendController.IsFrontendActive;
            var host = gameplay
                ? UnityEngine.Object.FindFirstObjectByType<HUDManager>()?.Document?.rootVisualElement
                : FrontendController.Shell?.Root;
            if (host?.panel == null) return;
            var message = _pendingNotice;
            _pendingNotice = null;
            Notice(message, gameplay);
        }

        private VisualElement BuildSurface(string title, string message, bool training)
        {
            var surface = new VisualElement { name = "steam-room-join-confirmation" };
            surface.AddToClassList("shell-modal");
            surface.pickingMode = PickingMode.Position;
            if (training)
            {
                surface.style.position = Position.Absolute;
                surface.style.left = 0;
                surface.style.right = 0;
                surface.style.top = 0;
                surface.style.bottom = 0;
                surface.style.alignItems = Align.Center;
                surface.style.justifyContent = Justify.Center;
                surface.style.backgroundColor = new Color(0.05f, 0.05f, 0.08f, 0.72f);
            }

            var box = new VisualElement { name = "steam-room-join-box" };
            box.AddToClassList("shell-modal-box");
            box.pickingMode = PickingMode.Position;
            if (training)
            {
                box.style.minWidth = 320;
                box.style.maxWidth = 560;
                box.style.paddingLeft = 24;
                box.style.paddingRight = 24;
                box.style.paddingTop = 20;
                box.style.paddingBottom = 20;
                box.style.backgroundColor = new Color(0.09f, 0.09f, 0.11f, 1f);
                box.style.borderTopWidth = box.style.borderBottomWidth = box.style.borderLeftWidth = box.style.borderRightWidth = 2;
                box.style.borderTopColor = box.style.borderBottomColor = box.style.borderLeftColor = box.style.borderRightColor = new Color(1f, 0.78f, 0.13f);
            }
            surface.Add(box);

            var heading = new Label(title) { name = "steam-room-join-title" };
            heading.AddToClassList("shell-modal-title");
            var help = new Label(message) { name = "steam-room-join-help" };
            help.AddToClassList("shell-modal-help");
            if (training)
            {
                heading.style.color = new Color(1f, 0.78f, 0.13f);
                heading.style.fontSize = 16;
                heading.style.unityFontStyleAndWeight = FontStyle.Bold;
                help.style.marginTop = 8;
                help.style.marginBottom = 14;
                help.style.color = new Color(0.77f, 0.74f, 0.77f);
                help.style.whiteSpace = WhiteSpace.Normal;
            }
            box.Add(heading);
            box.Add(help);

            var row = new VisualElement { name = "steam-room-join-row" };
            row.AddToClassList("shell-modal-row");
            box.Add(row);
            if (training)
            {
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Stretch;
            }
            _acceptButton = new Button(() => Close(invokeAccept: true)) { text = "JOIN" };
            _cancelButton = new Button(() => Close(invokeCancel: true)) { text = "CANCEL" };
            _acceptButton.AddToClassList("shell-modal-submit");
            _cancelButton.AddToClassList("shell-modal-submit");
            _cancelButton.style.backgroundColor = new Color(0.14f, 0.12f, 0.17f);
            _cancelButton.style.color = new Color(0.94f, 0.93f, 0.91f);
            if (training)
            {
                _acceptButton.style.minHeight = _cancelButton.style.minHeight = 40;
                _acceptButton.style.flexGrow = _cancelButton.style.flexGrow = 1;
                _acceptButton.style.marginRight = 6;
            }
            row.Add(_acceptButton);
            row.Add(_cancelButton);
            return surface;
        }

        private void OnNavigationCancel(NavigationCancelEvent evt)
        {
            evt.StopImmediatePropagation();
            Close(invokeCancel: true);
        }

        private void Close(bool invokeAccept = false, bool invokeCancel = false)
        {
            if (_surface == null)
                return;
            var surface = _surface;
            _surface = null;
            if (_cancelHandler != null)
                surface.UnregisterCallback(_cancelHandler, TrickleDown.TrickleDown);
            surface.RemoveFromHierarchy();
            _acceptButton = null;
            _cancelButton = null;
            _cancelHandler = null;

            if (_ownsModal)
            {
                if (_active == this) _active = null;
                UiModalState.Pop();
                _ownsModal = false;
            }
            if (_host != null && _host == FrontendController.Shell?.ModalHost)
                _host.pickingMode = _originalPickingMode;
            if (_pauseMenu != null && !_wasPaused)
                _pauseMenu.SetPaused(false);
            if (_restoreFocus?.panel != null)
                _restoreFocus.Focus();
            if (_host != null && _host == FrontendController.Shell?.ModalHost)
                FrontendFocusRouter.Instance?.NotifyModalClosed();

            var callback = invokeAccept ? _accept : invokeCancel ? _cancel : null;
            _accept = null;
            _cancel = null;
            _host = null;
            _restoreFocus = null;
            _pauseMenu = null;
            callback?.Invoke();
        }
    }
}
