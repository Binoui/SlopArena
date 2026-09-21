using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Binds the stable FrontendShell document (issue #219): named hosts for
    /// page header/body and the lower row's social cell plus summary/actions
    /// cell, hidden expanded-social and modal hosts, and the one-time
    /// geometry/density registration on the stable shell root. This view has
    /// no lobby, network, navigation or social logic — the controllers keep
    /// those responsibilities.
    /// </summary>
    public sealed class FrontendShellView : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument = null!;

        // Windows narrower in width or height than these panel-space pixels
        // (physical pixels at the project's 96 dpi) use compact density:
        // 1280×720/800-class windows are compact, roomy desktop windows are
        // not (issue #218 size targets).
        private const float CompactMaxWidth = 1600f;
        private const float CompactMaxHeight = 860f;

        private VisualElement? _root;
        private VisualElement? _topBar;
        private VisualElement? _pageContextLabel;
        private VisualElement? _pageHeaderHost;
        private VisualElement? _pageBodyHost;
        private VisualElement? _socialHost;
        private VisualElement? _pageLowerHost;
        private VisualElement? _pageSummaryHost;
        private VisualElement? _pageActionsHost;
        private VisualElement? _expandedSocialHost;
        private VisualElement? _modalHost;
        private VisualElement? _lowerRow;
        private bool _compact;
        private bool _densityBound;
        private bool _densityGeometryApplied;

        /// <summary>The stable shell root; null before the document binds.</summary>
        public VisualElement? Root => _root;

        /// <summary>The shell top bar row.</summary>
        public VisualElement? TopBar => _topBar;

        /// <summary>
        /// The page content container (header, body, lower-right cells). The
        /// chat presenter uses it as the Page focus region; it contains the
        /// social cell too, so focusability toggles exclude the social
        /// subtree.
        /// </summary>
        public VisualElement? PageContentRoot { get; private set; }

        /// <summary>The reserved bottom-left conversation cell.</summary>
        public VisualElement? SocialHost => _socialHost;

        /// <summary>The hidden host for a later expanded social surface.</summary>
        public VisualElement? ExpandedSocialHost => _expandedSocialHost;

        /// <summary>The hidden host for modal surfaces.</summary>
        public VisualElement? ModalHost => _modalHost;

        private void OnEnable()
        {
            Bind();
        }

        /// <summary>
        /// Binds the shell document once. The frontend controller binds
        /// explicitly before its first activation so binding never depends on
        /// Unity's component-order luck.
        /// </summary>
        public void Bind()
        {
            if (_root != null)
                return;
            var documentRoot = _uiDocument != null ? _uiDocument.rootVisualElement : null;
            if (documentRoot == null)
            {
                Debug.LogError("[FrontendShellView] UIDocument is missing or unbound; the shell frame cannot host pages.");
                enabled = false;
                return;
            }

            _root = documentRoot.Q<VisualElement>("frontend-shell");
            _topBar = documentRoot.Q<VisualElement>("shell-top-bar");
            _pageContextLabel = documentRoot.Q<VisualElement>("shell-page-context");
            _pageHeaderHost = documentRoot.Q<VisualElement>("page-header-host");
            _pageBodyHost = documentRoot.Q<VisualElement>("page-body-host");
            _socialHost = documentRoot.Q<VisualElement>("social-host");
            _pageLowerHost = documentRoot.Q<VisualElement>("page-lower-host");
            _pageSummaryHost = documentRoot.Q<VisualElement>("page-summary-host");
            _pageActionsHost = documentRoot.Q<VisualElement>("page-actions-host");
            _expandedSocialHost = documentRoot.Q<VisualElement>("expanded-social-host");
            _modalHost = documentRoot.Q<VisualElement>("modal-host");

            if (_root == null || _pageHeaderHost == null || _pageBodyHost == null || _socialHost == null
                || _pageLowerHost == null || _pageSummaryHost == null || _pageActionsHost == null
                || _expandedSocialHost == null || _modalHost == null)
            {
                Debug.LogError("[FrontendShellView] FrontendShell.uxml is missing required hosts; the shell frame cannot host pages.");
                enabled = false;
                return;
            }

            // The page content region spans the three page hosts; the social
            // cell sits inside the workspace too, so region operations must
            // not treat chat controls as page controls.
            PageContentRoot = documentRoot.Q<VisualElement>("shell-workspace");
            _lowerRow = documentRoot.Q<VisualElement>("shell-lower-row");

            RegisterDensityHandling();
        }

        /// <summary>
        /// Density handling registers once on the stable shell root (issue
        /// #218): the density class changes only when the selected mode
        /// changes, so resizing never thrashes layout. The root's size is set
        /// by the panel, not by its content, so no feedback loop exists.
        /// </summary>
        private void RegisterDensityHandling()
        {
            if (_densityBound || _root == null)
                return;
            _densityBound = true;
            _root.RegisterCallback<GeometryChangedEvent>(_ => EvaluateDensity());
            EvaluateDensity();
        }

        private void EvaluateDensity()
        {
            if (_root == null)
                return;
            var rect = _root.contentRect;
            // Density is decided from the shell's physical size (issue #218):
            // panel coordinates are scaled points, so convert through the
            // panel's scale factor before comparing against the targets.
            float scale = _root.panel?.scaledPixelsPerPoint ?? 1f;
            if (scale <= 0f)
                scale = 1f;
            float physicalWidth = rect.width * scale;
            float physicalHeight = rect.height * scale;
            if (physicalWidth <= 0f || physicalHeight <= 0f)
                return;
            bool compact = physicalWidth < CompactMaxWidth || physicalHeight < CompactMaxHeight;
            bool modeChanged = compact != _compact;
            _compact = compact;
            if (modeChanged)
            {
                _root.EnableInClassList("frontend-shell--compact", compact);
                _root.EnableInClassList("frontend-shell--roomy", !compact);
            }
            // The first usable layout always applies the geometry (issue
            // #219); later layouts only re-apply when the mode changed, so
            // resizing never thrashes and no feedback loop exists.
            if (modeChanged || !_densityGeometryApplied)
                ApplyDensityGeometry();
        }

        /// <summary>
        /// Applies the density's physical size targets (issue #218): ~56 px
        /// top bar and ~380×190 px conversation cell with ~16 px outer
        /// spacing compact; ~64 px, ~480×230 px, ~24 px roomy. PanelSettings
        /// run in scaled points, so targets divide by the panel's scale
        /// factor. Re-evaluated only when the mode changes or on the first
        /// usable layout.
        /// </summary>
        private void ApplyDensityGeometry()
        {
            if (_root?.panel == null || _topBar == null || _socialHost == null
                || PageContentRoot == null)
                return;
            float scale = _root.panel.scaledPixelsPerPoint;
            if (scale <= 0f)
                scale = 1f;
            float spacing = (_compact ? 16f : 24f) / scale;
            float barHeight = (_compact ? 56f : 64f) / scale;
            float cellWidth = (_compact ? 380f : 480f) / scale;
            float cellHeight = (_compact ? 190f : 230f) / scale;
            float topGap = (_compact ? 10f : 16f) / scale;
            float cellGap = (_compact ? 16f : 20f) / scale;

            _topBar.style.height = barHeight;
            _topBar.style.minHeight = barHeight;
            _topBar.style.paddingLeft = spacing;
            _topBar.style.paddingRight = spacing;

            PageContentRoot.style.paddingLeft = spacing;
            PageContentRoot.style.paddingRight = spacing;
            PageContentRoot.style.paddingTop = topGap;

            if (_lowerRow != null)
            {
                _lowerRow.style.marginTop = topGap;
                _lowerRow.style.paddingBottom = spacing;
            }

            _socialHost.style.width = cellWidth;
            _socialHost.style.height = cellHeight;
            _socialHost.style.marginRight = cellGap;
            _densityGeometryApplied = true;
        }

        /// <summary>Whether the shell currently renders compact density.</summary>
        public bool IsCompact => _compact;

        /// <summary>
        /// Removes mounted page sections from the page hosts. The social
        /// host, expanded-social host, modal host and top bar are never
        /// cleared by page teardown (issue #218).
        /// </summary>
        public void ClearPageHosts()
        {
            _pageHeaderHost?.Clear();
            _pageBodyHost?.Clear();
            _pageSummaryHost?.Clear();
            _pageActionsHost?.Clear();
        }

        /// <summary>
        /// The shell's social cell only hosts the conversation for
        /// shell-mounted pages; legacy per-page documents keep their own
        /// temporary coexistence hosting (issue #219 non-goal note).
        /// </summary>
        public void SetSocialHostVisible(bool visible)
        {
            _socialHost?.style.SetDisplay(visible);
            _pageLowerHost?.style.SetDisplay(visible);
        }

        /// <summary>Sets the concise page/mode context in the top bar.</summary>
        public void SetPageContext(string? text)
        {
            if (_pageContextLabel is Label label)
            {
                label.enableRichText = false;
                label.text = text ?? string.Empty;
            }
        }

        /// <summary>
        /// Per-page shell chrome (issue #219): fragment-mounted pages render
        /// inside the frame with the reserved social cell; legacy per-page
        /// documents keep their own full-screen composition until they
        /// migrate (issues #221/#222), so the shell stands down entirely
        /// there — the frame is temporary coexistence, not a compatibility
        /// router.
        /// </summary>
        public void ApplyPageMode(FrontendPage page, bool shellMounted)
        {
            _root?.style.SetDisplay(shellMounted);
            SetSocialHostVisible(shellMounted);
            SetPageContext(page switch
            {
                FrontendPage.Home => "HOME",
                FrontendPage.FighterSelect => "MATCH SETUP // FIGHTER SELECT",
                FrontendPage.StageSelect => "MATCH SETUP // STAGE SELECT",
                FrontendPage.Results => "RESULTS",
                FrontendPage.ServerBrowser => "ONLINE // SERVER BROWSER",
                FrontendPage.LobbyRoom => "ONLINE // ROOM",
                _ => string.Empty
            });
        }

        /// <summary>
        /// The chat presenter's shell hosting surfaces; valid only after
        /// binding and on fragment-mounted pages.
        /// </summary>
        public bool TryGetChatHosts(out VisualElement socialHost, out VisualElement pageContentRoot)
        {
            socialHost = null!;
            pageContentRoot = null!;
            if (_root == null || _socialHost == null || PageContentRoot == null)
                return false;
            socialHost = _socialHost;
            pageContentRoot = PageContentRoot;
            return true;
        }

        /// <summary>
        /// Clones the page's fragment source once for this activation and
        /// mounts its named sections into the shell hosts (issue #218 page
        /// contract): <c>page-header</c>, <c>page-body</c>, <c>page-summary</c>
        /// and <c>page-actions</c>. Unnamed extra children mount into the
        /// body host with a warning — silent content loss is never
        /// acceptable.
        /// </summary>
        public FrontendPageContext MountPage(FrontendPage page, VisualTreeAsset fragment)
        {
            var context = new FrontendPageContext(page, this);
            if (_pageHeaderHost == null || _pageBodyHost == null || _pageSummaryHost == null
                || _pageActionsHost == null)
                return context;

            var clone = fragment.CloneTree();
            bool anySection = false;
            foreach (var (sectionName, host) in new (string, VisualElement?)[]
                     {
                         ("page-header", _pageHeaderHost),
                         ("page-body", _pageBodyHost),
                         ("page-summary", _pageSummaryHost),
                         ("page-actions", _pageActionsHost)
                     })
            {
                if (host == null)
                    continue;
                var section = clone.Q(sectionName);
                if (section == null)
                    continue;
                host.Add(section);
                context.AttachRoot(section);
                anySection = true;
            }

            if (!anySection)
            {
                Debug.LogWarning($"[{page}] Fragment exposes no named sections; mounting the whole fragment into the body host.");
                _pageBodyHost.Add(clone);
                context.AttachRoot(clone);
            }
            return context;
        }


        private void OnDisable()
        {
            // The shell dies with the frontend scene; the stable-root
            // registrations and mounted content die with it. Chat re-hosts
            // through the presenter's own attach path on frontend recreation.
            _root = null;
            _topBar = null;
            _pageContextLabel = null;
            _pageHeaderHost = null;
            _pageBodyHost = null;
            _socialHost = null;
            _pageLowerHost = null;
            _pageSummaryHost = null;
            _pageActionsHost = null;
            _expandedSocialHost = null;
            _modalHost = null;
            _lowerRow = null;
            PageContentRoot = null;
            _densityBound = false;
            _densityGeometryApplied = false;
        }
    }

    /// <summary>Shared display helpers for shell cells.</summary>
    internal static class FrontendShellStyleExtensions
    {
        public static void SetDisplay(this IStyle style, bool displayed)
        {
            style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
