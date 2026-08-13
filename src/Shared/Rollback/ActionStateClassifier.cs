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
            ActionState.Idle or ActionState.Dashing or ActionState.JumpSquat or ActionState.AirDodging or ActionState.Run;

        /// <summary>True when the self entity's continuous sim may snap wire fields and replay
        /// through this state (LocalTrack correction). LedgeHang has no ServerAbility instance and
        /// recomputes its ledge from the wire position, so it is snap-safe in both directions even
        /// though it is NOT Predictable for opponents (occupancy is a multi-entity server decision).</summary>
        public static bool IsSnapSafe(ActionState state)
            => IsPredictable(state) || state == ActionState.LedgeHang;
    }
}
