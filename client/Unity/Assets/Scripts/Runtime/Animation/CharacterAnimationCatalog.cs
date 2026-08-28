using System;
using UnityEngine;
using SlopArena.Shared;

namespace SlopArena.Client.Animation
{
    public sealed class CharacterAnimationCatalog : ScriptableObject
    {
        public const ushort SchemaVersion = 1;

        [SerializeField] private string _packageId = "";
        [SerializeField] private ushort _catalogSchemaVersion = SchemaVersion;
        [SerializeField] private ushort _bindingSchemaVersion = SchemaVersion;
        [SerializeField] private int _sampleRate = 60;
        [SerializeField] private string _sourceHash = "";
        [SerializeField] private GameObject _rig;
        [SerializeField] private SlopArena.Client.Entities.WeaponAttachConfig _weaponConfig;
        [SerializeField] private AnimationEntry[] _animations = Array.Empty<AnimationEntry>();

        public string PackageId { get => _packageId; set => _packageId = value; }
        public ushort CatalogSchemaVersion { get => _catalogSchemaVersion; set => _catalogSchemaVersion = value; }
        public ushort BindingSchemaVersion { get => _bindingSchemaVersion; set => _bindingSchemaVersion = value; }
        public int SampleRate { get => _sampleRate; set => _sampleRate = value; }
        public string SourceHash { get => _sourceHash; set => _sourceHash = value; }
        public GameObject Rig { get => _rig; set => _rig = value; }
        public SlopArena.Client.Entities.WeaponAttachConfig WeaponConfig { get => _weaponConfig; set => _weaponConfig = value; }
        public AnimationEntry[] Animations { get => _animations ?? Array.Empty<AnimationEntry>(); set => _animations = value ?? Array.Empty<AnimationEntry>(); }

        [Serializable]
        public sealed class AnimationEntry
        {
            [SerializeField] private string _semanticId = "";
            [SerializeField] private string _poseTrackId = "";
            [SerializeField] private AnimationClip _clip;
            [SerializeField] private int _frameCount;
            [SerializeField] private int _sampleRate = 60;
            [SerializeField] private ExtrapolationMode _extrapolation;

            public string SemanticId { get => _semanticId; set => _semanticId = value; }
            public string PoseTrackId { get => _poseTrackId; set => _poseTrackId = value; }
            public AnimationClip Clip { get => _clip; set => _clip = value; }
            public int FrameCount { get => _frameCount; set => _frameCount = value; }
            public int SampleRate { get => _sampleRate; set => _sampleRate = value; }
            public ExtrapolationMode Extrapolation { get => _extrapolation; set => _extrapolation = value; }
        }
    }
}
