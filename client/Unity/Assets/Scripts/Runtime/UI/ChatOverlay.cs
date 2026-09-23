#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SlopArena.Client.Input;
using SlopArena.Client.Network;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Shared chat surface. The overlay owns presentation and focus only; all
    /// identity, routing, history, drafts, mute state, and send outcomes live
    /// in the persistent ChatSession.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class ChatOverlay : MonoBehaviour
    {
        private const int MaxMessageScalars = 500;
        private const double CompactLifetimeSeconds = 8d;

        /// <summary>
        /// Reserved right-hand band for the Social Dock at roomy sizes, as a
        /// percentage of the shell panel (issue #214). Page layout shrinks to
        /// the remainder, so page controls never sit under the conversation.
        /// </summary>
        private const float DockWidthPercent = 21f;
        /// <summary>Windows narrower than this use the compact strip instead of the dock.</summary>
        private const float DockMinWindowWidth = 1600f;
        public const string DockOpenPlayerPrefsKey = "SlopArena.Chat.DockOpen";

        private static ChatOverlay? _instance;

        /// <summary>The persistent presenter instance; the focus router uses
        /// it for shell-page cancel layers and social presentation.</summary>
        public static ChatOverlay? Instance => _instance;

        private VisualElement? _root;
        private VisualElement? _frame;
        private VisualElement? _collapsed;
        private VisualElement? _panel;
        private VisualElement? _main;
        private VisualElement? _conversationList;
        private VisualElement? _onlineList;
        private VisualElement? _compactFeed;
        private Label? _historyFeedback;
        private ScrollView? _history;
        private Button? _open;
        private Button? _close;
        private Button? _newest;
        private Button? _refreshDirectory;
        private Button? _send;
        private Button? _identity;
        private Button? _expand;
        private Button? _globalTab;
        private Button? _serverTab;
        private Button? _directTab;
        private Label? _statusCollapsed;
        private Label? _statusHeader;
        private Label? _titleHeader;
        private Label? _onlineFeedback;
        private Label? _unread;
        private TextField? _draft;

        private ChatSession? _session;
        private IVisualElementScheduledItem? _compactTick;
        private bool _viewBound;
        private bool _expanded;
        // The deliberate expanded social view (issue #220): presentation-only
        // on the one attached subtree; the presenter is never rehosted and
        // the state is transient — relaunch returns to the remembered
        // compact/minimized presentation.
        private bool _socialExpanded;
        private bool _suppressDraftChanged;
        private string _lastConversationKey = string.Empty;
        private string _lastDraft = string.Empty;
        private bool _showDirectory;
        private bool _enterConsumed;
        private bool _pinToNewest = true;
        private bool _ownsCursor;
        private CursorLockMode _previousCursorLock;
        private bool _previousCursorVisible;

        // Social presentation preference (issues #214/#220). On shell-hosted
        // pages this is the remembered manual minimization: true keeps the
        // compact cell visible, false keeps it minimized. Manual
        // minimize/restore rewrites it; resizing never does.
        private bool _dockOpen;
        // Per-conversation scroll anchors, launch-scoped presentation state:
        // key -> pinned-to-newest and last scroll offset survive page changes,
        // gameplay and frontend recreation via this persistent overlay.
        private readonly Dictionary<string, bool> _pinnedNewest = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Vector2> _scrollOffsets = new(StringComparer.Ordinal);
        // Conversations whose reading anchor was evicted by the bounded
        // buffer (issue #214): the next render returns them to newest with
        // the explanation already written to the conversation Feedback.
        private readonly HashSet<string> _evictionReset = new(StringComparer.Ordinal);
        private VisualElement? _hostPageRoot;
        private Label? _regionHint;
        private float _lastWindowWidth = -1f;
        // Frontend shell hosting (issue #219): when the active page is
        // fragment-mounted, the presenter attaches ONCE to the shell's
        // reserved bottom-left social cell and page navigation among shell
        // pages never re-hosts it. Legacy per-page documents (unmigrated
        // pages) keep temporary coexistence hosting until they migrate.
        private bool _hostIsShell;
        // Zero-size focusable element parked on the gameplay HUD root when
        // the panel collapses (issue #216): focus never rests on the chat
        // surface while the panel hides, so UI Toolkit's focus fixup — which
        // re-focuses the visible strip at the panel's next dirty pass and
        // would re-arm the gate as if the player had navigated there — never
        // has a lost focus to repair.
        private VisualElement? _focusParker;

        /// <summary>
        /// Explicit focus regions in the frontend shell (issue #215). Page is
        /// the active page's controls; Social is the chat surface (the panel
        /// when open, otherwise the persistent strip). Directional navigation
        /// stays inside the active region because the inactive region's
        /// controls are not focusable.
        /// </summary>
        private enum UiRegion { Page, Social }

        private UiRegion _region = UiRegion.Page;
        // Last valid focused control per region; index = (int)UiRegion.
        // Launch-scoped: validated with panel != null before use.
        private readonly VisualElement?[] _regionFocus = new VisualElement?[2];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
                return;

            ChatOverlay? existing = FindFirstObjectByType<ChatOverlay>();
            if (existing != null)
            {
                _instance = existing;
                DontDestroyOnLoad(existing.gameObject);
                return;
            }

            var gameObject = new GameObject("ChatOverlay");
            gameObject.SetActive(false);
            gameObject.AddComponent<ChatOverlay>();
            DontDestroyOnLoad(gameObject);
            gameObject.SetActive(true);
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            _dockOpen = PlayerPrefs.GetInt(DockOpenPlayerPrefsKey, 1) == 1;
        }

        private void OnEnable()
        {
            BindView();
            AttachToScene(SceneManager.GetActiveScene());
            TryBindSession();
            SceneManager.sceneLoaded += OnSceneLoaded;
            FrontendController.PageChanged += OnFrontendPageChanged;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            FrontendController.PageChanged -= OnFrontendPageChanged;
            if (_expanded)
                ChatInputGate.End();
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                _session.Changed -= OnSessionChanged;
                _session.ConversationHistoryTrimmed -= OnConversationHistoryTrimmed;
            }
            if (_instance == this)
                _instance = null;
            _root?.RemoveFromHierarchy();
            ChatInputGate.End();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _ownsCursor = false;
            // Scene boundaries are hard transitions (issue #215): the page
            // region owns the next frontend activation. Social focus memory
            // is launch-scoped and revalidated on restore.
            _region = UiRegion.Page;
            // Frontend recreation must not reset the conversation view
            // (issue #214): scene loads keep the dock/expanded state and
            // re-attach to the explicit host. Entering gameplay is the one
            // deliberate collapse — match entry ends interactive chat while
            // ChatSession keeps drafts and selection. The transient expanded
            // state is reset with it (issue #220).
            if (!FrontendController.IsFrontendActive && ChatInputGate.IsGameplayScene)
            {
                _socialExpanded = false;
                SetExpanded(false, false);
            }
            AttachToScene(scene);
        }

        private void AttachToScene(Scene scene)
        {
            if (_root == null) return;
            // Explicit presentation hosts (issue #214, shell cell from
            // #219): the shell's reserved bottom-left social cell on
            // fragment-mounted pages, the legacy page document on unmigrated
            // pages, and the match HUD document in gameplay. No
            // first-document discovery; anything else detaches.
            if (FrontendController.IsFrontendActive)
            {
                if (FrontendController.TryGetShellChatHost(out var socialHost, out var pageContentRoot))
                {
                    AttachToShell(socialHost, pageContentRoot);
                    return;
                }
                // Legacy per-page hosting (temporary coexistence until the
                // remaining pages migrate).
                AttachToDocumentHost(scene);
                return;
            }
            AttachToDocumentHost(scene);
        }

        /// <summary>
        /// The one shell attach (issue #219): the presenter moves into the
        /// reserved social cell and stays there for every shell-hosted page
        /// change. Compact ↔ expanded is presentation-only on this one
        /// subtree (issue #220) — the compact cell is visible unless the
        /// player explicitly minimized it, and the remembered preference
        /// decides which presentation re-entry restores.
        /// </summary>
        private void AttachToShell(VisualElement socialHost, VisualElement pageContentRoot)
        {
            _hostIsShell = true;
            if (_root.parent != socialHost)
            {
                _root.RemoveFromHierarchy();
                socialHost.Add(_root);
            }
            _hostPageRoot = pageContentRoot;
            _expanded = _dockOpen;
            _socialExpanded = false;
            ApplyPresentation();
            FrontendFocusRouter.NotifyPresentationChanged();
        }

        private void AttachToDocumentHost(Scene scene)
        {
            UIDocument? host = FrontendController.IsFrontendActive
                ? FrontendController.ActivePageDocument
                : FindFirstObjectByType<HUDManager>()?.Document;
            if (host == null || !host.isActiveAndEnabled ||
                host.gameObject.scene != scene || host.rootVisualElement == null)
            {
                _hostPageRoot = null;
                if (_root.panel != null)
                    _root.RemoveFromHierarchy();
                return;
            }
            bool wasShell = _hostIsShell;
            _hostIsShell = false;
            if (_root.parent != host.rootVisualElement)
            {
                _root.RemoveFromHierarchy();
                host.rootVisualElement.Add(_root);
            }
            RegisterRegionClickFollow(host.rootVisualElement);
            // Clear first: a legacy re-hosting that flips presentation (shell
            // → remembered dock/strip) must not touch the shell workspace.
            _hostPageRoot = null;
            if (wasShell)
                RestoreLegacyPresentation();
            _hostPageRoot = FindHostPageRoot(host);
            _root.BringToFront();
            ApplyPresentation();
        }

        /// <summary>
        /// Re-entering a legacy full-screen page after a shell page (issue
        /// #219): the always-visible shell cell is not the remembered dock
        /// preference, so the legacy presentation honors it (#214).
        /// </summary>
        private void RestoreLegacyPresentation()
        {
            SetExpanded(IsRoomyWindow() && _dockOpen, false);
        }

        private VisualElement? FindHostPageRoot(UIDocument host)
        {
            if (_root == null || _root.parent != host.rootVisualElement)
                return null;
            // The page's UXML root is the host document's first child; the
            // chat root itself is a later sibling added by this overlay.
            foreach (var child in host.rootVisualElement.Children())
            {
                if (child != _root)
                    return child;
            }
            return null;
        }

        /// <summary>
        /// Input follows the last-touched side (issue #215): a click into the
        /// page makes the page the active region and a click into the chat
        /// surface makes Social active, so mouse users get region-local
        /// navigation without the explicit switch. Registered on the host
        /// document root, which UI Toolkit re-clones on every page enable, so
        /// the registration never accumulates across page re-entries. The
        /// same root consumes the keyboard Tab switch before UI Toolkit's
        /// focus cycling can move focus inside the old region.
        /// </summary>
        private void RegisterRegionClickFollow(VisualElement hostRoot)
        {
            if (hostRoot.userData is bool)
                return;
            hostRoot.userData = true;
            hostRoot.RegisterCallback<ClickEvent>(evt =>
            {
                if (!FrontendController.IsFrontendActive || ChatInputGate.IsGameplayScene
                    || UiModalState.Presented)
                    return;
                if (evt.target is not VisualElement target)
                    return;
                if (_root.Contains(target))
                {
                    SetActiveRegion(UiRegion.Social, focusTarget: false);
                    return;
                }
                bool inPage = _hostPageRoot != null && _hostPageRoot.Contains(target);
                bool changed = _region != UiRegion.Page;
                SetActiveRegion(UiRegion.Page, focusTarget: false);
                if (changed && inPage)
                {
                    // The clicked page control only grabs focus while it was
                    // focusable, which happened after its pointer-down; focus
                    // it explicitly so directional input continues there.
                    FocusClickedPageControl(target);
                }
            });
        }

        private void FocusClickedPageControl(VisualElement target)
        {
            for (var element = target; element != null; element = element.parent)
            {
                if (element is Focusable && TryFocus(element))
                    return;
            }
        }


        private void OnFrontendPageChanged(FrontendPage page)
        {
            // Page activation replaces scene-load teardown: re-host the chat on
            // the newly active page without collapsing it or touching state.
            // On shell-hosted pages the attach is a no-op when the presenter
            // already sits in the reserved cell (issue #219).
            AttachToScene(SceneManager.GetActiveScene());
            // Roomy legacy menus show the dock open according to the remembered
            // preference; compact layouts keep the strip (issue #214). Shell
            // pages keep their own presentation (issue #220).
            if (!_hostIsShell && FrontendController.IsFrontendActive && !_expanded && _dockOpen && IsRoomyWindow())
                SetExpanded(true, false);
        }

        private void LateUpdate()
        {
            if (!_ownsCursor) return;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
        }

        private void Update()
        {
            TryBindSession();
            // Evaluate the gate every frame so an armed release requirement
            // clears as soon as every held control is released (issue #215):
            // nothing evaluates it in menus, and a stale arm would turn the
            // next fresh back press into a dead one.
            _ = ChatInputGate.SuppressShortcuts;
            var keyboard = Keyboard.current;
            bool enterHeld = keyboard != null && (keyboard.enterKey.isPressed || keyboard.numpadEnterKey.isPressed);
            if (!enterHeld) _enterConsumed = false;
            if (keyboard?.escapeKey.wasPressedThisFrame == true && OwnsBackPress())
            {
                HandleEscape();
                return;
            }
            // Face-button submit/cancel for controller navigation (issue
            // #215): the dpad move path is native, these are not. On
            // shell-hosted pages the focus router is the single pump owner
            // (issue #220); legacy documents and gameplay keep this pump.
            bool shellFrontend = _hostIsShell && FrontendController.IsFrontendActive
                && !ChatInputGate.IsGameplayScene;
            if (!shellFrontend)
                GamepadUIBridge.Pump(_root?.panel);
            PollRegionSwitch();
            float windowWidth = Screen.width;
            if (!Mathf.Approximately(windowWidth, _lastWindowWidth))
            {
                bool wasRoomy = _lastWindowWidth >= DockMinWindowWidth;
                _lastWindowWidth = windowWidth;
                // Presentation transitions are a menu concern (issue #216):
                // a gameplay window resize never opens or closes the chat
                // panel by itself; gameplay keeps the legacy overlay at any
                // width.
                // Presentation transitions on resize are a legacy-frontend
                // concern (issues #214/#216): the shell cell owns its geometry
                // through the shell density classes, and gameplay keeps the
                // legacy overlay at any width.
                if (FrontendController.IsFrontendActive && !_hostIsShell)
                {
                    // Resizing between roomy and compact changes the presentation,
                    // never the remembered roomy preference (issue #214): a chat
                    // opened as a compact replacement collapses back to the
                    // remembered preference when the window becomes roomy again.
                    if (IsRoomyWindow() && !wasRoomy && _expanded && !_dockOpen)
                        SetExpanded(false, false);
                    else if (IsRoomyWindow() && !wasRoomy && !_expanded && _dockOpen)
                    {
                        // Widening into roomy restores the remembered dock: the
                        // player never chose to close it (issue #215).
                        SetExpanded(true, false);
                    }
                    else
                    {
                        bool enteredReplace = !IsRoomyWindow() && wasRoomy && _expanded;
                        ApplyPresentation();
                        // A dock shrunk into the compact replacement keeps the
                        // conversation interactive: put the composer in focus
                        // (issue #215).
                        if (enteredReplace)
                            _root?.schedule.Execute(() => _draft?.Focus()).StartingIn(0);
                    }
                }
            }
            if (!enterHeld || _enterConsumed)
                return;
            _enterConsumed = true;
            if (!ChatInputGate.IsGameplayScene)
                return;
            if (UiModalState.Presented)
            {
                // The pause menu wins over chat (issue #216): chat neither
                // opens over the modal nor keeps strip focus, so the pause
                // owner's Escape is never swallowed by an armed gate.
                if (IsFocusInsideChatSurface())
                {
                    (_root?.panel?.focusController?.focusedElement as VisualElement)?.Blur();
                    ChatInputGate.End();
                }
                return;
            }
            if (!_expanded && !IsEditorFocused())
            {
                SetExpanded(true, true);
            }
            else if (_expanded && IsEditorFocused(_draft))
            {
                SendDraft(true);
            }
        }

        private static bool IsRoomyWindow() => Screen.width >= DockMinWindowWidth;

        /// <summary>
        /// Whether the chat surface owns the current back press (issue #215).
        /// The topmost modal always wins over chat. In menus the chat owns the
        /// press only while the focus — or the replaced page — is inside the
        /// chat surface; a page-focused press falls through to the page's
        /// Back. In gameplay an open panel always owns the press.
        /// </summary>
        private bool OwnsBackPress()
        {
            if (UiModalState.Presented)
                return false;
            if (ChatInputGate.IsGameplayScene)
            {
                // An open panel always owns the press; collapsed strip focus
                // is explicit chat navigation, so the first press exits it
                // and the next fresh press reaches the pause owner (issue
                // #216).
                return _expanded || IsFocusInsideChatSurface();
            }
            // Shell pages resolve cancel through the focus router (issue
            // #220); the overlay handles only legacy documents here.
            if (!_expanded || !FrontendController.IsFrontendActive || _hostIsShell)
                return false;
            if (!IsRoomyWindow())
                return true;
            return _root?.panel?.focusController?.focusedElement is VisualElement focused
                && _root.Contains(focused);
        }

        /// <summary>
        /// The explicit Page/Social region switch (issue #215): Q on the
        /// keyboard, the north face button (Y / Triangle) on controller.
        /// Tab keeps UI Toolkit's own focus cycling, which stays inside the
        /// active region because the inactive region is not focusable, so the
        /// switch needs a key the panel does not consume pre-dispatch.
        /// Skipped while a text field is typing and while a modal owns the
        /// surface; gameplay keeps the legacy composer interaction.
        /// </summary>
        private void PollRegionSwitch()
        {
            if (!FrontendController.IsFrontendActive || ChatInputGate.IsGameplayScene)
                return;
            // The focus router owns region switching on shell pages (issue
            // #220); the chat overlay keeps the legacy document behavior.
            if (_hostIsShell)
                return;
            if (UiModalState.Presented)
                return;
            bool pad = Gamepad.current?.buttonNorth.wasPressedThisFrame == true;
            bool qKey = !IsEditorFocused() && Keyboard.current?.qKey.wasPressedThisFrame == true;
            if (!pad && !qKey)
                return;
            SetActiveRegion(_region == UiRegion.Page ? UiRegion.Social : UiRegion.Page, focusTarget: true);
            UISFX.PlayClick();
        }

        /// <summary>
        /// Switch the active focus region (issue #215). The inactive region's
        /// controls stop being focusable, so directional navigation can never
        /// leave the active region; each region's last valid focus is
        /// remembered and restored on the next switch.
        /// </summary>
        private void SetActiveRegion(UiRegion region, bool focusTarget)
        {
            if (_region != region)
            {
                CaptureRegionFocus(_region);
                _region = region;
                ApplyRegionFocusability();
                UpdateRegionHint();
            }
            if (focusTarget)
                FocusRegionDefault(region);
        }

        /// <summary>Remember a region's currently focused control, if the
        /// focus actually sits inside that region (issue #215).</summary>
        private void CaptureRegionFocus(UiRegion region)
        {
            if (_root?.panel?.focusController?.focusedElement is not VisualElement focused)
                return;
            bool inChat = _root.Contains(focused);
            bool inPage = _hostPageRoot != null && _hostPageRoot.Contains(focused);
            if (region == UiRegion.Social && inChat)
                _regionFocus[(int)UiRegion.Social] = focused;
            else if (region == UiRegion.Page && inPage)
                _regionFocus[(int)UiRegion.Page] = focused;
        }

        /// <summary>Restore the remembered focus of a region, falling back to
        /// the region's conventional entry point (issue #215).</summary>
        private void FocusRegionDefault(UiRegion region)
        {
            if (TryFocus(_regionFocus[(int)region]))
                return;
            if (region == UiRegion.Page)
            {
                if (TryFocus(MenuNavigation.PageInitialFocus))
                    return;
                TryFocus(_hostPageRoot == null ? null : FindFirstFocusable(_hostPageRoot));
                return;
            }
            if (!_expanded)
            {
                TryFocus(_open);
                return;
            }
            if (!TryFocus(_draft))
                TryFocus(_panel == null ? null : FindFirstFocusable(_panel));
        }

        private static bool TryFocus(VisualElement? element)
        {
            if (element == null || element.panel == null || !element.focusable || !element.enabledSelf)
                return false;
            element.Focus();
            return true;
        }

        private static VisualElement? FindFirstFocusable(VisualElement root, VisualElement? exclude = null)
        {
            foreach (var element in root.Query<VisualElement>().ToList())
            {
                if (exclude != null && exclude.Contains(element))
                    continue;
                if (element is Focusable focusable && focusable.focusable && element.enabledSelf
                    && element.style.display != DisplayStyle.None)
                    return element;
            }
            return null;
        }

        /// <summary>
        /// Region-local navigation (issue #215): only the active region's
        /// controls are focusable, so keyboard/controller navigation stays
        /// inside it. Gameplay keeps the legacy behavior — the HUD surface is
        /// not region-split, so everything there stays focusable.
        /// </summary>
        private void ApplyRegionFocusability()
        {
            if (_hostIsShell && FrontendController.IsFrontendActive && !ChatInputGate.IsGameplayScene)
            {
                // The focus router owns shell-page regions (issue #220); the
                // presenter re-applies after every dynamic rebuild so new
                // rows respect the active region.
                FrontendFocusRouter.Instance?.ApplyRegionFocusability();
                return;
            }
            bool social, page;
            if (!FrontendController.IsFrontendActive || ChatInputGate.IsGameplayScene)
            {
                // Gameplay keeps the open panel's controls focusable, but a
                // closed chat takes no navigation focus at all (user
                // decision, issue #216): Enter is the only route to the
                // combat composer, and the collapsed strip is click-only.
                social = ChatInputGate.IsGameplayScene ? _expanded : true;
                page = true;
            }
            else if (!IsRoomyWindow() && _expanded)
            {
                // Compact chat replaced the page; the page has no controls to
                // navigate and the conversation owns the input (issue #215).
                social = true;
                page = false;
            }
            else
            {
                social = _region == UiRegion.Social;
                page = !social;
            }
            SetRegionFocusable(_root, social);
            SetRegionFocusable(_hostPageRoot, page);
            if (_history != null)
                _history.focusable = social;
        }

        /// <summary>Buttons and text fields are the only focusable controls in
        /// these surfaces; toggling exactly those keeps scroll views and
        /// labels out of the navigation ring (issue #215).</summary>
        private static void SetRegionFocusable(VisualElement? root, bool focusable, VisualElement? exclude = null)
        {
            if (root == null)
                return;
            root.Query<Button>().ForEach(button =>
            {
                if (exclude == null || !exclude.Contains(button))
                    button.focusable = focusable;
            });
            root.Query<TextField>().ForEach(field =>
            {
                if (exclude == null || !exclude.Contains(field))
                    field.focusable = focusable;
            });
        }

        /// <summary>The visible region-switch affordance (issue #215): a small
        /// chip naming the region the switch reaches. Hidden in gameplay and
        /// while no host surface is attached.</summary>
        private void UpdateRegionHint()
        {
            if (_regionHint == null)
                return;
            if (_hostIsShell && FrontendController.IsFrontendActive && !ChatInputGate.IsGameplayScene)
            {
                // The focus router owns the shell hint (issue #220).
                _regionHint.style.display = DisplayStyle.None;
                return;
            }
            bool frontend = FrontendController.IsFrontendActive && !ChatInputGate.IsGameplayScene;
            _regionHint.style.display = frontend ? DisplayStyle.Flex : DisplayStyle.None;
            if (!frontend)
                return;
            _regionHint.text = _region == UiRegion.Page ? "Q/Y // SOCIAL" : "Q/Y // PAGE";
            // Legacy overlay defaults (Chat.uss): bottom-left of the host.
            _regionHint.style.top = StyleKeyword.Auto;
            _regionHint.style.bottom = 12;
            _regionHint.style.left = 16;
        }

        private void BindView()
        {
            if (_viewBound)
                return;

            _root = Resources.Load<VisualTreeAsset>("UI/Chat").CloneTree();
            _root.style.position = Position.Absolute;
            _root.style.left = 0;
            _root.style.right = 0;
            _root.style.top = 0;
            _root.style.bottom = 0;
            _root.pickingMode = PickingMode.Ignore;
            var frame = _root.Q<VisualElement>("chat-root");
            if (frame != null) frame.pickingMode = PickingMode.Ignore;
            _frame = frame;
            _collapsed = _root.Q<VisualElement>("chat-collapsed");
            _panel = _root.Q<VisualElement>("chat-panel");
            _main = _root.Q<VisualElement>("chat-main");
            _conversationList = _root.Q<VisualElement>("conversation-list");
            _onlineList = _root.Q<VisualElement>("online-list");
            _compactFeed = _root.Q<VisualElement>("chat-public-feed");
            _historyFeedback = _root.Q<Label>("history-feedback");
            _history = _root.Q<ScrollView>("chat-history");
            _open = _root.Q<Button>("chat-open");
            _close = _root.Q<Button>("chat-close");
            _newest = _root.Q<Button>("chat-newest");
            _refreshDirectory = _root.Q<Button>("directory-refresh");
            _send = _root.Q<Button>("chat-send");
            _identity = _root.Q<Button>("chat-identity");
            _expand = _root.Q<Button>("chat-expand");
            _globalTab = _root.Q<Button>("chat-tab-global");
            _serverTab = _root.Q<Button>("chat-tab-server");
            _directTab = _root.Q<Button>("chat-tab-direct");
            _statusCollapsed = _root.Q<Label>("chat-status-collapsed");
            _statusHeader = _root.Q<Label>("chat-status-header");
            _titleHeader = _root.Q<Label>("chat-title-header");
            _onlineFeedback = _root.Q<Label>("online-feedback");
            _unread = _root.Q<Label>("chat-unread");
            _draft = _root.Q<TextField>("chat-draft");
            _regionHint = _root.Q<Label>("chat-region-hint");
            if (_regionHint != null)
                _regionHint.pickingMode = PickingMode.Ignore;

            if (_open != null) _open.clicked += OnOpenButtonClicked;
            if (_close != null) _close.clicked += OnCloseButtonClicked;
            if (_expand != null) _expand.clicked += ExpandSocial;
            if (_identity != null) _identity.clicked += OpenShellIdentitySurface;
            if (_newest != null) _newest.clicked += ScrollToNewest;
            if (_refreshDirectory != null) _refreshDirectory.clicked += RefreshDirectory;
            if (_send != null) _send.clicked += () => SendDraft(ChatInputGate.IsGameplayScene);
            if (_globalTab != null) _globalTab.clicked += SelectGlobal;
            if (_serverTab != null) _serverTab.clicked += SelectServer;
            if (_directTab != null) _directTab.clicked += SelectDirectTab;
            if (_history != null)
            {
                // Controller navigation of the retained history (issue #215):
                // a focused scroll view scrolls with directional input.
                _history.focusable = true;
                _history.contentContainer.RegisterCallback<GeometryChangedEvent>(_ =>
                {
                    if (_pinToNewest) ScrollToNewest();
                });
                _history.verticalScroller.valueChanged += _ =>
                {
                    UpdateScrollAnchor();
                    TryMarkActiveRead();
                };
            }

            if (_draft != null)
            {
                _draft.maxLength = -1;
                _draft.RegisterValueChangedCallback(OnDraftChanged);
                RegisterEditorFocus(_draft);
            }

            _root.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
            // Explicit chat interaction arms the gameplay input gate (issue
            // #216): any chat control gaining focus during a match suppresses
            // fighter input until focus leaves the chat surface. The
            // collapsed strip is unfocusable, so combat focus only reaches
            // the panel after the player explicitly opens it. Focus events
            // bubble, so these handlers cover the whole surface.
            _root.RegisterCallback<FocusInEvent>(OnChatSurfaceFocusIn);
            _root.RegisterCallback<FocusOutEvent>(OnChatSurfaceFocusOut);
            // Gamepad B (and keyboard cancel) with focus inside the chat
            // surface: one press leaves the interaction (issue #215). The page
            // root is not an ancestor of the chat surface, so this handler is
            // the only cancel path for chat-focused presses.
            _root.RegisterCallback<NavigationCancelEvent>(OnRootNavigationCancel);
            _compactTick = _compactFeed?.schedule.Execute(UpdateCompactFeed).Every(200);
            _viewBound = true;
            SetExpanded(false, false);
        }

        private void RegisterEditorFocus(TextField field)
        {
            field.RegisterCallback<FocusInEvent>(_ => ChatInputGate.Begin());
            field.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (!IsEditorFocused() && !(_expanded && ChatInputGate.IsGameplayScene))
                    ChatInputGate.End();
            });
        }

        private void TryBindSession()
        {
            if (_session != null)
                return;

            ChatSession? session = ChatSession.Instance;
            if (session == null)
                return;

            _session = session;
            _session.Changed += OnSessionChanged;
            _session.ConversationHistoryTrimmed += OnConversationHistoryTrimmed;
            RenderSession();
        }

        private void OnSessionChanged()
        {
            if (!isActiveAndEnabled || _suppressDraftChanged)
                return;
            RenderSession();
        }

        private void RenderSession()
        {
            if (_root == null)
                return;

            if (_session == null)
            {
                SetText(_statusCollapsed, "CHAT SESSION STARTING");
                SetText(_statusHeader, "CHAT SESSION STARTING");
                return;
            }

            ChatConversation? active = _session.ActiveConversation;
            if (active?.Unread > 0 && IsConversationShown() && ShouldMarkActiveRead())
                _session.MarkActiveRead();
            SetText(_statusCollapsed, _session.Status);
            SetText(_statusHeader, _session.Status);

            SetText(_titleHeader, active?.Title ?? "CHAT");
            int directUnread = 0;
            foreach (ChatConversation conversation in _session.Conversations)
            {
                if (conversation.Key.StartsWith("direct:", StringComparison.Ordinal))
                    directUnread += Math.Max(0, conversation.Unread);
            }
            if (_unread != null)
            {
                _unread.text = directUnread > 0 ? directUnread.ToString() : string.Empty;
                _unread.EnableInClassList("chat-unread--visible", directUnread > 0);
            }
            _main?.SetDisplayed(true);
            // Identity presentation and renaming moved to the shared shell
            // identity surface (issue #220); the presenter only routes there.
            if (_identity != null)
                _identity.SetDisplayed(FrontendController.IsFrontendActive);

            RenderTabs(active, _session.IsConnected);
            RenderConversations(active);
            RenderDirectory(active);
            RenderHistory(active);
            RenderCompactFeed();
            // Dynamic rows are new Buttons with default focusability; re-apply
            // the active region's gating after every rebuild (issue #215).
            ApplyRegionFocusability();
        }

        private void RenderTabs(ChatConversation? active, bool connected)
        {
            string key = active?.Key ?? string.Empty;
            bool isGlobal = !_showDirectory && string.Equals(key, "global", StringComparison.Ordinal);
            bool isServer = !_showDirectory && key.StartsWith("server:", StringComparison.Ordinal);
            bool isDirect = _showDirectory || key.StartsWith("direct:", StringComparison.Ordinal);

            _globalTab?.EnableInClassList("chat-tab--active", isGlobal);
            _serverTab?.EnableInClassList("chat-tab--active", isServer);
            _directTab?.EnableInClassList("chat-tab--active", isDirect);
            _serverTab?.SetEnabled(connected && (_session?.JoinedServerId.HasValue == true || isServer));
            _directTab?.SetEnabled(true);
        }

        private void RenderConversations(ChatConversation? active)
        {
            if (_conversationList == null || _session == null)
                return;

            _conversationList.Clear();
            foreach (ChatConversation conversation in _session.Conversations)
                _conversationList.Add(BuildConversationRow(conversation, active));
        }

        /// <summary>
        /// One conversation row shared by the expanded view's management
        /// column and the directory view (issue #220): conversation
        /// management is behind explicit controls, not a permanently visible
        /// list.
        /// </summary>
        private VisualElement BuildConversationRow(ChatConversation conversation, ChatConversation? active)
        {
            var row = new VisualElement();
            row.AddToClassList("conversation-row");
            row.EnableInClassList("conversation-row--active", active != null && conversation.Key == active.Key);

            var select = new Button(() =>
            {
                _showDirectory = false;
                _session.SelectConversation(conversation.Key);
                RenderSession();
            });
            select.text = conversation.Title;
            select.enableRichText = false;
            select.AddToClassList("conversation-select");
            row.Add(select);

            if (conversation.Unread > 0 && active?.Key != conversation.Key)
            {
                var unread = new Label(conversation.Unread.ToString());
                unread.enableRichText = false;
                unread.AddToClassList("conversation-unread");
                row.Add(unread);
            }
            return row;
        }

        private void RenderDirectory(ChatConversation? active)
        {
            bool showDirectory = _showDirectory;
            _onlineList?.SetDisplayed(showDirectory);
            _refreshDirectory?.SetDisplayed(showDirectory);
            _onlineFeedback?.SetDisplayed(showDirectory);
            // The directory view replaces the history while it is shown, so
            // read acknowledgement is suppressed for it (issue #218).
            _history?.SetDisplayed(!showDirectory);
            if (!showDirectory || _onlineList == null || _session == null)
                return;

            _onlineList.Clear();

            // Conversation management lives behind the explicit DIRECT view
            // (issue #220): switching conversations never needs a persistent
            // list next to the messages.
            var conversationsLabel = new Label("CONVERSATIONS") { enableRichText = false };
            conversationsLabel.AddToClassList("chat-section-label");
            _onlineList.Add(conversationsLabel);
            foreach (ChatConversation conversation in _session.Conversations)
                _onlineList.Add(BuildConversationRow(conversation, active));

            var mutedLabel = new Label("MUTED PLAYERS") { enableRichText = false };
            mutedLabel.AddToClassList("chat-section-label");
            _onlineList.Add(mutedLabel);
            foreach (var player in _session.MutedPlayers)
            {
                var unmute = new Button(() => _session.SetMuted(player.PlayerId, false))
                {
                    text = $"UNMUTE {FormatIdentity(player)}",
                    enableRichText = false
                };
                unmute.AddToClassList("chat-mute");
                _onlineList.Add(unmute);
            }
            if (!_session.DirectoryAvailable)
            {
                SetText(_onlineFeedback, string.IsNullOrEmpty(_session.Status)
                    ? "ONLINE DIRECTORY UNAVAILABLE"
                    : _session.Status);
                return;
            }

            SetText(_onlineFeedback, "SELECT A PLAYER FOR A DIRECT MESSAGE");
            ChatPlayer? self = _session.Self;
            foreach (ChatPlayer player in _session.OnlinePlayers)
            {
                if (self != null && player.PlayerId == self.PlayerId)
                    continue;

                var row = new VisualElement();
                row.AddToClassList("online-row");
                var select = new Button(() =>
                {
                    _showDirectory = false;
                    _session.OpenDirect(player);
                    SetExpanded(true, false);
                });
                select.text = FormatIdentity(player);
                select.enableRichText = false;
                select.AddToClassList("online-player");
                row.Add(select);

                var mute = new Button(() =>
                {
                    _session.SetMuted(player.PlayerId, !_session.IsMuted(player.PlayerId));
                    RenderSession();
                });
                mute.text = _session.IsMuted(player.PlayerId) ? "UNMUTE" : "MUTE";
                mute.AddToClassList("chat-mute");
                row.Add(mute);
                _onlineList.Add(row);
            }
        }

        private void RenderHistory(ChatConversation? active)
        {
            if (_history == null || _draft == null || _session == null)
                return;

            bool changedConversation = active?.Key != _lastConversationKey;
            // A pending eviction reset (issue #214) wins over the live scroller
            // state: the live anchor still reflects the evicted reading
            // position, so return to newest instead of re-capturing it.
            bool evictionReset = active != null && _evictionReset.Remove(active.Key);
            if (!evictionReset && !changedConversation && active != null)
            {
                // Capture the live anchor before rebuilding so a re-render of
                // the same conversation keeps its reading position (issue #214).
                UpdateScrollAnchor();
            }
            string key = active?.Key ?? string.Empty;
            bool atNewest = evictionReset || (changedConversation ? IsPinnedNewest(key) : _pinToNewest);
            if (evictionReset)
                _pinnedNewest[key] = true;
            Vector2 oldOffset = changedConversation ? GetScrollOffset(key) : _history.scrollOffset;
            _history.Clear();

            if (active == null)
            {
                _lastConversationKey = string.Empty;
                _newest?.SetDisplayed(false);
                return;
            }

            foreach (ChatMessage message in active.Messages)
            {
                if (!_session.IsMessageVisible(message))
                    continue;
                _history.Add(BuildMessageRow(message));
            }

            _lastConversationKey = active.Key;
            if (_draft.value != active.Draft)
                _draft.SetValueWithoutNotify(active.Draft);
            _lastDraft = active.Draft;
            _pinToNewest = atNewest;

            bool canSend = active.CanSend && !active.IsSending && _session.IsConnected;
            _draft.SetEnabled(!active.IsSending);
            _send?.SetEnabled(canSend && IsMessageValid(active.Draft));
            if (_send != null)
                _send.text = active.IsSending ? "SENDING" : "SEND";
            SetText(_historyFeedback, active.Feedback);

            _history.schedule.Execute(() =>
            {
                if (atNewest)
                    ScrollToNewest();
                else
                    _history.scrollOffset = oldOffset;
                // The directory view replaces history while it is shown, so
                // the return-to-newest affordance stays hidden with it
                // (issue #220).
                _newest?.SetDisplayed(!atNewest && _history.childCount > 0 && !_showDirectory);
            }).StartingIn(0);

        }

        private VisualElement BuildMessageRow(ChatMessage message)
        {
            // Per-line mute buttons are gone (issue #220): personal mute is
            // the explicit sender action in the directory view.
            var row = new VisualElement();
            row.AddToClassList("chat-message");
            var text = new Label($"{message.Sender.DisplayName} [{message.Sender.SessionTag}]: {message.Text}");
            text.enableRichText = false;
            text.AddToClassList("chat-message__text");
            row.Add(text);
            return row;
        }

        private void RenderCompactFeed()
        {
            if (_compactFeed == null || _session == null)
                return;

            var publicMessages = new List<ChatMessage>();
            foreach (ChatConversation conversation in _session.Conversations)
            {
                if (conversation.Key != "global"
                    && (!conversation.Key.StartsWith("server:", StringComparison.Ordinal)
                        || !_session.JoinedServerId.HasValue
                        || conversation.Key != $"server:{_session.JoinedServerId.Value}"))
                    continue;
                foreach (ChatMessage message in conversation.Messages)
                {
                    if (_session.IsMessageVisible(message))
                        publicMessages.Add(message);
                }
            }

            publicMessages.Sort((left, right) => left.SentAt.CompareTo(right.SentAt));
            _compactFeed.Clear();
            int first = Math.Max(0, publicMessages.Count - 3);
            for (int i = first; i < publicMessages.Count; i++)
            {
                ChatMessage message = publicMessages[i];
                var line = new Label($"{ChannelLabel(message.Channel)} // {FormatIdentity(message.Sender)}: {message.Text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ')}");
                line.enableRichText = false;
                line.userData = message.SentAt;
                line.AddToClassList("chat-public-line");
                _compactFeed.Add(line);
            }
            UpdateCompactFeed();
        }

        private void UpdateCompactFeed()
        {
            if (_compactFeed == null)
                return;

            bool anyVisible = false;
            foreach (VisualElement child in _compactFeed.Children())
            {
                if (child is not Label line || line.userData is not DateTimeOffset sentAt)
                    continue;
                double age = (DateTimeOffset.UtcNow - sentAt).TotalSeconds;
                float opacity = Mathf.Clamp01(1f - (float)(age / CompactLifetimeSeconds));
                line.style.opacity = opacity;
                line.style.display = opacity <= 0f ? DisplayStyle.None : DisplayStyle.Flex;
                anyVisible |= opacity > 0f;
            }
            _compactFeed.style.display = anyVisible && !_expanded && ChatInputGate.IsGameplayScene
                ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnRootKeyDown(KeyDownEvent evt)
        {
            if (IsEnter(evt.keyCode) && ChatInputGate.IsGameplayScene)
            {
                evt.StopImmediatePropagation();
                return;
            }
            if (evt.keyCode == KeyCode.Escape && OwnsBackPress())
            {
                evt.StopImmediatePropagation();
                HandleEscape();
                return;
            }

            if (!IsEnter(evt.keyCode) || !_expanded || !IsEditorFocused(_draft))
                return;

            evt.StopImmediatePropagation();
            SendDraft(false);
        }
        /// <summary>Cancel with focus inside the chat surface (issue #215):
        /// one press leaves the interaction; it never reaches a page Back
        /// handler because the page root is not an ancestor of the chat
        /// surface.</summary>
        private void OnRootNavigationCancel(NavigationCancelEvent evt)
        {
            if (!OwnsBackPress())
                return;
            evt.StopImmediatePropagation();
            HandleEscape();
        }

        /// <summary>Combat chat interaction is explicit (issue #216): any
        /// chat control gaining focus during gameplay arms the input gate.
        /// The collapsed strip is unfocusable, so focus only lands here
        /// inside the panel the player explicitly opened — navigation within
        /// it suppresses fighter input just like typing does.</summary>
        private void OnChatSurfaceFocusIn(FocusInEvent evt)
        {
            if (!ChatInputGate.IsGameplayScene)
                return;
            ChatInputGate.Begin();
        }

        /// <summary>Focus leaving the chat surface ends an unexpanded
        /// gameplay navigation session. An open panel keeps the gate until it
        /// is closed — SetExpanded/HandleEscape own that transition.</summary>
        private void OnChatSurfaceFocusOut(FocusOutEvent evt)
        {
            if (!ChatInputGate.IsGameplayScene || _expanded)
                return;
            if (evt.relatedTarget is VisualElement next && _root != null && _root.Contains(next))
                return;
            ChatInputGate.End();
        }

        private bool IsFocusInsideChatSurface()
        {
            return _root?.panel?.focusController?.focusedElement is VisualElement focused
                && _root.Contains(focused);
        }

        /// <summary>Move gameplay focus off the chat surface onto a parked,
        /// invisible focusable element on the HUD root before the panel
        /// hides (issue #216). Focus then survives the panel hide, so no
        /// rescue pass can re-focus the strip and re-arm the gate.</summary>
        private void ParkFocusOutsideChat()
        {
            if (!ChatInputGate.IsGameplayScene)
                return;
            var hostRoot = FindFirstObjectByType<HUDManager>()?.Document?.rootVisualElement;
            if (hostRoot == null)
                return;
            if (_focusParker == null)
            {
                _focusParker = new VisualElement { name = "chat-focus-parker", focusable = true };
                _focusParker.pickingMode = PickingMode.Ignore;
                _focusParker.style.width = 0;
                _focusParker.style.height = 0;
            }
            if (_focusParker.parent != hostRoot)
            {
                _focusParker.RemoveFromHierarchy();
                hostRoot.Add(_focusParker);
            }
            _focusParker.Focus();
        }

        /// <summary>
        /// Routes the chat's identity affordance to the shared shell identity
        /// surface (issue #220): first-run entry, rename and the joined-server
        /// prohibition all live there.
        /// </summary>
        private void OpenShellIdentitySurface()
        {
            if (UiModalState.Presented)
                return;
            FrontendController.Identity?.OpenIdentitySurface(rename: true);
        }

        private void OnDraftChanged(ChangeEvent<string> evt)
        {
            if (_suppressDraftChanged) return;
            if (ChatTextValidation.CountUnicodeScalars(evt.newValue) > MaxMessageScalars)
            {
                _draft?.SetValueWithoutNotify(_lastDraft);
                SetText(_historyFeedback, "MESSAGE MUST BE 500 UNICODE CHARACTERS OR FEWER");
                return;
            }
            _lastDraft = evt.newValue;
            _suppressDraftChanged = true;
            _session?.SetDraft(evt.newValue);
            _suppressDraftChanged = false;
            _send?.SetEnabled(_session?.ActiveConversation?.CanSend == true && IsMessageValid(evt.newValue));
        }

        private async void SendDraft(bool closeAfterSend)
        {
            if (_session == null || _session.ActiveConversation == null) return;
            var conversation = _session.ActiveConversation;
            _session.SetDraft(_draft?.value ?? string.Empty);
            if (closeAfterSend) SetExpanded(false, false);
            if (conversation.IsSending) return;
            try { await _session.SendDraftAsync(); }
            catch (Exception exception) { Debug.LogException(exception); }
            finally { RenderSession(); }
        }

        private async void RefreshDirectory()
        {
            if (_session == null)
                return;
            try
            {
                await _session.RefreshDirectoryAsync();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                RenderSession();
            }
        }

        private void SelectGlobal()
        {
            _showDirectory = false;
            if (_session?.SelectConversation("global") == true)
                RenderSession();
        }

        private void SelectServer()
        {
            _showDirectory = false;
            if (_session?.JoinedServerId is Guid serverId
                && _session.SelectConversation($"server:{serverId}"))
                RenderSession();
        }

        private void SelectDirectTab()
        {
            _showDirectory = true;
            RenderSession();
            RefreshDirectory();
        }

        private void SetExpanded(bool expanded, bool focusComposer)
        {
            _expanded = expanded;
            if (_hostIsShell && FrontendController.IsFrontendActive && !ChatInputGate.IsGameplayScene)
            {
                // Shell presentation states are owned by the explicit shell
                // actions (issue #220); this legacy entry point only
                // re-applies the current presentation.
                ApplyPresentation();
                return;
            }
            if (!expanded)
            {
                // Blur whatever the chat surface still holds before restoring
                // focus: a focused strip CHAT button is chat navigation too,
                // and its focus must not survive the exit (issue #216).
                if (_root?.panel?.focusController?.focusedElement is VisualElement focusedInChat
                    && _root.Contains(focusedInChat))
                    focusedInChat.Blur();
                _draft?.Blur();
                if (FrontendController.IsFrontendActive && !_hostIsShell && !ChatInputGate.IsGameplayScene)
                {
                    // Remember the conversation's focused control and return
                    // the page region to the page (issue #215).
                    CaptureRegionFocus(UiRegion.Social);
                    _region = UiRegion.Page;
                }
                ChatInputGate.End();
                ReleaseCursor();
            }
            else if (!ChatInputGate.IsGameplayScene)
            {
                // Remember where page focus came from so leaving the
                // conversation can return it without guessing (issue #214).
                CaptureRegionFocus(UiRegion.Page);
            }
            if (ChatInputGate.IsGameplayScene)
            {
                if (expanded)
                {
                    ChatInputGate.Begin();
                    if (!_ownsCursor)
                    {
                        _previousCursorLock = UnityEngine.Cursor.lockState;
                        _previousCursorVisible = UnityEngine.Cursor.visible;
                        _ownsCursor = true;
                    }
                }
                else
                {
                    // Park gameplay focus off the chat surface BEFORE the
                    // panel hides: the fixup that re-focuses the strip runs
                    // only when focus was lost to the hide (issue #216).
                    ParkFocusOutsideChat();
                }
            }
            ApplyPresentation();
            if (expanded)
                RenderSession();
            if (!expanded && !ChatInputGate.IsGameplayScene)
                RestorePageFocus();
            if (focusComposer)
                _root?.schedule.Execute(() => _draft?.Focus()).StartingIn(0);
        }

        /// <summary>
        /// The strip/restore button. Dispatches by context: shell pages
        /// restore the remembered compact presentation; legacy and gameplay
        /// open through the existing entry point.
        /// </summary>
        private void OnOpenButtonClicked()
        {
            if (_hostIsShell && FrontendController.IsFrontendActive && !ChatInputGate.IsGameplayScene)
            {
                RestoreFromMinimized();
                return;
            }
            OpenChat();
        }

        /// <summary>
        /// The header close button. Dispatches by context: on shell pages it
        /// minimizes the compact cell or collapses the expanded view;
        /// legacy/gameplay keep the existing close.
        /// </summary>
        private void OnCloseButtonClicked()
        {
            if (_hostIsShell && FrontendController.IsFrontendActive && !ChatInputGate.IsGameplayScene)
            {
                if (_socialExpanded)
                    CollapseSocialFromShell();
                else
                    MinimizeChat();
                return;
            }
            CloseChat();
        }

        /// <summary>
        /// Open chat from the visible entry point. In a roomy frontend this
        /// opens the Social Dock and persists the preference; in compact
        /// layouts and gameplay it opens the overlay panel without touching
        /// the remembered roomy preference (issue #214). The player explicitly
        /// entered the conversation, so the composer takes focus (issue #215).
        /// </summary>
        private void OpenChat()
        {
            // The topmost modal wins over chat (issue #216): a presented
            // modal — e.g. the pause menu or the direct-connect dialog — is
            // never covered by an opened conversation.
            if (UiModalState.Presented)
                return;
            if (FrontendController.IsFrontendActive && IsRoomyWindow())
            {
                _dockOpen = true;
                PlayerPrefs.SetInt(DockOpenPlayerPrefsKey, 1);
                PlayerPrefs.Save();
            }
            SetExpanded(true, true);
        }

        /// <summary>
        /// Close chat. At a roomy frontend this collapses the Social Dock and
        /// persists the preference; compact and gameplay closes only hide the
        /// panel (issue #214).
        /// </summary>
        private void CloseChat()
        {
            if (FrontendController.IsFrontendActive && IsRoomyWindow())
            {
                _dockOpen = false;
                PlayerPrefs.SetInt(DockOpenPlayerPrefsKey, 0);
                PlayerPrefs.Save();
            }
            SetExpanded(false, false);
        }

        /// <summary>
        /// Restore the compact cell from the minimized strip (issue #220):
        /// a manual restore is remembered across launches.
        /// </summary>
        private void RestoreFromMinimized()
        {
            if (UiModalState.Presented)
                return;
            _expanded = true;
            _socialExpanded = false;
            _dockOpen = true;
            PlayerPrefs.SetInt(DockOpenPlayerPrefsKey, 1);
            PlayerPrefs.Save();
            ApplyPresentation();
            RenderSession();
            FrontendFocusRouter.NotifyPresentationChanged();
            _root?.schedule.Execute(() => _draft?.Focus()).StartingIn(0);
        }

        /// <summary>
        /// Explicit manual minimization (issue #220): the compact cell hides
        /// into the strip inside the reserved cell; the preference is
        /// remembered and never rewritten by resizing.
        /// </summary>
        private void MinimizeChat()
        {
            _expanded = false;
            _socialExpanded = false;
            _dockOpen = false;
            PlayerPrefs.SetInt(DockOpenPlayerPrefsKey, 0);
            PlayerPrefs.Save();
            _draft?.Blur();
            ApplyPresentation();
            RenderSession();
            FrontendFocusRouter.NotifyPresentationChanged();
            FrontendFocusRouter.Instance?.RestorePageRegion();
        }

        /// <summary>
        /// The explicit Expand action (issue #220): the single attached
        /// social host is re-presented as the larger surface — presentation
        /// only, never a detach/rehost, and never a second store.
        /// </summary>
        private void ExpandSocial()
        {
            if (UiModalState.Presented)
                return;
            _socialExpanded = true;
            _expanded = true;
            ApplyPresentation();
            RenderSession();
            FrontendFocusRouter.NotifyPresentationChanged();
        }

        /// <summary>
        /// Close the expanded view (issue #220): the presentation returns to
        /// the remembered compact/minimized state, the page region gets focus
        /// back, and the page controller's selections were never disturbed.
        /// </summary>
        public void CollapseSocialFromShell()
        {
            if (!_socialExpanded)
                return;
            _socialExpanded = false;
            _expanded = _dockOpen;
            _draft?.Blur();
            ApplyPresentation();
            RenderSession();
            FrontendFocusRouter.Instance?.RestorePageRegion();
        }

        /// <summary>Blur the chat's text fields without changing state; the
        /// focus router calls this when leaving the social interaction.</summary>
        public void BlurChatEditors()
        {
            _draft?.Blur();
            if (FrontendController.IsFrontendActive && !ChatInputGate.IsGameplayScene)
                ChatInputGate.End();
        }

        /// <summary>True while the deliberate expanded social view is open on
        /// a shell page; the focus router reads it for cancel-layer and
        /// focusability decisions.</summary>
        public static bool IsExpandedSocialOpen => _instance != null && _instance._socialExpanded;

        /// <summary>Escape from chat: leave interaction first. A roomy dock
        /// stays visible with page focus restored; compact chat and gameplay
        /// chat close the panel (issue #214). Shell pages resolve cancel
        /// through the focus router (issue #220).</summary>
        private void HandleEscape()
        {
            if (FrontendController.IsFrontendActive && IsRoomyWindow())
            {
                LeaveChatInteraction();
                return;
            }
            SetExpanded(false, false);
        }

        /// <summary>Leave the interactive chat surface. A roomy dock stays
        /// visible; the page region becomes active again with its last valid
        /// focus restored (issues #214, #215).</summary>
        private void LeaveChatInteraction()
        {
            if (FrontendController.IsFrontendActive && !ChatInputGate.IsGameplayScene)
            {
                CaptureRegionFocus(UiRegion.Social);
                SetActiveRegion(UiRegion.Page, focusTarget: true);
            }
            _draft?.Blur();
            ChatInputGate.End();
            ReleaseCursor();
        }

        private void ReleaseCursor()
        {
            if (!_ownsCursor)
                return;
            UnityEngine.Cursor.lockState = _previousCursorLock;
            UnityEngine.Cursor.visible = _previousCursorVisible;
            _ownsCursor = false;
        }

        private void RestorePageFocus()
        {
            if (!FrontendController.IsFrontendActive || ChatInputGate.IsGameplayScene || _hostIsShell)
                return;
            // The remembered page focus — or the page's conventional entry
            // point — returns focus without guessing (issues #214, #215).
            FocusRegionDefault(UiRegion.Page);
        }

        /// <summary>
        /// Apply the current presentation mode (issue #214):
        /// roomy frontend + expanded → reserved right-hand dock band with the
        /// page shrunk beside it; roomy + collapsed → bottom-right strip;
        /// compact + expanded → chat replaces the page content temporarily;
        /// compact + collapsed → strip; gameplay → the legacy overlay panel.
        /// </summary>
        private void ApplyPresentation()
        {
            if (_root == null)
                return;

            // Shell-hosted presentation (issues #219/#220): the presenter
            // fills the reserved bottom-left cell the shell laid out; the
            // cell — not the presenter — owns geometry and density, and no
            // page reservation exists because the reservation is structural.
            // Three presentation states on the one attached subtree:
            // expanded (the deliberate larger surface), compact (the default
            // cell), minimized (the explicit strip).
            if (_hostIsShell && FrontendController.IsFrontendActive && !ChatInputGate.IsGameplayScene)
            {
                bool expanded = _socialExpanded;
                FrontendController.Shell?.SetSocialExpanded(expanded);
                if (_frame != null)
                {
                    _frame.style.position = Position.Absolute;
                    _frame.style.left = 0;
                    _frame.style.right = 0;
                    _frame.style.top = 0;
                    _frame.style.bottom = 0;
                    _frame.style.width = StyleKeyword.Auto;
                    _frame.style.height = StyleKeyword.Auto;
                    // The expanded surface intentionally occludes the page;
                    // the compact cell lets clicks through to the page.
                    _frame.pickingMode = expanded ? PickingMode.Position : PickingMode.Ignore;
                }
                // Menu-vs-combat appearance split (issue #220): the menu
                // presentation keeps its translucent surface and restrained
                // accent classes; the combat presenter styling is untouched.
                _panel?.EnableInClassList("chat-panel--menu", true);
                _panel?.EnableInClassList("chat-panel--menu-opaque", expanded);
                _root?.EnableInClassList("chat-root--menu", true);
                _root?.EnableInClassList("chat-root--expanded", expanded);
                _panel?.SetDisplayed(_expanded);
                _collapsed?.SetDisplayed(!_expanded);
                // Conversation management is an expanded-view surface; the
                // compact cell keeps the channel row instead (issue #220).
                _conversationList?.SetDisplayed(expanded);
                if (_expand != null)
                    _expand.style.display = !expanded && _expanded
                        ? DisplayStyle.Flex : DisplayStyle.None;
                if (_close != null)
                    _close.text = expanded ? "COLLAPSE" : "MINIMIZE";
                ApplyRegionFocusability();
                return;
            }

            bool frontend = FrontendController.IsFrontendActive;
            bool roomy = IsRoomyWindow();
            bool dock = frontend && roomy && _expanded;
            bool replace = frontend && !roomy && _expanded;

            ApplyPageReservation(dock, replace);

            if (_frame != null)
            {
                if (dock)
                {
                    // Reserved band: full height on the right of the page.
                    _frame.style.position = Position.Absolute;
                    _frame.style.left = StyleKeyword.Auto;
                    _frame.style.right = 0;
                    _frame.style.top = 0;
                    _frame.style.bottom = 0;
                    _frame.style.width = Length.Percent(DockWidthPercent);
                    _frame.style.height = StyleKeyword.Auto;
                }
                else if (replace)
                {
                    // Compact chat replaces the page content temporarily.
                    _frame.style.position = Position.Absolute;
                    _frame.style.left = 0;
                    _frame.style.right = 0;
                    _frame.style.top = 0;
                    _frame.style.bottom = 0;
                    _frame.style.width = StyleKeyword.Auto;
                    _frame.style.height = StyleKeyword.Auto;
                }
                else
                {
                    // Legacy overlay (collapsed strip, compact strip, gameplay
                    // chat): inline styles mirror the .chat-root USS defaults.
                    _frame.style.position = Position.Absolute;
                    _frame.style.left = StyleKeyword.Auto;
                    _frame.style.top = StyleKeyword.Auto;
                    _frame.style.right = 24;
                    _frame.style.bottom = 94;
                    _frame.style.width = 540;
                    _frame.style.height = 620;
                }
            }

            bool opaque = dock || replace;
            // Combat and legacy presentations never carry the menu classes
            // (issue #220): new dock styling cannot alter the match HUD.
            _panel?.EnableInClassList("chat-panel--menu", false);
            _panel?.EnableInClassList("chat-panel--menu-opaque", false);
            _root?.EnableInClassList("chat-root--menu", false);
            _root?.EnableInClassList("chat-root--expanded", false);
            if (_expand != null)
                _expand.style.display = DisplayStyle.None;
            _panel?.EnableInClassList("chat-panel--solid", opaque);
            _panel?.SetDisplayed(_expanded);
            _collapsed?.SetDisplayed(!_expanded);
            ApplyRegionFocusability();
            UpdateRegionHint();
        }

        private void ApplyPageReservation(bool dock, bool replace)
        {
            if (_hostPageRoot == null)
                return;
            // Shrink the page root itself: page containers are absolutely
            // positioned against its border box, so a width reservation moves
            // backdrops, headers and content together (issue #214). The shell
            // root is a column flex container: flex-grow sizes the page's
            // height and must stay; the dock band is reserved on the cross
            // axis through the width alone.
            _hostPageRoot.style.width = dock ? Length.Percent(100f - DockWidthPercent) : StyleKeyword.Auto;
            // Compact chat replaces the page content temporarily; selections
            // live in the page controllers and survive the hide (issue #214).
            _hostPageRoot.style.display = replace ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>
        /// Whether the selected conversation is actually shown right now: the
        /// panel is visible (compact cell or expanded view, not the minimized
        /// strip) and no directory view replaces the history. Gameplay keeps
        /// the legacy panel-visible rule (issue #220).
        /// </summary>
        private bool IsConversationShown()
        {
            if (ChatInputGate.IsGameplayScene)
                return _expanded;
            return _expanded && !_showDirectory;
        }

        /// <summary>
        /// The unread contract (issue #214): the selected conversation counts
        /// as read only while it is visible (dock/panel open), the application
        /// is focused, no modal obscures the surface and the view sits at the
        /// conversation's newest messages.
        /// </summary>
        private bool ShouldMarkActiveRead()
        {
            if (_session?.ActiveConversation is not ChatConversation active)
                return false;
            if (!Application.isFocused || UiModalState.Presented)
                return false;
            return _pinToNewest || IsAtNewest();
        }

        private void TryMarkActiveRead()
        {
            if (_session?.ActiveConversation?.Unread > 0 && _expanded && ShouldMarkActiveRead())
                _session.MarkActiveRead();
        }

        /// <summary>Store the active conversation's reading anchor.</summary>
        private void UpdateScrollAnchor()
        {
            var active = _session?.ActiveConversation;
            if (active == null || _history == null)
                return;
            bool pinned = IsAtNewest();
            _pinToNewest = pinned;
            _pinnedNewest[active.Key] = pinned;
            _scrollOffsets[active.Key] = _history.scrollOffset;
        }

        private bool IsPinnedNewest(string key) =>
            _pinnedNewest.TryGetValue(key, out bool pinned) ? pinned : true;

        private Vector2 GetScrollOffset(string key) =>
            _scrollOffsets.TryGetValue(key, out Vector2 offset) ? offset : Vector2.zero;

        /// <summary>
        /// The bounded history buffer evicted older messages the player was
        /// reading (issue #214). Record a pending return-to-newest for the
        /// next render; the explanation lives on the conversation Feedback.
        /// The normal read rule applies afterwards.
        /// </summary>
        private void OnConversationHistoryTrimmed(string key)
        {
            if (_session == null)
                return;
            if (IsPinnedNewest(key))
                return;
            _evictionReset.Add(key);
            var conversation = _session.Conversations.FirstOrDefault(c => c.Key == key);
            if (conversation != null && string.IsNullOrEmpty(conversation.Feedback))
                conversation.Feedback =
                    "Older messages left the history buffer (the newest 50 are kept). Returned to the newest messages.";
        }

        private void ScrollToNewest()
        {
            if (_history == null || _history.childCount == 0)
                return;
            _history.verticalScroller.value = Mathf.Max(0f, _history.verticalScroller.highValue);
            _newest?.SetDisplayed(false);
        }

        private bool IsAtNewest()
        {
            if (_history == null)
                return true;
            return _history.verticalScroller.value >= _history.verticalScroller.highValue - 1f;
        }

        private bool IsEditorFocused()
        {
            if (_root?.panel?.focusController?.focusedElement is not VisualElement focused)
                return false;
            return IsWithin(focused, _draft);
        }

        private bool IsEditorFocused(TextField? field)
        {
            return _root?.panel?.focusController?.focusedElement is VisualElement focused && IsWithin(focused, field);
        }

        private static bool IsWithin(VisualElement focused, VisualElement? field)
        {
            return field != null && (focused == field || field.Contains(focused));
        }


        private static bool IsEnter(KeyCode keyCode)
        {
            return keyCode == KeyCode.Return || keyCode == KeyCode.KeypadEnter;
        }

        private static bool IsMessageValid(string value)
        {
            return ChatTextValidation.TryValidateMessage(value, out _, out _);
        }


        private static string FormatIdentity(ChatPlayer player)
        {
            return string.IsNullOrEmpty(player.SessionTag)
                ? player.DisplayName
                : $"{player.DisplayName} [{player.SessionTag}]";
        }

        private static string ChannelLabel(string channel)
        {
            return string.Equals(channel, "server", StringComparison.OrdinalIgnoreCase)
                ? "SERVER"
                : "GLOBAL";
        }

        private static void SetText(Label? label, string? text)
        {
            if (label == null)
                return;
            label.enableRichText = false;
            label.text = text ?? string.Empty;
        }
    }

    internal static class ChatOverlayVisualElementExtensions
    {
        public static void SetDisplayed(this VisualElement element, bool displayed)
        {
            element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
