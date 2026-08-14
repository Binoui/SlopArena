using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SlopArena.Shared
{
    /// <summary>
    /// Writes hitbox edits back into the character data source files
    /// (src/Shared/Characters/*Data.cs) — the compiled source of truth for the
    /// simulation. The Ability Lab edits a character, then Save rewrites the exact
    /// `HitboxEvents = ...` initializer of the edited (slot, airborne, stage) in the
    /// C# file, leaving everything else byte-identical. There is no intermediate
    /// JSON: what lands in the file is what the server and client compile.
    ///
    /// The file is parsed structurally (comments/strings skipped, brace-balanced),
    /// never with string replacement: several stages share identical hitbox blocks,
    /// and `Stages` must not match `ChargedStages`.
    /// </summary>
    public static class CSharpCharacterWriter
    {
        /// <summary>
        /// C# property holding the spec for a (slot, airborne) pair — mirrors
        /// CharacterDefinition.GetSlotAbility field layout (ADR-0016).
        /// </summary>
        public static string PropertyName(int slotIndex, bool airborne) => (slotIndex, airborne) switch
        {
            (0, false) => "LMB", (0, true) => "AirLMB",
            (1, false) => "RMB", (1, true) => "AirRMB",
            (2, false) => "Slot1", (2, true) => "AirSlot1",
            (3, false) => "E", (3, true) => "AirE",
            (4, false) => "R", (4, true) => "AirR",
            (5, false) => "F", (5, true) => "AirF",
            (6, false) => "Slot2", (6, true) => "AirSlot2",
            (7, false) => "Slot3", (7, true) => "AirSlot3",
            (8, false) => "Slot4", (8, true) => "AirSlot4",
            (9, false) => "Slot5", (9, true) => "AirSlot5",
            (10, false) => "A", (10, true) => "AirA",
            _ => throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "No ability property for slot"),
        };

        /// <summary>Stage address key: "slot:airborne(0|1):stage".</summary>
        public static string Key(int slotIndex, bool airborne, int stageIndex)
            => $"{slotIndex}:{(airborne ? 1 : 0)}:{stageIndex}";

        /// <summary>Parse a stage address key produced by <see cref="Key"/>. False on malformed input.</summary>
        public static bool TryParseKey(string key, out int slotIndex, out bool airborne, out int stageIndex)
        {
            slotIndex = -1;
            airborne = false;
            stageIndex = -1;
            var parts = key.Split(':');
            if (parts.Length != 3) return false;
            if (!int.TryParse(parts[0], out slotIndex)) return false;
            if (parts[1] != "0" && parts[1] != "1") return false;
            airborne = parts[1] == "1";
            return int.TryParse(parts[2], out stageIndex);
        }

        /// <summary>
        /// Replace the Nth stage's HitboxEvents initializer in the given spec property.
        /// Returns false (source unchanged) when the property, Stages array, or stage
        /// index cannot be located.
        /// </summary>
        public static bool TryReplaceHitboxEvents(string source, string property, int stageIndex, HitboxEvent[] events, out string result)
        {
            result = source;
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(property)) return false;

            // 1. Locate the spec assignment: `property = new AbilitySpec { ... }`.
            if (!TryFindAssignment(source, 0, source.Length, property, out int tokenIdx, out int afterEquals)) return false;
            int specOpen = FindFirstChar(source, afterEquals, source.Length, '{', out _);
            if (specOpen < 0) return false;
            int specClose = FindMatchingBrace(source, specOpen);
            if (specClose < 0) return false;

            // 2. Locate the Stages array inside the spec.
            if (!TryFindAssignment(source, specOpen + 1, specClose, "Stages", out _, out int stagesAfter)) return false;
            int arrayOpen = FindFirstChar(source, stagesAfter, specClose, '{', out _);
            if (arrayOpen < 0) return false;
            int arrayClose = FindMatchingBrace(source, arrayOpen);
            if (arrayClose < 0) return false;

            // 3. Split the array into top-level elements (commas at depth 0, comments
            //    and strings skipped so commas inside them never split an element).
            var starts = new List<int> { arrayOpen + 1 };
            int depth = 0;
            for (int i = arrayOpen + 1; i < arrayClose; i++)
            {
                char c = source[i];
                if (c == '/' && i + 1 < arrayClose && source[i + 1] == '/') { i = SkipLineComment(source, i, arrayClose); continue; }
                if (c == '/' && i + 1 < arrayClose && source[i + 1] == '*') { i = SkipBlockComment(source, i, arrayClose); continue; }
                if (c == '"') { i = SkipString(source, i, arrayClose); continue; }
                if (c == '{') depth++;
                else if (c == '}') depth--;
                else if (c == ',' && depth == 0) starts.Add(i + 1);
            }

            // 4. Pick the Nth non-empty element (trailing comma yields a ws-only tail).
            int elemStart = -1, elemEnd = -1;
            int elementIndex = 0;
            for (int k = 0; k < starts.Count; k++)
            {
                int sStart = starts[k];
                int sEnd = k + 1 < starts.Count ? starts[k + 1] : arrayClose;
                if (string.IsNullOrWhiteSpace(source.Substring(sStart, sEnd - sStart))) continue;
                if (elementIndex == stageIndex) { elemStart = sStart; elemEnd = sEnd; break; }
                elementIndex++;
            }
            if (elemStart < 0) return false;

            // 5. Replace (or insert) the HitboxEvents initializer in that element.
            if (TryFindAssignment(source, elemStart, elemEnd, "HitboxEvents", out int hbToken, out int hbAfter, requireNewSpec: false))
            {
                int v = hbAfter;
                while (v < elemEnd && char.IsWhiteSpace(source[v])) v++;
                if (v >= elemEnd) return false;
                int valStart, valEnd;
                if (source[v] == 'A') // Array.Empty<HitboxEvent>()
                {
                    int close = source.IndexOf(')', v);
                    if (close < 0 || close >= elemEnd) return false;
                    valStart = v; valEnd = close + 1;
                }
                else if (source[v] == 'n') // new[] / new HitboxEvent[] { ... }
                {
                    int brace = FindFirstChar(source, v, elemEnd, '{', out _);
                    if (brace < 0) return false;
                    int cb = FindMatchingBrace(source, brace);
                    if (cb < 0 || cb >= elemEnd) return false;
                    valStart = v; valEnd = cb + 1;
                }
                else return false;

                string indent = LeadingWhitespace(source, hbToken);
                string newValue = FormatHitboxEvents(events, indent);
                result = source.Substring(0, valStart) + newValue + source.Substring(valEnd);
                return true;
            }
            else
            {
                // Stage has no HitboxEvents property — insert one before the element's
                // closing brace (element is `new() { ... }`).
                int elemOpen = FindFirstChar(source, elemStart, elemEnd, '{', out _);
                if (elemOpen < 0) return false;
                int elemClose = FindMatchingBrace(source, elemOpen);
                if (elemClose < 0 || elemClose >= elemEnd) return false;
                string innerRaw = source.Substring(elemOpen + 1, elemClose - elemOpen - 1);
                if (string.IsNullOrWhiteSpace(innerRaw))
                {
                    // `new() { }` — insert at the closing brace, keeping any inner spacing.
                    string insert = "HitboxEvents = " + FormatHitboxEvents(events, "");
                    result = source.Substring(0, elemClose) + insert + source.Substring(elemClose);
                }
                else
                {
                    // Append to the last property: `DurationTicks = 60 }` →
                    // `DurationTicks = 60, HitboxEvents = ... }`.
                    int insertAt = elemClose;
                    while (insertAt > elemOpen + 1 && char.IsWhiteSpace(source[insertAt - 1])) insertAt--;
                    string insert = ", HitboxEvents = " + FormatHitboxEvents(events, "");
                    result = source.Substring(0, insertAt) + insert + source.Substring(elemClose);
                }
                return true;
            }
        }

        /// <summary>
        /// Format events as a C# initializer matching the data files' style:
        /// single event → one-line `new HitboxEvent[] { new() { ... } }`; multiple
        /// events → the multi-line array shape; empty → `Array.Empty&lt;HitboxEvent&gt;()`.
        /// (Explicit `HitboxEvent[]` — `new() { }` elements cannot infer an
        /// implicitly-typed `new[]`, CS0826.)
        /// </summary>
        public static string FormatHitboxEvents(HitboxEvent[] events, string indent)
        {
            if (events == null || events.Length == 0) return "Array.Empty<HitboxEvent>()";
            var sb = new StringBuilder();
            if (events.Length == 1)
            {
                sb.Append("new HitboxEvent[] { new() { ");
                AppendEvent(sb, in events[0]);
                sb.Append(" } }");
            }
            else
            {
                sb.Append("new HitboxEvent[]\n").Append(indent).Append("{\n");
                string inner = indent + "    ";
                foreach (var e in events)
                {
                    sb.Append(inner).Append("new() { ");
                    AppendEvent(sb, in e);
                    sb.Append(" },\n");
                }
                sb.Append(indent).Append('}');
            }
            return sb.ToString();
        }

        private static void AppendEvent(StringBuilder sb, in HitboxEvent e)
        {
            sb.Append("TriggerTick = ").Append(e.TriggerTick).Append(", ");
            sb.Append("DurationTicks = ").Append(e.DurationTicks).Append(", ");
            if (e.Shape == HitboxShape.Capsule) sb.Append("Shape = HitboxShape.Capsule, ");
            sb.Append("Radius = ").Append(F(e.Radius)).Append(", ");
            sb.Append("OffX = ").Append(F(e.OffX)).Append(", ");
            sb.Append("OffY = ").Append(F(e.OffY)).Append(", ");
            sb.Append("OffZ = ").Append(F(e.OffZ)).Append(", ");
            if (e.EndOffX != 0f || e.EndOffY != 0f || e.EndOffZ != 0f)
                sb.Append("EndOffX = ").Append(F(e.EndOffX)).Append(", EndOffY = ").Append(F(e.EndOffY)).Append(", EndOffZ = ").Append(F(e.EndOffZ)).Append(", ");
            if (e.BoneName != null)
            {
                sb.Append("BoneName = \"").Append(e.BoneName).Append("\", ");
            }
            sb.Append("Damage = ").Append(F(e.Damage)).Append(", ");
            AppendKnockback(sb, in e.Knockback);
            sb.Append("StunTicks = ").Append(e.StunTicks).Append(", ");
            sb.Append("Interruptible = ").Append(e.Interruptible ? "true" : "false");
        }

        private static void AppendKnockback(StringBuilder sb, in KnockbackData kb)
        {
            bool custom = kb.Profile == KnockbackProfile.Custom;
            sb.Append("Knockback = new() { Profile = KnockbackProfile.").Append(kb.Profile);
            // Keep Custom's fields even when zeroed (Nilus' deliberately INERT hitbox
            // must not be silently converted into a Live profile).
            if (custom || kb.Angle != 0 || kb.BaseKnockback != 0f || kb.KnockbackGrowth != 0f)
                sb.Append(", Angle = ").Append((int)kb.Angle)
                  .Append(", BaseKnockback = ").Append(F(kb.BaseKnockback))
                  .Append(", KnockbackGrowth = ").Append(F(kb.KnockbackGrowth));
            sb.Append(" }, ");
        }

        /// <summary>0f-style float formatting: 0→"0f", 1→"1f", 0.4→"0.4f", -1.5→"-1.5f".</summary>
        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture) + "f";

        // ── Structural scanning helpers (comments + strings never counted) ──

        private static bool TryFindAssignment(string s, int start, int end, string token, out int tokenIdx, out int afterEquals, bool requireNewSpec = true)
        {
            tokenIdx = -1;
            afterEquals = -1;
            for (int i = start; i < end; i++)
            {
                char c = s[i];
                if (c == '/' && i + 1 < end && s[i + 1] == '/') { i = SkipLineComment(s, i, end); continue; }
                if (c == '/' && i + 1 < end && s[i + 1] == '*') { i = SkipBlockComment(s, i, end); continue; }
                if (c == '"') { i = SkipString(s, i, end); continue; }
                if (MatchToken(s, i, end, token))
                {
                    int j = i + token.Length;
                    while (j < end && char.IsWhiteSpace(s[j])) j++;
                    if (j < end && s[j] == '=')
                    {
                        if (requireNewSpec)
                        {
                            // Must be `= new ...` (an AbilitySpec assignment) — guards against
                            // matching BoneTrailDef's `A = 1f` / `R = 1f` before the real slot.
                            int k = j + 1;
                            while (k < end && char.IsWhiteSpace(s[k])) k++;
                            if (k >= end || s[k] != 'n') continue;
                        }
                        tokenIdx = i;
                        afterEquals = j + 1;
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool MatchToken(string s, int i, int end, string token)
        {
            if (i + token.Length > end) return false;
            if (i > 0 && IsIdentChar(s[i - 1])) return false;
            if (i + token.Length < end && IsIdentChar(s[i + token.Length])) return false;
            for (int t = 0; t < token.Length; t++)
                if (s[i + t] != token[t]) return false;
            return true;
        }

        private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        private static int SkipLineComment(string s, int i, int end)
        {
            while (i < end && s[i] != '\n') i++;
            return i;
        }

        private static int SkipBlockComment(string s, int i, int end)
        {
            i += 2;
            while (i + 1 < end && !(s[i] == '*' && s[i + 1] == '/')) i++;
            return i + 1;
        }

        private static int SkipString(string s, int i, int end)
        {
            i++;
            while (i < end && s[i] != '"')
            {
                if (s[i] == '\\') i++;
                i++;
            }
            return i;
        }

        /// <summary>First non-trivia occurrence of `c` at/after start (trivia = ws/comments/strings).</summary>
        private static int FindFirstChar(string s, int start, int end, char c, out int foundIdx)
        {
            foundIdx = -1;
            for (int i = start; i < end; i++)
            {
                char ch = s[i];
                if (ch == '/' && i + 1 < end && s[i + 1] == '/') { i = SkipLineComment(s, i, end); continue; }
                if (ch == '/' && i + 1 < end && s[i + 1] == '*') { i = SkipBlockComment(s, i, end); continue; }
                if (ch == '"') { i = SkipString(s, i, end); continue; }
                if (ch == c) { foundIdx = i; return i; }
            }
            return -1;
        }

        private static int FindMatchingBrace(string s, int open)
        {
            int depth = 1;
            for (int i = open + 1; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { i = SkipLineComment(s, i, s.Length); continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*') { i = SkipBlockComment(s, i, s.Length); continue; }
                if (c == '"') { i = SkipString(s, i, s.Length); continue; }
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        private static string LeadingWhitespace(string s, int idx)
        {
            int lineStart = s.LastIndexOf('\n', Math.Max(0, idx - 1)) + 1;
            int ws = lineStart;
            while (ws < idx && (s[ws] == ' ' || s[ws] == '\t')) ws++;
            return s.Substring(lineStart, ws - lineStart);
        }
    }
}
