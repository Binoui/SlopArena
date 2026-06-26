using System.IO;
using UnityEngine;
using SlopArena.Shared;

namespace SlopArena.Client.World
{
    public abstract class MatchBase : MonoBehaviour
    {
        protected abstract void OnMatchStart();
        protected abstract void OnMatchFixedUpdate();

        private void Start() => OnMatchStart();
        private void FixedUpdate() => OnMatchFixedUpdate();

        protected static ArenaDefinition LoadArenaFromFile(string path)
        {
            if (File.Exists(path))
            {
                var result = ArenaBinaryFormat.LoadFromFile(path);
                if (result.HasValue) return result.Value;
            }
            return ArenaRegistry.Get("training");
        }

        protected static BakedAnimationData? LoadBakedData(CharacterDefinition def)
        {
            if (string.IsNullOrEmpty(def.BakedDataPath)) return null;
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", def.BakedDataPath.Replace("res://", "")));
            if (!File.Exists(path)) return null;
            return BakedAnimationData.LoadFromBin(File.ReadAllBytes(path));
        }
    }
}
