# NPC/Bot System - SlopArena

Complete documentation for creating and managing bots/NPCs in SlopArena.

## Architecture

```
PlayerController (CharacterBody3D)
├── Used by: Player AND NPCs
├── Mode: SetNPC(true) for bot, SetNPC(false) for player
└── Components:
    ├── MovementComponent (movement)
    ├── AnimationController (animations)
    ├── CombatComponent (combat)
    └── BotController (AI, optional)
```

## Spawning an NPC

### Simple Method (in Main.cs)

```csharp
// 1. Create instance
var npc = new PlayerController();
npc.Name = "NPC_0";
npc.SetClass(CharacterClass.Manki);
npc.SetNPC(true);  // ← Enable NPC mode
AddChild(npc);

// 2. Position
Vector3 spawnPos = _arenaManager.GetSpawnPosition(0);
npc.Position = spawnPos;
npc.SetNpcSpawnPosition(spawnPos); // For respawn

// 3. Setup combat
npc.SetupCombat(_simulation, ArenaRegistry.Get("split"));

// 4. Register in simulation
ulong entityId = 100; // IDs 100-104 for NPCs
_simulation.Entities[entityId] = (npc.GlobalPosition, 1.5f, true);
_simulation.CombatComponents[entityId] = npc.GetCombatComponent();
```

## AI System (BotController)

### Structure

`BotController` is a Node attached to the PlayerController NPC. It provides:
- **Combat behavior**: approach, circle strafe, attacks
- **Wander behavior**: random movement when far
- **Ability usage**: attacks, dash, jump

### Setup

```csharp
var botAI = new BotController();
botAI.Setup(npc, player); // npc = the bot, player = target (human)
npc.AddChild(botAI);
```

### Behavior Parameters

```csharp
EngageRange = 15f    // Distance to start engaging
AttackRange = 3f     // Distance to attack
CircleRadius = 5f    // Circle strafe radius
WanderRadius = 10f   // Random wander area
```

## NPC Mode in PlayerController

### Respawn Properties (Player + NPC)

```csharp
private bool _isNPC = false;           // NPC mode enabled
private float _respawnTimer = 0f;      // Respawn timer (20s for all)
private const float RespawnDelay = 20.0f; // 20 seconds for everyone
```

### Public API

```csharp
void SetNPC(bool isNpc)               // Enable/disable NPC mode
bool IsNPC()                          // Check if NPC
bool IsAlive()                        // Check if alive (not respawning)
bool IsNpcAlive()                     // Legacy alias for IsAlive()
void TriggerRespawn()                 // Trigger 20s respawn (both player & NPC)
void NpcKnockOut()                    // Legacy alias for TriggerRespawn()
float GetRespawnTimeRemaining()       // Get respawn countdown (0 if alive)
ushort GetDamagePercent()             // Get current damage % (via MovementComponent)
```

### Damage System (Smash-Style)

Player AND NPCs use the **same** system:
- **No HP system** - only Damage %
- Hits increase Damage % (stored in `CharacterState.DamagePercent`)
- Higher % = bigger knockback (Smash Bros formula: `1 + dmg% * 0.01`)
- Eliminated by going **out-of-bounds** (below kill height)
- Respawn after **20 seconds** at **arena center, in the air** with 0% damage

```csharp
// On hit (automatic via CombatComponent)
// 1. Damage % increases
// 2. Knockback scales with current %
// 3. No HP system

// On knockout (void/out-of-bounds) - same for player and NPCs
character.TriggerRespawn(); // Triggers 20s respawn at center

// In Main.cs _Process:
if (_arenaManager.IsBelowKillHeight(character.GlobalPosition))
{
    character.TriggerRespawn(); // 20s delay, respawn at center in air
}

// Respawn location:
// - Arena center (average of spawn points)
// - 20 units above ground (spawns in air)
// - Damage % reset to 0
```

## Player vs NPC Differences

| Feature | Player | NPC |
|---------|--------|-----|
| Input | Keyboard/Mouse | BotController |
| Camera | Attached | None |
| Damage System | Damage % only (Smash) | Damage % only (Smash) |
| Elimination | Out-of-bounds → 20s respawn | Out-of-bounds → 20s respawn |
| Respawn Location | Arena center, in air (+20Y) | Arena center, in air (+20Y) |
| Visual | Normal mesh | Mesh + red emission |
| Visibility | Always visible | Hidden during respawn timer |
| Target | Not targetable | Targetable (ID 100-104) |
| UI | Shows own damage % | Shows damage % in target frame |

## Improving the AI

### Step 1: Input Injection (TODO)

Currently BotController directly manipulates `Velocity`. Better:

```csharp
// In PlayerController.cs, add:
public void InjectInput(InputState input)
{
    if (!_isNPC) return;
    // Process input like a player
}

// In BotController.cs:
private void SimulateInput(float dt)
{
    var input = new InputState
    {
        MoveX = calculatedX,
        MoveY = calculatedZ,
        Jump = shouldJump,
        // etc.
    };
    _npc.InjectInput(input);
}
```

### Step 2: Ability Usage

```csharp
// In PlayerController.cs, add:
public void UseAbility(int slot)
{
    ExecuteSlot(slot);
}

// In BotController.cs:
private void ExecuteAttack()
{
    _npc?.UseAbility(0); // LMB
}

private void ExecuteDash()
{
    _npc?.UseAbility(1); // Dash ability
}
```

### Step 3: Advanced Behaviors

```csharp
// Combo chains
private void ExecuteCombo()
{
    if (_npc == null) return;
    
    _npc.UseAbility(0); // LMB stage 1
    await Task.Delay(300);
    _npc.UseAbility(0); // LMB stage 2
    await Task.Delay(300);
    _npc.UseAbility(0); // LMB stage 3
}

// Threat assessment
private PlayerController? PickBestTarget()
{
    // Prioritize:
    // - Closest enemy
    // - Lowest HP enemy
    // - Player over other bots
}

// Ability cooldown tracking
private bool CanUseAbility(int slot)
{
    // Check cooldown state
    return _npc?.GetCooldown(slot) == 0;
}
```

## Example: Spawn 5 Bots

```csharp
private void SpawnBots()
{
    for (int i = 0; i < 5; i++)
    {
        // Create bot
        var bot = new PlayerController();
        bot.Name = $"Bot_{i}";
        bot.SetClass(CharacterClass.Manki);
        bot.SetNPC(true);
        AddChild(bot);
        
        // Position
        bot.Position = GetRandomSpawnPoint();
        bot.SetNpcSpawnPosition(bot.Position);
        
        // Combat
        bot.SetupCombat(_simulation, _arena);
        
        // AI
        var ai = new BotController();
        ai.Setup(bot, _player);
        bot.AddChild(ai);
        
        // Register
        ulong id = (ulong)(100 + i);
        _simulation.Entities[id] = (bot.GlobalPosition, 1.5f, true);
        _simulation.CombatComponents[id] = bot.GetCombatComponent();
    }
}
```

## Debugging

### Check NPCs

```csharp
// In _Process or _PhysicsProcess
GD.Print($"NPC Count: {_npcs.Count(n => n != null)}");
GD.Print($"Alive NPCs: {_npcs.Count(n => n?.IsNpcAlive() ?? false)}");

foreach (var npc in _npcs)
{
    if (npc != null)
    {
        GD.Print($"{npc.Name}: Dmg={npc.GetDamagePercent()}%, Pos={npc.GlobalPosition}, Alive={npc.IsNpcAlive()}");
    }
}
```

### Visual Debug

NPCs have red emission when NPC mode is enabled (see PlayerController:1092).

### Console Commands

```csharp
// Add in Main.cs _UnhandledInput:
if (key.Keycode == Key.F1)
{
    GD.Print("=== NPC STATUS ===");
    for (int i = 0; i < _npcs.Length; i++)
    {
        if (_npcs[i] != null)
        {
            GD.Print($"NPC {i}: Dmg={_npcs[i].GetDamagePercent()}%, Alive={_npcs[i].IsNpcAlive()}");
        }
    }
}
```

## AI Roadmap

### MVP (Current)
- ✅ Random wandering
- ✅ Approach player
- ✅ Circle strafe
- ✅ Basic attacks
- ✅ Occasional jump/dash

### V1 (Next Steps)
- [ ] Proper input injection
- [ ] Ability cooldown tracking
- [ ] Combo chains (LMB 1-2-3)
- [ ] Ability usage (Q/E/R/F)
- [ ] Tactical dash (approach/escape)

### V2 (Advanced)
- [ ] Threat management (multi-targets)
- [ ] Team coordination
- [ ] Projectile dodging
- [ ] Edge guarding (Smash style)
- [ ] DI (Directional Influence) during knockback
- [ ] Tech roll timing

### V3 (Expert)
- [ ] Behavior trees / State machines
- [ ] Learn from player patterns
- [ ] Difficulty levels (Easy/Medium/Hard)
- [ ] Character-specific strategies
- [ ] Combo prediction & counter

## Multiplayer Considerations

When implementing real netcode:

1. **Server-side NPCs**: AI runs on server only
2. **Client prediction**: Clients predict NPC movement
3. **Entity IDs**: Keep 100-199 for NPCs, 200+ for players
4. **Sync**: CharacterStatePacket includes NPCs too

```csharp
// Server
foreach (var npc in _npcs)
{
    if (npc.IsNPC())
    {
        // AI runs here
        npc.BotAI.Process(delta);
        
        // Send state to clients
        SendNPCState(npc.EntityId, npc.GetState());
    }
}

// Client
void OnNPCStateReceived(ulong entityId, CharacterState state)
{
    // Apply state to local NPC instance (visual only)
    var npc = GetNPC(entityId);
    npc.ApplyState(state);
}
```

## References

- **PlayerController.cs**: Line 62-86 (NPC state)
- **PlayerController.cs**: Line 1099-1120 (NPC methods)
- **BotController.cs**: Complete AI
- **Main.cs**: Line 273-299 (Entity registration)
- **Main.cs**: SpawnNPCs() (Spawning logic)
