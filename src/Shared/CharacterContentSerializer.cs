using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SlopArena.Shared;

/// <summary>
/// Deterministic JSON persistence for authored character definitions.
/// Runtime-only behavior and Unity objects are intentionally not serialized.
/// </summary>
public static class CharacterContentSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    private sealed class CharacterContentDocument
    {
        public int? SchemaVersion;
        public string? Id;
        public CharacterClass? Class;
        public string? DisplayName;
        public MovementStats Movement;
        public float Weight;
        public float CapsuleRadius;
        public float CapsuleHeight;
        public float HipHeight;
        public float HurtboxRadius;
        public HurtboxCapsule[]? HurtboxCapsules;
        public HurtboxBoneDef[]? HurtboxBoneDefs;
        public string? BakedDataPath;
        public string? ModelResourcePath;
        public float HurtboxBoneScale;
        public float ModelYOffset;
        public float ModelSoleOffset;
        public bool AutoModelYOffset;
        public float VisualScale;
        public string? IdleAnim;
        public string? RunAnim;
        public string? DashAnim;
        public string? JumpAnim;
        public string? FallAnim;
        public string? HitSmallAnim;
        public string? HitMediumAnim;
        public string? HitHardAnim;
        public float LandStartOffset;
        public AnimationClipConfig[]? ClipOverrides;
        public Dictionary<string, AbilitySpec?>? Abilities;
        public Dictionary<string, string>? AirAliases;
    }

    public static CharacterDefinition Load(string json)
    {
        if (json == null) throw new InvalidDataException("Invalid character content: JSON is null.");

        CharacterContentDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<CharacterContentDocument>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw InvalidJson(ex);
        }
        catch (NotSupportedException ex)
        {
            throw new InvalidDataException($"Invalid character content: {ex.Message}", ex);
        }

        if (document == null)
            throw new InvalidDataException("Invalid character content: JSON document is null.");

        ValidateEnvelope(document);
        if (document.Abilities == null)
            throw new InvalidDataException("Invalid character definition: missing abilities.");

        var abilities = new Dictionary<string, AbilitySpec?>(document.Abilities.Count);
        foreach (var pair in document.Abilities)
        {
            if (!IsAbilityName(pair.Key))
                throw InvalidAbility(pair.Key, "unknown ability key");
            if (pair.Value == null)
                throw InvalidAbility(pair.Key, "null ability entry");
            ValidateAbility(pair.Key, pair.Value);
            abilities.Add(pair.Key, pair.Value);
        }

        var definition = new CharacterDefinition
        {
            Class = document.Class!.Value,
            DisplayName = document.DisplayName ?? "",
            Movement = document.Movement,
            Weight = document.Weight,
            CapsuleRadius = document.CapsuleRadius,
            CapsuleHeight = document.CapsuleHeight,
            HipHeight = document.HipHeight,
            HurtboxRadius = document.HurtboxRadius,
            HurtboxCapsules = document.HurtboxCapsules,
            HurtboxBoneDefs = document.HurtboxBoneDefs,
            BakedDataPath = document.BakedDataPath ?? "",
            ModelResourcePath = document.ModelResourcePath ?? "",
            HurtboxBoneScale = document.HurtboxBoneScale,
            ModelYOffset = document.ModelYOffset,
            ModelSoleOffset = document.ModelSoleOffset,
            AutoModelYOffset = document.AutoModelYOffset,
            VisualScale = document.VisualScale,
            IdleAnim = document.IdleAnim ?? "idle",
            RunAnim = document.RunAnim ?? "run",
            DashAnim = document.DashAnim ?? "dash",
            JumpAnim = document.JumpAnim ?? "jump",
            FallAnim = document.FallAnim ?? "fall",
            HitSmallAnim = document.HitSmallAnim ?? "hit_light",
            HitMediumAnim = document.HitMediumAnim ?? "hit_medium",
            HitHardAnim = document.HitHardAnim ?? "hit_hard",
            LandStartOffset = document.LandStartOffset,
            ClipOverrides = document.ClipOverrides,
        };

        foreach (var pair in abilities)
            SetAbility(definition, pair.Key, pair.Value!);

        if (document.AirAliases != null)
        {
            foreach (var alias in document.AirAliases)
            {
                if (!IsAirAbilityName(alias.Key) || !IsGroundAbilityName(alias.Value)
                    || !abilities.TryGetValue(alias.Value, out var target) || target == null)
                {
                    throw new InvalidDataException(
                        $"Invalid ability definition alias '{alias.Key}' -> '{alias.Value}': invalid alias target.");
                }

                SetAbility(definition, alias.Key, target);
            }
        }
        return definition;
    }

    public static CharacterDefinition LoadFile(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
        {
            throw new InvalidDataException($"Failed to read character content '{path}': {ex.Message}", ex);
        }

        try
        {
            return Load(json);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"Failed to load character content '{path}': {ex.Message}", ex);
        }
    }

    public static string Serialize(string id, CharacterDefinition definition)
    {
        if (definition == null) throw new InvalidDataException("Invalid character definition: definition is null.");
        ValidateIdentity(id, definition.Class);

        var document = new CharacterContentDocument
        {
            SchemaVersion = 1,
            Id = id,
            Class = definition.Class,
            DisplayName = definition.DisplayName,
            Movement = definition.Movement,
            Weight = definition.Weight,
            CapsuleRadius = definition.CapsuleRadius,
            CapsuleHeight = definition.CapsuleHeight,
            HipHeight = definition.HipHeight,
            HurtboxRadius = definition.HurtboxRadius,
            HurtboxCapsules = definition.HurtboxCapsules,
            HurtboxBoneDefs = definition.HurtboxBoneDefs,
            BakedDataPath = definition.BakedDataPath,
            ModelResourcePath = definition.ModelResourcePath,
            HurtboxBoneScale = definition.HurtboxBoneScale,
            ModelYOffset = definition.ModelYOffset,
            ModelSoleOffset = definition.ModelSoleOffset,
            AutoModelYOffset = definition.AutoModelYOffset,
            VisualScale = definition.VisualScale,
            IdleAnim = definition.IdleAnim,
            RunAnim = definition.RunAnim,
            DashAnim = definition.DashAnim,
            JumpAnim = definition.JumpAnim,
            FallAnim = definition.FallAnim,
            HitSmallAnim = definition.HitSmallAnim,
            HitMediumAnim = definition.HitMediumAnim,
            HitHardAnim = definition.HitHardAnim,
            LandStartOffset = definition.LandStartOffset,
            ClipOverrides = definition.ClipOverrides,
            Abilities = CreateAbilityMap(definition, out var aliases),
            AirAliases = aliases,
        };

        return JsonSerializer.Serialize(document, SerializerOptions);
    }

    public static void SaveFile(string path, string id, CharacterDefinition definition)
    {
        string json = Serialize(id, definition);
        try
        {
            File.WriteAllText(path, json);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
        {
            throw new InvalidDataException($"Failed to write character content '{path}': {ex.Message}", ex);
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        return options;
    }

    private static void ValidateEnvelope(CharacterContentDocument document)
    {
        if (!document.SchemaVersion.HasValue)
            throw new InvalidDataException("Missing character schemaVersion.");
        if (document.SchemaVersion.Value != 1)
            throw new InvalidDataException($"Unsupported character schemaVersion {document.SchemaVersion.Value}.");
        if (string.IsNullOrWhiteSpace(document.Id))
            throw new InvalidDataException("Missing character id.");
        if (!document.Class.HasValue || document.Class.Value == CharacterClass.None)
            throw new InvalidDataException("Invalid character class.");
        ValidateIdentity(document.Id, document.Class.Value);
    }

    private static void ValidateIdentity(string id, CharacterClass characterClass)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException("Missing character id.");
        if (characterClass == CharacterClass.None)
            throw new InvalidDataException("Invalid character class.");

        string expectedId = characterClass.ToString().ToLowerInvariant();
        if (!string.Equals(id, expectedId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Character id '{id}' does not agree with class '{characterClass}' (expected '{expectedId}').");
        }
    }

    private static Dictionary<string, AbilitySpec?> CreateAbilityMap(
        CharacterDefinition definition, out Dictionary<string, string> aliases)
    {
        var abilities = new Dictionary<string, AbilitySpec?>();
        aliases = new Dictionary<string, string>();

        AddAbility(abilities, "lmb", definition.LMB);
        AddAbility(abilities, "rmb", definition.RMB);
        AddAbility(abilities, "airLmb", definition.AirLMB);
        AddAbility(abilities, "airRmb", definition.AirRMB);
        AddAbility(abilities, "slot1", definition.Slot1);
        AddAbility(abilities, "airSlot1", definition.AirSlot1);
        AddAbility(abilities, "e", definition.E);
        AddAbility(abilities, "airE", definition.AirE);
        AddAbility(abilities, "r", definition.R);
        AddAbility(abilities, "airR", definition.AirR);
        AddAbility(abilities, "f", definition.F);
        AddAbility(abilities, "airF", definition.AirF);
        AddAbility(abilities, "slot2", definition.Slot2);
        AddAbility(abilities, "airSlot2", definition.AirSlot2);
        AddAbility(abilities, "slot3", definition.Slot3);
        AddAbility(abilities, "airSlot3", definition.AirSlot3);
        AddAbility(abilities, "slot4", definition.Slot4);
        AddAbility(abilities, "airSlot4", definition.AirSlot4);
        AddAbility(abilities, "slot5", definition.Slot5);
        AddAbility(abilities, "airSlot5", definition.AirSlot5);
        AddAbility(abilities, "a", definition.A);
        AddAbility(abilities, "airA", definition.AirA);

        AddAliasIfShared(aliases, "airLmb", "lmb", definition.AirLMB, definition.LMB);
        AddAliasIfShared(aliases, "airRmb", "rmb", definition.AirRMB, definition.RMB);
        AddAliasIfShared(aliases, "airE", "e", definition.AirE, definition.E);
        AddAliasIfShared(aliases, "airR", "r", definition.AirR, definition.R);
        AddAliasIfShared(aliases, "airF", "f", definition.AirF, definition.F);
        AddAliasIfShared(aliases, "airA", "a", definition.AirA, definition.A);

        return abilities;
    }

    private static void AddAbility(Dictionary<string, AbilitySpec?> abilities, string name, AbilitySpec? ability)
    {
        if (ability != null) ValidateAbility(name, ability);
        if (ability != null) abilities.Add(name, ability);
    }

    private static void AddAliasIfShared(
        Dictionary<string, string> aliases, string airName, string groundName,
        AbilitySpec? airAbility, AbilitySpec? groundAbility)
    {
        if (airAbility != null && ReferenceEquals(airAbility, groundAbility))
            aliases.Add(airName, groundName);
    }

    private static void ValidateAbility(string name, AbilitySpec ability)
    {
        if (ability.Stages == null)
            throw InvalidAbility(name, "missing stages");
    }

    private static InvalidDataException InvalidAbility(string name, string reason)
        => new($"Invalid ability definition '{name}': {reason}.");

    private static bool IsAbilityName(string name) => name switch
    {
        "lmb" or "rmb" or "airLmb" or "airRmb" or "slot1" or "airSlot1"
            or "e" or "airE" or "r" or "airR" or "f" or "airF"
            or "slot2" or "airSlot2" or "slot3" or "airSlot3"
            or "slot4" or "airSlot4" or "slot5" or "airSlot5"
            or "a" or "airA" => true,
        _ => false,
    };

    private static bool IsAirAbilityName(string name) => name switch
    {
        "airLmb" or "airRmb" or "airSlot1" or "airE" or "airR" or "airF"
            or "airSlot2" or "airSlot3" or "airSlot4" or "airSlot5" or "airA" => true,
        _ => false,
    };

    private static bool IsGroundAbilityName(string name) => name switch
    {
        "lmb" or "rmb" or "slot1" or "e" or "r" or "f" or "slot2"
            or "slot3" or "slot4" or "slot5" or "a" => true,
        _ => false,
    };

    private static void SetAbility(CharacterDefinition definition, string name, AbilitySpec ability)
    {
        switch (name)
        {
            case "lmb": definition.LMB = ability; break;
            case "rmb": definition.RMB = ability; break;
            case "airLmb": definition.AirLMB = ability; break;
            case "airRmb": definition.AirRMB = ability; break;
            case "slot1": definition.Slot1 = ability; break;
            case "airSlot1": definition.AirSlot1 = ability; break;
            case "e": definition.E = ability; break;
            case "airE": definition.AirE = ability; break;
            case "r": definition.R = ability; break;
            case "airR": definition.AirR = ability; break;
            case "f": definition.F = ability; break;
            case "airF": definition.AirF = ability; break;
            case "slot2": definition.Slot2 = ability; break;
            case "airSlot2": definition.AirSlot2 = ability; break;
            case "slot3": definition.Slot3 = ability; break;
            case "airSlot3": definition.AirSlot3 = ability; break;
            case "slot4": definition.Slot4 = ability; break;
            case "airSlot4": definition.AirSlot4 = ability; break;
            case "slot5": definition.Slot5 = ability; break;
            case "airSlot5": definition.AirSlot5 = ability; break;
            case "a": definition.A = ability; break;
            case "airA": definition.AirA = ability; break;
            default: throw InvalidAbility(name, "unknown ability key");
        }
    }

    private static InvalidDataException InvalidJson(JsonException ex)
    {
        string path = string.IsNullOrEmpty(ex.Path) ? "" : $" at {ex.Path}";
        return new InvalidDataException($"Invalid character content{path}: {ex.Message}", ex);
    }
}
