using UnityEngine;
using SlopArena.Shared;

namespace SlopArena.Client.Animation
{
    /// <summary>
    /// Maps character animation clips to the states PlayerRenderer expects.
    /// Create one asset per character via the generator tool
    /// (right-click model → Create SlopArena Animator).
    /// </summary>
    [CreateAssetMenu(fileName = "NewCharacterAnimConfig", menuName = "SlopArena/Character Animation Config")]
    public class CharacterAnimationConfig : ScriptableObject
    {
        [Header("Movement")]
        public AnimationClip Idle;
        public AnimationClip Run;
        public AnimationClip Jump;
        public AnimationClip Fall;
        public AnimationClip Land;

        [Header("Combat")]
        public AnimationClip Attack1;
        public AnimationClip Attack2;
        public AnimationClip Attack3;
        public AnimationClip Dash;
        public AnimationClip HitSmall;
        public AnimationClip HitLarge;
        public AnimationClip Death;

        [Header("Abilities")]
        public AnimationClip SpellQ;
        public AnimationClip SpellE;
        public AnimationClip SpellR;
        public AnimationClip SpellF;

        /// <summary>
        /// Look up a clip by animation name (matches GLB embedded clip names).
        /// Returns null if not found.
        /// </summary>
        public AnimationClip GetClipByName(string name)
        {
            return name switch
            {
                "idle" => Idle,
                "run" => Run,
                "jump" => Jump,
                "fall" => Fall,
                "land" => Land,
                "attack_1" => Attack1,
                "attack_2" => Attack2,
                "attack_3" => Attack3,
                "dash" => Dash,
                "hit_small" => HitSmall,
                "hit_large" => HitLarge,
                "death" => Death,
                "spell_q" => SpellQ,
                "spell_e" => SpellE,
                "spell_r" => SpellR,
                "spell_f" => SpellF,
                _ => null,
            };
        }
    }
}
