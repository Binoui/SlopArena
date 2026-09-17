#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;

namespace SlopArena.Client.Network
{
    /// <summary>Authenticated identity used by every chat destination.</summary>
    public sealed class ChatPlayer
    {
        public ChatPlayer() { }
        public ChatPlayer(string playerId, string displayName, string sessionTag)
        {
            PlayerId = playerId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            SessionTag = sessionTag ?? string.Empty;
        }

        public string PlayerId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string SessionTag { get; set; } = string.Empty;
    }

    /// <summary>Master-authored chat message. Text and names are always literal.</summary>
    public sealed class ChatMessage
    {
        public Guid MessageId { get; set; }
        public long Sequence { get; set; }
        public string Channel { get; set; } = string.Empty;
        public Guid? ServerId { get; set; }
        public string? RecipientId { get; set; }
        public ChatPlayer Sender { get; set; } = new();
        public string Text { get; set; } = string.Empty;
        public DateTimeOffset SentAt { get; set; }
    }

    public sealed class ChatPresence
    {
        public ChatPlayer Player { get; set; } = new();
        public bool Online { get; set; }
    }

    public sealed class ServerChatState
    {
        public Guid? ServerId { get; set; }
        public ChatMessage[] Messages { get; set; } = Array.Empty<ChatMessage>();
    }

    public sealed class ChatSnapshot
    {
        public ChatPlayer Self { get; set; } = new();
        public ChatMessage[] GlobalMessages { get; set; } = Array.Empty<ChatMessage>();
        public ServerChatState Server { get; set; } = new();
    }

    /// <summary>
    /// Client-owned bounded conversation view. Mutations happen only through
    /// ChatSession, and its Changed event is delivered on Unity's main thread.
    /// </summary>
    public sealed class ChatConversation
    {
        private readonly List<ChatMessage> _messages = new();

        internal ChatConversation(string key, string title)
        {
            Key = key;
            Title = title;
        }

        public string Key { get; }
        public string Title { get; internal set; }
        public string Draft { get; internal set; } = string.Empty;
        public string Feedback { get; internal set; } = string.Empty;
        public IReadOnlyList<ChatMessage> Messages => _messages;
        public int Unread { get; internal set; }
        public bool IsSending { get; internal set; }
        public bool CanSend { get; internal set; }

        internal List<ChatMessage> MutableMessages => _messages;
    }

    /// <summary>Shared client-side validation matching Master ChatText rules.</summary>
    public static class ChatTextValidation
    {
        public const int MaxDisplayNameScalars = 24;
        public const int MaxMessageScalars = 500;

        public static bool TryValidateDisplayName(string? value, out string normalized, out string error)
        {
            normalized = (value ?? string.Empty).Trim();
            if (!TryCountScalars(normalized, MaxDisplayNameScalars, out _, out error))
                return false;
            if (normalized.Length == 0)
            {
                error = "Choose a display name first.";
                return false;
            }
            bool hasVisibleContent = false;
            for (int i = 0; i < normalized.Length; i++)
            {
                int scalar = ReadScalar(normalized, ref i, out var category);
                hasVisibleContent |= category != UnicodeCategory.Format && !char.IsWhiteSpace(normalized, i);
                if (category == UnicodeCategory.Control || scalar == 0x2028 || scalar == 0x2029 ||
                    (category == UnicodeCategory.Format && scalar != 0x200C && scalar != 0x200D))
                {
                    error = "Display name contains unsupported characters.";
                    return false;
                }
            }
            if (!hasVisibleContent)
            {
                error = "Choose a display name with visible characters.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        public static bool TryValidateMessage(string? value, out string normalized, out string error)
        {
            normalized = value ?? string.Empty;
            if (normalized.Length == 0 || string.IsNullOrWhiteSpace(normalized))
            {
                error = "Message cannot be empty.";
                return false;
            }
            if (!TryCountScalars(normalized, MaxMessageScalars, out _, out error))
                return false;
            bool hasVisibleContent = false;
            for (int i = 0; i < normalized.Length; i++)
            {
                int scalar = ReadScalar(normalized, ref i, out var category);
                hasVisibleContent |= category != UnicodeCategory.Format && !char.IsWhiteSpace(normalized, i);
                if (category == UnicodeCategory.Control && scalar != '\t' && scalar != '\n' && scalar != '\r')
                {
                    error = "Message contains unsupported control characters.";
                    return false;
                }
            }
            if (!hasVisibleContent)
            {
                error = "Message must contain visible characters.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        public static int CountUnicodeScalars(string? value)
        {
            if (value == null) return 0;
            if (!TryCountScalars(value, int.MaxValue, out var count, out _)) return -1;
            return count;
        }

        private static bool TryCountScalars(string value, int max, out int count, out string error)
        {
            count = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[++i]))
                    {
                        error = "Text contains malformed Unicode.";
                        return false;
                    }
                }
                else if (char.IsLowSurrogate(c))
                {
                    error = "Text contains malformed Unicode.";
                    return false;
                }
                count++;
                if (count > max)
                {
                    error = $"Text is limited to {max} characters.";
                    return false;
                }
            }
            error = string.Empty;
            return true;
        }

        private static int ReadScalar(string value, ref int index, out UnicodeCategory category)
        {
            int scalar = char.ConvertToUtf32(value, index);
            category = CharUnicodeInfo.GetUnicodeCategory(value, index);
            if (scalar > 0xFFFF) index++;
            return scalar;
        }
    }

}
