using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

namespace SlopArena.Client.UI
{
    /// <summary>Pages hosted by the single frontend scene (ADR-0032, issues #210, #212).</summary>
    public enum FrontendPage
    {
        Home,
        FighterSelect,
        StageSelect,
        Results,
        ServerBrowser,
        LobbyRoom
    }

    /// <summary>
    /// Shared frontend shell (ADR-0032, issues #210, #212). One scene hosts
    /// Home, Fighter Select, Stage Select, Results, Server Browser and Lobby
    /// Room; explicit page activation/deactivation replaces scene-per-page
    /// navigation. Gameplay scenes stay separate: the frontend unloads for a
    /// match and is recreated on return through a pending page.
    ///
    /// Since #219 the shell is also the frame: migrated pages (Home, Fighter
    /// Select) are fragment sources cloned into the FrontendShell hosts once
    /// per activation through a per-activation <see cref="FrontendPageContext"/>;
    /// unmigrated pages keep their per-page documents until they migrate
    /// (issues #221/#222) and are temporary coexistence, not a compatibility
    /// router. The navigation controller keeps sole ownership of page
    /// routing, pending returns and gameplay handoff.
    /// </summary>
    public sealed class FrontendController : MonoBehaviour
    {
        public const string SceneName = "Frontend";

        [SerializeField] private GameObject _homePage = null!;
        [SerializeField] private GameObject _fighterSelectPage = null!;
        [SerializeField] private GameObject _stageSelectPage = null!;
        [SerializeField] private GameObject _resultsPage = null!;
        [SerializeField] private GameObject _serverBrowserPage = null!;
        [SerializeField] private GameObject _lobbyRoomPage = null!;
        [SerializeField] private FrontendShellView? _shell;
        [SerializeField] private VisualTreeAsset? _homeFragment;
        [SerializeField] private VisualTreeAsset? _fighterSelectFragment;

        private static FrontendController? _instance;
        private static FrontendPage? _pendingPage;
        private FrontendPage _current;
        private FrontendPageContext? _currentContext;
        private int _generation;
        /// <summary>
        /// Raised after the active page switched. Persistent shell guests
        /// (e.g. chat presentation) reconcile their host attachment here;
        /// departed pages no longer receive callbacks.
        /// </summary>
        public static event Action<FrontendPage>? PageChanged;

        /// <summary>The currently active page; Home before the shell starts.</summary>
        public static FrontendPage CurrentPage =>
            _instance != null ? _instance._current : FrontendPage.Home;

        /// <summary>True while the frontend shell is loaded (issue #214).</summary>
        public static bool IsFrontendActive => _instance != null;

        /// <summary>
        /// The live page context of the active activation; null for legacy
        /// per-page-document pages and outside the frontend scene.
        /// </summary>
        public static FrontendPageContext? CurrentContext => _instance?._currentContext;

        /// <summary>The stable shell view; null outside the frontend scene.</summary>
        public static FrontendShellView? Shell => _instance?._shell;

        /// <summary>Whether the given page is fragment-mounted into the shell.</summary>
        public static bool IsMigratedPage(FrontendPage page) =>
            page is FrontendPage.Home or FrontendPage.FighterSelect;

        /// <summary>
        /// The active page's UI document — the explicit host for shell
        /// overlays on pages that still own a full-screen document. Null for
        /// fragment-mounted pages (the shell document hosts those) and
        /// outside the frontend scene.
        /// </summary>
        public static UIDocument? ActivePageDocument
        {
            get
            {
                var shell = _instance;
                if (shell == null)
                    return null;
                if (IsMigratedPage(shell._current))
                    return null;
                var pageObject = shell.PageObject(shell._current);
                if (pageObject == null || !pageObject.activeInHierarchy)
                    return null;
                return pageObject.GetComponent<UIDocument>();
            }
        }

        /// <summary>
        /// The chat presenter's shell hosting surfaces (issue #219): the
        /// reserved bottom-left social cell and the page content region.
        /// Available only while a fragment-mounted page is active.
        /// </summary>
        public static bool TryGetShellChatHost(
            out VisualElement socialHost, out VisualElement pageContentRoot)
        {
            socialHost = null!;
            pageContentRoot = null!;
            var shell = _instance;
            if (shell == null || shell._shell == null || !IsMigratedPage(shell._current))
                return false;
            return shell._shell.TryGetChatHosts(out socialHost, out pageContentRoot);
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void Start()
        {
            _shell?.Bind();
            Show(_pendingPage ?? FrontendPage.Home);
            _pendingPage = null;
        }

        /// <summary>
        /// Navigate to a frontend page. Inside the frontend scene this
        /// deactivates the current page (its cleanup runs) and activates the
        /// target; from any other scene — gameplay, browser, lobby — it queues
        /// the target page and (re)loads the frontend scene.
        /// </summary>
        public static void Show(FrontendPage page)
        {
            var shell = _instance;
            if (shell == null)
            {
                _pendingPage = page;
                SceneManager.LoadScene(SceneName);
                return;
            }
            shell.Activate(page);
        }

        /// <summary>
        /// The activation/teardown sequence (issue #218 contract): the
        /// departing activation is invalidated immediately, the departing
        /// controller is disabled through its Unity lifecycle so its cleanup
        /// runs while its visual references still exist, fragments are
        /// removed and the departing context released, the destination
        /// fragments are cloned and mounted and their context injected while
        /// the destination controller is inactive, and only then is the
        /// destination generation set and the controller activated. A newer
        /// navigation generation from a nested redirect suppresses the stale
        /// page-changed publication.
        /// </summary>
        private void Activate(FrontendPage page)
        {
            _generation++;
            // The current page deactivates first so a same-page Show() still
            // re-runs the page's OnEnable cleanup/rebuild (issue #213), and
            // so re-entering Home resets preparation only on an explicit
            // activation (issue #210).
            _homePage?.SetActive(false);
            _fighterSelectPage?.SetActive(false);
            _stageSelectPage?.SetActive(false);
            _resultsPage?.SetActive(false);
            _serverBrowserPage?.SetActive(false);
            _lobbyRoomPage?.SetActive(false);

            // Fragments come down and the departing context releases while
            // its elements are still attached (release actions may close
            // page-owned dialogs). Shell, social and identity UI survive.
            _currentContext?.Release();
            _currentContext = null;
            _shell?.ClearPageHosts();

            if (IsMigratedPage(page))
            {
                var fragment = page == FrontendPage.Home ? _homeFragment : _fighterSelectFragment;
                if (fragment == null)
                {
                    Debug.LogError($"[FrontendController] No fragment source assigned for {page}; page stays blank.");
                }
                else
                {
                    var context = _shell!.MountPage(page, fragment);
                    _currentContext = context;
                    if (PageObject(page) is not { } pageObject
                        || pageObject.GetComponent<IFrontendPageController>() is not { } controller)
                    {
                        Debug.LogError($"[FrontendController] {page} has no {nameof(IFrontendPageController)}; its fragment renders but the page stays inert.");
                    }
                    else
                    {
                        controller.InjectPageContext(context);
                    }
                }
            }

            // The destination/current-page generation is set before the
            // controller activates (issue #218), so the controller's context
            // is already valid inside its OnEnable.
            _current = page;
            _shell?.ApplyPageMode(page, IsMigratedPage(page));

            int generationAtMount = _generation;
            PageObject(page)?.SetActive(true);

            // Legacy coexistence (issue #219): unmigrated page documents keep
            // their full-screen composition. Their document container must
            // fill the panel the way the pre-shell page documents did — the
            // shell root's flex growth would otherwise collapse them. The
            // container exists only after activation, so this runs here;
            // Pass 4 removes the adapter with the legacy hosting.
            if (!IsMigratedPage(page)
                && PageObject(page)?.GetComponent<UIDocument>() is { } legacyDocument
                && legacyDocument.rootVisualElement != null)
            {
                legacyDocument.rootVisualElement.style.flexGrow = 1;
            }

            // A nested Show() from the activation (e.g. Fighter Select
            // without a lobby connection) supersedes this activation: never
            // publish the stale page-changed event.
            if (_generation != generationAtMount || _current != page)
                return;
            PageChanged?.Invoke(page);
        }

        private GameObject PageObject(FrontendPage page) => page switch
        {
            FrontendPage.Home => _homePage,
            FrontendPage.FighterSelect => _fighterSelectPage,
            FrontendPage.StageSelect => _stageSelectPage,
            FrontendPage.Results => _resultsPage,
            FrontendPage.ServerBrowser => _serverBrowserPage,
            FrontendPage.LobbyRoom => _lobbyRoomPage,
            _ => null!
        };
    }
}
