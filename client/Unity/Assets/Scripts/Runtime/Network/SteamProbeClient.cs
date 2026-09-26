#nullable enable
using System;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;
using Steamworks;

namespace SlopArena.Client.Network
{
    /// <summary>Isolated, command-line-only SteamNetworkingSockets route probe.</summary>
    public sealed class SteamProbeClient : MonoBehaviour
    {
        private const string Argument = "--steam-probe-client";
        private const int PayloadBytes = 200;
        private const int RateHz = 60;
        private const int TestSeconds = 10;
        private const int SampleCount = RateHz * TestSeconds;
        private const int MaxReceiveBatch = 64;
        private const double ConnectionTimeoutSeconds = 30;
        private const double ReceiveGraceSeconds = 1;

        private static readonly IntPtr[] ReceiveBuffer = new IntPtr[MaxReceiveBatch];

        private readonly long[] _sentAt = new long[SampleCount];
        private readonly bool[] _received = new bool[SampleCount];
        private readonly byte[] _echo = new byte[PayloadBytes];
        private ulong _expectedServerId;
        private HSteamNetConnection _connection;
        private IntPtr _sendBuffer;
        private long _startedAt;
        private long _connectedAt;
        private long _testStartedAt;
        private int _attempted;
        private int _echoed;
        private double _rttTotalMs;
        private bool _steamInitialized;
        private bool _hasConnection;
        private bool _authenticated;
        private bool _finished;

        public static bool IsRequested()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length; i++)
                if (string.Equals(args[i], Argument, StringComparison.Ordinal))
                    return true;
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (!IsRequested())
                return;
            var go = new GameObject(nameof(SteamProbeClient));
            DontDestroyOnLoad(go);
            go.AddComponent<SteamProbeClient>();
        }

        private void Start()
        {
            if (!TryReadServerId(out _expectedServerId))
            {
                Debug.LogError("Steam probe requires exactly one --steam-probe-client <expected-server-steamid> with a nonzero numeric SteamID64.");
                _finished = true;
                return;
            }

            try
            {
                if (!SteamAPI.Init())
                    throw new InvalidOperationException("SteamAPI.Init() failed; launch through Steam or provide a local steam_appid.txt containing the actual AppID (not the Steamworks default 480).");
                _steamInitialized = true;
                _sendBuffer = Marshal.AllocHGlobal(PayloadBytes);
                Marshal.Copy(_echo, 0, _sendBuffer, PayloadBytes);
                SteamNetworkingUtils.InitRelayNetworkAccess();
                SteamNetworkingSockets.InitAuthentication();
                _startedAt = Stopwatch.GetTimestamp();
                Debug.Log("Steam probe waiting for Steam authentication and relay access.");
            }
            catch (Exception exception)
            {
                Debug.LogError($"Steam probe startup failed: {exception}");
                ShutdownSteam();
                _finished = true;
            }
        }

        private void Update()
        {
            if (!_steamInitialized || _finished)
                return;

            SteamAPI.RunCallbacks();
            var now = Stopwatch.GetTimestamp();
            if (!_hasConnection)
            {
                var auth = SteamNetworkingSockets.GetAuthenticationStatus(out var authDetails);
                var relay = SteamNetworkingUtils.GetRelayNetworkStatus(out var relayDetails);
                if (now - _startedAt >= ConnectionTimeoutSeconds * Stopwatch.Frequency)
                {
                    Fail($"Steam network not ready logged-on={SteamUser.BLoggedOn()} auth={auth} ({authDetails.m_debugMsg}) relay={relay} ({relayDetails.m_debugMsg})");
                    return;
                }
                if (!SteamUser.BLoggedOn() ||
                    auth != ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Current ||
                    relay != ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Current)
                    return;

                var remoteIdentity = default(SteamNetworkingIdentity);
                remoteIdentity.SetSteamID64(_expectedServerId);
                _connection = SteamNetworkingSockets.ConnectP2P(ref remoteIdentity, 0, 0, null!);
                if (_connection == HSteamNetConnection.Invalid)
                {
                    Fail("ConnectP2P returned an invalid connection.");
                    return;
                }
                _hasConnection = true;
                _startedAt = now;
                Debug.Log($"Steam probe connecting expected-server={_expectedServerId} virtual-port=0");
                return;
            }
            if (!_authenticated)
            {
                if (now - _startedAt >= ConnectionTimeoutSeconds * Stopwatch.Frequency)
                {
                    Fail("connection timed out before peer identity authentication");
                    return;
                }

                if (!SteamNetworkingSockets.GetConnectionInfo(_connection, out var info))
                    return;
                if (info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally ||
                    info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer)
                {
                    Fail($"connection failed state={info.m_eState} reason={info.m_eEndReason} detail={info.m_szEndDebug}");
                    return;
                }
                if (info.m_eState != ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
                    return;

                var actualServerId = info.m_identityRemote.GetSteamID64();
                if (actualServerId != _expectedServerId)
                {
                    Fail($"remote identity mismatch expected={_expectedServerId} actual={actualServerId}");
                    return;
                }
                _authenticated = true;
                _connectedAt = now;
                _testStartedAt = now;
                Debug.Log($"Steam probe authenticated server={actualServerId}; starting {RateHz}Hz, {PayloadBytes}-byte unreliable test for {TestSeconds}s");
            }

            ReceiveEchoes(now);
            if (_finished)
                return;
            var elapsed = (now - _testStartedAt) / (double)Stopwatch.Frequency;
            if (elapsed < TestSeconds)
            {
                while (_attempted < SampleCount && _attempted / (double)RateHz <= elapsed)
                    SendSample(_attempted);
            }
            else if (elapsed >= TestSeconds + ReceiveGraceSeconds)
            {
                LogSummary();
                _finished = true;
                ShutdownSteam();
            }
        }

        private void SendSample(int sequence)
        {
            WriteSequence(_sendBuffer, unchecked((uint)sequence));
            _sentAt[sequence] = Stopwatch.GetTimestamp();
            _attempted++;
            var result = SteamNetworkingSockets.SendMessageToConnection(
                _connection, _sendBuffer, PayloadBytes, Constants.k_nSteamNetworkingSend_Unreliable, out _);
            if (result != EResult.k_EResultOK && result != EResult.k_EResultIgnored)
                Debug.LogWarning($"Steam probe send sequence={sequence} result={result}");
        }

        private void ReceiveEchoes(long now)
        {
            var count = SteamNetworkingSockets.ReceiveMessagesOnConnection(_connection, ReceiveBuffer, MaxReceiveBatch);
            if (count < 0)
            {
                Fail($"receiving echoed packets returned {count}");
                return;
            }
            for (var i = 0; i < count; i++)
            {
                var messagePointer = ReceiveBuffer[i];
                ReceiveBuffer[i] = IntPtr.Zero;
                try
                {
                    var message = Marshal.PtrToStructure<SteamNetworkingMessage_t>(messagePointer);
                    if (message.m_cbSize != PayloadBytes)
                        continue;
                    Marshal.Copy(message.m_pData, _echo, 0, PayloadBytes);
                    var sequence = ReadSequence(_echo);
                    if (sequence >= _attempted || sequence >= SampleCount || _received[sequence])
                        continue;
                    var valid = true;
                    for (var b = 4; b < PayloadBytes; b++)
                        if (_echo[b] != 0)
                        {
                            valid = false;
                            break;
                        }
                    if (!valid)
                        continue;
                    _received[sequence] = true;
                    _echoed++;
                    _rttTotalMs += (now - _sentAt[sequence]) * 1000.0 / Stopwatch.Frequency;
                }
                finally
                {
                    SteamNetworkingMessage_t.Release(messagePointer);
                }
            }
        }

        private void LogSummary()
        {
            var rtt = _echoed == 0 ? "n/a" : (_rttTotalMs / _echoed).ToString("F2", CultureInfo.InvariantCulture) + "ms";
            var loss = _attempted == 0 ? 0 : (_attempted - _echoed) * 100.0 / _attempted;
            var connectSeconds = (_connectedAt - _startedAt) / (double)Stopwatch.Frequency;
            var elapsed = (Stopwatch.GetTimestamp() - _testStartedAt) / (double)Stopwatch.Frequency;
            var remotePop = "unknown";
            var relayPop = "unknown";
            var queuedUnreliableBytes = -1;
            var pingMs = -1;
            if (SteamNetworkingSockets.GetConnectionInfo(_connection, out var info))
            {
                remotePop = info.m_idPOPRemote.ToString();
                relayPop = info.m_idPOPRelay.ToString();
            }
            var status = default(SteamNetConnectionRealTimeStatus_t);
            var lane = default(SteamNetConnectionRealTimeLaneStatus_t);
            if (SteamNetworkingSockets.GetConnectionRealTimeStatus(_connection, ref status, 0, ref lane) == EResult.k_EResultOK)
            {
                queuedUnreliableBytes = status.m_cbPendingUnreliable;
                pingMs = status.m_nPing;
            }
            Debug.Log($"Steam probe result server={_expectedServerId} connect-time={connectSeconds:F2}s duration={elapsed:F2}s attempted={_attempted} echoed={_echoed} loss={loss:F2}% mean-rtt={rtt} ping={pingMs}ms remote-pop={remotePop} relay-pop={relayPop} queued-unreliable-bytes={queuedUnreliableBytes}");
        }

        private static void WriteSequence(IntPtr buffer, uint sequence)
        {
            Marshal.WriteByte(buffer, 0, (byte)sequence);
            Marshal.WriteByte(buffer, 1, (byte)(sequence >> 8));
            Marshal.WriteByte(buffer, 2, (byte)(sequence >> 16));
            Marshal.WriteByte(buffer, 3, (byte)(sequence >> 24));
        }

        private static uint ReadSequence(byte[] payload) =>
            (uint)(payload[0] | payload[1] << 8 | payload[2] << 16 | payload[3] << 24);

        private static bool TryReadServerId(out ulong serverId)
        {
            var args = Environment.GetCommandLineArgs();
            serverId = 0;
            var found = false;
            for (var i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], Argument, StringComparison.Ordinal))
                    continue;
                if (found || i + 1 >= args.Length ||
                    !ulong.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out serverId) || serverId == 0)
                    return false;
                found = true;
            }
            return found;
        }

        private void Fail(string message)
        {
            Debug.LogError($"Steam probe failed: {message}");
            _finished = true;
            ShutdownSteam();
        }

        private void OnDestroy() => ShutdownSteam();
        private void OnApplicationQuit() => ShutdownSteam();

        private void ShutdownSteam()
        {
            if (_hasConnection && _steamInitialized)
            {
                SteamNetworkingSockets.CloseConnection(_connection, 0, "Steam probe shutdown", false);
                _hasConnection = false;
            }
            if (_sendBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_sendBuffer);
                _sendBuffer = IntPtr.Zero;
            }
            if (_steamInitialized)
            {
                SteamAPI.Shutdown();
                _steamInitialized = false;
            }
        }
    }
}
