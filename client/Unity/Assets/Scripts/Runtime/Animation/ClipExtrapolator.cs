using SlopArena.Shared;
using UnityEngine;
using System.Collections.Generic;
namespace SlopArena.Client.Animation
{
    /// <summary>
    /// Extrapolates bone positions past a clip's natural end using baked skeleton data.
    /// V1: positions only (from BakedAnimationData.Frames float[]). Rotation holds last
    /// keyframe via Animancer's default behavior.
    /// </summary>
    public class ClipExtrapolator
    {
        private struct BoneExtrapData
        {
            public string BoneName;
            public Vector3 LastPosition;
            public Vector3 Velocity; // (lastFrame - secondLastFrame) * frameRate
        }

        private BoneExtrapData[] _bones;

        private ClipExtrapolator() { }

        /// <summary>
        /// Build extrapolation data from baked animation frames.
        /// Returns null if the animation is not found or has fewer than 2 frames.
        /// </summary>
        public static ClipExtrapolator? FromBakedData(BakedAnimationData data, string animName, float frameRate = 30f)
        {
            int animIdx = data.FindAnimIndex(animName);
            if (animIdx < 0) return null;
            var anim = data.Animations[animIdx];
            if (anim.FrameCount < 2) return null;

            int last = anim.FrameCount - 1;
            int prev = anim.FrameCount - 2;
            int boneCount = data.BoneNames.Length;
            var result = new ClipExtrapolator();
            result._bones = new BoneExtrapData[boneCount];
            float[] lastFrame = anim.Frames[last];
            float[] prevFrame = anim.Frames[prev];

            for (int b = 0; b < boneCount; b++)
            {
                int i = b * 3;
                result._bones[b] = new BoneExtrapData
                {
                    BoneName = data.BoneNames[b],
                    LastPosition = new Vector3(lastFrame[i], lastFrame[i + 1], lastFrame[i + 2]),
                    Velocity = new Vector3(
                        (lastFrame[i] - prevFrame[i]) * frameRate,
                        (lastFrame[i + 1] - prevFrame[i + 1]) * frameRate,
                        (lastFrame[i + 2] - prevFrame[i + 2]) * frameRate)
                };
            }
            return result;
        }

        /// <summary>
        /// Apply extrapolated bone positions to the hierarchy.
        /// ExtraTime is seconds past the clip's natural end.
        /// </summary>
        public void Apply(Transform rootBone, float extraTime)
        {
            if (rootBone == null || _bones == null) return;
            var boneMap = new Dictionary<string, Transform>();
            foreach (var t in rootBone.GetComponentsInChildren<Transform>(true))
                boneMap[t.name] = t;

            foreach (var bone in _bones)
            {
                if (!boneMap.TryGetValue(bone.BoneName, out var t)) continue;
                t.localPosition = bone.LastPosition + bone.Velocity * extraTime;
            }
        }
    }
}
