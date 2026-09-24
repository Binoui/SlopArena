using SlopArena.Shared;
using SlopArena.Client.UI;
using UnityEngine;

namespace SlopArena.Client.Combat
{
    /// <summary>Hit-confirm sound grammar with character-specific impact overrides.</summary>
    public sealed class CombatSFX : MonoBehaviour
    {
        private readonly AudioClip[] _light = new AudioClip[3];
        private readonly AudioClip[] _medium = new AudioClip[3];
        private readonly AudioClip[] _heavy = new AudioClip[2];
        private readonly AudioClip[] _kistuImpact = new AudioClip[3];
        private readonly AudioClip[] _bonkImpact = new AudioClip[3];
        private AudioSource _source;
        private int _sequence;

        public void Play(ImpactTier tier, CharacterClass character)
        {
            if (_source == null)
                return;

            AudioClip clip;
            if (character == CharacterClass.Kistu || character == CharacterClass.Bonk)
            {
                AudioClip[] impacts = character == CharacterClass.Kistu ? _kistuImpact : _bonkImpact;
                int impactIndex = Mathf.Clamp((int)tier, 0, impacts.Length - 1);
                clip = impacts[impactIndex];
            }
            else
            {
                AudioClip[] pool = tier switch
                {
                    ImpactTier.Heavy or ImpactTier.Launch => _heavy,
                    ImpactTier.Medium => _medium,
                    _ => _light
                };
                clip = pool[_sequence++ % pool.Length];
            }

            if (clip != null)
                _source.PlayOneShot(clip);
        }

        public void Play(ImpactTier tier) => Play(tier, CharacterClass.FightGuy);

        private void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.outputAudioMixerGroup = ClientSettingsService.Instance.FindBus("SFX");
            _source.volume = 0.8f;
            Load(_light, "punch_light");
            Load(_medium, "punch_medium");
            Load(_heavy, "punch_heavy");
            LoadImpact(_kistuImpact, "Kistu");
            LoadImpact(_bonkImpact, "Bonk");
        }

        private static void Load(AudioClip[] clips, string prefix)
        {
            for (int i = 0; i < clips.Length; i++)
                clips[i] = Resources.Load<AudioClip>($"Audio/SFX/{prefix}_{i + 1:00}");
        }

        private static void LoadImpact(AudioClip[] clips, string character)
        {
            clips[0] = Resources.Load<AudioClip>($"Audio/SFX/{character}/impact_light");
            clips[1] = Resources.Load<AudioClip>($"Audio/SFX/{character}/impact_medium");
            clips[2] = Resources.Load<AudioClip>($"Audio/SFX/{character}/impact_heavy");
        }
    }
}
