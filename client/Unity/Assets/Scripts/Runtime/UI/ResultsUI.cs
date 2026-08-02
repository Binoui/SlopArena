#nullable enable
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using SlopArena.Client;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Results screen shown after a PvP match ends (issue #40). Reads the final
    /// standings stashed by <c>PvPMatch.BuildAndShowResults</c>, renders winner +
    /// standings, then auto-returns to the lobby room after a 6s countdown. The
    /// SignalR lobby connection stayed alive through the match (decision 1), so
    /// landing back in LobbyRoom re-joins the same lobby — host role intact.
    /// </summary>
    public class ResultsUI : MonoBehaviour
    {
        private const float ReturnCountdownSeconds = 6f;

        [SerializeField] private UIDocument _uiDocument;

        private VisualElement _standings;
        private Label _lblWinner;
        private Label _lblCountdown;
        private float _remaining = ReturnCountdownSeconds;

        private void OnEnable()
        {
            var root = _uiDocument.rootVisualElement;
            _lblWinner = root.Q<Label>("lbl-winner");
            _standings = root.Q<VisualElement>("standings-list");
            _lblCountdown = root.Q<Label>("lbl-countdown");
            _remaining = ReturnCountdownSeconds;

            RenderResults();
            UpdateCountdownLabel();
        }

        private void Update()
        {
            // Drain queued lobby events while we wait — the connection is still
            // alive (decision 1) and LobbyRoomUI will pump after we load it too.
            ClientSession.ActiveLobby?.Pump();

            _remaining -= Time.deltaTime;
            UpdateCountdownLabel();
            if (_remaining <= 0f)
                SceneManager.LoadScene("LobbyRoom");
        }

        private void RenderResults()
        {
            var results = ClientSession.CurrentMatchResults;
            if (results == null)
            {
                // Defensive: unreachable in practice (PvPMatch always stashes
                // results before loading this scene).
                _lblWinner.text = "MATCH OVER";
                return;
            }

            if (results.SharedVictory)
            {
                _lblWinner.text = "SHARED VICTORY";
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

        private void UpdateCountdownLabel()
        {
            _lblCountdown.text = $"Returning to lobby in {Mathf.CeilToInt(Mathf.Max(0f, _remaining))}s";
        }
    }
}
