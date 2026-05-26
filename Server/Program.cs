using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SlopArena.Shared;

namespace SlopArena.Server
{
	class Program
	{
		private static UdpClient? _udpServer;
		private static IPEndPoint? _clientEndPoint;
		private static bool _running = true;

		// Player state variables (spawn at arena center 5000x5000)
		private static uint _serverTick = 0;
		private static float _posX = 2500f;
		private static float _posY = 2500f;
		private static float _posZ = 0f;
		private static float _velX = 0f;
		private static float _velY = 0f;
		private static float _velZ = 0f;

		private static ActionState _actionState = ActionState.Idle;
		private static ushort _stateTicksRemaining = 0;
		private static ushort _dashCooldownTicks = 0;
		private static float _dashDirX = 0f;
		private static float _dashDirY = 0f;
		private static ushort _combatLockoutTicks = 0;
		private static bool _slideMomentumActive = false;

		// Input buffering queue
		private static readonly System.Collections.Generic.List<ClientInputPacket> _inputQueue = new System.Collections.Generic.List<ClientInputPacket>();

		static void Main(string[] args)
		{
			Console.WriteLine("=== SlopArena Authoritative Physics Server ===");
			
			PhysicsConfig.Initialize();
			_posZ = PhysicsConfig.GetGroundHeight(_posX, _posY);

			int port = 7777;
			try
			{
				_udpServer = new UdpClient(port);
				_udpServer.Client.Blocking = false;
				Console.WriteLine($"Server started on port {port} (UDP)");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error starting server: {ex.Message}");
				return;
			}

			var stopwatch = Stopwatch.StartNew();
			double nextTickTime = 0;
			double tickDurationMs = 1000.0 / PhysicsConfig.TickRate;

			Console.WriteLine($"Simulation running at {PhysicsConfig.TickRate}Hz. Press Ctrl+C to stop.");

			while (_running)
			{
				double currentTime = stopwatch.Elapsed.TotalMilliseconds;
				if (currentTime >= nextTickTime)
				{
					ReceiveInputs();
					Tick();
					nextTickTime += tickDurationMs;

					if (currentTime > nextTickTime + tickDurationMs * 10)
					{
						nextTickTime = currentTime;
					}
				}
				else
				{
					int sleepTime = (int)(nextTickTime - currentTime) - 1;
					if (sleepTime > 0)
					{
						Thread.Sleep(sleepTime);
					}
					else
					{
						Thread.Yield();
					}
				}
			}

			_udpServer.Close();
		}

		private static void ReceiveInputs()
		{
			if (_udpServer == null) return;

			while (true)
			{
				try
				{
					if (_udpServer.Available == 0) break;
					
					var remoteEP = new IPEndPoint(IPAddress.Any, 0);
					byte[] data = _udpServer.Receive(ref remoteEP);

					if (data.Length >= ClientInputPacket.Size)
					{
						var packet = ClientInputPacket.Deserialize(data);
						if (packet.TickNumber > _serverTick)
						{
							_clientEndPoint = remoteEP;
							
							// Prevent duplicates in the queue
							bool exists = false;
							for (int i = 0; i < _inputQueue.Count; i++)
							{
								if (_inputQueue[i].TickNumber == packet.TickNumber)
								{
									exists = true;
									break;
								}
							}
							if (!exists)
							{
								_inputQueue.Add(packet);
							}
						}
					}
				}
				catch (SocketException ex)
				{
					if (ex.SocketErrorCode != SocketError.WouldBlock)
					{
						Console.WriteLine($"Socket exception: {ex.Message}");
					}
					break;
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error receiving input: {ex.Message}");
					break;
				}
			}
		}

		private static void Tick()
		{
			if (_inputQueue.Count > 0)
			{
				// Sort inputs chronologically
				_inputQueue.Sort((a, b) => a.TickNumber.CompareTo(b.TickNumber));

				int simulatedCount = 0;
				foreach (var input in _inputQueue)
				{
					if (input.TickNumber > _serverTick)
					{
						_serverTick = input.TickNumber;
						SimulatePhysics(input);
						simulatedCount++;
						if (simulatedCount >= 120) // Cap to prevent packet-burst exploit/freezing
						{
							break;
						}
					}
				}

				_inputQueue.Clear();
				SendState();
			}
		}

		private static void SimulatePhysics(ClientInputPacket input)
		{
			PhysicsConfig.SimulateStep(
				ref _posX, ref _posY, ref _posZ,
				ref _velX, ref _velY, ref _velZ,
				ref _actionState, ref _stateTicksRemaining, ref _dashCooldownTicks,
				ref _dashDirX, ref _dashDirY,
				ref _combatLockoutTicks, ref _slideMomentumActive,
				input
			);
		}

		private static void SendState()
		{
			if (_udpServer == null || _clientEndPoint == null) return;

			var state = new CharacterStatePacket
			{
				TickNumber = _serverTick,
				PositionX = _posX,
				PositionY = _posY,
				PositionZ = _posZ,
				VelocityX = _velX,
				VelocityY = _velY,
				VelocityZ = _velZ,
				CurrentActionState = (byte)_actionState,
				StateDurationFrames = _stateTicksRemaining
			};

			Span<byte> buffer = stackalloc byte[CharacterStatePacket.Size];
			state.Serialize(buffer);

			try
			{
				_udpServer.Send(buffer.ToArray(), buffer.Length, _clientEndPoint);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error sending state snapshot: {ex.Message}");
			}
		}
	}
}
