#nullable enable
using System;
using System.Globalization;
using System.Text;
using Steamworks;

namespace SlopArena.Client.Network
{
    /// <summary>Room ID is a locator, never proof of admission.</summary>
    public static class RoomConnection
    {
        public static string Format(Guid roomId) => roomId == Guid.Empty
            ? throw new ArgumentException("A Room ID is required.", nameof(roomId))
            : "+slop_room " + roomId.ToString("D");
        public static bool TryParse(string? raw, out Guid roomId)
        {
            roomId = Guid.Empty;
            const string prefix = "+slop_room ";
            const int payloadLength = 47; // Prefix plus a canonical 36-character D GUID.

            // Check the bounded, exact form before invoking the GUID parser. Steam's
            // connect buffer is 256 bytes including NUL; this ASCII form is well below it.
            if (raw == null || raw.Length != payloadLength ||
                !raw.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            for (int i = prefix.Length; i < raw.Length; i++)
            {
                char c = raw[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') ||
                    (i == prefix.Length + 8 || i == prefix.Length + 13 ||
                     i == prefix.Length + 18 || i == prefix.Length + 23) && c == '-'))
                    return false;
            }

            return Guid.TryParseExact(raw.Substring(prefix.Length), "D", out roomId) &&
                roomId != Guid.Empty;
        }
    }

    /// <summary>Publishes only the current, confirmed Master Room; no Steam lifecycle ownership.</summary>
    public sealed class RoomSteamPresence
    {
        private const int MaxValueBytes = 255; // Steam's 256-byte buffer includes the terminating NUL.
        private const float RetrySeconds = 5f;
        private const int MaxRetries = 5;
        private static readonly string[] Keys =
        {
            "connect", "steam_player_group", "steam_player_group_size",
            "room_name", "room_phase", "room_count", "steam_display", "status"
        };
        private readonly Func<string, string, bool> _set;
        private LobbyClient? _lobby;
        private RoomSnapshot? _mappedRoom;
        private RoomSnapshot? _room;
        private string[] _desired = new string[Keys.Length];
        private string[] _published = new string[Keys.Length];
        private bool _dirty;
        private int _retries;
        private float _nextRetry;

        public RoomSteamPresence(Func<string, string, bool>? set = null)
        {
            _set = set ?? SteamFriends.SetRichPresence;
            Array.Fill(_desired, string.Empty);
            Array.Fill(_published, string.Empty);
        }

        public void Attach(LobbyClient? lobby)
        {
            if (ReferenceEquals(_lobby, lobby)) return;
            if (_lobby != null)
            {
                _lobby.RoomUpdated -= OnRoomUpdated;
                _lobby.RoomDeleted -= OnRoomLost;
                _lobby.RoomMembershipRevoked -= OnRoomLost;
                _lobby.Disconnected -= OnDisconnected;
            }
            _room = null;
            _lobby = lobby;
            if (lobby != null)
            {
                lobby.RoomUpdated += OnRoomUpdated;
                lobby.RoomDeleted += OnRoomLost;
                lobby.RoomMembershipRevoked += OnRoomLost;
                lobby.Disconnected += OnDisconnected;
            }
        }

        private void OnRoomUpdated(RoomSnapshot room)
        {
            if (_lobby?.IsConnected == true && room.Id != Guid.Empty &&
                _lobby.JoinedRoomId == room.Id)
                _room = room;
        }

        private void OnRoomLost(Guid roomId)
        {
            if (_room?.Id == roomId) _room = null;
        }

        private void OnDisconnected(Exception? _) => _room = null;
        public bool IsCurrentRoom(RoomSnapshot? room) =>
            room != null && ReferenceEquals(_room, room) &&
            _lobby?.IsConnected == true && _lobby.JoinedRoomId == room.Id;


        public void Refresh(bool steamReady, bool authenticated, bool sessionUsable, float now)
        {
            // Membership can change outside RoomUpdated (create, leave, chat resync).
            var room = steamReady && authenticated && sessionUsable && _lobby?.IsConnected == true &&
                _room != null && _lobby.JoinedRoomId == _room.Id ? _room : null;
            if (!ReferenceEquals(room, _mappedRoom))
            {
                _mappedRoom = room;
                var values = Map(room, _lobby?.JoinedRoomId ?? Guid.Empty, room != null);
                bool changed = false;
                for (int i = 0; i < Keys.Length; i++)
                    if (_desired[i] != values[i]) { changed = true; break; }
                if (changed)
                {
                    _desired = values;
                    _dirty = true;
                    _retries = 0;
                }
            }
            if (!steamReady || !_dirty ||
                (_retries > 0 && (now < _nextRetry || _retries > MaxRetries))) return;
            Publish(now);
        }

        private void Publish(float now)
        {
            bool failed = false;
            // Clear connect first; never leave a Join Game action when another key fails.
            for (int i = 0; i < Keys.Length; i++)
            {
                if (!_dirty && _published[i] == _desired[i]) continue;
                try
                {
                    if (!_set(Keys[i], _desired[i])) { failed = true; continue; }
                    _published[i] = _desired[i];
                }
                catch (Exception)
                {
                    failed = true; // Steam outages cannot interrupt local play or a match.
                }
            }
            _dirty = failed;
            if (failed)
            {
                _retries++;
                _nextRetry = now + RetrySeconds;
            }
            else _retries = 0;
        }

        public void Reset(bool steamReady, float now)
        {
            Attach(null);
            _mappedRoom = null;
            _desired = Map(null, Guid.Empty, false);
            _dirty = true;
            _retries = 0;
            if (steamReady) Publish(now);
        }

        public static string[] Map(RoomSnapshot? room, Guid joinedRoomId, bool sessionUsable)
        {
            var values = new string[Keys.Length];
            Array.Fill(values, string.Empty);
            if (!sessionUsable || room == null || room.Id == Guid.Empty ||
                room.Id != joinedRoomId) return values;
            string name = LimitUtf8(room.Name, 160);
            string phase = room.Phase switch
            {
                "Lobby" => "Lobby",
                "Character Select" => "Character Select",
                "Stage Select" => "Stage Select",
                "Match Starting" => "Match Starting",
                "In Match" => "In Match",
                _ => "Room"
            };
            string count = Math.Max(0, room.MemberCount).ToString(CultureInfo.InvariantCulture) +
                "/" + Math.Max(0, room.Capacity).ToString(CultureInfo.InvariantCulture);
            values[0] = room.Joinable ? RoomConnection.Format(room.Id) : string.Empty;
            values[1] = room.Id.ToString("D");
            values[2] = Math.Max(0, room.MemberCount).ToString(CultureInfo.InvariantCulture);
            values[3] = name;
            values[4] = phase;
            values[5] = count;
            values[6] = "#SlopArenaRoom";
            values[7] = LimitUtf8(name + " — " + phase + " (" + count + ")", MaxValueBytes);
            return values;
        }

        public static string LimitUtf8(string? text, int maxBytes)
        {
            if (string.IsNullOrEmpty(text)) return "Room";
            var result = new StringBuilder();
            int bytes = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsControl(c) || char.IsSurrogate(c) &&
                    (i + 1 == text.Length || !char.IsHighSurrogate(c) || !char.IsLowSurrogate(text[i + 1])))
                    continue;
                int length = char.IsHighSurrogate(c) ? 2 : 1;
                int size = Encoding.UTF8.GetByteCount(text, i, length);
                if (bytes + size > maxBytes) break;
                result.Append(text, i, length);
                bytes += size;
                i += length - 1;
            }
            return result.Length == 0 ? "Room" : result.ToString();
        }
    }
}
