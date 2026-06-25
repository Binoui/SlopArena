using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using SlopArena.Client.World;
using SlopArena.Client.Entities;
using SlopArena.Client.Input;
using SlopArena.Client.Camera;
using Unity.Cinemachine;
using SlopArena.Shared;

namespace SlopArena.Client.Editor
{
    public static class SlopArenaSceneSetup
    {
        [MenuItem("Tools/SlopArena/Create Offline Scene")]
        public static void CreateOfflineScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            scene.name = "Arena_Offline";

            // Remove default camera
            var defaultCamera = GameObject.Find("Main Camera");
            if (defaultCamera != null) Object.DestroyImmediate(defaultCamera);

            // ── 1. Directional Light ──
            var lightGO = new GameObject("Directional Light", typeof(Light));
            lightGO.GetComponent<Light>().type = LightType.Directional;
            lightGO.GetComponent<Light>().intensity = 1.5f;
            lightGO.transform.rotation = Quaternion.Euler(50, -30, 0);
            if (lightGO.GetComponent<UniversalAdditionalLightData>() == null)
                lightGO.AddComponent<UniversalAdditionalLightData>();

            // ── 2. Floor ──
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.position = new Vector3(0, -0.25f, 0);
            floor.transform.localScale = new Vector3(50, 0.5f, 50);

            // ── 3. CameraMount ──
            var camMountGO = new GameObject("CameraMount");
            var cmCam = camMountGO.AddComponent<CinemachineCamera>();
            var orbital = camMountGO.AddComponent<CinemachineOrbitalFollow>();
            orbital.VerticalAxis.Range = new Vector2(0.5f, 6f);
            orbital.VerticalAxis.Value = 2f;
            orbital.HorizontalAxis.Range = new Vector2(3f, 15f);
            orbital.HorizontalAxis.Value = 8f;
            var panTilt = camMountGO.AddComponent<CinemachinePanTilt>();
            var camMount = camMountGO.AddComponent<CameraMount>();
            var camGO = new GameObject("Main Camera", typeof(UnityEngine.Camera), typeof(AudioListener));
            camGO.tag = "MainCamera";
            camGO.transform.SetParent(camMountGO.transform);
            camGO.transform.localPosition = Vector3.zero;
            camGO.transform.localRotation = Quaternion.identity;
            camGO.AddComponent<CinemachineBrain>();

            // ── 4. Player ──
            var playerGO = new GameObject("Player");
            playerGO.transform.position = new Vector3(0, 5, 5);
            var playerAnimator = playerGO.AddComponent<Animator>();
            var playerRenderer = playerGO.AddComponent<PlayerRenderer>();
            playerRenderer.EntityName = "Player";
            playerRenderer.EntityId = 1;
            var playerSo = new SerializedObject(playerRenderer);
            playerSo.FindProperty("_animator").objectReferenceValue = playerAnimator;
            playerSo.ApplyModifiedProperties();
            playerGO.AddComponent<InputController>();
            camMount.SetTarget(playerGO.transform);

            // ── 5. Training Dummy ──
            var dummyGO = new GameObject("TrainingDummy");
            dummyGO.transform.position = new Vector3(0, 5, -5);
            dummyGO.transform.rotation = Quaternion.Euler(0, 180, 0);
            var dummyAnimator = dummyGO.AddComponent<Animator>();
            var dummyRenderer = dummyGO.AddComponent<PlayerRenderer>();
            dummyRenderer.EntityName = "TrainingDummy";
            dummyRenderer.EntityId = 2;
            var dummySo = new SerializedObject(dummyRenderer);
            dummySo.FindProperty("_animator").objectReferenceValue = dummyAnimator;
            dummySo.ApplyModifiedProperties();

            // ── 6. GameManager ──
            var gmGO = new GameObject("GameManager");
            var gm = gmGO.AddComponent<GameManager>();
            var gmSo = new SerializedObject(gm);
            gmSo.FindProperty("_offlineMode").boolValue = true;
            gmSo.FindProperty("_playerRenderer").objectReferenceValue = playerRenderer;
            gmSo.FindProperty("_opponentRenderer").objectReferenceValue = dummyRenderer;
            gmSo.ApplyModifiedProperties();

            // ── Save ──
            string path = "Assets/Scenes/Arena_Offline.unity";
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"[SlopArena] Offline scene created at {path}");

            var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            if (asset != null)
                EditorGUIUtility.PingObject(asset);
        }
    }
}
