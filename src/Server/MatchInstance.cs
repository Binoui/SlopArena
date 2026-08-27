using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using SlopArena.Shared;
using SlopArena.Shared.Rollback;

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
		private readonly MatchContentCatalog _contentCatalog;
		private readonly List<PlayerSlot> _slots;
		private UdpClient? _udpServer;
		private bool _running = true;

		private ArenaDefinition _arena;
		private ServerSimulation _sim = null!;
		private uint _serverTick;

		/// <summary>
		/// The inputs the server actually consumed for the last sim tick, keyed by
		/// entity. Sent back to clients as the input-relay section of each state
		/// broadcast (issue #80, ADR-0010): membership in this dict is the relay
		/// signal, so empty queues, eliminated entities, and disconnected players
		/// all broadcast the explicit no-input marker. Null until the first
		/// playing tick (countdown broadcasts relay nothing).
		/// </summary>
		private Dictionary<ulong, InputState>? _lastTickInputs;

		private const double TimeoutSeconds = 5.0;

		// Match lifecycle
		private MatchState _matchState = MatchState.Waiting;
		private ushort _countdownTicks;
		private const ushort CountdownDuration = 300; // 5 seconds at 60Hz
		private readonly IMatchRule _rule;
		private ulong _winnerEntityId;
		private MatchResultPacket? _matchResultPacket;

		private ushort _postMatchTicks;
		private const ushort PostMatchDuration = 180; // 3 seconds before cleanup

		private Thread? _thread;
		private readonly Action<int> _onMatchEnd;
		private readonly Action<Guid, long>? _onMatchResult;

		/// <param name="roster">Ordered players (index 0 = host). Each carries an entity ID (1..N) and a character class.</param>
		/// <param name="maxStocks">Stocks per player (default 3, issue #37).</param>
		/// <param name="onMatchResult">Optional callback invoked once when the match ends (match guid, winner steam id).</param>
		public MatchInstance(int port, string matchId, string arenaName,
			IReadOnlyList<MatchPlayer> roster, MatchContentCatalog contentCatalog, Action<int> onMatchEnd,
			byte maxStocks = MatchDefaults.DefaultMaxStocks, Action<Guid, long>? onMatchResult = null)
		{
			_port = port;
			_matchId = matchId;
			_arenaName = arenaName;
			_contentCatalog = contentCatalog ?? throw new ArgumentNullException(nameof(contentCatalog));
			_onMatchEnd = onMatchEnd;
			_onMatchResult = onMatchResult;
			_rule = new StockMatchRule(maxStocks);

			_slots = new List<PlayerSlot>(roster.Count);
			foreach (var p in roster)
			{
				var content = _contentCatalog.Resolve(p.CharacterClass)
					?? throw new InvalidDataException($"Roster selector '{p.CharacterClass}' is not in the match content catalog.");
				_slots.Add(new PlayerSlot((ulong)p.EntityId, p.CharacterClass, p.SteamId, content));
			}
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

			var arenaOpt = ArenaRegistry.Get(_arenaName);
			if (!arenaOpt.HasValue)
			{
				Console.WriteLine($"[Match:{_matchId}] Unknown arena '{_arenaName}' — aborting match.");
				_onMatchEnd(_port);
				return;
			}
			_arena = arenaOpt.Value;

			_sim = new ServerSimulation(_arena, _rule);
			for (int i = 0; i < _slots.Count; i++)
			{
				var slot = _slots[i];
				var def = slot.Content.Definition;
				_sim.RegisterEntity(slot.EntityId, def, CreateInitialState(def, i), slot.Content.BakedAnimation);
				// Respawn at the same distributed spawn point as initial spawn (issue #37).
				var respawnSpawn = PickSpawn(i);
				_sim.SetRespawnPosition(slot.EntityId, respawnSpawn.X, respawnSpawn.Y, respawnSpawn.Z, respawnSpawn.Yaw);
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
						// Check for disconnected players: mark the slot, clear its
						// stale queue, and let the entity go idle (issue #36).
						var now = DateTime.UtcNow;
						foreach (var slot in _slots)
						{
							if (slot.EndPoint == null || slot.Disconnected) continue;
							if ((now - slot.LastPacket).TotalSeconds > TimeoutSeconds)
							{
								slot.Disconnected = true;
								slot.Queue.Clear();
								Console.WriteLine($"[Match:{_matchId}] Player (entity {slot.EntityId}) disconnected — entity goes idle.");
							}
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


		private CharacterState CreateInitialState(CharacterDefinition def, int spawnIndex)
		{
			var spawn = PickSpawn(spawnIndex);

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

		/// <summary>Distributed spawn: spawn point by slot index, falling back to a
		/// hardcoded default when the arena has fewer points than players.</summary>
		private SpawnPoint PickSpawn(int spawnIndex)
		{
			if (_arena.SpawnPoints.Length > spawnIndex)
				return _arena.SpawnPoints[spawnIndex];
			return new SpawnPoint { X = 40f, Y = 0.5f, Z = 40f, Yaw = 0f };
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

					// Client packet format: entityId(8) + tick(4) + InputState(20) = 32 bytes (ADR-0016 short-hop bit)
					if (data.Length < 8 + 4 + InputState.Size) continue;

					ulong entityId = BitConverter.ToUInt64(data, 0);
					uint clientTick = BitConverter.ToUInt32(data, 8);

					var slot = FindSlot(entityId);
					if (slot == null) continue; // not a rostered player

					// A previously disconnected player reconnecting resumes control.
					if (slot.Disconnected)
					{
						slot.Disconnected = false;
						Console.WriteLine($"[Match:{_matchId}] Player (entity {entityId}) reconnected.");
					}

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

					// TickInputBuffer.Push replaces same-tick duplicates.
					slot.Queue.Push(clientTick, inputState);
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
					PrimeTickCounter();
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
				else
				{
					SendState();
				}
				return;
			}

			var inputs = new Dictionary<ulong, InputState>();
			uint targetTick = _serverTick + 1;
			bool anyPending = false;
			foreach (var slot in _slots)
			{
				if (slot.Disconnected || _rule.IsEliminated(_sim.GetState(slot.EntityId)))
				{
					slot.Queue.Clear();
					continue;
				}

				slot.Queue.Prune(_serverTick);
				if (slot.Queue.Count == 0) continue;
				anyPending = true;

				InputState input = slot.LastInput;
				if (slot.Queue.TryTake(targetTick, out var queuedInput))
					input = queuedInput;
				slot.LastInput = input;
				inputs[slot.EntityId] = input;
			}

			// Run authoritative simulation (movement + hit detection + hurtboxes + void death).
			_lastTickInputs = inputs;
			if (anyPending)
			{
				_serverTick = targetTick;
				_sim.Tick(inputs);

				var outcome = _rule.Evaluate(_sim.GetAllStates());
				if (outcome.IsEnded)
				{
					_matchState = MatchState.Ended;
					_winnerEntityId = outcome.WinnerEntityId;
					_matchResultPacket = BuildMatchResultPacket(outcome);
					_postMatchTicks = PostMatchDuration;
					Console.WriteLine(outcome.IsSharedVictory
						? $"[Match:{_matchId}] Shared victory — all players eliminated simultaneously."
						: $"[Match:{_matchId}] Winner: {_winnerEntityId}");

					if (_onMatchResult != null && Guid.TryParse(_matchId, out var matchGuid))
					{
						long winnerSteamId = 0;
						var winnerSlot = FindSlot(_winnerEntityId);
						if (winnerSlot != null) winnerSteamId = winnerSlot.SteamId;
						_onMatchResult(matchGuid, winnerSteamId);
					}
				}
			}

			// Broadcast every tick, including empty ones.
			SendState();
		}


		private void SendState()
		{
			if (_udpServer == null) return;

			var packets = new List<(byte[] buffer, int length)>(_slots.Count);
			foreach (var slot in _slots)
			{
				var statePacket = CharacterStatePacket.FromState(_sim.GetState(slot.EntityId), _serverTick);
				statePacket.MatchState = _matchState;

				InputState consumed = default;
				bool hasInput = _lastTickInputs != null && _lastTickInputs.TryGetValue(slot.EntityId, out consumed);
				var packet = new ServerEntityPacket
				{
					EntityId = slot.EntityId,
					Tick = _serverTick,
					State = statePacket,
					HasInput = hasInput,
					Input = consumed,
				};

				var buf = new byte[ServerEntityPacket.MaxSize];
				packet.Serialize(buf);
				packets.Add((buf, packet.WireSize));
			}

			var presentationPackets = new List<(byte[] buffer, int length)>();
			foreach (var evt in _sim.GetPresentationEvents(clear: true))
			{
				var eventPacket = new PresentationEventPacket(evt.MatchTick, evt.EntityId, evt.OperationIndex, evt.PresentationId);
				var eventBuffer = new byte[eventPacket.WireSize];
				eventPacket.Serialize(eventBuffer);
				presentationPackets.Add((eventBuffer, eventBuffer.Length));
			}

			try
			{
				foreach (var slot in _slots)
				{
					if (slot.EndPoint == null || slot.Disconnected) continue;
					foreach (var pkt in packets)
						_udpServer.Send(pkt.buffer, pkt.length, slot.EndPoint);

					if (_matchState == MatchState.Ended && _matchResultPacket != null)
					{
						var resultBuffer = new byte[_matchResultPacket.WireSize];
						_matchResultPacket.Serialize(resultBuffer);
						_udpServer.Send(resultBuffer, resultBuffer.Length, slot.EndPoint);
					}
					foreach (var evt in presentationPackets)
						_udpServer.Send(evt.buffer, evt.length, slot.EndPoint);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[Match:{_matchId}] Send error: {ex.Message}");
			}
		}

		private MatchResultPacket BuildMatchResultPacket(MatchOutcome outcome)
		{
			var entries = new List<MatchResultEntry>(_slots.Count);
			foreach (var slot in _slots)
			{
				var state = _sim.GetState(slot.EntityId);
				entries.Add(new MatchResultEntry(
					slot.EntityId,
					placement: 0,
					_sim.GetKOs(slot.EntityId),
					state.Deaths));
			}

			entries.Sort((a, b) =>
			{
				int winnerOrder = outcome.IsSharedVictory
					? 0
					: (a.EntityId == outcome.WinnerEntityId ? -1 : b.EntityId == outcome.WinnerEntityId ? 1 : 0);
				if (winnerOrder != 0) return winnerOrder;

				int byFalls = a.Falls.CompareTo(b.Falls);
				if (byFalls != 0) return byFalls;
				int byKOs = b.KOs.CompareTo(a.KOs);
				return byKOs != 0 ? byKOs : a.EntityId.CompareTo(b.EntityId);
			});

			var ranked = new MatchResultEntry[entries.Count];
			for (int i = 0; i < entries.Count; i++)
			{
				var entry = entries[i];
				ranked[i] = new MatchResultEntry(entry.EntityId, (byte)(i + 1), entry.KOs, entry.Falls);
			}

			return new MatchResultPacket(_serverTick, outcome.IsSharedVictory, ranked);
		}

		/// <summary>
		/// On GO, the clients' tick counters are already ~CountdownDuration ahead (they
		/// predict and send during countdown). Discard the countdown-era input backlog
		/// and start the shared tick counter at the clients' current tick, so the server
		/// and client sim clocks stay aligned from the first Playing tick instead of the
		/// server replaying five seconds of stale inputs.
		/// </summary>
		private void PrimeTickCounter()
		{
			uint maxQueued = 0;
			foreach (var slot in _slots)
				if (slot.Queue.MaxTick is uint maxTick)
					maxQueued = Math.Max(maxQueued, maxTick);
			if (maxQueued > 0)
			{
				_serverTick = maxQueued;
				foreach (var slot in _slots)
					slot.Queue.Clear();
			}
		}

		/// <summary>
		/// Per-player state held outside the simulation: the client's UDP endpoint,
		/// its input queue, and the last-seen packet time for timeout detection.
		/// </summary>
		private sealed class PlayerSlot
		{
			public ulong EntityId { get; }
			public CharacterClass CharacterClass { get; }
			public long SteamId { get; }
			public MatchContentEntry Content { get; }
			public IPEndPoint? EndPoint { get; set; }
			public DateTime LastPacket { get; set; } = DateTime.UtcNow;
			public bool Disconnected { get; set; }
			public TickInputBuffer Queue { get; } = new();
			/// <summary>Last input consumed for this slot.</summary>
			public InputState LastInput;

			public PlayerSlot(ulong entityId, CharacterClass characterClass, long steamId, MatchContentEntry content)
			{
				EntityId = entityId;
				CharacterClass = characterClass;
				SteamId = steamId;
				Content = content;
			}
		}
	}
}
