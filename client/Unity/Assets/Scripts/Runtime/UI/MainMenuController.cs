using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using SlopArena.Shared;

namespace SlopArena.Client.UI
{
    public class MainMenuController : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;

        private void OnEnable()
        {
            MatchConfig.Reset();
            var root = _uiDocument.rootVisualElement;
            var btnTraining = root.Q<Button>("btn-training");
            var btnSolo = root.Q<Button>("btn-solo");
            var btnMultiplayer = root.Q<Button>("btn-multiplayer");

            if (btnTraining != null)
                btnTraining.clicked += OpenTraining;
            if (btnSolo != null)
                btnSolo.clicked += OpenSolo;
            if (btnMultiplayer != null)
                btnMultiplayer.clicked += OpenMultiplayer;

            var initial = btnMultiplayer ?? btnSolo ?? btnTraining;
            if (initial != null)
                MenuNavigation.Configure(root, initial, ReturnToMainMenu);

            BuildRoster(root);
        }

        private static void OpenTraining()
        {
            MatchConfig.Mode = GameMode.Training;
            MatchConfig.IsHost = true;
            SceneManager.LoadScene("CharSelect");
        }

        private static void OpenSolo()
        {
            MatchConfig.Mode = GameMode.Solo;
            MatchConfig.IsHost = true;
            SceneManager.LoadScene("CharSelect");
        }

        private static void OpenMultiplayer()
        {
            MatchConfig.Mode = GameMode.PvP;
            MatchConfig.IsHost = false;
            SceneManager.LoadScene("ServerBrowser");
        }

        private static void ReturnToMainMenu()
        {
            // MainMenu is the root of this flow; Escape here is intentionally a no-op.
        }

        private static void BuildRoster(VisualElement root)
        {
            var rosterPanel = root.Q<VisualElement>("roster-panel");
            var countLabel = root.Q<Label>("online-count");
            if (rosterPanel == null)
                return;

            rosterPanel.Clear();
            var classes = MenuRoster.Classes;
            if (countLabel != null)
                countLabel.text = $"{classes.Length} CHARACTERS";

            for (int i = 0; i < classes.Length; i++)
            {
                CharacterClass fighter = classes[i];
                var card = new VisualElement { name = "menu-roster-card" };
                card.AddToClassList("menu-roster-card");
                card.AddToClassList($"menu-roster-card--{ColorClass(i)}");

                var portrait = new VisualElement { name = "menu-portrait" };
                portrait.AddToClassList("menu-portrait");
                var texture = Resources.Load<Texture2D>($"UI/Portraits/{fighter}");
                if (texture != null)
                    portrait.style.backgroundImage = new StyleBackground(texture);

                var copy = new VisualElement { name = "menu-roster-copy" };
                copy.AddToClassList("menu-roster-copy");
                var name = new Label(DisplayName(fighter)) { name = "menu-fighter-name" };
                name.AddToClassList("menu-fighter-name");
                var role = new Label(MenuRoster.Description(fighter)) { name = "menu-fighter-role" };
                role.AddToClassList("menu-fighter-role");
                copy.Add(name);
                copy.Add(role);

                card.Add(portrait);
                card.Add(copy);
                rosterPanel.Add(card);
            }
        }

        private static string DisplayName(CharacterClass fighter) =>
            fighter == CharacterClass.FightGuy ? "FIGHTGUY" : fighter.ToString().ToUpperInvariant();

        private static string ColorClass(int index) => (index % 4) switch
        {
            0 => "orange",
            1 => "blue",
            2 => "green",
            _ => "yellow"
        };
    }
}
