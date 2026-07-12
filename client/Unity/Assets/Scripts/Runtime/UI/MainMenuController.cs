using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using SlopArena.Client.UI;

namespace SlopArena.Client.UI
{
    public class MainMenuController : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;

        private void OnEnable()
        {
            MatchConfig.Reset();
            var root = _uiDocument.rootVisualElement;

            var submenu        = root.Q<VisualElement>("submenu");
            var btnTraining    = root.Q<Button>("btn-training");
            var btnMultiplayer = root.Q<Button>("btn-multiplayer");
            var btnHost        = root.Q<Button>("btn-host");
            var btnJoin        = root.Q<Button>("btn-join");
            var ipField        = root.Q<TextField>("ip-field");

            btnTraining.clicked += () =>
            {
                MatchConfig.Mode   = GameMode.Training;
                MatchConfig.IsHost = true;
                SceneManager.LoadScene("CharSelect");
            };

            btnMultiplayer.clicked += () =>
            {
                bool visible = submenu.style.display == DisplayStyle.Flex;
                submenu.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
            };

            btnHost.clicked += () =>
            {
                MatchConfig.Mode     = GameMode.PvP;
                MatchConfig.IsHost   = true;
                MatchConfig.ServerIP = "127.0.0.1";
                SceneManager.LoadScene("Lobby");
            };

            btnJoin.clicked += () =>
            {
                string ip = ipField.value.Trim();
                if (string.IsNullOrEmpty(ip)) ip = "127.0.0.1";
                MatchConfig.Mode     = GameMode.PvP;
                MatchConfig.IsHost   = false;
                MatchConfig.ServerIP = ip;
                SceneManager.LoadScene("Lobby");
            };
        }
    }
}
