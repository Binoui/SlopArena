using System;
using System.IO;
using Unity.Cinemachine;
using UnityEngine;
using SlopArena.Shared;
using SlopArena.Client.Camera;
using SlopArena.Client.Combat;
using SlopArena.Client.UI;
using SlopArena.Client.Input;
using SlopArena.Client.Entities;
using SlopArena.Client.Simulation;

namespace SlopArena.Client.World
{
    public abstract class MatchBase : MonoBehaviour
    {
        // Subclasses provide the bridge (local sim or network)
        protected abstract ISimulationBridge Bridge { get; }

        [Header("Entities")]
        [SerializeField] protected PlayerRenderer _playerRenderer;

        [Header("Input")]
        [SerializeField] protected InputController _inputController;
        [SerializeField] protected CameraMount _cameraMount;
        [Header("Aiming")]
        [SerializeField] protected AimHandler _aimHandler;
        [SerializeField] protected HUDManager _hudManager;
        [SerializeField] private Texture2D _crosshairTexture;
        [SerializeField] private float _crosshairSize = 32f;

        protected const ulong PlayerEntityId = 1;

        protected bool _showCrosshair;
        protected CharacterDefinition _playerDef = null!;
        protected UnityEngine.Camera _mainCamera;

        protected abstract void OnMatchStart();
        protected abstract void OnMatchFixedUpdate();

        private void Start() => OnMatchStart();
        private void FixedUpdate() => OnMatchFixedUpdate();

        // ── Shared setup helpers ────────────────────────────────────────────

        protected void SetupPlayerRenderer(CharacterDefinition def, BakedAnimationData? baked)
        {
            _playerRenderer.ModelYOffset = def.ModelYOffset;
            _playerRenderer.CapsuleRadius = def.CapsuleRadius;
            _playerRenderer.CapsuleHeight = def.CapsuleHeight;
            _playerRenderer.HurtboxBoneDefs = def.HurtboxBoneDefs;
            _playerRenderer.SetBakedData(baked);
            _playerRenderer.SetCharacterDefinition(def);
            _playerRenderer.LoadModel(def);
            _playerRenderer.GetComponent<WeaponAttach>()
                ?.Init(_playerRenderer, Resources.Load<WeaponAttachConfig>($"WeaponConfigs/{def.Class}"));
        }

        protected void SetupCamera()
        {
            if (_cameraMount == null) return;
            _cameraMount.SetTarget(_playerRenderer.transform);
            _cameraMount.ResetView(_playerRenderer.transform);
            var brain = FindFirstObjectByType<CinemachineBrain>();
            if (brain != null)
                brain.DefaultBlend = new CinemachineBlendDefinition(
                    CinemachineBlendDefinition.Styles.EaseInOut, 0.2f);
        }

        protected void SetupHUD(CharacterDefinition def)
        {
            _hudManager?.Initialize(() => Bridge.GetState(PlayerEntityId));
            _hudManager?.SetCharacterDefinition(def);
            for (int slot = 0; slot < 6; slot++)
            {
                var spec = def.GetSlotAbility(slot, airborne: false);
                if (spec != null) _hudManager?.SetSlotMaxCooldown(slot, spec.CooldownTicks);
                var specAir = def.GetSlotAbility(slot, airborne: true);
                if (specAir != null && specAir.CooldownTicks > spec?.CooldownTicks)
                    _hudManager?.SetSlotMaxCooldown(slot, specAir.CooldownTicks);
            }
        }

        protected void SetupAimHandler(CharacterDefinition def)
        {
            _aimHandler?.Init(_cameraMount, _cameraMount?.RenderCamera,
                _playerRenderer.transform, def.CapsuleHeight);
        }

        /// <summary>
        /// Pick the nearest enemy within 20m that is closest to screen center.
        /// Returns entity ID (cast to byte) or 0 if none found.
        /// </summary>
        protected byte PickScreenTarget(PlayerRenderer[] renderers, UnityEngine.Camera cam)
        {
            if (cam == null || renderers == null || renderers.Length == 0 || _playerRenderer == null)
                return 0;

            byte bestId = 0;
            float bestScreenDist = float.MaxValue;
            Vector2 screenCenter = new(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f);
            Vector3 playerPos = _playerRenderer.transform.position;

            foreach (var renderer in renderers)
            {
                if (renderer == null || renderer == _playerRenderer || renderer.EntityId == 0)
                    continue;

                Vector3 worldPos = renderer.transform.position;
                Vector3 screenPos3 = cam.WorldToScreenPoint(worldPos);
                if (screenPos3.z < 0) continue;

                float screenDist = Vector2.Distance(new Vector2(screenPos3.x, screenPos3.y), screenCenter);
                float worldDist = Vector3.Distance(playerPos, worldPos);

                if (screenDist < bestScreenDist && worldDist <= 20f)
                {
                    bestScreenDist = screenDist;
                    bestId = (byte)renderer.EntityId;
                }
            }
            return bestId;
        }

        protected virtual void OnGUI()
        {
            if (!_showCrosshair) return;
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            if (_crosshairTexture != null)
            {
                float half = _crosshairSize * 0.5f;
                GUI.DrawTexture(new Rect(cx - half, cy - half, _crosshairSize, _crosshairSize), _crosshairTexture);
            }
            else
            {
                var style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 28,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white }
                };
                GUI.Label(new Rect(cx - 20, cy - 20, 40, 40), "+", style);
            }
        }

        // ── Static utilities ────────────────────────────────────────────────

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
            try
            {
                return BakedAnimationData.LoadFromBin(File.ReadAllBytes(path));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MatchBase] Failed to load baked data from {path}: {ex.Message}. Falling back to capsule hurtboxes.");
                return null;
            }
        }
    }
}
