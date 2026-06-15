using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using SlopArena.Shared;

namespace SlopArena.Server
{
	/// <summary>
	/// One match instance — 2 players, 60Hz UDP simulation on a dedicated port.
	/// Runs on its own thread. Uses ServerSimulation for full hit detection,
	/// hurtbox tracking, and void death.
	/// </summary>
	public class MatchInstance
	{
		private const ulong P1EntityId = 1;
		private const ulong P2EntityId = 2;

		private readonly int _port;
		private readonly string _matchId;
		private readonly string _arenaName;

		private UdpClient? _udpServer;
		private IPEndPoint? _player1EndPoint;
		private IPEndPoint? _player2EndPoint;
		private bool _running = true;

		private ArenaDefinition _arena;
		private ServerSimulation _sim = null!;

		// Input buffering (per player, keyed by tick)
		private readonly List<(uint tick, InputState input)> _p1Queue = new();
		private readonly List<(uint tick, InputState input)> _p2Queue = new();
		private uint _serverTick;

		// Connection timeout
		private DateTime _lastP1Packet = DateTime.UtcNow;
		private DateTime _lastP2Packet = DateTime.UtcNow;
		private const double TimeoutSeconds = 5.0;

		// Match lifecycle
		private MatchState _matchState = MatchState.Waiting;
		private ushort _countdownTicks;
		private const ushort CountdownDuration = 180; // 3 seconds at 60Hz
		private const byte MaxDeaths = 3;
		private ulong _winnerEntityId;
		private ushort _postMatchTicks;
		private const ushort PostMatchDuration = 180; // 3 seconds before cleanup

		private Thread? _thread;
		private readonly Action<int> _onMatchEnd;

		public MatchInstance(int port, string matchId, string arenaName, Action<int> onMatchEnd)
		{
			_port = port;
			_matchId = matchId;
			_arenaName = arenaName;
			_onMatchEnd = onMatchEnd;
		}

		public void Start()
		{
			_thread = new Thread(Run) { IsBackground = true, Name = $"Match-{_matchId}" };
			_thread.Start();
		}

		public void Stop()
		{
			_running = false;
			try { _udpServer?.Close(); } catch { }
		}

		public bool IsRunning => _running;

		private void Run()
		{
			Console.WriteLine($"[Match:{_matchId}] Starting on port {_port}");

			_arena = ArenaRegistry.Get(_arenaName);
			var p1Def = CharacterRegistry.Get(CharacterClass.Manki);
			var p2Def = CharacterRegistry.Get(CharacterClass.Manki);

			// Load baked skeleton data
			var p1Baked = LoadBakedData(p1Def);
			var p2Baked = LoadBakedData(p2Def);

			// Create simulation and register both entities
			_sim = new ServerSimulation(_arena);
			_sim.RegisterEntity(P1EntityId, p1Def, CreateInitialState(p1Def, 0), p1Baked);
			_sim.RegisterEntity(P2EntityId, p2Def, CreateInitialState(p2Def, 1), p2Baked);

			try
			{
				_udpServer = new UdpClient(_port);
				_udpServer.Client.Blocking = false;
				Console.WriteLine($"[Match:{_matchId}] Listening on UDP {_port}, waiting for 2 players...");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[Match:{_matchId}] Error binding port {_port}: {ex.Message}");
				_onMatchEnd(_port);
				return;
			}

			var stopwatch = Stopwatch.StartNew();
			double nextTickTime = 0;
			const double tickDurationMs = 1000.0 / 60.0;

			while (_running)
			{
				double currentTime = stopwatch.Elapsed.TotalMilliseconds;
				if (currentTime >= nextTickTime)
				{
					ReceiveInputs();
					if (_player1EndPoint != null && _player2EndPoint != null)
					{
						// Check for disconnected players
						var now = DateTime.UtcNow;
						bool p1Timeout = (now - _lastP1Packet).TotalSeconds > TimeoutSeconds;
						bool p2Timeout = (now - _lastP2Packet).TotalSeconds > TimeoutSeconds;

						if (p1Timeout || p2Timeout)
						{
							Console.WriteLine($"[Match:{_matchId}] Player {(p1Timeout ? "1" : "2")} timed out — stopping match.");
							_running = false;
							continue;
						}

						Tick();
					}
					nextTickTime += tickDurationMs;

					if (currentTime > nextTickTime + tickDurationMs * 10)
						nextTickTime = currentTime;
				}
				else
				{
					int sleepTime = (int)(nextTickTime - currentTime) - 1;
					if (sleepTime > 0)
						Thread.Sleep(sleepTime);
					else
						Thread.Yield();
				}
			}

			try { _udpServer?.Close(); } catch { }
			Console.WriteLine($"[Match:{_matchId}] Stopped.");
			_onMatchEnd(_port);
		}

		private static BakedAnimationData? LoadBakedData(CharacterDefinition def)
		{
			if (string.IsNullOrEmpty(def.BakedDataPath)) return null;

			try
			{
				string sysPath = def.BakedDataPath.Replace("res://", "");
				var binData = System.IO.File.ReadAllBytes(sysPath);
				var baked = BakedAnimationData.LoadFromBin(binData);
				Console.WriteLine($"[Server] Loaded baked data: {sysPath} ({binData.Length} bytes, {baked.Animations.Length} anims)");
				return baked;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[Server] Failed to load baked data: {ex.Message} — using fallback capsules");
				return null;
			}
		}

		private CharacterState CreateInitialState(CharacterDefinition def, int spawnIndex)
		{
			var spawn = _arena.SpawnPoints.Length > spawnIndex
				? _arena.SpawnPoints[spawnIndex]
				: new SpawnPoint { X = 40f, Y = 0.5f, Z = 40f, Yaw = 0f };

			return new CharacterState
			{
				PX = spawn.X,
				PY = spawn.Y,
				PZ = spawn.Z,
				FacingYaw = spawn.Yaw,
				State = ActionState.Idle,
				IsGrounded = true,
				JumpsLeft = def.Movement.MaxJumps,
				AirDodgesLeft = 1,
				DamagePercent = 0,
			};
		}

		private void ReceiveInputs()
		{
			if (_udpServer == null) return;

			while (true)
			{
				try
				{
					if (_udpServer.Available == 0) break;

					var remoteEP = new IPEndPoint(IPAddress.Any, 0);
					byte[] data = _udpServer.Receive(ref remoteEP);

					// Client packet format: entityId(8) + tick(4) + InputState(10) = 22 bytes
					if (data.Length < 8 + 4 + InputState.Size) continue;

					ulong entityId = BitConverter.ToUInt64(data, 0);
					uint clientTick = BitConverter.ToUInt32(data, 8);

					// Map entity ID to player slot
					bool isP1 = entityId == P1EntityId;
					bool isP2 = entityId == P2EntityId;

					// New player connecting — register their endpoint
					if (!isP1 && !isP2)
						continue;

					if (isP1 && _player1EndPoint == null)
					{
						_player1EndPoint = remoteEP;
						_lastP1Packet = DateTime.UtcNow;
						Console.WriteLine($"[Match:{_matchId}] Player 1 (entity {entityId}) connected: {remoteEP}");
						continue;
					}
					if (isP2 && _player2EndPoint == null)
					{
						_player2EndPoint = remoteEP;
						_lastP2Packet = DateTime.UtcNow;
						_matchState = MatchState.Countdown;
						_countdownTicks = CountdownDuration;
						Console.WriteLine($"[Match:{_matchId}] Player 2 (entity {entityId}) connected: {remoteEP} — countdown started!");
						continue;
					}

					// Update last packet time for timeout detection
					if (isP1) _lastP1Packet = DateTime.UtcNow;
					if (isP2) _lastP2Packet = DateTime.UtcNow;

					if (clientTick <= _serverTick) continue;

					// Parse input and wrap in a minimal packet for the queue
					var inputState = InputState.Deserialize(data.AsSpan(12));
					var queue = isP1 ? _p1Queue : _p2Queue;

					// Prevent duplicates
					bool exists = false;
					for (int i = 0; i < queue.Count; i++)
					{
						if (queue[i].tick == clientTick)
						{ exists = true; break; }
					}
					if (!exists)
						queue.Add((clientTick, inputState));
				}
				catch (SocketException ex)
				{
					if (ex.SocketErrorCode != SocketError.WouldBlock)
						Console.WriteLine($"[Match:{_matchId}] Socket error: {ex.Message}");
					break;
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[Match:{_matchId}] Receive error: {ex.Message}");
					break;
				}
			}
		}

		private void Tick()
		{
			// ── Countdown ──
			if (_matchState == MatchState.Countdown)
			{
				if (--_countdownTicks == 0)
				{
					_matchState = MatchState.Playing;
					Console.WriteLine($"[Match:{_matchId}] GO!");
				}
				SendState();
				return;
			}

			// ── Ended ──
			if (_matchState == MatchState.Ended)
			{
				if (--_postMatchTicks == 0)
				{
					Console.WriteLine($"[Match:{_matchId}] Post-match complete — stopping.");
					_running = false;
				}
				return;
			}

			var p1Input = FlushQueue(_p1Queue, out _);
			var p2Input = FlushQueue(_p2Queue, out _);

			var inputs = new Dictionary<ulong, InputState>();

			if (p1Input.HasValue)
			{
				_serverTick = Math.Max(_serverTick, p1Input.Value.tick);
				inputs[P1EntityId] = p1Input.Value.input;
			}

			if (p2Input.HasValue)
			{
				_serverTick = Math.Max(_serverTick, p2Input.Value.tick);
				inputs[P2EntityId] = p2Input.Value.input;
			}

			// Run authoritative simulation (movement + hit detection + hurtboxes + void death)
			if (inputs.Count > 0)
			{
				_sim.Tick(inputs);

				// Check for match end (first to MaxDeaths loses)
				var p1State = _sim.GetState(P1EntityId);
				var p2State = _sim.GetState(P2EntityId);

				if (p1State.Deaths >= MaxDeaths)
				{
					_matchState = MatchState.Ended;
					_winnerEntityId = P2EntityId;
					_postMatchTicks = PostMatchDuration;
					Console.WriteLine($"[Match:{_matchId}] Player 1 eliminated! Player 2 wins! ({p1State.Deaths}-{p2State.Deaths})");
				}
				else if (p2State.Deaths >= MaxDeaths)
				{
					_matchState = MatchState.Ended;
					_winnerEntityId = P1EntityId;
					_postMatchTicks = PostMatchDuration;
					Console.WriteLine($"[Match:{_matchId}] Player 2 eliminated! Player 1 wins! ({p1State.Deaths}-{p2State.Deaths})");
				}

				SendState();
			}
		}

		private void SendState()
		{
			if (_udpServer == null) return;

			// Packet format (matching NetworkClient expectations):
			//   entityId(8) + tick(4) + CharacterStatePacket(39)
			const int envelopeSize = 8 + 4 + CharacterStatePacket.Size; // 51 bytes

			var p1Packet = CharacterStatePacket.FromState(_sim.GetState(P1EntityId), _serverTick);
			p1Packet.MatchState = _matchState;
			var p2Packet = CharacterStatePacket.FromState(_sim.GetState(P2EntityId), _serverTick);
			p2Packet.MatchState = _matchState;

			Span<byte> p1Buf = stackalloc byte[envelopeSize];
			BitConverter.TryWriteBytes(p1Buf.Slice(0, 8), P1EntityId);
			BitConverter.TryWriteBytes(p1Buf.Slice(8, 4), _serverTick);
			p1Packet.Serialize(p1Buf.Slice(12));

			Span<byte> p2Buf = stackalloc byte[envelopeSize];
			BitConverter.TryWriteBytes(p2Buf.Slice(0, 8), P2EntityId);
			BitConverter.TryWriteBytes(p2Buf.Slice(8, 4), _serverTick);
			p2Packet.Serialize(p2Buf.Slice(12));

			try
			{
				// Send both states to both players (T1.6)
				if (_player1EndPoint != null)
				{
					_udpServer.Send(p1Buf.ToArray(), envelopeSize, _player1EndPoint);
					_udpServer.Send(p2Buf.ToArray(), envelopeSize, _player1EndPoint);
				}
				if (_player2EndPoint != null)
				{
					_udpServer.Send(p1Buf.ToArray(), envelopeSize, _player2EndPoint);
					_udpServer.Send(p2Buf.ToArray(), envelopeSize, _player2EndPoint);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[Match:{_matchId}] Send error: {ex.Message}");
			}
		}

		/// <summary>
		/// Flush the input queue: take the last valid packet, discard the rest.
		/// Returns the packet to process, or null if queue was empty.
		/// </summary>
		private static (uint tick, InputState input)? FlushQueue(List<(uint tick, InputState input)> queue, out int count)
		{
			count = queue.Count;
			if (count == 0) return null;

			// Use the LAST packet (most recent input for this tick's batch)
			var last = queue[count - 1];
			queue.Clear();
			return last;
		}

	}
}
