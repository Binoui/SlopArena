#nullable enable
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using SlopArena.Client;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Results screen shown after a PvP match ends (issue #40). Reads the final
    /// standings stashed by <c>PvPMatch.BuildAndShowResults</c> and waits for an
    /// explicit return-to-lobby action.
    /// </summary>
    public class ResultsUI : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;

        private VisualElement _standings;
        private Label _lblWinner;
        private Button _returnButton;

        private void OnEnable()
        {
            var root = _uiDocument.rootVisualElement;
            _lblWinner = root.Q<Label>("lbl-winner");
            _standings = root.Q<VisualElement>("standings-list");
            _returnButton = root.Q<Button>("btn-return-lobby");
            _returnButton.text = MatchConfig.Mode == GameMode.Solo
                ? "BACK TO MENU"
                : "RETURN TO LOBBY";
            _returnButton.clicked += ReturnFromResults;
            RenderResults();
        }

        private void Update()
        {
            ClientSession.ActiveLobby?.Pump();
        }

        private static void ReturnFromResults()
        {
            SceneManager.LoadScene(
                MatchConfig.Mode == GameMode.Solo ? "MainMenu" : "LobbyRoom");
        }



        private void RenderResults()
        {
            var results = ClientSession.CurrentMatchResults;
            if (results == null)
            {
                _lblWinner.text = "MATCH OVER";
                return;
            }

            if (results.SharedVictory)
            {
                _lblWinner.text = "DOUBLE K.O.!";
            }
            else
            {
                var winner = results.Entries.Find(e => e.IsWinner);
                _lblWinner.text = winner != null
                    ? $"{winner.Name} WINS!"
                    : "WINNER: P?";
            }

            for (int i = 0; i < results.Entries.Count; i++)
            {
                var e = results.Entries[i];
                var row = new Label($"{i + 1}. {e.Name} — {e.ClassName} — {e.StocksRemaining} STOCKS");
                row.AddToClassList("player-slot");
                row.AddToClassList("slot-name");
                _standings.Add(row);
            }
        }
    }
}
