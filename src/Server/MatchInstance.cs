using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using SlopArena.Shared;

namespace SlopArena.Server
{
	/// <summary>
	/// One match instance — 2-4 players, 60Hz UDP simulation on a dedicated port.
	/// Runs on its own thread. Uses ServerSimulation for full hit detection,
	/// hurtbox tracking, and void death.
	///
	/// Roster-driven (issue #35): the master server sends the locked-in character
	/// classes + entity IDs via <c>POST /match/start</c>; this instance spawns one
	/// entity per player with the correct <see cref="CharacterClass"/> instead of
	/// hardcoded Manki. Countdown starts once every rostered player has connected.
	/// </summary>
	public class MatchInstance
	{
		private readonly int _port;
		private readonly string _matchId;
		private readonly string _arenaName;
		private readonly List<PlayerSlot> _slots;

		private UdpClient? _udpServer;
		private bool _running = true;

		private ArenaDefinition _arena;
		private ServerSimulation _sim = null!;
		private uint _serverTick;

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

		/// <param name="roster">Ordered players (index 0 = host). Each carries an entity ID (1..N) and a character class.</param>
		public MatchInstance(int port, string matchId, string arenaName,
			IReadOnlyList<MatchPlayer> roster, Action<int> onMatchEnd)
		{
			_port = port;
			_matchId = matchId;
			_arenaName = arenaName;
			_onMatchEnd = onMatchEnd;

			_slots = new List<PlayerSlot>(roster.Count);
			foreach (var p in roster)
				_slots.Add(new PlayerSlot((ulong)p.EntityId, p.CharacterClass));
		}

		/// <summary>True while the match loop is active.</summary>
		public bool IsRunning => _running;

		/// <summary>Number of rostered players.</summary>
		public int PlayerCount => _slots.Count;

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

		private void Run()
		{
			Console.WriteLine($"[Match:{_matchId}] Starting on port {_port} ({_slots.Count} players)");

			_arena = ArenaRegistry.Get(_arenaName);

			_sim = new ServerSimulation(_arena);
			for (int i = 0; i < _slots.Count; i++)
			{
				var slot = _slots[i];
				var def = CharacterRegistry.Get(slot.CharacterClass);
				var baked = LoadBakedData(def);
				_sim.RegisterEntity(slot.EntityId, def, CreateInitialState(def, i), baked);
				Console.WriteLine($"[Match:{_matchId}] Slot {i}: entity {slot.EntityId} = {slot.CharacterClass}");
			}

			try
			{
				_udpServer = new UdpClient(_port);
				_udpServer.Client.Blocking = false;
				Console.WriteLine($"[Match:{_matchId}] Listening on UDP {_port}, waiting for {_slots.Count} players...");
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
					if (AllConnected())
					{
						// Check for disconnected players.
						var now = DateTime.UtcNow;
						bool anyTimeout = false;
						string timedOut = "";
						foreach (var slot in _slots)
						{
							if (slot.EndPoint != null && (now - slot.LastPacket).TotalSeconds > TimeoutSeconds)
							{
								anyTimeout = true;
								timedOut = slot.EntityId.ToString();
								break;
							}
						}

						if (anyTimeout)
						{
							Console.WriteLine($"[Match:{_matchId}] Player (entity {timedOut}) timed out — stopping match.");
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

		private bool AllConnected()
		{
			foreach (var slot in _slots)
				if (slot.EndPoint == null) return false;
			return true;
		}

		private static BakedAnimationData? LoadBakedData(CharacterDefinition def)
		{
			// Reuse the existing BakedDataPath + LoadFromBin path (same as the
			// pre-refactor server). Falls back to null (no baked skeleton) when
			// the file is absent or unreadable.
			if (string.IsNullOrEmpty(def.BakedDataPath)) return null;

			try
			{
				string sysPath = def.BakedDataPath.Replace("res://", "");
				var binData = File.ReadAllBytes(sysPath);
				var baked = BakedAnimationData.LoadFromBin(binData);
				Console.WriteLine($"[Match] Loaded baked data: {sysPath} ({binData.Length} bytes, {baked.Animations.Length} anims)");
				return baked;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[Match] Failed to load baked data: {ex.Message} — using fallback");
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

					var slot = FindSlot(entityId);
					if (slot == null) continue; // not a rostered player

					// New player connecting — register their endpoint.
					if (slot.EndPoint == null)
					{
						slot.EndPoint = remoteEP;
						slot.LastPacket = DateTime.UtcNow;
						Console.WriteLine($"[Match:{_matchId}] Player (entity {entityId}) connected: {remoteEP}");

						// Start the countdown once the last player connects.
						if (AllConnected() && _matchState == MatchState.Waiting)
						{
							_matchState = MatchState.Countdown;
							_countdownTicks = CountdownDuration;
							Console.WriteLine($"[Match:{_matchId}] All {_slots.Count} players connected — countdown started!");
						}
						continue;
					}

					slot.LastPacket = DateTime.UtcNow;

					if (clientTick <= _serverTick) continue;

					var inputState = InputState.Deserialize(data.AsSpan(12));

					// Prevent duplicates
					bool exists = false;
					for (int i = 0; i < slot.Queue.Count; i++)
					{
						if (slot.Queue[i].tick == clientTick)
						{ exists = true; break; }
					}
					if (!exists)
						slot.Queue.Add((clientTick, inputState));
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

		private PlayerSlot? FindSlot(ulong entityId)
		{
			foreach (var s in _slots)
				if (s.EntityId == entityId) return s;
			return null;
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

			var inputs = new Dictionary<ulong, InputState>();
			foreach (var slot in _slots)
			{
				var input = FlushQueue(slot.Queue, out _);
				if (input.HasValue)
				{
					_serverTick = Math.Max(_serverTick, input.Value.tick);
					inputs[slot.EntityId] = input.Value.input;
				}
			}

			// Run authoritative simulation (movement + hit detection + hurtboxes + void death)
			if (inputs.Count > 0)
			{
				_sim.Tick(inputs);

				// Check for match end (first to MaxDeaths loses).
				ulong? winner = null;
				ulong? loser = null;
			foreach (var slot in _slots)
				{
					var st = _sim.GetState(slot.EntityId);
					if (st.Deaths >= MaxDeaths)
					{
						loser = slot.EntityId;
						break;
					}
				}
				if (loser.HasValue)
				{
					// Winner = the first other player still under the death limit.
					foreach (var slot in _slots)
					{
						if (slot.EntityId != loser.Value)
						{
							var st = _sim.GetState(slot.EntityId);
							if (st.Deaths < MaxDeaths) { winner = slot.EntityId; break; }
						}
					}
					_matchState = MatchState.Ended;
					_winnerEntityId = winner ?? 0;
					_postMatchTicks = PostMatchDuration;
					Console.WriteLine($"[Match:{_matchId}] Entity {loser.Value} eliminated! Winner: {_winnerEntityId}");
				}

				SendState();
			}
		}

		private void SendState()
		{
			if (_udpServer == null) return;

			// Packet format (matching NetworkClient expectations):
			//   entityId(8) + tick(4) + CharacterStatePacket(48)
			const int envelopeSize = 8 + 4 + CharacterStatePacket.Size;

			// Build a packet per entity once, then send each to every connected client.
			var packets = new List<(ulong entityId, byte[] buffer)>(_slots.Count);
			foreach (var slot in _slots)
			{
				var statePacket = CharacterStatePacket.FromState(_sim.GetState(slot.EntityId), _serverTick);
				statePacket.MatchState = _matchState;

				var buf = new byte[envelopeSize];
				BitConverter.TryWriteBytes(buf.AsSpan(0, 8), slot.EntityId);
				BitConverter.TryWriteBytes(buf.AsSpan(8, 4), _serverTick);
				statePacket.Serialize(buf.AsSpan(12));
				packets.Add((slot.EntityId, buf));
			}

			try
			{
				foreach (var slot in _slots)
				{
					if (slot.EndPoint == null) continue;
					foreach (var pkt in packets)
						_udpServer.Send(pkt.buffer, envelopeSize, slot.EndPoint);
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

		/// <summary>
		/// Per-player state held outside the simulation: the client's UDP endpoint,
		/// its input queue, and the last-seen packet time for timeout detection.
		/// </summary>
		private sealed class PlayerSlot
		{
			public ulong EntityId { get; }
			public CharacterClass CharacterClass { get; }
			public IPEndPoint? EndPoint { get; set; }
			public DateTime LastPacket { get; set; } = DateTime.UtcNow;
			public List<(uint tick, InputState input)> Queue { get; } = new();

			public PlayerSlot(ulong entityId, CharacterClass characterClass)
			{
				EntityId = entityId;
				CharacterClass = characterClass;
			}
		}
	}
}
