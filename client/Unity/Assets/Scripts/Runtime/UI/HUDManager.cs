using System;
using System.Collections.Generic;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// HUD for stock matches (ADR-0007, issue #38): one panel per player showing
    /// name, damage % and stock count, with the local player highlighted. The
    /// local player's ability cooldown slots stay visible below the panels.
    /// Panels are built at runtime from the roster so the layout adapts to
    /// 2, 3 or 4 players with no per-count UXML variants.
    /// </summary>
    public class HUDManager : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;

        /// <summary>One HUD panel's backing data: entity id, label, local flag.</summary>
        public readonly struct HudPlayer
        {
            public readonly ulong EntityId;
            public readonly string Label;
            public readonly bool IsLocal;

            public HudPlayer(ulong entityId, string label, bool isLocal)
            {
                EntityId = entityId;
                Label = label;
                IsLocal = isLocal;
            }
        }

        private sealed class Panel
        {
            public VisualElement Root = null!;
            public Label DamageLabel = null!;
            public VisualElement[] StockIcons = Array.Empty<VisualElement>();
            public Label StockCountLabel = null!;
        }

        // Stock icons beyond this many become a "×N" count label instead (MaxStocks ≤ 99).
        private const int MaxStockIcons = 8;

        private Func<ulong, CharacterState> _getState;
        private ulong _localEntityId;
        private int _maxStocks;
        private readonly Dictionary<ulong, Panel> _panels = new();

        // Local player's cooldown slots (unchanged from the single-player HUD)
        private VisualElement[] _slotIcons = new VisualElement[6];
        private VisualElement[] _slotCooldownFills = new VisualElement[6];
        private ushort[] _slotMaxCooldowns = new ushort[6];
        private CharacterDefinition? _charDef;

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

            // Rebuild player panels from the roster.
            var panelsContainer = root.Q<VisualElement>("player-panels");
            panelsContainer.Clear();
            _panels.Clear();
            foreach (var p in players)
            {
                var panel = BuildPanel(p);
                _panels[p.EntityId] = panel;
                panelsContainer.Add(panel.Root);
                if (p.IsLocal) _localEntityId = p.EntityId;
            }

            for (int i = 0; i < 6; i++)
            {
                _slotIcons[i] = root.Q<VisualElement>($"slot-{i}");
                _slotCooldownFills[i] = root.Q<VisualElement>($"slot-{i}-cooldown");
            }
        }

        private Panel BuildPanel(HudPlayer p)
        {
            var root = new VisualElement();
            root.name = $"player-panel-{p.EntityId}";
            root.AddToClassList("player-panel");
            if (p.IsLocal) root.AddToClassList("local");

            var nameLabel = new Label(p.Label);
            nameLabel.AddToClassList("player-name");
            root.Add(nameLabel);

            var damageLabel = new Label("0%");
            damageLabel.AddToClassList("player-damage");
            root.Add(damageLabel);

            var panel = new Panel { Root = root, DamageLabel = damageLabel };

            if (_maxStocks > 0)
            {
                var stockRow = new VisualElement();
                stockRow.AddToClassList("stock-row");
                root.Add(stockRow);

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

            return panel;
        }

        public void SetSlotMaxCooldown(int slot, ushort ticks)
        {
            if (slot >= 0 && slot < 6)
                _slotMaxCooldowns[slot] = ticks;
        }

        public void SetCharacterDefinition(CharacterDefinition def)
        {
            _charDef = def;
            LoadIcons();
        }

        private void LoadIcons()
        {
            if (_charDef == null || _slotIcons == null) return;
            for (int i = 0; i < 6; i++)
            {
                var spec = _charDef.GetSlotAbility(i, airborne: false);
                if (spec == null || string.IsNullOrEmpty(spec.IconName)) continue;

                string path = $"Icons/{_charDef.Class}/{spec.IconName}";
                var tex = Resources.Load<Texture2D>(path);
                if (tex != null)
                    _slotIcons[i].style.backgroundImage = new StyleBackground(tex);
                else
                    Debug.LogWarning($"[HUD] Icon not found: {path}");
            }
        }

        public void Refresh()
        {
            if (_getState == null || _uiDocument == null) return;

            // Player panels: damage % + stocks for everyone.
            foreach (var kv in _panels)
            {
                var state = _getState(kv.Key);
                var panel = kv.Value;

                panel.DamageLabel.text = $"{(int)state.DamagePercent}%";

                if (_maxStocks > 0)
                {
                    int stocksLeft = _maxStocks - state.Deaths;
                    if (stocksLeft < 0) stocksLeft = 0;

                    if (panel.StockIcons.Length > 0)
                    {
                        for (int i = 0; i < panel.StockIcons.Length; i++)
                            panel.StockIcons[i].EnableInClassList("lost", i >= stocksLeft);
                    }
                    else if (panel.StockCountLabel != null)
                    {
                        panel.StockCountLabel.text = $"×{stocksLeft}";
                    }

                    // Eliminated (0 stocks) → dim the panel (spectator).
                    panel.Root.EnableInClassList("eliminated", stocksLeft <= 0);
                }
            }

            // Cooldown slots — local player only.
            if (_localEntityId != 0)
            {
                var state = _getState(_localEntityId);
                ushort[] cooldowns = {
                    state.Cooldown0, state.Cooldown1, state.Cooldown2,
                    state.Cooldown3, state.Cooldown4, state.Cooldown5
                };

                for (int i = 0; i < 6; i++)
                {
                    ushort cd = cooldowns[i];
                    bool onCooldown = cd > 0;

                    if (_slotCooldownFills[i] != null)
                    {
                        _slotCooldownFills[i].style.display = onCooldown
                            ? DisplayStyle.Flex
                            : DisplayStyle.None;

                        if (onCooldown)
                        {
                            float fraction = _slotMaxCooldowns[i] > 0
                                ? Mathf.Clamp01(cd / (float)_slotMaxCooldowns[i])
                                : 1f;
                            // Scale Y from bottom (1 = full height = just started)
                            _slotCooldownFills[i].style.scale = new Scale(new Vector2(1f, fraction));
                        }
                    }
                }
            }
        }
    }
}
