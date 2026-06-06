# SlopArena — Animation & State Machine System

> Architecture du système d'animations et de machine à états custom.

---

## Architecture générale

Deux couches indépendantes qui communiquent par `Travel()` :

```
AnimationTree (StateMachine root)          ← transitions d'animations
    Idle ↔ Run → Jump → Fall → Land → Idle

Custom FSM (StateMachine.cs)                ← logique + transitions
    IdleState → RunState → JumpState → FallState → LandingState
```

**Principe :** le FSM custom décide QUEL état, l'AnimationTree gère COMMENT l'animation transitionne (blend, xfade).

---

## Lifecycle d'un State

```
Enter()
  ↓
OnProcess(dt)        ← _Process, pour les checks de transition
  ↓
OnPhysicsProcess(dt) ← _PhysicsProcess, pour les forces (jump, etc.)
  ↓
Exit()
```

### Ordre d'exécution dans une frame

```
1. _Process sur tous les nodes
   └─ FSM._Process → CurrentState.OnProcess()
      → ici qu'on checke les Input (jump press, etc.)
      → transitionne vers le state suivant si condition remplie

2. _PhysicsProcess sur tous les nodes
   └─ PlayerController._PhysicsProcess
      ├─ BuildInputState()
      └─ MovementComponent.Tick(input)
         → simulation tick-based (gravité, friction, dash, knockback)
         → MoveAndSlide()

   └─ FSM._PhysicsProcess → CurrentState.OnPhysicsProcess()
      → ici qu'on applique les forces state-specific
      → ex: JumpState met velocity.Y = JumpForce
      → NE PAS appeler MoveAndSlide() — déjà fait par Tick
```

---

## Créer un nouveau State

1. **Créer le fichier** dans `Scripts/Animation/States/`

```csharp
using Godot;

public sealed partial class MyState : State
{
    public MyState()
    {
        AnimationName = "Idle"; // nom dans l'AnimationTree
    }

    public override void Enter()
    {
        // reset, setup
        base.Enter(); // appelle AnimPlayback.Travel(AnimationName)
    }

    public override void Exit()
    {
        // cleanup
    }

    public override void OnProcess(float delta)
    {
        // check conditions → StateMachine.TransitionTo("otherState");
    }

    public override void OnPhysicsProcess(float delta)
    {
        // forces state-specific (optionnel)
    }
}
```

2. **Ajouter le node dans manki.tscn** (éditeur Godot) :
   - Node enfant de `FSM`
   - Script = `Scripts/Animation/States/MyState.cs`
   - Nom du node = le nom utilisé par `TransitionTo()`

3. **Ajouter l'état dans l'AnimationTree** (éditeur) :
   - Nouveau state dans le StateMachine root
   - Animation correspondante

---

## Convention des états

| State | Animation | Transition vers | Conditions |
|-------|-----------|----------------|------------|
| `idle` | `Idle` | run, jump, fall | input.move, jump press, off floor |
| `run` | `Run` | idle, jump, fall | stop, jump press, off floor |
| `jump` | `Jump` | fall, landing | velocity.Y < 0, on floor |
| `fall` | `Fall` | landing | on floor |
| `landing` | `Land` | idle, run | timer expired |

### Règles

- **`OnProcess()`** : lecture des Input, transitions. Pas de modification de velocity.
- **`OnPhysicsProcess()`** : forces directes sur `Player.Velocity`. Ex: jump applique `velocity.Y = JumpForce`.
- **Pas de `MoveAndSlide()` dans les States** — déjà fait par `MovementComponent.Tick()`.
- **Jump force appliquée par JumpState, pas par la simulation** — la simulation gère gravité/friction/dash/knockback.
- **AnimationName en constructor** : valeur par défaut, peut être overridée dans l'Inspector.

---

## AnimationTree (manki.tscn)

```
AnimationNodeStateMachine (root)
├── Start ──0.1s──→ Idle ←──0.15s──→ Run
│   │  0.1s            │  0.1s
│   │   └── Jump ──0.2s──→ Fall
│   │                      │  0.15s
│   │                      ↓
│   │                    Land ──at end──→ Idle
│   │
│   └─── LMB (combat) ──0.15s──→ End
```

Chaque state = `AnimationNodeAnimation` avec l'anim correspondante.
Les `xfade_time` sur les transitions donnent les blend entre animations.

---

## Ajouter un nouveau personnage

1. Créer son .tscn (modèle + AnimationPlayer + AnimationTree en StateMachine root)
2. Ajouter un node `FSM` avec `StateMachine.cs` comme script
3. Ajouter les enfants State (idle, run, jump, fall, landing, etc.)
4. Dans `PlayerController.cs`, la ligne `_fsm = _playerModel.GetNodeOrNull<StateMachine>("FSM")` le trouve automatiquement
5. Chaque State est auto-enregistré par son nom de node (lowercased, suffix "state" retiré)
