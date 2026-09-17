#nullable enable
using System;
using System.Collections.Generic;
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

        private static ChatOverlay? _instance;

        private VisualElement? _root;
        private VisualElement? _collapsed;
        private VisualElement? _panel;
        private VisualElement? _setup;
        private VisualElement? _main;
        private VisualElement? _rename;
        private VisualElement? _conversationList;
        private VisualElement? _onlineList;
        private VisualElement? _compactFeed;
        private Label? _historyFeedback;
        private Label? _setupFeedback;
        private ScrollView? _history;
        private Button? _open;
        private Button? _close;
        private Button? _newest;
        private Button? _refreshDirectory;
        private Button? _send;
        private Button? _setName;
        private Button? _renameButton;
        private Button? _globalTab;
        private Button? _serverTab;
        private Button? _directTab;
        private Label? _statusCollapsed;
        private Label? _statusHeader;
        private Label? _titleHeader;
        private Label? _onlineFeedback;
        private Label? _unread;
        private TextField? _draft;
        private TextField? _name;
        private TextField? _renameField;

        private ChatSession? _session;
        private IVisualElementScheduledItem? _compactTick;
        private bool _viewBound;
        private bool _expanded;
        private bool _suppressDraftChanged;
        private string _lastConversationKey = string.Empty;
        private string _lastDraft = string.Empty;
        private bool _showDirectory;
        private bool _enterConsumed;
        private bool _pinToNewest = true;
        private bool _ownsCursor;
        private CursorLockMode _previousCursorLock;
        private bool _previousCursorVisible;

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
        }

        private void OnEnable()
        {
            BindView();
            AttachToScene(SceneManager.GetActiveScene());
            TryBindSession();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_expanded)
                ChatInputGate.End();
        }

        private void OnDestroy()
        {
            if (_session != null)
                _session.Changed -= OnSessionChanged;
            if (_instance == this)
                _instance = null;
            _root?.RemoveFromHierarchy();
            ChatInputGate.End();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _ownsCursor = false;
            SetExpanded(false, false);
            AttachToScene(scene);
        }

        private void AttachToScene(Scene scene)
        {
            if (_root == null) return;
            foreach (var document in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                if (document.gameObject.scene != scene) continue;
                document.rootVisualElement.Add(_root);
                _root.BringToFront();
                return;
            }
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
            var keyboard = Keyboard.current;
            bool enterHeld = keyboard != null && (keyboard.enterKey.isPressed || keyboard.numpadEnterKey.isPressed);
            if (!enterHeld) _enterConsumed = false;
            if (_expanded && keyboard?.escapeKey.wasPressedThisFrame == true)
            {
                SetExpanded(false, false);
                return;
            }
            if (!enterHeld || _enterConsumed)
                return;
            _enterConsumed = true;
            if (!ChatInputGate.IsGameplayScene)
                return;
            if (!_expanded && !IsEditorFocused())
            {
                SetExpanded(true, true);
            }
            else if (_expanded && IsEditorFocused(_draft))
            {
                SendDraft(true);
            }
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
            _collapsed = _root.Q<VisualElement>("chat-collapsed");
            _panel = _root.Q<VisualElement>("chat-panel");
            _setup = _root.Q<VisualElement>("chat-setup");
            _main = _root.Q<VisualElement>("chat-main");
            _rename = _root.Q<VisualElement>("chat-rename");
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
            _setName = _root.Q<Button>("name-submit");
            _renameButton = _root.Q<Button>("rename-submit");
            _globalTab = _root.Q<Button>("chat-tab-global");
            _serverTab = _root.Q<Button>("chat-tab-server");
            _directTab = _root.Q<Button>("chat-tab-direct");
            _statusCollapsed = _root.Q<Label>("chat-status-collapsed");
            _statusHeader = _root.Q<Label>("chat-status-header");
            _titleHeader = _root.Q<Label>("chat-title-header");
            _setupFeedback = _root.Q<Label>("setup-feedback");
            _onlineFeedback = _root.Q<Label>("online-feedback");
            _unread = _root.Q<Label>("chat-unread");
            _draft = _root.Q<TextField>("chat-draft");
            _name = _root.Q<TextField>("display-name");
            _renameField = _root.Q<TextField>("rename-name");

            if (_open != null) _open.clicked += () => SetExpanded(true, false);
            if (_close != null) _close.clicked += () => SetExpanded(false, false);
            if (_newest != null) _newest.clicked += ScrollToNewest;
            if (_refreshDirectory != null) _refreshDirectory.clicked += RefreshDirectory;
            if (_send != null) _send.clicked += () => SendDraft(ChatInputGate.IsGameplayScene);
            if (_setName != null) _setName.clicked += () => SubmitName(false);
            if (_renameButton != null) _renameButton.clicked += () => SubmitName(true);
            if (_globalTab != null) _globalTab.clicked += SelectGlobal;
            if (_serverTab != null) _serverTab.clicked += SelectServer;
            if (_directTab != null) _directTab.clicked += SelectDirectTab;
            if (_history != null)
            {
                _history.contentContainer.RegisterCallback<GeometryChangedEvent>(_ =>
                {
                    if (_pinToNewest) ScrollToNewest();
                });
                _history.verticalScroller.valueChanged += _ => _pinToNewest = IsAtNewest();
            }

            if (_draft != null)
            {
                _draft.maxLength = -1;
                _draft.RegisterValueChangedCallback(OnDraftChanged);
                RegisterEditorFocus(_draft);
            }

            if (_name != null)
            {
                _name.maxLength = -1;
                _name.RegisterValueChangedCallback(OnNameChanged);
                RegisterEditorFocus(_name);
                _name.RegisterCallback<KeyDownEvent>(evt => OnNameKeyDown(evt, false));
            }

            if (_renameField != null)
            {
                _renameField.maxLength = -1;
                _renameField.RegisterValueChangedCallback(OnRenameChanged);
                RegisterEditorFocus(_renameField);
                _renameField.RegisterCallback<KeyDownEvent>(evt => OnNameKeyDown(evt, true));
            }

            _root.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
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
            if (_expanded && active?.Unread > 0)
                _session.MarkActiveRead();
            bool needsName = _session.NeedsDisplayName;
            bool connected = _session.IsConnected;
            _setup?.SetDisplayed(needsName);
            SetText(_statusCollapsed, _session.Status);
            SetText(_statusHeader, _session.Status);
            SetText(_setupFeedback, _session.Status);

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
            _main?.SetDisplayed(!needsName);
            _rename?.SetDisplayed(!needsName && !_session.JoinedServerId.HasValue);
            if (_setName != null)
                _setName.text = needsName ? "SET DISPLAY NAME" : "NAME SAVED";
            if (_setName != null)
                _setName.SetEnabled(needsName && IsNameValid(_name?.value ?? string.Empty));

            if (_renameField != null && !IsEditorFocused(_renameField) && !needsName)
            {
                string displayName = _session.Self?.DisplayName ?? string.Empty;
                if (_renameField.value != displayName)
                    _renameField.SetValueWithoutNotify(displayName);
            }

            RenderTabs(active, connected);
            RenderConversations(active);
            RenderDirectory(active);
            RenderHistory(active);
            RenderCompactFeed();
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
            {
                var row = new VisualElement();
                row.AddToClassList("conversation-row");
                row.EnableInClassList("conversation-row--active", active != null && conversation.Key == active.Key);

                var select = new Button(() =>
                {
                    _showDirectory = false;
                    _session.SelectConversation(conversation.Key);
                    SetExpanded(true, false);
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

                _conversationList.Add(row);
            }
        }

        private void RenderDirectory(ChatConversation? active)
        {
            bool showDirectory = _showDirectory;
            _onlineList?.SetDisplayed(showDirectory);
            _refreshDirectory?.SetDisplayed(showDirectory);
            _onlineFeedback?.SetDisplayed(showDirectory);
            if (!showDirectory || _onlineList == null || _session == null)
                return;

            _onlineList.Clear();
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
            bool atNewest = IsAtNewest();
            Vector2 oldOffset = _history.scrollOffset;
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
            _pinToNewest = changedConversation || atNewest;

            bool canSend = active.CanSend && !active.IsSending && _session.IsConnected;
            _draft.SetEnabled(!active.IsSending);
            _send?.SetEnabled(canSend && IsMessageValid(active.Draft));
            if (_send != null)
                _send.text = active.IsSending ? "SENDING" : "SEND";
            SetText(_historyFeedback, active.Feedback);

            _history.schedule.Execute(() =>
            {
                if (changedConversation || atNewest)
                    ScrollToNewest();
                else
                    _history.scrollOffset = oldOffset;
                _newest?.SetDisplayed(!changedConversation && !atNewest && _history.childCount > 0);
            }).StartingIn(0);

        }

        private VisualElement BuildMessageRow(ChatMessage message)
        {
            var row = new VisualElement();
            row.AddToClassList("chat-message");
            var text = new Label($"{message.Sender.DisplayName} [{message.Sender.SessionTag}]: {message.Text}");
            text.enableRichText = false;
            text.AddToClassList("chat-message__text");
            row.Add(text);

            ChatPlayer? self = _session?.Self;
            if (self == null || message.Sender.PlayerId != self.PlayerId)
            {
                var mute = new Button(() =>
                {
                    if (_session == null)
                        return;
                    _session.SetMuted(message.Sender.PlayerId, !_session.IsMuted(message.Sender.PlayerId));
                    RenderSession();
                });
                mute.text = _session != null && _session.IsMuted(message.Sender.PlayerId) ? "UNMUTE" : "MUTE";
                mute.AddToClassList("chat-mute");
                row.Add(mute);
            }
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
            if (IsEnter(evt.keyCode) && ChatInputGate.IsGameplayScene &&
                !IsEditorFocused(_name) && !IsEditorFocused(_renameField))
            {
                evt.StopImmediatePropagation();
                return;
            }
            if (evt.keyCode == KeyCode.Escape && _expanded)
            {
                evt.StopImmediatePropagation();
                SetExpanded(false, false);
                return;
            }

            if (!IsEnter(evt.keyCode) || !_expanded || !IsEditorFocused(_draft))
                return;

            evt.StopImmediatePropagation();
            SendDraft(false);
        }
        private void OnNameKeyDown(KeyDownEvent evt, bool rename)
        {
            if (!IsEnter(evt.keyCode))
                return;
            evt.StopImmediatePropagation();
            SubmitName(rename);
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

        private void OnNameChanged(ChangeEvent<string> evt)
        {
            if (_setName != null)
                _setName.SetEnabled(IsNameValid(evt.newValue));
        }

        private void OnRenameChanged(ChangeEvent<string> evt)
        {
            if (_renameButton != null)
                _renameButton.SetEnabled(IsNameValid(evt.newValue));
        }

        private async void SubmitName(bool rename)
        {
            if (_session == null)
                return;
            string raw = rename ? _renameField?.value ?? string.Empty : _name?.value ?? string.Empty;
            if (!ChatTextValidation.TryValidateDisplayName(raw, out string value, out string error))
            {
                SetText(rename ? _historyFeedback : _setupFeedback, error);
                return;
            }

            try
            {
                bool accepted = rename
                    ? await _session.RenameAsync(value)
                    : await _session.SetDisplayNameAsync(value);
                if (!accepted)
                    RenderSession();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                RenderSession();
            }
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
            _panel?.SetDisplayed(expanded);
            _collapsed?.SetDisplayed(!expanded);
            if (!expanded)
            {
                _draft?.Blur();
                _name?.Blur();
                _renameField?.Blur();
                ChatInputGate.End();
                if (_ownsCursor)
                {
                    UnityEngine.Cursor.lockState = _previousCursorLock;
                    UnityEngine.Cursor.visible = _previousCursorVisible;
                    _ownsCursor = false;
                }
                return;
            }
            if (ChatInputGate.IsGameplayScene)
            {
                ChatInputGate.Begin();
                if (!_ownsCursor)
                {
                    _previousCursorLock = UnityEngine.Cursor.lockState;
                    _previousCursorVisible = UnityEngine.Cursor.visible;
                    _ownsCursor = true;
                }
            }
            RenderSession();
            if (focusComposer)
                _root?.schedule.Execute(() =>
                    (_session?.NeedsDisplayName == true ? _name : _draft)?.Focus()).StartingIn(0);
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
            return IsWithin(focused, _draft) || IsWithin(focused, _name) || IsWithin(focused, _renameField);
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

        private static bool IsNameValid(string value)
        {
            return ChatTextValidation.TryValidateDisplayName(value, out _, out _);
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
