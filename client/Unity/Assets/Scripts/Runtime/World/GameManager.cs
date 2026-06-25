using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using SlopArena.Shared;
using SlopArena.Client.Entities;
using SlopArena.Client.Network;

namespace SlopArena.Client.World
{
    public class GameManager : MonoBehaviour
    {
        private const int RollbackFrames = 30;
        private const ulong PlayerEntityId = 1;
        private const ulong OpponentEntityId = 2;
        private const float RollbackThresholdSq = 0.25f;

        public static GameManager Instance { get; private set; }

        private readonly InputState[] _inputBuffer = new InputState[RollbackFrames];
        private readonly CharacterState[] _stateBuffer = new CharacterState[RollbackFrames];

        private uint _sendTick;
        private uint _lastConfirmedTick;
        private ServerSimulation _localSim;

        [Header("Offline Mode")]
        [SerializeField] private bool _offlineMode;
        [SerializeField] private PlayerRenderer _playerRenderer;
        [SerializeField] private PlayerRenderer _opponentRenderer;

        public NetworkClient Net { get; set; }
        public PlayerRenderer Player { get; set; }
        public PlayerRenderer Opponent { get; set; }

        private ArenaDefinition _arenaDef;
        private CharacterDefinition _charDef;
        private BakedAnimationData _bakedData;

        private readonly Dictionary<ulong, CharacterState> _serverConfirmedStates = new();

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (!_offlineMode) return;
            if (_playerRenderer == null || _opponentRenderer == null) return;

            Player = _playerRenderer;
            Opponent = _opponentRenderer;

            var arena = ArenaRegistry.Get("training");
            var charDef = CharacterRegistry.Get(CharacterClass.Manki);
            Initialize(null, Player, Opponent, arena, charDef);
        }

        public void Initialize(
            NetworkClient net,
            PlayerRenderer player,
            PlayerRenderer opponent,
            ArenaDefinition arenaDef,
            CharacterDefinition charDef,
            BakedAnimationData bakedData = null)
        {
            Net = net;
            Player = player;
            Opponent = opponent;
            _arenaDef = arenaDef;
            _charDef = charDef;
            _bakedData = bakedData;

            _sendTick = 0;
            _lastConfirmedTick = 0;
            _serverConfirmedStates.Clear();

            var spawn = _arenaDef.SpawnPoints[0];
            var playerState = new CharacterState
            {
                PX = spawn.X,
                PY = spawn.Y + 5f,
                PZ = spawn.Z,
                FacingYaw = spawn.Yaw,
                JumpsLeft = _charDef.Movement.MaxJumps,
            };

            _localSim = new ServerSimulation(_arenaDef);

            int playerSpawnIdx = 0;
            int oppSpawnIdx = _arenaDef.SpawnPoints.Length > 1 ? 1 : 0;

            var playerSpawn = _arenaDef.SpawnPoints[playerSpawnIdx];
            var oppSpawn = _arenaDef.SpawnPoints[oppSpawnIdx];

            _localSim.RegisterEntity(PlayerEntityId, _charDef, new CharacterState
            {
                PX = playerSpawn.X, PY = playerSpawn.Y + 5f, PZ = playerSpawn.Z,
                FacingYaw = playerSpawn.Yaw, JumpsLeft = _charDef.Movement.MaxJumps,
            }, _bakedData);

            _stateBuffer[0] = playerState;

            _localSim.RegisterEntity(OpponentEntityId, _charDef, new CharacterState
            {
                PX = oppSpawn.X, PY = oppSpawn.Y + 1f, PZ = oppSpawn.Z,
                FacingYaw = oppSpawn.Yaw, JumpsLeft = _charDef.Movement.MaxJumps,
            }, _bakedData);
        }
        private void FixedUpdate()
        {
            if (_localSim == null) return;

            InputState input = BuildInputState();

            _sendTick++;
            _inputBuffer[_sendTick % RollbackFrames] = input;

            var inputs = new Dictionary<ulong, InputState> { { PlayerEntityId, input } };
            _localSim.Tick(inputs);

            var predicted = _localSim.GetState(PlayerEntityId);
            _stateBuffer[_sendTick % RollbackFrames] = predicted;

            if (Net != null)
                Net.SendInput(input, _sendTick);


            if (Player != null)
                Player.ApplyServerState(predicted);

            var oppState = _localSim.GetState(OpponentEntityId);
            if (Opponent != null)
                Opponent.ApplyServerState(oppState);
        }

        private void Update()
        {
            if (Net == null || _localSim == null) return;

            var serverStates = Net.ReceiveStates();
            foreach (var kvp in serverStates)
                _serverConfirmedStates[kvp.Key] = kvp.Value;

            if (serverStates.TryGetValue(PlayerEntityId, out var serverState))
            {
                uint serverTick = Net.LastServerTick;
                if (serverTick > _lastConfirmedTick)
                {
                    _lastConfirmedTick = serverTick;
                    int idx = (int)(serverTick % RollbackFrames);
                    var predicted = _stateBuffer[idx];

                    float dx = predicted.PX - serverState.PX;
                    float dy = predicted.PY - serverState.PY;
                    float dz = predicted.PZ - serverState.PZ;

                    if (dx * dx + dy * dy + dz * dz > RollbackThresholdSq)
                    {
                        var oldOppState = _localSim.GetState(OpponentEntityId);
                        _localSim = new ServerSimulation(_arenaDef);
                        _localSim.RegisterEntity(PlayerEntityId, _charDef, serverState, _bakedData);

                        var oppState = _serverConfirmedStates.TryGetValue(OpponentEntityId, out var os)
                            ? os : oldOppState;
                        _localSim.RegisterEntity(OpponentEntityId, _charDef, oppState, _bakedData);

                        uint currentTick = _sendTick;
                        for (uint t = serverTick + 1; t <= currentTick; t++)
                        {
                            var pastInput = _inputBuffer[t % RollbackFrames];
                            _localSim.Tick(new Dictionary<ulong, InputState> { { PlayerEntityId, pastInput } });
                        }

                        var corrected = _localSim.GetState(PlayerEntityId);
                        _stateBuffer[currentTick % RollbackFrames] = corrected;
                        if (Player != null) Player.ApplyServerState(corrected);
                    }
                }
            }

            if (serverStates.TryGetValue(OpponentEntityId, out var oppServer))
            {
                if (Opponent != null) Opponent.ApplyServerState(oppServer);
            }
        }

        private InputState BuildInputState()
        {
            InputState input = new();

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            bool usingInputSystem = keyboard != null;

            if (usingInputSystem)
            {
                bool w = keyboard.wKey.isPressed;
                bool s = keyboard.sKey.isPressed;
                bool a = keyboard.aKey.isPressed;
                bool d = keyboard.dKey.isPressed;
                input.Up = w; input.Down = s; input.Left = a; input.Right = d;
            }
            else
            {
                input.Up    = UnityEngine.Input.GetKey(KeyCode.W) || UnityEngine.Input.GetKey(KeyCode.UpArrow);
                input.Down  = UnityEngine.Input.GetKey(KeyCode.S) || UnityEngine.Input.GetKey(KeyCode.DownArrow);
                input.Left  = UnityEngine.Input.GetKey(KeyCode.A) || UnityEngine.Input.GetKey(KeyCode.LeftArrow);
                input.Right = UnityEngine.Input.GetKey(KeyCode.D) || UnityEngine.Input.GetKey(KeyCode.RightArrow);
            }

            float mx = 0f, my = 0f;
            if (input.Right) mx += 1f;
            if (input.Left)  mx -= 1f;
            if (input.Up)    my += 1f;
            if (input.Down)  my -= 1f;
            float mag = Mathf.Sqrt(mx * mx + my * my);
            if (mag > 1f) { mx /= mag; my /= mag; }
            input.MoveX = mx;
            input.MoveY = my;

            if (UnityEngine.Camera.main != null)
            {
                float camYaw = UnityEngine.Camera.main.transform.eulerAngles.y;
                input.FacingYaw = (short)(camYaw * 100f);
            }

            if (usingInputSystem)
            {
                input.Jump   = keyboard.spaceKey.isPressed;
                input.Dash   = keyboard.leftShiftKey.isPressed;
                input.Crouch = keyboard.leftCtrlKey.isPressed;

                if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                    input.ActiveSlot = 1;
                else if (mouse != null && mouse.rightButton.isPressed)
                { input.ActiveSlot = 2; input.IsAiming = true; }
                else if (keyboard.qKey.wasPressedThisFrame) input.ActiveSlot = 3;
                else if (keyboard.eKey.wasPressedThisFrame) input.ActiveSlot = 4;
                else if (keyboard.rKey.wasPressedThisFrame) input.ActiveSlot = 5;
                else if (keyboard.fKey.wasPressedThisFrame) input.ActiveSlot = 6;
            }
            else
            {
                input.Jump   = UnityEngine.Input.GetKey(KeyCode.Space);
                input.Dash   = UnityEngine.Input.GetKey(KeyCode.LeftShift);
                input.Crouch = UnityEngine.Input.GetKey(KeyCode.LeftControl);

                if (UnityEngine.Input.GetMouseButtonDown(0)) input.ActiveSlot = 1;
                else if (UnityEngine.Input.GetMouseButton(1))
                { input.ActiveSlot = 2; input.IsAiming = true; }
                else if (UnityEngine.Input.GetKeyDown(KeyCode.Q)) input.ActiveSlot = 3;
                else if (UnityEngine.Input.GetKeyDown(KeyCode.E)) input.ActiveSlot = 4;
                else if (UnityEngine.Input.GetKeyDown(KeyCode.R)) input.ActiveSlot = 5;
                else if (UnityEngine.Input.GetKeyDown(KeyCode.F)) input.ActiveSlot = 6;
            }

            if (UnityEngine.Camera.main != null)
            {
                Vector3 mousePos = usingInputSystem && mouse != null
                    ? mouse.position.ReadValue() : UnityEngine.Input.mousePosition;
                Vector3 playerPos = Player != null ? Player.transform.position : Vector3.zero;
                var plane = new Plane(Vector3.up, playerPos.y);
                var ray = UnityEngine.Camera.main.ScreenPointToRay(mousePos);
                if (plane.Raycast(ray, out float enter))
                {
                    Vector3 worldPoint = ray.GetPoint(enter);
                    Vector3 dir = worldPoint - playerPos;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.001f)
                    {
                        float yawDeg = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                        input.AimYaw = (short)(yawDeg * 100f);
                        input.AimDistance = (ushort)Mathf.Clamp(dir.magnitude * 100f, 0f, 6500f);
                    }
                }
            }

            return input;
        }

        public ServerSimulation GetLocalSim() => _localSim;
        public uint GetSendTick() => _sendTick;
        public InputState[] GetInputBuffer() => _inputBuffer;
        public CharacterState[] GetStateBuffer() => _stateBuffer;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
