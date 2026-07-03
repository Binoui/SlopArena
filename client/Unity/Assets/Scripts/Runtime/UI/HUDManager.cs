using SlopArena.Shared;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    public class HUDManager : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;

        private ServerSimulation _sim;
        private ulong _playerEntityId;

        // Queried elements
        private Label _damagePercentLabel;
        private VisualElement[] _slotIcons = new VisualElement[6];
        private VisualElement[] _slotCooldownFills = new VisualElement[6];
        private ushort[] _slotMaxCooldowns = new ushort[6];
        private CharacterDefinition? _charDef;

        private static readonly string[] SlotKeys = { "LMB", "RMB", "Q", "E", "R", "F" };

        public void Initialize(ServerSimulation sim, ulong playerEntityId)
        {
            _sim = sim;
            _playerEntityId = playerEntityId;

            if (_uiDocument == null)
            {
                Debug.LogWarning("[HUD] No UIDocument assigned");
                return;
            }

            var root = _uiDocument.rootVisualElement;
            _damagePercentLabel = root.Q<Label>("damage-percent");

            for (int i = 0; i < 6; i++)
            {
                _slotIcons[i] = root.Q<VisualElement>($"slot-{i}");
                _slotCooldownFills[i] = root.Q<VisualElement>($"slot-{i}-cooldown");
            }
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
            if (_sim == null || _uiDocument == null) return;

            var state = _sim.GetState(_playerEntityId);

            // Damage %
            if (_damagePercentLabel != null)
                _damagePercentLabel.text = $"{state.DamagePercent}%";

            // Slot cooldowns
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
