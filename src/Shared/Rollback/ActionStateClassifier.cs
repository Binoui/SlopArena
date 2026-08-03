namespace SlopArena.Shared.Rollback
{
    /// <summary>
    /// The Predictable/Complex ActionState partition (ADR-0011, D9). Predictable states
    /// depend only on fields the wire carries (see CharacterStatePacket's D10 fields) —
    /// PredictedTrack and LocalTrack's correction path may safely re-simulate through them.
    /// Complex states depend on the ServerAbility instance layer and/or SpellResolver's
    /// hitbox/projectile list, neither of which is ever serialized — entities in these
    /// states must never be rebuilt from a snapshot (RawTrack for opponents; LocalTrack
    /// skips its own correction replay through them, see LocalTrack.ReconcileWithServer).
    /// </summary>
    public static class ActionStateClassifier
    {
        public static bool IsPredictable(ActionState state) => state is
            ActionState.Idle or ActionState.Dashing or ActionState.JumpSquat or ActionState.AirDodging;
    }
}
