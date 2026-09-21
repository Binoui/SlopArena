using System;
using SlopArena.Client.Network;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Home page (issue #219): a fragment mounted into the FrontendShell
    /// hosts. Large branding plus exactly three mode actions; no roster
    /// showcase. All mode actions stay disabled until a locally valid display
    /// name is accepted (issue #209), so first launch cannot skip name setup.
    /// Identity presentation moves to the shell in Pass 2 (#220).
    /// </summary>
    public class MainMenuController : MonoBehaviour, IFrontendPageController
    {
        // Not serialized: the shell injects the per-activation context at
        // mount time (issue #219).
        private FrontendPageContext _context = null!;

        private ChatSession? _session;
        private VisualElement? _entryRow;
        private VisualElement? _savedRow;
        private TextField? _identityField;
        private Button? _identitySubmit;
        private Label? _entryFeedback;
        private Label? _savedFeedback;
        private Button? _btnTraining;
        private Button? _btnSolo;
        private Button? _btnMultiplayer;
        private bool _initialFocusPending;
        private bool _refocusPending;

        public void InjectPageContext(FrontendPageContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        private void OnEnable()
        {
            MatchConfig.Reset();
            if (_context == null)
            {
                Debug.LogError("[MainMenuController] No page context was injected; menu actions stay locked until identity renders.");
                enabled = false;
                return;
            }
            _btnTraining = _context.Q<Button>("btn-training");
            _btnSolo = _context.Q<Button>("btn-solo");
            _btnMultiplayer = _context.Q<Button>("btn-multiplayer");

            if (_btnTraining != null)
                _btnTraining.clicked += OpenTraining;
            if (_btnSolo != null)
                _btnSolo.clicked += OpenSolo;
            if (_btnMultiplayer != null)
                _btnMultiplayer.clicked += OpenMultiplayer;

            _session = ChatSession.Instance;
            var initial = _btnMultiplayer ?? _btnSolo ?? _btnTraining;
            // Single focus owner: while the first-run name gate holds, the
            // identity field owns focus; MenuNavigation gets no initial button.
            bool gated = _session == null || _session.NeedsDisplayName;
            MenuNavigation.Configure(_context, gated ? null : initial, ReturnToMainMenu);

            if (_session != null)
                _session.Changed += OnSessionChanged;
            BuildIdentityCard();
            RenderIdentity();
        }

        private void OnDisable()
        {
            if (_session != null)
                _session.Changed -= OnSessionChanged;
            _session = null;
            if (_btnTraining != null)
                _btnTraining.clicked -= OpenTraining;
            if (_btnSolo != null)
                _btnSolo.clicked -= OpenSolo;
            if (_btnMultiplayer != null)
                _btnMultiplayer.clicked -= OpenMultiplayer;
            _btnTraining = null;
            _btnSolo = null;
            _btnMultiplayer = null;
        }

        private void BuildIdentityCard()
        {
            var panel = _context.Q<VisualElement>("identity-panel");
            if (panel == null)
                return;

            panel.Clear();

            var title = new Label("DISPLAY NAME") { name = "identity-title" };
            title.AddToClassList("menu-identity-title");
            panel.Add(title);

            _savedRow = new VisualElement { name = "identity-saved" };
            var savedName = new Label(string.Empty) { name = "identity-saved-name" };
            savedName.AddToClassList("menu-identity-saved-name");
            _savedFeedback = new Label(string.Empty) { name = "identity-saved-feedback" };
            _savedFeedback.AddToClassList("menu-identity-feedback");
            _savedRow.Add(savedName);
            _savedRow.Add(_savedFeedback);
            panel.Add(_savedRow);

            _entryRow = new VisualElement { name = "identity-entry" };
            var help = new Label("Your guest identity stays with this launch. Solo and Training work offline; chat connects when the network allows.")
            {
                name = "identity-help"
            };
            help.AddToClassList("menu-identity-help");
            _identityField = new TextField { name = "identity-name" };
            _identityField.maxLength = -1;
            _identityField.AddToClassList("menu-identity-field");
            _identitySubmit = new Button(SubmitIdentity) { name = "identity-submit", text = "SET DISPLAY NAME" };
            _identitySubmit.AddToClassList("menu-identity-submit");
            var row = new VisualElement { name = "identity-field-row" };
            row.AddToClassList("menu-identity-row");
            row.Add(_identityField);
            row.Add(_identitySubmit);
            _entryFeedback = new Label(string.Empty) { name = "identity-entry-feedback" };
            _entryFeedback.AddToClassList("menu-identity-feedback");
            _entryRow.Add(help);
            _entryRow.Add(row);
            _entryRow.Add(_entryFeedback);
            panel.Add(_entryRow);

            _identityField.RegisterValueChangedCallback(_ => RenderIdentity());
            _identityField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    evt.StopImmediatePropagation();
                    SubmitIdentity();
                }
            });
            _initialFocusPending = true;
        }

        private void OnSessionChanged()
        {
            if (!isActiveAndEnabled)
                return;
            RenderIdentity();
        }

        private void RenderIdentity()
        {
            var panel = _context.Q<VisualElement>("identity-panel");

            // Fail closed: without a locally valid name, every mode action
            // stays locked even if the identity panel itself is missing.
            bool needsName = _session == null || _session.NeedsDisplayName;
            bool unlocked = !needsName && panel != null;
            if (_btnMultiplayer != null) _btnMultiplayer.SetEnabled(unlocked);
            if (_btnSolo != null) _btnSolo.SetEnabled(unlocked);
            if (_btnTraining != null) _btnTraining.SetEnabled(unlocked);
            if (panel == null)
            {
                if (needsName)
                    Debug.LogWarning("[MainMenuController] identity-panel is missing from the menu layout; first-run entry unavailable.");
                return;
            }

            _entryRow?.SetDisplayed(needsName);
            _savedRow?.SetDisplayed(!needsName);

            if (needsName)
            {
                _refocusPending = true;
                if (_identitySubmit != null)
                    _identitySubmit.SetEnabled(IsNameValid(_identityField?.value));
                if (_entryFeedback != null)
                    SetFeedback(_entryFeedback, _session?.Status ?? "Chat session is starting…");
                if (_initialFocusPending)
                {
                    _initialFocusPending = false;
                    var field = _identityField;
                    panel.schedule.Execute(() => field?.Focus());
                }
                return;
            }

            if (_session == null)
                return;
            var savedName = panel.Q<Label>("identity-saved-name");
            if (savedName != null)
                savedName.text = $"FIGHTING AS {_session.SavedDisplayName.ToUpperInvariant()}";
            if (_savedFeedback != null)
            {
                string status = _session.Status;
                if (_session.SavedNameRejected)
                    status += " Open CHAT to correct your display name.";
                SetFeedback(_savedFeedback, status);
            }

            var initial = _btnMultiplayer ?? _btnSolo ?? _btnTraining;
            if (_refocusPending && initial != null && initial.enabledSelf)
            {
                _refocusPending = false;
                panel.schedule.Execute(() => initial.Focus());
            }
        }

        private async void SubmitIdentity()
        {
            var session = _session;
            if (session == null)
                return;
            try
            {
                bool accepted = await session.AcceptDisplayNameLocallyAsync(_identityField?.value ?? string.Empty);
                if (!accepted)
                    RenderIdentity();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                RenderIdentity();
            }
        }

        private static bool IsNameValid(string? value) =>
            ChatTextValidation.TryValidateDisplayName(value, out _, out _);

        private static void SetFeedback(Label label, string text)
        {
            label.enableRichText = false;
            label.text = text ?? string.Empty;
        }

        private static void OpenTraining()
        {
            MatchConfig.Mode = GameMode.Training;
            MatchConfig.IsHost = true;
            FrontendController.Show(FrontendPage.FighterSelect);
        }

        private static void OpenSolo()
        {
            MatchConfig.Mode = GameMode.Solo;
            MatchConfig.IsHost = true;
            FrontendController.Show(FrontendPage.FighterSelect);
        }

        private static void OpenMultiplayer()
        {
            MatchConfig.Mode = GameMode.PvP;
            MatchConfig.IsHost = false;
            FrontendController.Show(FrontendPage.ServerBrowser);
        }

        private static void ReturnToMainMenu()
        {
            // MainMenu is the root of this flow; Escape here is intentionally a no-op.
        }
    }
}
