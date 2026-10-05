using System.Collections.Generic;
using SlopArena.Shared;
using SlopArena.Shared.Rollback;
using SlopArena.Client.Network;

namespace SlopArena.Client.Simulation
{
    /// <summary>
    /// ISimulationBridge backed by RollbackSimulator (ADR-0011): the self entity predicts
    /// continuously (LocalTrack); opponents predict while in a Predictable ActionState
    /// (PredictedTrack) and render raw from the server otherwise (RawTrack). Replaces
    /// NetworkSimulationBridge for PvPMatch — Training keeps LocalSimulationBridge.
    /// </summary>
    public class RollbackSimulationBridge : ISimulationBridge
    {
        private readonly RollbackSimulator _core;
        private readonly NetworkClient _client;
        private readonly ulong _selfId;
        private readonly HashSet<ulong> _receivedInitialStates = new();
        private uint _tick;
        private bool _timelineSeeded;
        private InputState _bufferedInput;
        private InputState _lastAppliedInput;
        private bool _hasBufferedInput;
        private readonly NetplayClock _clock = new();
        private readonly List<NetworkClient.ControlFrame> _lastControlFrames = new();
        private readonly List<SpellResolver.HitResult> _lastTickHits = new();
        private readonly List<TimelinePresentationEvent> _lastTickPresentationEvents = new();
        public MatchResultPacket? LatestMatchResult { get; private set; }
        public MatchState AuthoritativeMatchState { get; private set; } = MatchState.Waiting;
        public IReadOnlyList<NetworkClient.ControlFrame> LastControlFrames => _lastControlFrames;
        public bool ClockSeeded => _clock.IsSeeded;


        public RollbackSimulationBridge(ArenaDefinition arena, NetworkClient client, ulong selfEntityId, IMatchRule? rule = null)
        {
            _core = new RollbackSimulator(arena, selfEntityId, rule);
            _client = client;
            _selfId = selfEntityId;
        }

        /// <summary>Debug overlay data (Task 10) — not part of ISimulationBridge.</summary>
        public int CorrectionCount => _core.CorrectionCount;
        public uint LastFrontierTicks => _core.LastFrontierTicks;

        public void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked = null)
            => _core.RegisterEntity(id, def, initialState, baked);
        public bool HasInitialState(ulong entityId) => _receivedInitialStates.Contains(entityId);
        public void ResetInitialStates() => _receivedInitialStates.Clear();

        public void PumpNetwork()
        {
            _client.ReceiveControlPackets(_lastControlFrames);
            foreach (var frame in _lastControlFrames)
            {
                var control = frame.Packet;
                if (control.EntityId != _selfId || control.Kind != NetplayControlKind.Clock)
                    continue;
                if (!_clock.Observe(control.Tick, control.StartTick, frame.ReceivedAtSeconds, frame.RoundTripMilliseconds) ||
                    _timelineSeeded || control.StartTick == 0)
                    continue;

                uint baselineTick = control.Tick < control.StartTick
                    ? control.StartTick - 1
                    : control.Tick;
                if (_core.SetTimeline(baselineTick))
                {
                    _tick = baselineTick;
                    _timelineSeeded = true;
                }
            }

            var packets = _client.ReceiveEntityPackets();
            foreach (var entityPacket in packets)
            {
                _receivedInitialStates.Add(entityPacket.EntityId);
                if ((byte)entityPacket.State.MatchState > (byte)AuthoritativeMatchState)
                    AuthoritativeMatchState = entityPacket.State.MatchState;
            }
            _core.IngestAuthoritativeBatch(packets);
            foreach (var result in _client.ReceiveMatchResults())
                LatestMatchResult = result;
            if (LatestMatchResult != null)
                AuthoritativeMatchState = MatchState.Ended;

            _core.IngestPresentationEvents(_client.ReceivePresentationEvents());
            _lastTickPresentationEvents.Clear();
            _lastTickPresentationEvents.AddRange(_core.DrainPresentationEvents());
        }

        public void Tick(Dictionary<ulong, InputState> inputs)
            => Tick(inputs, NetworkClient.MonotonicSeconds);

        public void Tick(Dictionary<ulong, InputState> inputs, double nowSeconds)
        {
            _lastTickHits.Clear();
            if (!_timelineSeeded || !_clock.IsSeeded ||
                AuthoritativeMatchState != MatchState.Playing ||
                _clock.EstimateTick(nowSeconds) < _clock.StartTick)
                return;

            InputState latest = inputs.TryGetValue(_selfId, out var value) ? value : default;
            if (_hasBufferedInput)
                latest = MergeBufferedEdges(_bufferedInput, latest);
            _bufferedInput = latest;
            _hasBufferedInput = true;

            uint desiredTick = _clock.DesiredLocalTick(nowSeconds);
            uint steps = _clock.CatchUpSteps(_tick, nowSeconds);
            for (uint i = 0; i < steps; i++)
            {
                uint nextTick = _tick + 1;
                bool atFrontier = nextTick == desiredTick;
                InputState input = atFrontier
                    ? _bufferedInput
                    : WithoutEdges(_lastAppliedInput);
                inputs[_selfId] = input;
                _core.Tick(inputs);
                _tick = nextTick;
                _lastTickHits.AddRange(_core.LastTickHits);

                if (atFrontier)
                {
                    _client.SendInput(input, nextTick);
                    _lastAppliedInput = WithoutEdges(input);
                    _bufferedInput = default;
                    _hasBufferedInput = false;
                }
            }
        }

        private static InputState MergeBufferedEdges(InputState previous, InputState latest)
        {
            latest.Jump |= previous.Jump;
            latest.DownPressed |= previous.DownPressed;
            latest.ShieldPressed |= previous.ShieldPressed;
            latest.GrabPressed |= previous.GrabPressed;
            latest.FaceToCamera |= previous.FaceToCamera;
            latest.ToggleLock |= previous.ToggleLock;
            latest.RetargetPressed |= previous.RetargetPressed;
            if (latest.ActiveSlot == 0) latest.ActiveSlot = previous.ActiveSlot;
            return latest;
        }

        private static InputState WithoutEdges(InputState input)
        {
            input.Jump = false;
            input.DownPressed = false;
            input.ShieldPressed = false;
            input.GrabPressed = false;
            input.FaceToCamera = false;
            input.ToggleLock = false;
            input.RetargetPressed = false;
            input.ActiveSlot = 0;
            return input;
        }
 

        public CharacterState GetState(ulong id) => _core.GetState(id);
        public Dictionary<ulong, CharacterState> GetAllStates() => _core.GetAllStates();
        public IReadOnlyList<TimelinePresentationEvent> LastTickPresentationEvents => _lastTickPresentationEvents;
        public SpellResolver? Resolver => _core.Resolver;
        public IReadOnlyList<SpellResolver.HitResult> LastTickHits => _lastTickHits;
    }
}
