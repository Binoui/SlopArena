using UnityEngine;
using System.Collections.Generic;
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

        [Header("Combat")]
        public AnimationClip Attack1;
        public AnimationClip Attack2;
        public AnimationClip Attack3;
        public AnimationClip Dash;
        public AnimationClip HitSmall;
        public AnimationClip HitMedium;
        public AnimationClip HitHard;
        public AnimationClip Death;

        [Header("Abilities")]
        public AnimationClip SpellQStart;
        public AnimationClip SpellQLoop;
        public AnimationClip SpellQEnd;
        public AnimationClip SpellE;
        public AnimationClip SpellRStart;
        public AnimationClip SpellRLoop;
        public AnimationClip SpellREnd;
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
                "attack_1" => Attack1,
                "attack_2" => Attack2,
                "attack_3" => Attack3,
                "dash" => Dash,
                "hit_small" => HitSmall,
                "hit_medium" => HitMedium,
                "hit_hard" => HitHard,
                "hit_light" => HitSmall,
                "death" => Death,
                "spell_q_start" => SpellQStart,
                "spell_q_loop" => SpellQLoop,
                "spell_q_end" => SpellQEnd,
                "spell_q" => SpellQStart,
                "spell_e" => SpellE,
                "spell_r_start" => SpellRStart,
                "spell_r_loop" => SpellRLoop,
                "spell_r_end" => SpellREnd,
                "spell_f" => SpellF,
                "spell_lmb_1" => Attack1,
                "spell_lmb_2" => Attack2,
                "spell_lmb_3" => Attack3,
                _ => null,
            };
        }
    }
}

