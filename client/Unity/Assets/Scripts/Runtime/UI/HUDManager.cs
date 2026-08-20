using System;
using System.Collections.Generic;
using SlopArena.Shared;
using SlopArena.Client.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Combat HUD (spec §1-§3), rebuilt on UI Toolkit.
    ///
    /// Two layers:
    ///  • Overhead — one screen-space-tracked panel per player (badge, damage %,
    ///    stocks), clamped to each player's simulation position via
    ///    Camera.WorldToScreenPoint → RuntimePanelUtils.ScreenToPanel. Panels are
    ///    built at runtime from the roster, so 1v1, 2/3/4-player PvP all adapt
    ///    with no per-count UXML variants.
    ///  • Action bar — the local player's cooldowns: Dash + abilities 1-4 + A/E/R/F
    ///    + Burst (doc §2). Key labels are read live from InputBindings; slot and
    ///    cooldown data come from the client-side simulation state (read-only —
    ///    the UI never drives gameplay).
    ///
    /// Juice (spec §3.2): cooldown-ready pulse (1.15x / 0.15s) + white flash,
    /// persistent burst glow while available, and a damage-taken hit-flash on the
    /// overhead percent.
    /// </summary>
    public class HUDManager : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;

        /// <summary>Identity required by both the tracked readout and broadcast card.</summary>
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
            public Label DamageOutline = null!;
            public VisualElement[] StockIcons = Array.Empty<VisualElement>();
            public Label StockCountLabel = null!;
            public Color IdentityColor = Color.white;
            public bool UseIdentityColor;
            public Color TierColor = Color.white;
            public Vector2 SmoothPos;
            public ushort PrevDamage;
            public float HitFlashTimer;
        }

        private sealed class ActionSlot
        {
            public readonly VisualElement Root;
            public readonly VisualElement Cooldown;
            public readonly Label Timer;
            public readonly Label Key;
            public readonly VisualElement Flash;
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
                Flash = root.Q<VisualElement>(flashName);
            }
        }

        private readonly struct AbilitySlotDef
        {
            public readonly string Name;
            public readonly int SlotIndex; // GetSlotAbility index == cooldown index (0-10)
            public readonly BindableAction Action;

            public AbilitySlotDef(string name, int slotIndex, BindableAction action)
            {
                Name = name;
                SlotIndex = slotIndex;
                Action = action;
            }
        }

        /// <summary>
        /// The action bar's ability slots, in doc §2 order: abilities 1-4 then A/E/R/F.
        /// SlotIndex is the AbilitySlots/cooldown index (key "1" = 2 … key "A" = 10).
        /// LMB/RMB and the dead Slot5 are intentionally excluded (they are not
        /// ability slots in the current re-tier; key "5" has no kit data).
        /// </summary>
        private static readonly AbilitySlotDef[] AbilitySlotDefs =
        {
            new("ab-1", 2, BindableAction.Slot1),
            new("ab-2", 6, BindableAction.Slot2),
            new("ab-3", 7, BindableAction.Slot3),
            new("ab-4", 8, BindableAction.Slot4),
            new("ab-a", 10, BindableAction.SlotA),
            new("ab-e", 3, BindableAction.SlotE),
            new("ab-r", 4, BindableAction.SlotR),
            new("ab-f", 5, BindableAction.SlotF),
        };

        // Stock icons beyond this many become a "×N" count label instead (MaxStocks ≤ 99).
        private const int MaxStockIcons = 8;

        /// <summary>Fixed screen-space gap (px) above the character's projected center the
        /// overhead panel hangs at — camera-pitch independent (spec §3.1).</summary>
        private const float OverheadScreenOffsetPx = 45f;
        private const float TrackLerpSpeed = 25f;
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
        private InputBindings _bindings;
        private UnityEngine.Camera _camera;

        private VisualElement _overheadLayer;
        private VisualElement _billboardLayer;
        private VisualElement _actionBar;
        private readonly Dictionary<ulong, OverheadPanel> _panels = new();
        private readonly Dictionary<ulong, OverheadPanel> _billboardPanels = new();

        private ActionSlot _dashSlot;
        private ActionSlot _burstSlot;
        private ActionSlot[] _abilitySlots = Array.Empty<ActionSlot>();

        // Consumed-feedback detection state. AttackSlot (1-based ActiveSlot) is the
        // authoritative "just cast" signal for abilities — it fires on press for every
        // slot including 0-cooldown normals (1-4), unlike the cooldown fill which only
        // moves at ability end. Dash/burst never touch AttackSlot, so they are detected
        // by their own immediate cooldown-start instead.
        private byte _prevAttackSlot;
        private ushort _prevDashCd;
        private ushort _prevBurstCd;

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
            _bindings = Resources.Load<InputBindings>("InputBindings");

            if (_uiDocument == null)
            {
                Debug.LogWarning("[HUD] No UIDocument assigned");
                return;
            }

            var root = _uiDocument.rootVisualElement;
            _overheadLayer = root.Q<VisualElement>("overhead-layer");
            _billboardLayer = root.Q<VisualElement>("player-billboard");
            _actionBar = root.Q<VisualElement>("action-bar");

            // Rebuild player panels from the roster (badge color by roster position).
            _overheadLayer?.Clear();
            _panels.Clear();
            _billboardLayer?.Clear();
            _billboardPanels.Clear();
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];

                if (!p.IsLocal)
                {
                    var panel = BuildOverheadPanel(p, i);
                    _panels[p.EntityId] = panel;
                    _overheadLayer?.Add(panel.Root);
                }
                else
                {
                    _localEntityId = p.EntityId;
                }

                var billboard = BuildBillboardPanel(p, i);
                _billboardPanels[p.EntityId] = billboard;
                _billboardLayer?.Add(billboard.Root);
            }

            SetupActionBar();

            if (_actionBar != null)
                _actionBar.style.display = _localEntityId != 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SetupActionBar()
        {
            if (_uiDocument == null) return;
            var root = _uiDocument.rootVisualElement;

            _dashSlot = new ActionSlot(root.Q<VisualElement>("dash-slot"), "dash-cooldown", "dash-timer", "dash-key", "dash-flash");
            _burstSlot = new ActionSlot(root.Q<VisualElement>("burst-slot"), "burst-cooldown", "burst-timer", "burst-key", "burst-flash");

            _abilitySlots = new ActionSlot[AbilitySlotDefs.Length];
            for (int i = 0; i < AbilitySlotDefs.Length; i++)
            {
                var d = AbilitySlotDefs[i];
                _abilitySlots[i] = new ActionSlot(
                    root.Q<VisualElement>(d.Name),
                    $"{d.Name}-cooldown", $"{d.Name}-timer", $"{d.Name}-key", $"{d.Name}-flash");
            }

            // Live key labels from the remappable bindings.
            _dashSlot.Key.text = KeyLabel(GetBoundKey(BindableAction.Dash));
            _burstSlot.Key.text = KeyLabel(GetBoundKey(BindableAction.Burst));
            for (int i = 0; i < AbilitySlotDefs.Length; i++)
                _abilitySlots[i].Key.text = KeyLabel(GetBoundKey(AbilitySlotDefs[i].Action));
        }

        private OverheadPanel BuildOverheadPanel(HudPlayer p, int colorIndex)
        {
            var root = new VisualElement { name = $"overhead-{p.EntityId}" };
            root.AddToClassList("overhead-panel");

            var identityColor = BadgeColors[colorIndex % BadgeColors.Length];
            var damageOutline = new Label("0%");
            damageOutline.AddToClassList("overhead-damage-outline");
            root.Add(damageOutline);

            var damage = new Label("0%");
            damage.AddToClassList("overhead-damage");
            damage.style.color = identityColor;
            root.Add(damage);

            return new OverheadPanel
            {
                Root = root,
                DamageOutline = damageOutline,
                Damage = damage,
                IdentityColor = identityColor,
                UseIdentityColor = true,
            };
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

            var damage = new Label("0%");
            damage.AddToClassList("overhead-damage");

            var panel = new OverheadPanel { Root = root, Damage = damage };
            AddStocks(panel, details);
            copy.Add(details);
            copy.Add(damage);
            root.Add(copy);
            return panel;
        }

        private void AddStocks(OverheadPanel panel, VisualElement parent)
        {
            if (_maxStocks <= 0) return;

            var stockRow = new VisualElement();
            stockRow.AddToClassList("stock-row");
            parent.Add(stockRow);

            if (_maxStocks <= MaxStockIcons)
            {
                panel.StockIcons = new VisualElement[_maxStocks];
                for (int i = 0; i < _maxStocks; i++)
                {
                    var icon = new VisualElement();
                    icon.AddToClassList("stock-icon");
                    stockRow.Add(icon);
                    panel.StockIcons[i] = icon;
                }
            }
            else
            {
                var count = new Label($"×{_maxStocks}");
                count.AddToClassList("stock-count");
                stockRow.Add(count);
                panel.StockCountLabel = count;
            }
        }

        /// <summary>
        /// Provide the character definition: resolves ability icons, per-slot max
        /// cooldowns (max of grounded/airborne), and the locked (no-data) state.
        /// </summary>
        public void SetCharacterDefinition(CharacterDefinition def)
        {
            _charDef = def;

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

                LoadSlotIcon(slot.Root.Q<VisualElement>($"{d.Name}-icon"), grounded ?? airborne);
            }

            _dashSlot.MaxCooldown = def.Movement.DashCooldownTicks;
            _burstSlot.MaxCooldown = BurstConfig.CooldownTicks;
        }

        private void LoadSlotIcon(VisualElement icon, AbilitySpec spec)
        {
            if (icon == null || _charDef == null) return;
            if (spec == null || string.IsNullOrEmpty(spec.IconName)) return;

            string path = $"Icons/{_charDef.Class}/{spec.IconName}";
            var tex = Resources.Load<Texture2D>(path);
            if (tex != null)
                icon.style.backgroundImage = new StyleBackground(tex);
            // No icon art yet — the USS .slot-icon-inner placeholder remains. Silent on purpose.
        }

        /// <summary>Apply damage % + stocks to one panel (overhead or scoreboard).</summary>
        private void UpdatePanel(OverheadPanel panel, CharacterState state)
        {
            int dmg = (int)state.DamagePercent;
            panel.TierColor = panel.UseIdentityColor ? panel.IdentityColor : DamageColor(dmg);
            panel.Damage.text = $"{dmg}%";
            if (panel.DamageOutline != null) panel.DamageOutline.text = panel.Damage.text;
            if (dmg > panel.PrevDamage) panel.HitFlashTimer = JuiceDuration;
            panel.PrevDamage = (ushort)dmg;

            if (_maxStocks > 0)
            {
                int left = Mathf.Max(0, _maxStocks - state.Deaths);
                if (panel.StockIcons.Length > 0)
                {
                    for (int i = 0; i < panel.StockIcons.Length; i++)
                        panel.StockIcons[i].EnableInClassList("lost", i >= left);
                }
                else if (panel.StockCountLabel != null)
                {
                    panel.StockCountLabel.text = $"×{left}";
                }
                panel.Root.EnableInClassList("eliminated", left <= 0);
            }
        }

        /// <summary>
        /// Refresh all HUD data from the simulation. Called by the owning MatchBase
        /// each fixed tick. Overhead panel screen positions are smoothed in LateUpdate.
        /// </summary>
        public void Refresh()
        {
            if (_getState == null || _uiDocument == null) return;

            // Floating overhead panels (opponents) + bottom scoreboard (everyone).
            foreach (var kv in _panels)
                UpdatePanel(kv.Value, _getState(kv.Key));
            foreach (var kv in _billboardPanels)
                UpdatePanel(kv.Value, _getState(kv.Key));

            // Action bar — local player only.
            if (_localEntityId != 0)
            {
                var state = _getState(_localEntityId);

                UpdateCooldownSlot(_dashSlot, state.DashCooldownTicks);

                ushort burstCd = state.BurstCooldownTicks;
                UpdateCooldownSlot(_burstSlot, burstCd);
                _burstSlot.Root.EnableInClassList("ready-glow", burstCd == 0);

                for (int i = 0; i < _abilitySlots.Length; i++)
                {
                    var slot = _abilitySlots[i];
                    if (slot.Locked) continue;
                    UpdateCooldownSlot(slot, state.GetCooldown((byte)(AbilitySlotDefs[i].SlotIndex + 1)));
                }

                // Consumed feedback: pulse the slot the instant its action starts.
                // Abilities pulse on the AttackSlot transition (0→cast — fires at press
                // for 0-cooldown normals too); dash/burst pulse on their immediate
                // cooldown start (they never set AttackSlot).
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
                if (state.DashCooldownTicks > 0 && _prevDashCd == 0) PulseConsumed(_dashSlot);
                _prevDashCd = state.DashCooldownTicks;
                if (state.BurstCooldownTicks > 0 && _prevBurstCd == 0) PulseConsumed(_burstSlot);
                _prevBurstCd = state.BurstCooldownTicks;
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

        /// <summary>Drive transient juice timers and overhead hit-flashes at render rate.</summary>
        private void Update()
        {
            float dt = Time.deltaTime;

            if (_dashSlot != null) TickSlotJuice(_dashSlot, dt);
            if (_burstSlot != null) TickSlotJuice(_burstSlot, dt);
            for (int i = 0; i < _abilitySlots.Length; i++) TickSlotJuice(_abilitySlots[i], dt);

            foreach (var panel in _panels.Values)
                TickPanelJuice(panel, dt);
            foreach (var panel in _billboardPanels.Values)
                TickPanelJuice(panel, dt);
        }

        private static void TickPanelJuice(OverheadPanel panel, float dt)
        {
            bool flashing = panel.HitFlashTimer > 0f;
            if (flashing) panel.HitFlashTimer -= dt;
            panel.Damage.style.color = flashing ? Color.white : panel.TierColor;
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

        /// <summary>Track overhead panels to player sim positions (after the camera moves).</summary>
        /// <summary>
        /// Resolve the render camera for screen→panel projection. Camera.main only works when
        /// the match camera is tagged MainCamera; the Cinemachine-driven CameraMount camera is
        /// not, so fall back to any enabled, active camera (the one real Unity camera in a match).
        /// </summary>
        private static UnityEngine.Camera FindRenderCamera()
        {
            var main = UnityEngine.Camera.main;
            if (main != null) return main;
            foreach (var c in UnityEngine.Camera.allCameras)
                if (c != null && c.enabled && c.gameObject.activeInHierarchy)
                    return c;
            return null;
        }

        private void LateUpdate()
        {
            if (_panels.Count == 0 || _uiDocument == null) return;
            _camera ??= FindRenderCamera();
            if (_camera == null) return;

            if (_uiDocument.rootVisualElement.panel is not UnityEngine.UIElements.IPanel runtimePanel) return;

            float dt = Time.deltaTime;
            foreach (var kv in _panels)
            {
                var state = _getState(kv.Key);
                var panel = kv.Value;

                // Project the character CENTER, then hang the panel at a fixed screen-space
                // gap above it — camera-pitch independent, so it stays over the head.
                Vector3 world = new UnityEngine.Vector3(state.PX, state.PY, state.PZ);
                Vector3 screenPoint = _camera.WorldToScreenPoint(world);
                bool visible = screenPoint.z > 0;

                if (visible)
                {
                    Vector2 target = RuntimePanelUtils.ScreenToPanel(runtimePanel, new Vector2(screenPoint.x, screenPoint.y));
                    // ScreenToPanel passes the screen Y through (bottom-left origin), but
                    // style.top is measured from the top: flip, then lift above the head.
                    target.y = runtimePanel.visualTree.layout.height - target.y - OverheadScreenOffsetPx;
                    panel.SmoothPos = Vector2.Lerp(panel.SmoothPos, target, dt * TrackLerpSpeed);
                    panel.Root.style.left = panel.SmoothPos.x;
                    panel.Root.style.top = panel.SmoothPos.y;
                    // Center the panel horizontally, its bottom edge at the tracked point.
                    panel.Root.style.translate = new Translate(
                        new Length(-50, LengthUnit.Percent),
                        new Length(-100, LengthUnit.Percent));
                }

                panel.Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // ── Small helpers ───────────────────────────────────────────────────

        private Key GetBoundKey(BindableAction action)
            => _bindings != null ? _bindings.GetKey(action) : InputBindings.DefaultKey(action);

        private static string KeyLabel(Key key)
        {
            if (key >= Key.A && key <= Key.Z)
                return ((char)((int)key - (int)Key.A + 'A')).ToString();
            if (key >= Key.Digit0 && key <= Key.Digit9)
                return ((char)((int)key - (int)Key.Digit0 + '0')).ToString();
            return key switch
            {
                Key.LeftShift => "Shift",
                Key.RightShift => "R-Shift",
                Key.Space => "Space",
                Key.None => "?",
                _ => key.ToString(),
            };
        }

        /// <summary>Damage-percent tier colors (spec §3.1): white &lt;40, orange 40-89, crimson 90+.</summary>
        private static Color DamageColor(int percent)
            => percent < 40 ? Color.white : percent < 90 ? OrangeDamage : CrimsonDamage;
    }
}
