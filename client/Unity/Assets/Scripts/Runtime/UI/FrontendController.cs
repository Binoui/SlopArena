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
    /// match and is recreated on return through a pending page. Pages own
    /// their content; the shell owns the navigation lifecycle and hands shell
    /// guests (chat) an explicit host document instead of a first-document
    /// search. Server Browser and Lobby Room keep their real hosting,
    /// joining, membership and cancellation semantics: page deactivation runs
    /// the same cleanup the old scene teardown did (ADR-0032), including a
    /// legitimate host-process handoff surviving page departure.
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

        private static FrontendController? _instance;
        private static FrontendPage? _pendingPage;
        private FrontendPage _current;

        /// <summary>
        /// Raised after the active page switched. Persistent shell guests
        /// (e.g. chat presentation) re-attach their host document here;
        /// departed pages no longer receive callbacks.
        /// </summary>
        public static event Action<FrontendPage>? PageChanged;

        /// <summary>The currently active page; Home before the shell starts.</summary>
        public static FrontendPage CurrentPage =>
            _instance != null ? _instance._current : FrontendPage.Home;

        /// <summary>True while the frontend shell is loaded (issue #214).</summary>
        public static bool IsFrontendActive => _instance != null;

        /// <summary>
        /// The active page's UI document — the explicit host for shell overlays
        /// inside the frontend scene. Null outside the frontend scene.
        /// </summary>
        public static UIDocument? ActivePageDocument
        {
            get
            {
                var shell = _instance;
                if (shell == null)
                    return null;
                var pageObject = shell.PageObject(shell._current);
                if (pageObject == null || !pageObject.activeInHierarchy)
                    return null;
                return pageObject.GetComponent<UIDocument>();
            }
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

        private void Activate(FrontendPage page)
        {
            // Always run a full deactivate/activate cycle: re-entering Home
            // deliberately resets preparation, and re-entry of any page must
            // rebuild its content from current state. SetActive(true) on an
            // already-active object is a no-op, so the current page is
            // deactivated first — a same-page Show() must still re-run the
            // page's OnEnable cleanup/rebuild (issue #213).
            _homePage?.SetActive(false);
            _fighterSelectPage?.SetActive(false);
            _stageSelectPage?.SetActive(false);
            _resultsPage?.SetActive(false);
            _serverBrowserPage?.SetActive(false);
            _lobbyRoomPage?.SetActive(false);
            PageObject(page)?.SetActive(true);
            _current = page;
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
