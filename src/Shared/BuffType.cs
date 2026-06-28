using System;

namespace SlopArena.Shared;

[Flags]
public enum BuffType : byte
{
    None = 0,
    Overclock = 1,    // Manki F: faster attacks + bonus damage
    // Future: Haste = 2, Shield = 4, etc.
}
