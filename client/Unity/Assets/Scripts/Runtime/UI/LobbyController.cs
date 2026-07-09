using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using SlopArena.Client.Network;

namespace SlopArena.Client.UI
{
    public class LobbyController : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private NetworkClient _networkClient;

        private Label _p2Status;
        private Button _btnStart;
        private Label _lblWaiting;

        private void OnEnable()
        {
            var root = _uiDocument.rootVisualElement;
            _p2Status   = root.Q<Label>("p2-status");
            _btnStart   = root.Q<Button>("btn-start");
            _lblWaiting = root.Q<Label>("lbl-waiting");
            var btnBack = root.Q<Button>("btn-back");

            // Training mode skips lobby entirely — this scene is PvP only.
            // But if somehow reached in Training, go straight to CharSelect.
            if (MatchConfig.Mode == GameMode.Training)
            {
                SceneManager.LoadScene("CharSelect");
                return;
            }

            if (MatchConfig.IsHost)
            {
                _btnStart.style.display   = DisplayStyle.Flex;
                _lblWaiting.style.display = DisplayStyle.None;
                _btnStart.SetEnabled(false); // enabled once P2 connects
                _btnStart.clicked += () => SceneManager.LoadScene("CharSelect");
            }
            else
            {
                _btnStart.style.display   = DisplayStyle.None;
                _lblWaiting.style.display = DisplayStyle.Flex;
            }

            btnBack.clicked += () => SceneManager.LoadScene("MainMenu");

            // Connect client to server
            if (_networkClient != null)
                _networkClient.Connect(MatchConfig.ServerIP, MatchConfig.ServerPort);

            StartCoroutine(PollConnection());
        }

        private IEnumerator PollConnection()
        {
            while (true)
            {
                yield return new WaitForSeconds(1f);
                if (_networkClient == null) yield break;

                bool connected = _networkClient.IsServerConnected;
                _p2Status.text = connected ? "Connected" : "Waiting...";
                _p2Status.RemoveFromClassList(connected ? "player-status--waiting" : "player-status--connected");
                _p2Status.AddToClassList(connected ? "player-status--connected" : "player-status--waiting");

                if (MatchConfig.IsHost)
                    _btnStart.SetEnabled(connected);

                // Client: once server is connected, show a Ready button to proceed
                if (!MatchConfig.IsHost && connected)
                {
                    _lblWaiting.text = "Server connected — press Ready when host starts";
                    var btnReady = new Button(() => SceneManager.LoadScene("CharSelect"))
                    {
                        text = "READY",
                        name = "btn-ready"
                    };
                    btnReady.AddToClassList("btn-primary");
                    _uiDocument.rootVisualElement.Q<VisualElement>("root").Add(btnReady);
                    yield break; // stop polling once shown
                }
            }
        }

        private void OnDisable()
        {
            StopAllCoroutines();
        }
    }
}
