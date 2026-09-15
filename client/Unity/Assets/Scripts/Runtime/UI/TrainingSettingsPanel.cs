using System;
using UnityEngine;
using UnityEngine.UIElements;
using SlopArena.Shared.AI;
using SlopArena.Client.World;
namespace SlopArena.Client.UI
{
    /// <summary>
    /// Training settings section shown inside the pause menu (issue #187). Training mode
    /// only: TrainingMatch attaches it to MatchPauseMenu's left side, so it appears
    /// alongside the pause buttons while the simulation is frozen. Controls: no ability
    /// cooldowns (player slot cooldowns only), CPU difficulty, NPC AI mode, NPC damage %,
    /// and NPC add/delete. The layout lives in Resources/UI/TrainingSettingsPanel.uxml.
    /// </summary>
    public class TrainingSettingsPanel : MonoBehaviour
    {
        private TrainingMatch _match;

        private VisualElement _panel;
        private VisualElement _rosterRow;
        private readonly System.Collections.Generic.List<Button> _modeButtons = new();
        private readonly System.Collections.Generic.List<Button> _difficultyButtons = new();

        // UXML-bound live-refresh labels.
        private Label _npcDamageLabel;
        private Label _npcModeLabel;
        private Label _npcDifficultyLabel;
        public void Init(TrainingMatch match, MatchPauseMenu pauseMenu)
        {
            _match = match;
            var template = Resources.Load<VisualTreeAsset>("UI/TrainingSettingsPanel");
            if (template == null)
            {
                Debug.LogError("[TrainingSettings] Missing Resources/UI/TrainingSettingsPanel.uxml — settings unavailable.");
                return;
            }
            // Instantiate() wraps the UXML root; the wrapper carries the <Style> ref, so
            // attach the whole tree — attaching the bare ts-panel child drops the USS.
            var root = template.Instantiate();
            _panel = root.childCount == 1 ? root[0] : root;
            BindControls(_panel);
            pauseMenu.AttachSettingsSection(root);
        }

        private void BindControls(VisualElement panel)
        {
            var cdToggle = panel.Q<Toggle>("no-cd-toggle");
            if (cdToggle != null)
                cdToggle.RegisterValueChangedCallback(evt => _match.SetNoCooldowns(evt.newValue));

            _npcModeLabel = panel.Q<Label>("npc-mode-label");
            _npcDifficultyLabel = panel.Q<Label>("npc-difficulty-label");
            _npcDamageLabel = panel.Q<Label>("npc-damage-label");
            _rosterRow = panel.Q<VisualElement>("roster-row");

            foreach (var mode in (NpcAiMode[])Enum.GetValues(typeof(NpcAiMode)))
            {
                var m = mode;
                var btn = panel.Q<Button>($"mode-{m.ToString().ToLowerInvariant()}");
                if (btn == null) continue;
                btn.clicked += () =>
                {
                    _match.SetNpcMode(m);
                    RefreshModeHighlight();
                };
                _modeButtons.Add(btn);
            }

            foreach (var difficulty in (CpuDifficulty[])Enum.GetValues(typeof(CpuDifficulty)))
            {
                var d = difficulty;
                var btn = panel.Q<Button>($"difficulty-{d.ToString().ToLowerInvariant()}");
                if (btn == null) continue;
                btn.clicked += () =>
                {
                    _match.SetNpcDifficulty(d);
                    RefreshDifficultyHighlight();
                };
                _difficultyButtons.Add(btn);
            }

            var dmgMinus = panel.Q<Button>("dmg-minus");
            if (dmgMinus != null)
                dmgMinus.clicked += () => _match.SetSelectedNpcDamage(_match.GetSelectedNpcDamage() - 10f);
            var dmgPlus = panel.Q<Button>("dmg-plus");
            if (dmgPlus != null)
                dmgPlus.clicked += () => _match.SetSelectedNpcDamage(_match.GetSelectedNpcDamage() + 10f);
            var dmgReset = panel.Q<Button>("dmg-reset");
            if (dmgReset != null)
                dmgReset.clicked += () => _match.SetSelectedNpcDamage(0f);

            RefreshRoster();
            RefreshModeHighlight();
            RefreshDifficultyHighlight();
        }


        private void Update()
        {
            if (_npcDamageLabel == null || _match == null) return;
            if (_match.NpcCount == 0) return;
            _npcDamageLabel.text = $"NPC {_match.SelectedNpcId} — {_match.GetSelectedNpcDamage():F0}%";
        }

        // ── Roster / mode refresh (called from TrainingMatch after add/delete) ──

        public void RefreshRoster()
        {
            if (_rosterRow == null) return;
            _rosterRow.Clear();
            for (int i = 0; i < _match.NpcCount; i++)
            {
                int index = i;
                var btn = MakeRosterButton($"NPC{_match.GetNpcIdAt(index)}", () =>
                {
                    _match.SelectNpc(index);
                    RefreshRoster();
                    RefreshModeHighlight();
                });
                _rosterRow.Add(btn);
            }

            var addBtn = MakeRosterButton("ADD NPC", _match.AddNpc);
            _rosterRow.Add(addBtn);

            var delBtn = MakeRosterButton("DELETE SELECTED", _match.DeleteSelectedNpc);
            delBtn.SetEnabled(_match.NpcCount > 1);
            _rosterRow.Add(delBtn);
        }

        private void RefreshModeHighlight()
        {
            if (_npcModeLabel != null)
                _npcModeLabel.text = $"NPC MODE: {_match.CurrentNpcMode}";
            foreach (var btn in _modeButtons)
            {
                bool active = btn.text == _match.CurrentNpcMode.ToString().ToUpperInvariant();
                btn.EnableInClassList("active", active);
            }
        }

        public void RefreshDifficultyHighlight()
        {
            if (_npcDifficultyLabel != null)
                _npcDifficultyLabel.text = $"CPU DIFFICULTY: {BotDifficultyProfile.DisplayName(_match.CurrentNpcDifficulty)}";
            foreach (var btn in _difficultyButtons)
            {
                bool active = btn.text == BotDifficultyProfile.DisplayName(_match.CurrentNpcDifficulty);
                btn.EnableInClassList("active", active);
            }
        }

        private static Button MakeRosterButton(string label, Action onClick)
        {
            var btn = new Button(onClick) { text = label };
            btn.AddToClassList("ts-btn");
            return btn;
        }
    }
}
