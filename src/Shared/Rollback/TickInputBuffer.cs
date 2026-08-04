using System.Collections.Generic;

namespace SlopArena.Shared.Rollback
{
    /// <summary>
    /// A tick-ordered buffer of one player's inputs (netplay input model). The server
    /// consumes ONE input per sim tick — the input whose tick equals the current sim
    /// tick, in arrival-irrelevant order. Unlike a newest-only queue, intermediate
    /// ticks are never dropped: a single-tick jump or slot press survives any backlog
    /// or burst. Missing ticks are handled by the caller via held-last-input.
    /// </summary>
    public sealed class TickInputBuffer
    {
        private readonly List<(uint Tick, InputState Input)> _entries = new();

        /// <summary>Number of queued inputs.</summary>
        public int Count => _entries.Count;

        /// <summary>Highest queued tick, or null when empty. Valid because the buffer
        /// is always kept sorted ascending by tick.</summary>
        public uint? MaxTick => _entries.Count > 0 ? _entries[_entries.Count - 1].Tick : null;

        /// <summary>Insert an input, keeping the buffer sorted by tick ascending.
        /// A duplicate tick replaces the existing entry.</summary>
        public void Push(uint tick, InputState input)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Tick == tick)
                {
                    _entries[i] = (tick, input); // replace duplicate
                    return;
                }
                if (_entries[i].Tick > tick)
                {
                    _entries.Insert(i, (tick, input));
                    return;
                }
            }
            _entries.Add((tick, input));
        }

        /// <summary>Remove and return the input for exactly <paramref name="tick"/>.
        /// Returns false (and leaves the buffer untouched) when absent.</summary>
        public bool TryTake(uint tick, out InputState input)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Tick == tick)
                {
                    input = _entries[i].Input;
                    _entries.RemoveAt(i);
                    return true;
                }
                if (_entries[i].Tick > tick)
                    break; // sorted ascending — not present
            }
            input = default;
            return false;
        }

        /// <summary>Drop every entry with tick ≤ <paramref name="upToTick"/> (consumed).</summary>
        public void Prune(uint upToTick)
        {
            int remove = 0;
            while (remove < _entries.Count && _entries[remove].Tick <= upToTick)
                remove++;
            if (remove > 0)
                _entries.RemoveRange(0, remove);
        }

        public void Clear() => _entries.Clear();
    }
}
