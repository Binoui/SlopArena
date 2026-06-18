#nullable enable
using Godot;

/// <summary>
/// Abstract base for all ability classes.
/// Each ability (LMB, RMB, AirLMB, AirRMB, Q, E, R, F) is its own class
/// that controls its lifecycle and drawing.
///
/// Abilities that need no special aiming (LMB, E, F, etc.) just set
/// ActiveSlot and return immediately. Abilities with targeting
/// (RMB charge cone, Q parabolic throw) override OnActivate/Tick/OnInput
/// to show indicators and stream aim data each frame.
/// </summary>
public abstract class Ability
{
    /// <summary>Display name (e.g. "Monkey Combo", "Round Bomb").</summary>
    public abstract string Name { get; }
    /// <summary>1-based slot index (1=LMB, 2=RMB, 3=Q, 4=E, 5=R, 6=F).</summary>
    public abstract byte SlotNumber { get; set; }

    /// <summary>
    /// The ability data from CharacterDefinition. Must be set before OnActivate.
    /// Already authoritative on the server — this is just for client-side access.
    /// </summary>
    public SlopArena.Shared.AbilitySpec Data { get; set; }

    /// <summary>
    /// Called when the ability is activated (key/button pressed).
    /// Override for aiming states (show indicators, set emission, etc.).
    /// </summary>
    public virtual void OnActivate(PlayerController player) { }

    /// <summary>
    /// Called every frame while this ability is active.
    /// Return null when done (ability finished).
    /// Return AbilityInputState with updated aim data to stream to sim.
    /// </summary>
    public abstract AbilityInputState? Tick(PlayerController player, float delta);

    /// <summary>
    /// Called when the ability is interrupted or finishes.
    /// Clean up indicators, clear emission, etc.
    /// </summary>
    public virtual void OnDeactivate(PlayerController player) { }

    /// <summary>
    /// Trigger special effects registered for this ability.
    /// Call in Tick() when setting ActiveSlot to fire.
    /// </summary>
    protected void TriggerEffects(PlayerController player)
    {
        if (Data.SpecialEffectKeys == null) return;
        var combat = player.GetCombatComponent();
        if (combat == null) return;
        foreach (var key in Data.SpecialEffectKeys)
            AbilityRegistry.Execute(key, combat);
    }

    /// <summary>
    /// Optional: receives raw input events (mouse motion, key events).
    /// Only called while this ability is active.
    /// </summary>
    public virtual void OnInput(InputEvent @event) { }
}
