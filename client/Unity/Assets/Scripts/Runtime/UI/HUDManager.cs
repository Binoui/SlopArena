using System;
using System.Collections.Generic;
using SlopArena.Shared;
using SlopArena.Client.Input;
using SlopArena.Client.Network;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Combat HUD, rebuilt on UI Toolkit.
    ///
    /// Player cards are built from the roster, so 1v1 and 2/3/4-player matches
    /// adapt without per-count UXML variants. Kit diamonds use effective Input
    /// System bindings; cooldown data remains read-only.
    ///
    /// Juice: cooldown-ready pulse (1.15x / 0.15s) + white flash,
    /// and a damage-taken hit-flash on the card percent.
    /// </summary>
    public class HUDManager : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private MatchTextVFX _textVfx;

        /// <summary>The HUD's UI document — the explicit host for match-surface
        /// overlays (pause menu) so nothing searches for a first document.</summary>
        public UIDocument? Document => _uiDocument;
        /// <summary>Identity required by the tracked damage readout and broadcast card.</summary>
        public readonly struct HudPlayer
        {
            public readonly ulong EntityId;
            public readonly string Label;
            public readonly CharacterClass Class;
            public readonly bool IsLocal;

            public HudPlayer(ulong entityId, string label, CharacterClass @class, bool isLocal)
            {
                EntityId = entityId;
                Label = label;
                Class = @class;
                IsLocal = isLocal;
            }
        }

        private sealed class OverheadPanel
        {
            public VisualElement Root = null!;
            public Label Damage = null!;
            public VisualElement[] StockIcons = Array.Empty<VisualElement>();
            public Label StockCountLabel = null!;
            public Label StockToast = null!;
            public float StockToastTimer;
            public Color TierColor = Color.white;
            public ushort PrevDamage;
            public int PrevStocks = -1;
            public float HitFlashTimer;
        }

        private sealed class ActionSlot
        {
            public readonly VisualElement Root;
            public readonly VisualElement Cooldown;
            public readonly Label Timer;
            public readonly Label Key;
            public readonly Image Button;
            public readonly VisualElement Flash;
            public readonly VisualElement Icon;
            public Texture2D GroundIcon;
            public Texture2D AirIcon;
            public Texture2D DisplayedIcon;
            public ushort MaxCooldown;
            public ushort PrevCooldown;
            public float PulseTimer;
            public float UsedTimer;
            public float FlashTimer;
            public bool Locked;

            public ActionSlot(VisualElement root, string cooldownName, string timerName, string keyName, string flashName)
            {
                Root = root;
                Cooldown = root.Q<VisualElement>(cooldownName);
                Timer = root.Q<Label>(timerName);
                Key = root.Q<Label>(keyName);
                Button = root.Q<Image>(root.name + "-button");
                Button.scaleMode = ScaleMode.ScaleToFit;
                Flash = root.Q<VisualElement>(flashName);
                Icon = root.Q<VisualElement>(root.name + "-icon");
                Icon.style.backgroundImage = StyleKeyword.None;
            }
        }

        private readonly struct AbilitySlotDef
        {
            public readonly string Name;
            public readonly int SlotIndex; // GetSlotAbility index == cooldown index (0-10)
            public readonly string Action;

            public AbilitySlotDef(string name, int slotIndex, string action)
            {
                Name = name;
                SlotIndex = slotIndex;
                Action = action;
            }
        }

        /// <summary>
        /// The kit diamonds' ability slots, in doc §2 order: normals 1–4 then specials A/E/R/F.
        /// SlotIndex is the AbilitySlots/cooldown index (key "1" = 2 … key "A" = 10).
        /// LMB/RMB remain utility controls, and the former extra key position is outside the
        /// canonical action grid, so neither is shown as an ability slot.
        /// </summary>
        private static readonly AbilitySlotDef[] AbilitySlotDefs =
        {
            new("ab-1", 2, "Slot1"),
            new("ab-2", 6, "Slot2"),
            new("ab-3", 7, "Slot3"),
            new("ab-4", 8, "Slot4"),
            new("ab-a", 10, "SlotA"),
            new("ab-e", 3, "SlotE"),
            new("ab-r", 4, "SlotR"),
            new("ab-f", 5, "SlotF"),
        };

        // More than eight lives use the explicit count without overflowing the card.
        private const int MaxStockIcons = 8;

        private const float JuiceDuration = 0.15f;

        private static readonly Color[] BadgeColors;
        private static readonly Color OrangeDamage;
        private static readonly Color CrimsonDamage;

        static HUDManager()
        {
            BadgeColors = new Color[4];
            TryHex("#FBBF24", out BadgeColors[0]); // P1 gold
            TryHex("#EA580C", out BadgeColors[1]); // P2 orange-red
            TryHex("#3B82F6", out BadgeColors[2]); // P3 blue
            TryHex("#22C55E", out BadgeColors[3]); // P4 green
            TryHex("#F97316", out OrangeDamage);   // damage 40-89
            TryHex("#EF4444", out CrimsonDamage);  // damage 90+
        }

        private static bool TryHex(string hex, out Color color)
            => ColorUtility.TryParseHtmlString(hex, out color);

        private Func<ulong, CharacterState> _getState;
        private ulong _localEntityId;
        private int _maxStocks;
        private CharacterDefinition _charDef;
        private VisualElement _billboardLayer;
        private VisualElement _kitDiamonds;
        public VisualElement TargetingRoot
            => _uiDocument != null
                ? _uiDocument.rootVisualElement.Q<VisualElement>("target-lock-indicator")
                : null;
        private Label _networkStatsLabel;
        private NetworkClient _networkClient;

        private readonly Dictionary<ulong, OverheadPanel> _billboardPanels = new();

        private ActionSlot[] _abilitySlots = Array.Empty<ActionSlot>();
        private InputPromptAtlas _promptAtlas;
        private int _seenBindingRevision = -1;
        private bool _showGamepadPrompts;

        // AttackSlot (1-based ActiveSlot) is the authoritative "just cast" signal for
        // abilities — it fires on press for every slot, including 0-cooldown normals.
        private byte _prevAttackSlot;

        /// <summary>
        /// Initialize the HUD.
        /// <paramref name="getState"/> is called each Refresh() per panel entity id —
        /// pass a method over whatever simulation source owns the states (local sim,
        /// network client, replay reader).
        /// </summary>
        /// <param name="players">Roster, one panel per entry. Local player must be marked.</param>
        /// <param name="maxStocks">Stocks per player; &lt;= 0 hides stock display (training).</param>
        public void Initialize(Func<ulong, CharacterState> getState, IReadOnlyList<HudPlayer> players, int maxStocks)
        {
            _getState = getState;
            _maxStocks = Mathf.Max(0, maxStocks);
            _localEntityId = 0;

            if (_uiDocument == null)
            {
                Debug.LogWarning("[HUD] No UIDocument assigned");
                return;
            }

            var root = _uiDocument.rootVisualElement;
            if (_textVfx == null)
                _textVfx = gameObject.AddComponent<MatchTextVFX>();
            var cam = UnityEngine.Camera.main ?? FindFirstObjectByType<UnityEngine.Camera>();
            if (cam != null)
                _textVfx.SetCamera(cam);
            _networkClient = FindFirstObjectByType<NetworkClient>();
            _networkStatsLabel = new Label();
            _networkStatsLabel.style.position = Position.Absolute;
            _networkStatsLabel.style.right = 18;
            _networkStatsLabel.style.top = 12;
            _networkStatsLabel.style.color = Color.white;
            _networkStatsLabel.style.unityTextAlign = TextAnchor.UpperRight;
            root.Add(_networkStatsLabel);
            _billboardLayer = root.Q<VisualElement>("player-billboard");
            _kitDiamonds = root.Q<VisualElement>("kit-diamonds");

            // Rebuild player panels from the roster (badge color by roster position).
            _billboardLayer?.Clear();
            _billboardPanels.Clear();
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p.IsLocal)
                    _localEntityId = p.EntityId;
                var billboard = BuildBillboardPanel(p, i);
                _billboardPanels[p.EntityId] = billboard;
                _billboardLayer?.Add(billboard.Root);
            }

            SetupKitDiamonds();

            if (_kitDiamonds != null)
                _kitDiamonds.style.display = _localEntityId != 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SetupKitDiamonds()
        {
            if (_uiDocument == null) return;
            var root = _uiDocument.rootVisualElement;


            _abilitySlots = new ActionSlot[AbilitySlotDefs.Length];
            for (int i = 0; i < AbilitySlotDefs.Length; i++)
            {
                var d = AbilitySlotDefs[i];
                _abilitySlots[i] = new ActionSlot(
                    root.Q<VisualElement>(d.Name),
                    $"{d.Name}-cooldown", $"{d.Name}-timer", $"{d.Name}-key", $"{d.Name}-flash");
            }

            UpdateBindingPrompts();
        }

        private void UpdateBindingPrompts()
        {
            _showGamepadPrompts = HumanInputActions.LastUsedGamepad && Gamepad.current != null;
            _seenBindingRevision = HumanInputActions.BindingRevision;
            string group = _showGamepadPrompts ? HumanInputActions.GamepadGroup : HumanInputActions.KeyboardGroup;

            for (int i = 0; i < _abilitySlots.Length; i++)
            {
                var slot = _abilitySlots[i];
                string action = _showGamepadPrompts && i >= 4
                    ? AbilitySlotDefs[i - 4].Action : AbilitySlotDefs[i].Action;
                string path = BindingPath(action, group);
                var buttonGlyph = default(InputPromptAtlas.Glyph);
                bool hasButton = _showGamepadPrompts &&
                    (_promptAtlas ??= new InputPromptAtlas()).TryXbox(path, out buttonGlyph);
                slot.Button.style.display = hasButton ? DisplayStyle.Flex : DisplayStyle.None;
                slot.Key.style.display = hasButton ? DisplayStyle.None : DisplayStyle.Flex;
                if (hasButton)
                    SetPromptImage(slot.Button, buttonGlyph);
                else
                {
                    string label = HumanInputActions.BindingLabel(action, group);
                    slot.Key.text = _showGamepadPrompts
                        ? label : InputPromptAtlas.KeyboardDisplayLabel(path, label);
                }
            }
        }

        private static string BindingPath(string action, string group)
        {
            int index = HumanInputActions.BindingIndex(action, group);
            return index < 0 ? null : HumanInputActions.Get(action).bindings[index].effectivePath;
        }

        private static void SetPromptImage(Image image, InputPromptAtlas.Glyph glyph)
        {
            image.image = glyph.Texture;
            image.uv = glyph.Uv;
        }


        private OverheadPanel BuildBillboardPanel(HudPlayer p, int colorIndex)
        {
            var identityColor = BadgeColors[colorIndex % BadgeColors.Length];
            var root = new VisualElement { name = $"billboard-{p.EntityId}" };
            root.AddToClassList("billboard-card");
            root.AddToClassList("billboard-left");
            root.EnableInClassList("local-player", p.IsLocal);
            root.style.borderBottomColor = identityColor;

            var portraitFrame = new VisualElement();
            portraitFrame.AddToClassList("portrait-frame");
            portraitFrame.style.backgroundColor = identityColor;
            var portrait = new VisualElement();
            portrait.AddToClassList("fighter-portrait");
            var portraitTexture = Resources.Load<Texture2D>($"UI/Portraits/{p.Class}");
            if (portraitTexture != null)
                portrait.style.backgroundImage = new StyleBackground(portraitTexture);
            else
                portraitFrame.AddToClassList("portrait-missing");
            portraitFrame.Add(portrait);
            root.Add(portraitFrame);

            var copy = new VisualElement();
            copy.AddToClassList("fighter-copy");

            var details = new VisualElement();
            details.AddToClassList("fighter-details");

            var tape = new VisualElement();
            tape.AddToClassList("fighter-tape");
            var badge = new Label(p.Label);
            badge.AddToClassList("badge");
            badge.style.backgroundColor = identityColor;
            tape.Add(badge);
            details.Add(tape);

            var fighterName = new Label(p.Class.ToString().ToUpperInvariant());
            fighterName.AddToClassList("fighter-name");
            details.Add(fighterName);
            copy.Add(details);

            var damage = new Label("0%");
            damage.AddToClassList("overhead-damage");
            copy.Add(damage);

            var panel = new OverheadPanel { Root = root, Damage = damage };
            AddStocks(panel, copy);
            root.Add(copy);

            var stockToast = new Label();
            stockToast.AddToClassList("stock-toast-player");
            stockToast.style.display = DisplayStyle.None;
            stockToast.style.borderLeftColor = identityColor;
            root.Add(stockToast);
            panel.StockToast = stockToast;
            return panel;
        }

        private void AddStocks(OverheadPanel panel, VisualElement parent)
        {
            if (_maxStocks <= 0) return;

            var stockRow = new VisualElement();
            stockRow.AddToClassList("stock-row");
            var label = new Label("LIVES");
            label.AddToClassList("stock-label");
            stockRow.Add(label);

            var count = new Label($"×{_maxStocks}") { name = "stock-count" };
            count.AddToClassList("stock-count");
            stockRow.Add(count);
            panel.StockCountLabel = count;

            if (_maxStocks <= MaxStockIcons)
            {
                var tickets = new VisualElement { name = "stock-tickets" };
                tickets.AddToClassList("stock-tickets");
                tickets.EnableInClassList("many-stocks", _maxStocks > 4);
                panel.StockIcons = new VisualElement[_maxStocks];
                for (int i = 0; i < _maxStocks; i++)
                {
                    var icon = new VisualElement();
                    icon.AddToClassList("stock-icon");
                    var strike = new VisualElement { name = "stock-strike" };
                    strike.AddToClassList("stock-strike");
                    icon.Add(strike);
                    tickets.Add(icon);
                    panel.StockIcons[i] = icon;
                }
                stockRow.Add(tickets);
            }

            parent.Add(stockRow);
        }


        /// <summary>
        /// Provide the character definition: resolves ability icons, per-slot max
        /// cooldowns (max of grounded/airborne), and the locked (no-data) state.
        /// </summary>
        public void SetCharacterDefinition(CharacterDefinition def)
        {
            _charDef = def;
            bool isGrounded = _getState == null || _localEntityId == 0 || _getState(_localEntityId).IsGrounded;

            if (_abilitySlots == null || _abilitySlots.Length == 0) return;
            for (int i = 0; i < _abilitySlots.Length; i++)
            {
                var slot = _abilitySlots[i];
                var d = AbilitySlotDefs[i];
                var grounded = def.GetSlotAbility(d.SlotIndex, airborne: false);
                var airborne = def.GetSlotAbility(d.SlotIndex, airborne: true);

                ushort max = 0;
                if (grounded != null) max = grounded.CooldownTicks;
                if (airborne != null && airborne.CooldownTicks > max) max = airborne.CooldownTicks;
                slot.MaxCooldown = max;
                slot.Locked = grounded == null && airborne == null;
                slot.Root.EnableInClassList("locked", slot.Locked);

                slot.GroundIcon = LoadSlotIcon(grounded ?? airborne, airborne: false);
                slot.AirIcon = LoadSlotIcon(airborne, airborne: true);
                SetSlotIcon(slot, isGrounded ? slot.GroundIcon : slot.AirIcon);
            }

        }

        private Texture2D LoadSlotIcon(AbilitySpec spec, bool airborne)
        {
            if (spec == null || string.IsNullOrEmpty(spec.IconName)) return null;
            string path = $"Icons/{_charDef.Class}/";
            if (airborne)
            {
                var variant = Resources.Load<Texture2D>($"{path}Air/{spec.IconName}");
                if (variant != null) return variant;
            }
            return Resources.Load<Texture2D>(path + spec.IconName);
        }

        private static void SetSlotIcon(ActionSlot slot, Texture2D texture)
        {
            if (slot.DisplayedIcon == texture) return;
            slot.DisplayedIcon = texture;
            slot.Icon.style.backgroundImage = texture != null
                ? new StyleBackground(texture)
                : new StyleBackground(StyleKeyword.None);
        }

        /// <summary>Apply damage % + stocks to one scoreboard card.</summary>
        private void UpdatePanel(OverheadPanel panel, CharacterState state)
        {
            int dmg = (int)state.DamagePercent;
            panel.TierColor = DamageColor(dmg);
            if (dmg != panel.PrevDamage)
                panel.Damage.text = $"{dmg}%";
            if (dmg > panel.PrevDamage && !ClientSettingsService.Instance.ReducedFlashing)
                panel.HitFlashTimer = JuiceDuration;
            panel.PrevDamage = (ushort)dmg;

            if (_maxStocks > 0)
            {
                int left = Mathf.Clamp(_maxStocks - state.Deaths, 0, _maxStocks);
                if (left != panel.PrevStocks)
                {
                    panel.StockCountLabel.text = $"×{left}";
                    for (int i = 0; i < panel.StockIcons.Length; i++)
                        panel.StockIcons[i].EnableInClassList("lost", i >= left);
                    panel.Root.EnableInClassList("eliminated", left == 0);
                    panel.PrevStocks = left;
                }
            }
        }

        /// <summary>
        /// Shows the authored particle-text broadcast. Match callouts deliberately
        /// have no UI Toolkit text fallback so one message cannot render twice.
        /// </summary>
        public void ShowMatchCallout(string text, float seconds = 0.9f)
        {
            _textVfx?.Show(text, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f),
                text == "SLOP IT OUT" ? 0.9f : 1f,
                Color.white, new Color(1f, 0.72f, 0.12f),
                seconds / 1.5f, centerInCamera: true);
        }

        public void ShowStockToast(ulong entityId, string text, Color identityColor, float seconds = 1.15f)
        {
            if (!_billboardPanels.TryGetValue(entityId, out var panel) || panel.StockToast == null)
                return;

            panel.StockToast.text = text;
            panel.StockToastTimer = Mathf.Max(0.01f, seconds);
            var bounds = panel.Root.worldBound;
            _textVfx?.Show(text,
                new Vector2(bounds.xMax + 70f, Screen.height - bounds.yMin - bounds.height * 0.5f),
                0.7f, identityColor, Color.white, seconds);
            panel.StockToast.style.display = DisplayStyle.Flex;
            panel.StockToast.style.opacity = 1f;
            panel.StockToast.style.borderLeftColor = identityColor;
        }

        public void SetCamera(UnityEngine.Camera camera)
        {
            if (_textVfx == null)
                _textVfx = gameObject.AddComponent<MatchTextVFX>();
            _textVfx.SetCamera(camera);
        }

        /// <summary>
        /// Refresh all HUD data from the simulation. Called by the owning MatchBase
        /// each fixed tick. The player cards are updated from simulation state.
        /// </summary>
        public void Refresh()
        {
            if (_getState == null || _uiDocument == null) return;

            foreach (var kv in _billboardPanels)
                UpdatePanel(kv.Value, _getState(kv.Key));
            // Kit diamonds — local player only.
            if (_localEntityId != 0)
            {
                var state = _getState(_localEntityId);


                for (int i = 0; i < _abilitySlots.Length; i++)
                {
                    var slot = _abilitySlots[i];
                    SetSlotIcon(slot, state.IsGrounded ? slot.GroundIcon : slot.AirIcon);
                    if (slot.Locked) continue;
                    UpdateCooldownSlot(slot, state.GetCooldown((byte)(AbilitySlotDefs[i].SlotIndex + 1)));
                }

                // Consumed feedback: pulse the slot the instant its action starts.
                // Abilities pulse on the AttackSlot transition (0→cast — fires at press
                // for 0-cooldown normals too).
                byte castSlot = state.AttackSlot;
                if (castSlot != _prevAttackSlot)
                {
                    if (castSlot != 0)
                    {
                        for (int i = 0; i < _abilitySlots.Length; i++)
                        {
                            if (AbilitySlotDefs[i].SlotIndex + 1 == castSlot)
                            {
                                PulseConsumed(_abilitySlots[i]);
                                break;
                            }
                        }
                    }
                    _prevAttackSlot = castSlot;
                }
            }
        }

        /// <summary>Quick shrink-and-spring pulse — visual "spent" feedback for a used action.</summary>
        private void PulseConsumed(ActionSlot s)
        {
            s.UsedTimer = JuiceDuration;
            s.Root.EnableInClassList("used-pulse", true);
        }

        private void UpdateCooldownSlot(ActionSlot s, ushort cooldown)
        {
            bool onCooldown = cooldown > 0;

            s.Cooldown.style.display = onCooldown ? DisplayStyle.Flex : DisplayStyle.None;
            if (onCooldown)
            {
                float frac = s.MaxCooldown > 0 ? Mathf.Clamp01(cooldown / (float)s.MaxCooldown) : 1f;
                s.Cooldown.style.scale = new Scale(new Vector2(1f, frac));
                s.Timer.text = $"{(cooldown / 60f):0.0}s";
            }
            else
            {
                s.Timer.text = "";
            }

            // Cooldown ready (was on cooldown, now 0) → pulse + flash (spec §3.2).
            if (s.PrevCooldown > 0 && cooldown == 0)
            {
                s.PulseTimer = JuiceDuration;
                s.FlashTimer = JuiceDuration;
                s.Root.EnableInClassList("ready-pulse", true);
                s.Flash.EnableInClassList("active", true);
            }
            // NOTE: no "consumed" pulse here — cooldown only moves at ability END, not
            // press, so this would fire ~1 move later. Press feedback is driven from the
            // AttackSlot transition in Refresh (see PulseConsumed).
            s.PrevCooldown = cooldown;
        }



        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_abilitySlots.Length > 0 &&
                (_seenBindingRevision != HumanInputActions.BindingRevision ||
                 _showGamepadPrompts != (HumanInputActions.LastUsedGamepad && Gamepad.current != null)))
                UpdateBindingPrompts();
            UpdateNetworkStats();

            for (int i = 0; i < _abilitySlots.Length; i++) TickSlotJuice(_abilitySlots[i], dt);

            foreach (var panel in _billboardPanels.Values)
                TickPanelJuice(panel, dt);
        }

        private void UpdateNetworkStats()
        {
            if (_networkStatsLabel == null) return;
            int mode = ClientSettingsService.Instance.NetworkStats;
            _networkStatsLabel.style.display = mode == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            if (mode == 0) return;
            if (_networkClient == null)
            {
                _networkStatsLabel.text = "NETWORK: OFFLINE";
                return;
            }
            if (!_networkClient.IsServerConnected)
            {
                _networkStatsLabel.text = "NETWORK: DISCONNECTED";
                return;
            }
            float? ping = _networkClient.LastPingMilliseconds;
            _networkStatsLabel.text = !ping.HasValue
                ? "PING: MEASURING…"
                : mode == 1
                    ? $"PING: {ping.Value:F0} ms"
                    : $"PING: {ping.Value:F0} ms\nSERVER: {_networkClient.ServerEndpoint}\nTICK: {_networkClient.LastPingServerTick}";
        }

        private static void TickPanelJuice(OverheadPanel panel, float dt)
        {
            bool flashing = panel.HitFlashTimer > 0f && !ClientSettingsService.Instance.ReducedFlashing;
            if (panel.HitFlashTimer > 0f) panel.HitFlashTimer -= dt;
            panel.Damage.style.color = flashing ? Color.white : panel.TierColor;

            if (panel.StockToastTimer > 0f)
            {
                panel.StockToastTimer -= dt;
                if (panel.StockToastTimer <= 0f)
                {
                    panel.StockToast.style.opacity = 0f;
                    panel.StockToast.style.display = DisplayStyle.None;
                }
            }
        }

        private static void TickSlotJuice(ActionSlot s, float dt)
        {
            if (s.PulseTimer > 0f)
            {
                s.PulseTimer -= dt;
                s.Root.EnableInClassList("ready-pulse", s.PulseTimer > 0f);
            }
            if (s.UsedTimer > 0f)
            {
                s.UsedTimer -= dt;
                s.Root.EnableInClassList("used-pulse", s.UsedTimer > 0f);
            }
            if (s.FlashTimer > 0f)
            {
                s.FlashTimer -= dt;
                s.Flash.EnableInClassList("active", s.FlashTimer > 0f);
            }
        }


        // ── Small helpers ───────────────────────────────────────────────────


        /// <summary>Damage-percent tier colors (spec §3.1): white &lt;40, orange 40-89, crimson 90+.</summary>
        internal static Color DamageColor(int percent)
            => percent < 40 ? Color.white : percent < 90 ? OrangeDamage : CrimsonDamage;
    }
}
