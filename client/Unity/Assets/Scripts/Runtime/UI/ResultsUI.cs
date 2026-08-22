#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using SlopArena.Shared;
using SlopArena.Client;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Post-fight broadcast presentation. It consumes only the immutable result
    /// snapshot prepared by ClientSession; fighter GameObjects are never queried.
    /// </summary>
    public sealed class ResultsUI : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument = null!;

        private VisualElement _root = null!;
        private VisualElement _winnerCard = null!;
        private Label _standingsLabel = null!;

        private VisualElement _standings = null!;
        private Label _stage = null!;
        private Label _metadata = null!;
        private Label _headline = null!;
        private Button _returnButton = null!;
        private TextField _chatInput = null!;
        private Button _chatSend = null!;
        private VisualElement _chatFeed = null!;

        private void OnEnable()
        {
            _root = _uiDocument.rootVisualElement;
            _winnerCard = _root.Q<VisualElement>("results-winner");
            _standings = _root.Q<VisualElement>("results-standings");
            _stage = _root.Q<Label>("results-stage");
            _metadata = _root.Q<Label>("results-metadata");
            _standingsLabel = _root.Q<Label>("results-standings-label");

            _headline = _root.Q<Label>("results-headline");
            _returnButton = _root.Q<Button>("btn-return-lobby");
            _chatFeed = _root.Q<VisualElement>("chat-feed");
            _chatInput = _root.Q<TextField>("chat-input");
            _chatSend = _root.Q<Button>("chat-send");

            _returnButton.text = MatchConfig.Mode == GameMode.Solo
                ? "BACK TO MENU"
                : "RETURN TO LOBBY";
            _returnButton.clicked += ReturnFromResults;
            _chatSend.clicked += SendChatMessage;
            _chatInput.RegisterCallback<KeyDownEvent>(OnChatKeyDown);
            RenderResults();
        }

        private void OnDisable()
        {
            if (_returnButton != null)
                _returnButton.clicked -= ReturnFromResults;
            if (_chatSend != null)
                _chatSend.clicked -= SendChatMessage;
            if (_chatInput != null)
                _chatInput.UnregisterCallback<KeyDownEvent>(OnChatKeyDown);
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
            _standings.Clear();
            if (_standingsLabel != null)
                _standings.Add(_standingsLabel);

            var results = ClientSession.CurrentMatchResults;
            if (results == null || results.Entries.Count == 0)
            {
                _headline.text = "FIGHT OVER";
                _stage.text = "MATCH COMPLETE // UNKNOWN STAGE";
                _metadata.text = "RESULT SNAPSHOT UNAVAILABLE";
                return;
            }

            _root.EnableInClassList("results-count-2", results.PlayerCount == 2);
            _root.EnableInClassList("results-count-3", results.PlayerCount == 3);
            _root.EnableInClassList("results-count-4", results.PlayerCount >= 4);

            string stage = string.IsNullOrEmpty(results.StageName)
                ? MatchConfig.ArenaName
                : results.StageName;
            _stage.text = $"MATCH COMPLETE // {stage.ToUpperInvariant()}";
            _metadata.text = $"{FormatDuration(results.DurationTicks)} // {results.PlayerCount} PLAYERS";
            _headline.text = results.SharedVictory ? "SHARED VICTORY" : "FIGHT OVER";

            var ordered = new List<ClientSession.ResultEntry>(results.Entries);
            ordered.Sort((a, b) =>
            {
                int byPlacement = a.Placement.CompareTo(b.Placement);
                return byPlacement != 0 ? byPlacement : a.EntityId.CompareTo(b.EntityId);
            });

            var hero = ordered[0];
            BuildWinnerCard(hero, results.SharedVictory);
            for (int i = 1; i < ordered.Count; i++)
                _standings.Add(BuildStandingRow(ordered[i]));

            _root.schedule.Execute(() => _root.AddToClassList("results-ready")).StartingIn(40);
        }

        private void BuildWinnerCard(ClientSession.ResultEntry entry, bool sharedVictory)
        {
            var metadata = ResolveMetadata(entry);
            var card = new VisualElement();
            card.AddToClassList("results-winner-card");
            card.style.borderLeftColor = AccentForPlacement(entry.Placement);

            var placement = new Label(entry.Placement.ToString("00"));
            placement.AddToClassList("results-winner-placement");
            card.Add(placement);

            var label = new Label(sharedVictory ? "TOP FINISHER" : "WINNER");
            label.AddToClassList("results-winner-label");
            card.Add(label);

            var portrait = BuildPortrait(metadata.Class);
            portrait.AddToClassList("results-winner-portrait");
            card.Add(portrait);

            var copy = new VisualElement();
            copy.AddToClassList("results-winner-copy");
            var playerName = new Label(metadata.PlayerName);
            playerName.AddToClassList("results-winner-player");
            copy.Add(playerName);
            var fighterName = new Label(metadata.FighterName);
            fighterName.AddToClassList("results-winner-fighter");
            copy.Add(fighterName);
            copy.Add(BuildStats(entry, "results-winner-stats"));
            card.Add(copy);

            _winnerCard.Add(card);
        }

        private VisualElement BuildStandingRow(ClientSession.ResultEntry entry)
        {
            var metadata = ResolveMetadata(entry);
            var row = new VisualElement();
            row.AddToClassList("results-player-row");
            row.style.borderLeftColor = AccentForPlacement(entry.Placement);

            var placement = new Label(entry.Placement.ToString("00"));
            placement.AddToClassList("results-row-placement");
            row.Add(placement);

            var portrait = BuildPortrait(metadata.Class);
            portrait.AddToClassList("results-row-portrait");
            row.Add(portrait);

            var identity = new VisualElement();
            identity.AddToClassList("results-row-identity");
            var playerName = new Label(metadata.PlayerName);
            playerName.AddToClassList("results-row-player");
            identity.Add(playerName);
            var fighterName = new Label(metadata.FighterName);
            fighterName.AddToClassList("results-row-fighter");
            identity.Add(fighterName);
            row.Add(identity);

            row.Add(BuildStats(entry, "results-row-stats"));
            return row;
        }

        private static VisualElement BuildStats(ClientSession.ResultEntry entry, string className)
        {
            var stats = new VisualElement();
            stats.AddToClassList(className);
            AddStat(stats, "KOs", entry.KOs);
            AddStat(stats, "FALLS", entry.Falls);
            return stats;
        }

        private static void AddStat(VisualElement parent, string label, int value)
        {
            var stat = new VisualElement();
            stat.AddToClassList("results-stat");
            var valueLabel = new Label(value.ToString());
            valueLabel.AddToClassList("results-stat-value");
            stat.Add(valueLabel);
            var nameLabel = new Label(label);
            nameLabel.AddToClassList("results-stat-label");
            stat.Add(nameLabel);
            parent.Add(stat);
        }

        private static VisualElement BuildPortrait(CharacterClass character)
        {
            var portrait = new VisualElement();
            portrait.AddToClassList("results-portrait");
            var texture = Resources.Load<Texture2D>($"UI/Portraits/{character}");
            if (texture != null)
                portrait.style.backgroundImage = new StyleBackground(texture);
            else
                portrait.AddToClassList("results-portrait-missing");
            return portrait;
        }

        private static PlayerMetadata ResolveMetadata(ClientSession.ResultEntry entry)
        {
            string playerName = entry.Name;
            CharacterClass character = ParseClass(entry.ClassName);

            if (ClientSession.MatchRoster != null)
            {
                foreach (var roster in ClientSession.MatchRoster)
                {
                    if (roster.EntityId != (long)entry.EntityId) continue;
                    if (string.IsNullOrEmpty(playerName))
                        playerName = roster.Name;
                    character = ParseClass(roster.CharacterSelection, character);
                    break;
                }
            }

            if (string.IsNullOrEmpty(playerName))
                playerName = $"P{entry.EntityId}";

            var definition = CharacterRegistry.Get(character);
            string fighterName = string.IsNullOrEmpty(definition.DisplayName)
                ? character.ToString()
                : definition.DisplayName;
            return new PlayerMetadata(
                playerName.ToUpperInvariant(),
                fighterName.ToUpperInvariant(),
                character);
        }
        private static CharacterClass ParseClass(string? value, CharacterClass fallback = CharacterClass.None)
        {
            if (!string.IsNullOrEmpty(value)
                && Enum.TryParse(value, true, out CharacterClass parsed)
                && parsed != CharacterClass.None)
                return parsed;
            if (fallback != CharacterClass.None)
                return fallback;
            foreach (var definition in CharacterRegistry.All)
                if (definition.Class != CharacterClass.None)
                    return definition.Class;
            return CharacterClass.None;
        }

        private void SendChatMessage()
        {
            string text = _chatInput.value.Trim();
            if (text.Length == 0) return;

            string sender = string.IsNullOrEmpty(ClientSession.Username)
                ? "YOU"
                : ClientSession.Username.ToUpperInvariant();
            var message = new Label($"{sender}: {text}");
            message.AddToClassList("global-chat__message");
            message.AddToClassList("global-chat__message--accent");
            _chatFeed.Add(message);
            _chatInput.value = string.Empty;
        }

        private void OnChatKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
                return;
            SendChatMessage();
            evt.StopPropagation();
        }

        private static string FormatDuration(uint ticks)
        {
            if (ticks == 0) return "DURATION N/A";
            int totalSeconds = Mathf.Max(1, Mathf.RoundToInt(ticks / 60f));
            return $"DURATION {totalSeconds / 60:00}:{totalSeconds % 60:00}";
        }

        private static Color AccentForPlacement(int placement)
        {
            return placement switch
            {
                1 => new Color(1f, 0.78f, 0.13f),
                2 => new Color(0.91f, 0.36f, 0.16f),
                3 => new Color(0.31f, 0.71f, 0.98f),
                _ => new Color(0.29f, 0.86f, 0.53f),
            };
        }

        private readonly struct PlayerMetadata
        {
            public PlayerMetadata(string playerName, string fighterName, CharacterClass @class)
            {
                PlayerName = playerName;
                FighterName = fighterName;
                Class = @class;
            }

            public string PlayerName { get; }
            public string FighterName { get; }
            public CharacterClass Class { get; }
        }
    }
}
