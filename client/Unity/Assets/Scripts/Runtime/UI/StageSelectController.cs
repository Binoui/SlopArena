using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using SlopArena.Shared;
using SlopArena.Client.UI;

namespace SlopArena.Client.UI
{
    public class StageSelectController : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;

        private string _selectedArena = "";

        private void OnEnable()
        {
            var root       = _uiDocument.rootVisualElement;
            var grid       = root.Q<VisualElement>("stage-grid");
            var btnConfirm = root.Q<Button>("btn-confirm");
            var lblWaiting = root.Q<Label>("lbl-waiting");
            var btnBack    = root.Q<Button>("btn-back");

            bool isHost = MatchConfig.Mode == GameMode.Training || MatchConfig.IsHost;

            // Host: confirm button hidden until a card is selected; client: show waiting label
            btnConfirm.style.display = DisplayStyle.None;
            lblWaiting.style.display = isHost ? DisplayStyle.None : DisplayStyle.Flex;

            // Build stage cards from ArenaRegistry
            foreach (var arena in ArenaRegistry.All)
            {
                string capturedName = arena.Name;
                var card = new Button(() => SelectStage(capturedName, root, btnConfirm))
                {
                    name = $"stage-{arena.Name}"
                };
                card.AddToClassList("stage-card");

                // Color swatch placeholder thumbnail
                var swatch = new VisualElement();
                swatch.AddToClassList("stage-swatch");
                if (!string.IsNullOrEmpty(arena.PreviewColor))
                    swatch.style.backgroundColor = ParseHex(arena.PreviewColor);

                var label = new Label(arena.DisplayName ?? arena.Name.ToUpper());
                label.AddToClassList("stage-name");

                card.Add(swatch);
                card.Add(label);
                card.SetEnabled(isHost);
                grid.Add(card);
            }

            btnConfirm.clicked += () =>
            {
                if (string.IsNullOrEmpty(_selectedArena)) return;
                MatchConfig.ArenaName = _selectedArena;
                string scene = MatchConfig.Mode == GameMode.Training ? "Arena_Offline" : "Arena_PvP";
                SceneManager.LoadScene(scene);
            };

            btnBack.clicked += () => SceneManager.LoadScene("CharSelect");
        }

        private void SelectStage(string name, VisualElement root, Button btnConfirm)
        {
            _selectedArena = name;

            foreach (var card in root.Q<VisualElement>("stage-grid").Children())
            {
                card.RemoveFromClassList("stage-card--selected");
                if (card.name == $"stage-{name}")
                    card.AddToClassList("stage-card--selected");
            }

            btnConfirm.style.display = DisplayStyle.Flex;
        }

        private static UnityEngine.Color ParseHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return UnityEngine.Color.gray;
            hex = hex.TrimStart('#');
            if (hex.Length != 6) return UnityEngine.Color.gray;
            float r = System.Convert.ToInt32(hex.Substring(0, 2), 16) / 255f;
            float g = System.Convert.ToInt32(hex.Substring(2, 2), 16) / 255f;
            float b = System.Convert.ToInt32(hex.Substring(4, 2), 16) / 255f;
            return new UnityEngine.Color(r, g, b);
        }
    }
}
