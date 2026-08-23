using System;
using System.IO;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.World
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            try
            {
                string? path = BakedContentPaths.ResolveFightGuyCharacter();
                if (path == null)
                {
                    throw new FileNotFoundException(
                        "Required FightGuy character content was not found at " +
                        "StreamingAssets/content/characters/fightguy/character.json or " +
                        "<repo-root>/content/characters/fightguy/character.json.");
                }

                var definition = CharacterContentSerializer.LoadFile(path);
                if (definition.Class != CharacterClass.FightGuy)
                {
                    throw new InvalidDataException(
                        $"FightGuy character content '{path}' declares class '{definition.Class}'.");
                }

                CharacterRegistry.RegisterOverride(definition);
                Debug.Log($"[Startup] Loaded FightGuy character content '{path}'.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Startup] FightGuy character content failed: {ex.Message}");
                throw;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
