namespace MoveBox.Shared
{
    /// <summary>
    /// Status effect identifiers that spells can apply or consume.
    /// Each status has a clear gameplay meaning; spells check HasStatus(type) 
    /// for conditional bonuses and ConsumeStatus(type) for one-shot effects.
    /// </summary>
    public enum StatusType : byte
    {
        /// <summary>Movement speed -40%. Consumed for immobilize/stun effects.</summary>
        Ralenti = 0,
        
        /// <summary>Takes +30% damage from next hit. Consumed for burst damage.</summary>
        Vulnerable = 1,
        
        /// <summary>Visible through walls. Consumed for gap-closers / special effects.</summary>
        Marque = 2,
        
        /// <summary>Blocks X flat damage. Consumed for defensive counter-effects.</summary>
        Bouclier = 3,
        
        /// <summary>Damage over time ticks. Consumed for AoE explosion effects.</summary>
        Brulure = 4,
        
        /// <summary>Stun buildup: at 2+ stacks → stun. Consumed for chain lightning.</summary>
        Electrifie = 5,
    }
}
