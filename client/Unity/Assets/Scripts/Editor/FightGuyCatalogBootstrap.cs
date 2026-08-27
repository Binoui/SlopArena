using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SlopArena.Client.Animation;

public static class FightGuyCatalogBootstrap
{
    [MenuItem("Tools/SlopArena/Create FightGuy Catalog")]
    public static void CreateFightGuyCatalog()
    {
        const string path = "Assets/CharacterPackages/FightGuy/CharacterAssetCatalog.asset";
        var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(path);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<CharacterAssetCatalog>();
            AssetDatabase.CreateAsset(catalog, path);
        }
        catalog.PackageId = "fightguy";
        catalog.CatalogSchemaVersion = CharacterAssetCatalog.SchemaVersion;
        catalog.SampleRate = 60;
        catalog.Rig = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Characters/FightGuy.prefab");

        var bindings = new List<CharacterAssetCatalog.AnimationBinding>();
        Add(bindings, "anim.idle", "Assets/Art/Characters/shared/Animations/Idle_Combat.anim");
        Add(bindings, "anim.run", "Assets/Art/Characters/fightguy/Animations/fightguy_run.anim");
        Add(bindings, "anim.dash", "Assets/Art/Characters/shared/Animations/dash.anim");
        Add(bindings, "anim.jump", "Assets/Art/Characters/fightguy/Animations/fightguy_jump_up.anim");
        Add(bindings, "anim.fall", "Assets/Art/Characters/fightguy/Animations/fall.anim");
        Add(bindings, "anim.hit-light", "Assets/Art/Characters/shared/Animations/hit_light.anim");
        Add(bindings, "anim.hit-medium", "Assets/Art/Characters/shared/Animations/hit_medium.anim");
        Add(bindings, "anim.hit-hard", "Assets/Art/Characters/shared/Animations/hit_hard.anim");
        Add(bindings, "anim.low-kick", "Assets/Art/Characters/fightguy/Animations/fightguy_g_1.anim");
        Add(bindings, "anim.double-punch", "Assets/Art/Characters/fightguy/Animations/fightguy_a_1.anim");
        Add(bindings, "anim.straight-punch", "Assets/Art/Characters/fightguy/Animations/fightguy_g_2.anim");
        Add(bindings, "anim.floating-kick", "Assets/Art/Characters/fightguy/Animations/fightguy_a_2.anim");
        Add(bindings, "anim.sweeping-kick", "Assets/Art/Characters/fightguy/Animations/fightguy_g_3.anim");
        Add(bindings, "anim.high-kick", "Assets/Art/Characters/fightguy/Animations/fightguy_a_3.anim");
        Add(bindings, "anim.double-kick", "Assets/Art/Characters/fightguy/Animations/fightguy_g_4.anim");
        Add(bindings, "anim.air-smash", "Assets/Art/Characters/fightguy/Animations/fightguy_a_4.anim");
        Add(bindings, "anim.ki-shot", LoadImportedClip("Assets/Art/Characters/fightguy/Animations/fightguy_spell_q_attack.fbx"));
        Add(bindings, "anim.rising-dragon", "Assets/Art/Characters/fightguy/Animations/fightguy_spell_e.anim");
        Add(bindings, "anim.cyclone-kick", "Assets/Art/Characters/fightguy/Animations/fightguy_spell_r.anim");
        Add(bindings, "anim.dragon-beam", "Assets/Art/Characters/fightguy/Animations/fightguy_spell_f_2.anim");
        catalog.Bindings = bindings.ToArray();
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Created explicit FightGuy catalog with {bindings.Count} bindings.");
    }

    private static void Add(List<CharacterAssetCatalog.AnimationBinding> bindings, string id, string path)
        => Add(bindings, id, AssetDatabase.LoadAssetAtPath<AnimationClip>(path));

    private static void Add(List<CharacterAssetCatalog.AnimationBinding> bindings, string id, AnimationClip clip)
    {
        if (clip == null) throw new InvalidOperationException($"Missing clip for {id}.");
        bindings.Add(new CharacterAssetCatalog.AnimationBinding
        {
            SemanticId = id,
            PoseTrackId = id,
            Clip = clip,
            Extrapolation = id == "anim.ki-shot" ? SlopArena.Shared.ExtrapolationMode.Continuous : SlopArena.Shared.ExtrapolationMode.None,
        });
    }

    private static AnimationClip LoadImportedClip(string path)
    {
        const long expectedLocalFileId = 3094330708855449807L;
        foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
        {
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string _, out long localId) &&
                localId == expectedLocalFileId)
                return clip;
        }
        throw new InvalidOperationException($"Expected FightGuy Ki Shot clip sub-asset is missing at {path}.");
    }
}
