# Blast Zones - Arena Balance

Blast zones are defined **per-map** in `Shared/ArenaDefinition.cs` via the `KillHeight` field. This is a critical balance lever that affects match pacing, comeback potential, and competitive viability.

## Concept

In Smash Bros-style games, the **blast zone** is the Y coordinate below which a character is eliminated. Unlike traditional fighters where you lose stocks by depleting HP, knockback-based fighters eliminate players by launching them off-stage.

```
┌────────────────────────────────┐
│         Arena Floor (Y=0)       │  ← Players fight here
│                                 │
│                                 │
├─────────────────────────────────┤
│         Void Space              │  ← Players fall through
│                                 │
│                                 │
├═════════════════════════════════┤
│      BLAST ZONE (KillHeight)    │  ← Elimination line
└─────────────────────────────────┘
```

## Balance Implications

### Shallow Blast Zones (closer to Y=0)
**Example:** "The Split" at Y=-7

**Effects:**
- ✅ Faster matches (quicker eliminations)
- ✅ Higher skill ceiling (precise spacing required)
- ✅ Aggressive play rewarded
- ❌ Less comeback potential (one mistake = death)
- ❌ High damage% matters less

**Best for:**
- Competitive 1v1
- Small stages
- Fast-paced tournaments
- Skilled players

### Deep Blast Zones (farther below Y=0)
**Example:** "The Pit" at Y=-15

**Effects:**
- ✅ Longer matches (more survivability)
- ✅ Higher comeback potential (damage% matters more)
- ✅ Forgiving for new players
- ❌ Can feel slow or campy
- ❌ Lower skill expression

**Best for:**
- Casual/party play
- Large stages
- Free-for-all (4+ players)
- Training mode

### Balanced Blast Zones
**Example:** "Crossroads" at Y=-10

**Effects:**
- Moderate match length
- Balanced risk/reward
- Works for both casual and competitive
- Good default for testing

## Current Maps

| Map | Size | KillHeight | Blast Depth | Intended Playstyle |
|-----|------|------------|-------------|-------------------|
| **The Pit** | 80x80 | -15f | Deep | Casual, long matches |
| **Crossroads** | 60x60 | -10f | Medium | All-around balanced |
| **The Split** | 60x60 | -7f | Shallow | Competitive, fast KOs |

## Design Guidelines

### Stage Size vs Blast Zone Correlation
Generally, larger stages should have deeper blast zones:

```
Stage Width (X/Z)  →  Recommended KillHeight Range
─────────────────────────────────────────────────
  40-50 (small)    →  -5 to -8   (shallow, fast)
  60-70 (medium)   →  -8 to -12  (balanced)
  80-100 (large)   →  -12 to -18 (deep, long)
```

**Why?** Large stages give more horizontal escape routes and recovery opportunities, so a shallow blast zone would feel unfair. Small stages already limit movement, so a deep blast zone would make matches drag.

### Competitive vs Casual Balance

**Competitive Stages:**
- Tighter blast zones (-5 to -9)
- Rewards precision and spacing
- Punishes mistakes heavily
- Example: Final Destination in Smash

**Casual Stages:**
- Deeper blast zones (-12 to -18)
- More forgiving
- Longer action sequences
- Example: Temple in Smash

### Testing Your Changes

When adjusting KillHeight, test these scenarios:

1. **Low % Launch** (0-50%): Character should survive
2. **Medium % Launch** (100-150%): Character might die depending on angle
3. **High % Launch** (200%+): Character should die reliably
4. **Edge Recovery** (150%): Character at stage edge can survive with good DI
5. **Center Stage** (150%): Character in center survives longer than edge

## Implementation

### Changing Blast Zones

Edit `Shared/ArenaDefinition.cs`:

```csharp
new ArenaDefinition
{
    Name = "my_arena",
    DisplayName = "My Arena",
    ScenePath = "res://assets/arenas/my_arena.tscn",
    KillHeight = -12f,   // ← BLAST ZONE (adjust this!)
    MinX = 0f, MaxX = 60f,
    MinZ = 0f, MaxZ = 60f,
    SpawnPoints = new[] { /* ... */ }
}
```

### How It Works

The elimination check runs in `Simulation.SimulateTick()`:

```csharp
// 7. Void death check
if (s.PY < arena.KillHeight)
{
    RespawnCharacter(ref s, arena);
}
```

- Server checks this every tick (60Hz)
- Client mirrors the check for prediction
- When triggered: 20-second respawn at arena center + 20Y, damage% reset to 0

## Future Enhancements

### Horizontal Blast Zones
Currently only vertical (bottom) blast zone is implemented. Consider adding:
- Left/Right blast zones (X < MinX or X > MaxX)
- Top blast zone (Y > MaxY for upward launches)

Would require adding to ArenaDefinition:
```csharp
public float BlastZoneLeft;   // X < this = death
public float BlastZoneRight;  // X > this = death  
public float BlastZoneTop;    // Y > this = death
public float BlastZoneBottom; // Y < this = death (current KillHeight)
```

### Dynamic Blast Zones
Game modes could modify blast zones:
- **Sudden Death**: Shrinking blast zones over time
- **Stamina Mode**: Fixed blast zone but HP system enabled
- **Giant Mode**: Expanded blast zones for giant characters

### Visual Indicators
Add danger zone visual effects:
- Red tint when falling below stage (Y < 0)
- Screen shake when approaching blast zone
- Warning arrow pointing up when falling

## References

- **Smash Bros Blast Zones**: https://www.ssbwiki.com/Blast_line
- **Stage Balance Analysis**: https://smashboards.com/threads/stage-blast-zone-data.491352/
- **ArenaDefinition.cs**: Shared/ArenaDefinition.cs
- **Simulation.cs**: Shared/Simulation.cs:95 (void death check)
- **Main.cs**: Scripts/World/Main.cs:478-491 (out-of-bounds detection)
