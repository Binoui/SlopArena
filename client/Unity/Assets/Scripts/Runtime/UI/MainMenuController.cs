using System;
using SlopArena.Client.Network;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Home page (issues #219/#220): a fragment mounted into the
    /// FrontendShell hosts — large branding plus exactly three mode actions;
    /// no roster showcase. All mode actions stay disabled until a locally
    /// valid display name is accepted (issue #209); the gating reads the
    /// session state directly (issue #220), never a Home-owned identity
    /// panel — identity presentation and renaming moved to the shared shell
    /// identity surface.
    /// </summary>
    public class MainMenuController : MonoBehaviour, IFrontendPageController
    {
        // Not serialized: the shell injects the per-activation context at
        // mount time (issue #219).
        private FrontendPageContext _context = null!;

        private ChatSession? _session;
        private Button? _btnTraining;
        private Button? _btnSolo;
        private Button? _btnMultiplayer;
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
            // shell identity surface owns focus; MenuNavigation gets no
            // initial button.
            bool gated = _session == null || _session.NeedsDisplayName;
            MenuNavigation.Configure(_context, gated ? null : initial, ReturnToMainMenu);

            if (_session != null)
                _session.Changed += OnSessionChanged;
            RenderIdentity();
            _refocusPending = gated;
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

        private void OnSessionChanged()
        {
            if (!isActiveAndEnabled)
                return;
            RenderIdentity();
        }

        private void RenderIdentity()
        {
            // Mode-button gating reads the session state directly (issue
            // #220): unlocking is no longer conditional on a Home-local
            // identity panel existing, because identity presentation lives in
            // the shared shell identity surface.
            bool needsName = _session == null || _session.NeedsDisplayName;
            bool unlocked = !needsName;
            if (_btnMultiplayer != null) _btnMultiplayer.SetEnabled(unlocked);
            if (_btnSolo != null) _btnSolo.SetEnabled(unlocked);
            if (_btnTraining != null) _btnTraining.SetEnabled(unlocked);

            if (needsName)
                return;

            // Unlocking after local acceptance returns focus to the first
            // mode action, even though Home no longer contains an identity
            // panel (issue #220 acceptance 1).
            var initial = _btnMultiplayer ?? _btnSolo ?? _btnTraining;
            if (_refocusPending && initial != null && initial.enabledSelf)
            {
                _refocusPending = false;
                _context.Q<VisualElement>("page-body")?.schedule.Execute(() => initial.Focus());
            }
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
