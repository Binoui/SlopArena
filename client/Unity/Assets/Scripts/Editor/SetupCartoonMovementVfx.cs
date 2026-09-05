using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using SlopArena.Client.Combat;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.Editor
{
    /// <summary>
    /// Regenerates local SlopArena movement-VFX variants from Cartoon FX Remaster Free.
    /// Cartoon FX is an optional Asset Store dependency and is never committed.
    /// </summary>
    public static class SetupCartoonMovementVfx
    {
        private const string Destination = "Assets/Resources/VFX/SlopArena";

        [MenuItem("Tools/SlopArena/Setup Cartoon Movement VFX")]
        public static void Setup()
        {
            string poofSource = FindPrefab("CFXR Magic Poof");
            string windSource = FindPrefab("CFXR4 Wind Trails");
            string ringSource = FindPrefab("CFXR Water Ripples");
            string groundSource = FindPrefab("CFXR2 Ground Hit");
            string smashSource = FindPrefab("CFXR Hit A (Red) + Text") ?? FindPrefab("CFXR _SMASH_");
            string fireExplosionSource = FindPrefab("CFXR3 Fire Explosion A (no smoke)");
            if (poofSource == null || windSource == null || ringSource == null || groundSource == null
                || fireExplosionSource == null)
                throw new InvalidOperationException(
                    "Cartoon FX Remaster Free is missing. Import it from the Unity Asset Store first.");

            ConfigureShaders(poofSource);
            Directory.CreateDirectory(Destination);
            Copy(poofSource, "MovementMagicPoof");
            Copy(windSource, "MovementWindTrails");
            Copy(ringSource, "MovementAirRing");
            Copy(groundSource, "MovementGroundHit");
            if (smashSource != null)
                Copy(smashSource, "MatchTextSmash");
            TunePoof();
            CopyAndConfigureMankiExplosion(fireExplosionSource);
            TuneWind();
            TuneRing();
            TuneGroundRing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SlopArena] Cartoon movement VFX generated under {Destination}.");
        }

        [MenuItem("Tools/SlopArena/Setup Manki R Explosion VFX")]
        public static void SetupMankiRExplosionVfx()
        {
            string source = FindPrefab("CFXR Explosion Smoke 2 (HDR)");
            if (source == null)
                throw new InvalidOperationException(
                    "CFXR Explosion Smoke 2 (HDR) is missing. Import Cartoon FX Remaster first.");

            ConfigureShaders(source);
            Directory.CreateDirectory(Destination);
            CopyAndConfigureMankiRExplosion(source);
            RegisterMankiExplosionOverride(AbilitySlots.R, "MankiRExplosionSmoke", 0.65f);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SlopArena] Manki R explosion VFX configured from {source}.");
        }

        private static string FindPrefab(string name)
        {
            return AssetDatabase.FindAssets($"{name} t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(path => Path.GetFileNameWithoutExtension(path) == name);
        }

        private static void ConfigureShaders(string sourcePrefab)
        {
            const string marker = "/CFXR Prefabs/";
            int markerIndex = sourcePrefab.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
                return;

            string packageRoot = sourcePrefab.Substring(0, markerIndex);
            string shaderDirectory = $"{packageRoot}/CFXR Assets/Shaders";
            string settingsPath = $"{shaderDirectory}/CFXR_SETTINGS.cginc";
            if (File.Exists(settingsPath))
            {
                string settings = File.ReadAllText(settingsPath);
                const string disabled = "// #define GLOBAL_DISABLE_SOFT_PARTICLES";
                if (settings.Contains(disabled))
                    File.WriteAllText(settingsPath,
                        settings.Replace(disabled, "#define GLOBAL_DISABLE_SOFT_PARTICLES"));
            }

            if (!Directory.Exists(shaderDirectory))
                return;
            foreach (string shader in Directory.GetFiles(shaderDirectory, "*.cfxrshader"))
                AssetDatabase.ImportAsset(shader, ImportAssetOptions.ForceUpdate);
        }

        private static void Copy(string source, string destinationName, bool removeRuntimeEffect = true)
        {
            string destination = $"{Destination}/{destinationName}.prefab";
            AssetDatabase.DeleteAsset(destination);
            if (!AssetDatabase.CopyAsset(source, destination))
                throw new InvalidOperationException($"Could not copy {source} to {destination}.");

            GameObject root = PrefabUtility.LoadPrefabContents(destination);
            if (removeRuntimeEffect)
            {
                foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true)
                             .Where(component => component != null
                                 && component.GetType().FullName == "CartoonFX.CFXR_Effect"))
                    UnityEngine.Object.DestroyImmediate(component, true);
            }
            PrefabUtility.SaveAsPrefabAsset(root, destination);
            PrefabUtility.UnloadPrefabContents(root);
        }

        private static void TunePoof()
        {
            EditPrefab("MovementMagicPoof", root =>
            {
                foreach (string childName in new[] { "Stars", "Lines" })
                {
                    Transform child = root.transform.Find(childName);
                    if (child != null)
                        UnityEngine.Object.DestroyImmediate(child.gameObject);
                }

                ParticleSystem particles = root.GetComponent<ParticleSystem>();
                var main = particles.main;
                main.loop = false;
                main.duration = 0.35f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.38f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.4f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.28f, 0.56f);
                main.gravityModifier = 0.08f;
                main.maxParticles = 16;
                var shape = particles.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.18f;
                var emission = particles.emission;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 10, 14) });
            });
        }

        private static void TuneWind()
        {
            EditPrefab("MovementWindTrails", root =>
            {
                ParticleSystem particles = root.GetComponent<ParticleSystem>();
                var main = particles.main;
                main.loop = false;
                main.duration = 0.16f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.3f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 8f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(0.68f, 0.86f, 1f, 0.8f),
                    new Color(0.9f, 0.96f, 1f, 1f));
                var shape = particles.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(0.9f, 0.15f, 0.1f);
                var emission = particles.emission;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8, 12) });
                var trails = particles.trails;
                trails.lifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
            });
        }

        private static void TuneRing()
        {
            EditPrefab("MovementAirRing", root =>
            {
                ParticleSystem particles = root.GetComponent<ParticleSystem>();
                var main = particles.main;
                main.loop = false;
                main.duration = 0.3f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.4f);
                main.startColor = new Color(0.45f, 0.9f, 1f, 0.9f);
            });
        }

        private static void TuneGroundRing()
        {
            EditPrefab("MovementGroundHit", root =>
            {
                ParticleSystem rootParticles = root.GetComponent<ParticleSystem>();
                if (rootParticles != null)
                    UnityEngine.Object.DestroyImmediate(rootParticles);
                ParticleSystemRenderer rootRenderer = root.GetComponent<ParticleSystemRenderer>();
                if (rootRenderer != null)
                    UnityEngine.Object.DestroyImmediate(rootRenderer);

                ParticleSystem ring = root.transform.Find("Ground ring")?.GetComponent<ParticleSystem>();
                if (ring == null)
                    return;
                var main = ring.main;
                main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.25f);
                main.startColor = new Color(0.72f, 0.84f, 0.96f, 0.85f);
            });
        }

        private static void CopyAndConfigureMankiExplosion(string source)
        {
            const string name = "MankiQExplosionFire";
            Copy(source, name);
            string path = $"{Destination}/{name}.prefab";
            EditPrefab(name, root =>
            {
                if (PrefabUtility.IsPartOfPrefabInstance(root))
                    PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

                Shader shader = Shader.Find("SlopArena/Particles/CFXR3 Fire Explosion");
                if (shader == null)
                    throw new InvalidOperationException("SlopArena CFXR3 fire explosion shader was not found.");

                var materials = new Dictionary<int, Material>();
                foreach (ParticleSystemRenderer renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    Material[] sourceMaterials = renderer.sharedMaterials;
                    var replacementMaterials = new Material[sourceMaterials.Length];
                    for (int i = 0; i < sourceMaterials.Length; i++)
                    {
                        Material sourceMaterial = sourceMaterials[i];
                        if (sourceMaterial == null)
                            continue;

                        int key = sourceMaterial.GetInstanceID();
                        if (!materials.TryGetValue(key, out Material replacement))
                        {
                            replacement = new Material(sourceMaterial)
                            {
                                name = $"MankiQExplosionFire_{materials.Count}"
                            };
                            replacement.shader = shader;
                            string materialPath = $"{Destination}/{replacement.name}.mat";
                            AssetDatabase.DeleteAsset(materialPath);
                            AssetDatabase.CreateAsset(replacement, materialPath);
                            materials.Add(key, replacement);
                        }
                        replacementMaterials[i] = replacement;
                    }
                    renderer.sharedMaterials = replacementMaterials;
                }
            });

            ProjectileVFXConfig config = Resources.Load<ProjectileVFXConfig>("VFXConfigs/ProjectileVisuals");
            if (config == null)
                throw new InvalidOperationException("Resources/VFXConfigs/ProjectileVisuals.asset was not found.");

            SerializedObject serializedConfig = new SerializedObject(config);
            SerializedProperty overrides = serializedConfig.FindProperty("ExplosionOverrides");
            for (int i = overrides.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty entry = overrides.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("Character").intValue == (int)CharacterClass.Manki
                    && entry.FindPropertyRelative("AttackSlot").intValue == AbilitySlots.A)
                    overrides.DeleteArrayElementAtIndex(i);
            }

            int index = overrides.arraySize;
            overrides.InsertArrayElementAtIndex(index);
            SerializedProperty added = overrides.GetArrayElementAtIndex(index);
            added.FindPropertyRelative("Character").intValue = (int)CharacterClass.Manki;
            added.FindPropertyRelative("AttackSlot").intValue = AbilitySlots.A;
            added.FindPropertyRelative("Prefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(path);
            added.FindPropertyRelative("Scale").floatValue = 0.2f;
            serializedConfig.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
        }

        private static void CopyAndConfigureMankiRExplosion(string source)
        {
            Copy(source, "MankiRExplosionSmoke", removeRuntimeEffect: true);
            EditPrefab("MankiRExplosionSmoke", root =>
            {
                AssignMaterial(root, "Sparks smoke", "cfxr stretch trait hdr ab");
                AssignMaterial(root, "Sub smoke", "cfxr smoke cloud x4 ab");
            });
        }

        private static void AssignMaterial(GameObject root, string rendererObjectName, string materialName)
        {
            ParticleSystemRenderer renderer = root.GetComponentsInChildren<ParticleSystemRenderer>(true)
                .FirstOrDefault(x => x.gameObject.name == rendererObjectName);
            if (renderer == null)
                throw new InvalidOperationException($"Renderer '{rendererObjectName}' was not found.");

            string materialPath = AssetDatabase.FindAssets($"{materialName} t:Material")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(path => Path.GetFileNameWithoutExtension(path) == materialName);
            Material material = materialPath == null ? null : AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
                throw new InvalidOperationException($"Material '{materialName}' was not found.");
            SerializedObject serializedRenderer = new SerializedObject(renderer);
            SerializedProperty materialArray = serializedRenderer.FindProperty("m_Materials");
            materialArray.arraySize = 1;
            materialArray.GetArrayElementAtIndex(0).objectReferenceValue = material;
            serializedRenderer.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RegisterMankiExplosionOverride(byte attackSlot, string prefabName, float scale)
        {
            ProjectileVFXConfig config = Resources.Load<ProjectileVFXConfig>("VFXConfigs/ProjectileVisuals");
            if (config == null)
                throw new InvalidOperationException("Resources/VFXConfigs/ProjectileVisuals.asset was not found.");

            SerializedObject serializedConfig = new SerializedObject(config);
            SerializedProperty overrides = serializedConfig.FindProperty("ExplosionOverrides");
            for (int i = overrides.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty entry = overrides.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("Character").intValue == (int)CharacterClass.Manki
                    && entry.FindPropertyRelative("AttackSlot").intValue == attackSlot)
                    overrides.DeleteArrayElementAtIndex(i);
            }

            int index = overrides.arraySize;
            overrides.InsertArrayElementAtIndex(index);
            SerializedProperty added = overrides.GetArrayElementAtIndex(index);
            added.FindPropertyRelative("Character").intValue = (int)CharacterClass.Manki;
            added.FindPropertyRelative("AttackSlot").intValue = attackSlot;
            added.FindPropertyRelative("Prefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>($"{Destination}/{prefabName}.prefab");
            added.FindPropertyRelative("Scale").floatValue = scale;
            serializedConfig.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
        }

        private static void EditPrefab(string name, Action<GameObject> edit)
        {
            string path = $"{Destination}/{name}.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            edit(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
