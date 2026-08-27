using System;
using UnityEngine;
using SlopArena.Shared;

namespace SlopArena.Client.Animation
{
    [CreateAssetMenu(fileName = "CharacterAssetCatalog", menuName = "SlopArena/Character Asset Catalog")]
    public sealed class CharacterAssetCatalog : ScriptableObject
    {
        public const ushort SchemaVersion = 1;

        [SerializeField] private string _packageId = "";
        [SerializeField] private ushort _catalogSchemaVersion = SchemaVersion;
        [SerializeField] private GameObject _rig;
        [SerializeField] private int _sampleRate = 60;
        [SerializeField] private AnimationBinding[] _bindings = Array.Empty<AnimationBinding>();

        public string PackageId { get => _packageId; set => _packageId = value; }
        public ushort CatalogSchemaVersion { get => _catalogSchemaVersion; set => _catalogSchemaVersion = value; }
        public GameObject Rig { get => _rig; set => _rig = value; }
        public int SampleRate { get => _sampleRate; set => _sampleRate = value; }
        public AnimationBinding[] Bindings { get => _bindings ?? Array.Empty<AnimationBinding>(); set => _bindings = value ?? Array.Empty<AnimationBinding>(); }

        [Serializable]
        public sealed class AnimationBinding
        {
            [SerializeField] private string _semanticId = "";
            [SerializeField] private AnimationClip _clip;
            [SerializeField] private ExtrapolationMode _extrapolation;
            [SerializeField] private string _poseTrackId = "";

            public string SemanticId { get => _semanticId; set => _semanticId = value; }
            public AnimationClip Clip { get => _clip; set => _clip = value; }
            public ExtrapolationMode Extrapolation { get => _extrapolation; set => _extrapolation = value; }
            public string PoseTrackId { get => _poseTrackId; set => _poseTrackId = value; }
        }
    }

}
