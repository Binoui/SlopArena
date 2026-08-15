using System;
using System.Collections.Generic;
using System.IO;
using Unity.Cinemachine;
using UnityEngine;
using SlopArena.Shared;
using SlopArena.Client.Camera;
using SlopArena.Client.Combat;
using SlopArena.Client.UI;
using SlopArena.Client.Input;
using SlopArena.Client.Entities;
using SlopArena.Client.Animation;
using SlopArena.Client.Simulation;

namespace SlopArena.Client.World
{
    public abstract class MatchBase : MonoBehaviour
    {
        // Subclasses provide the bridge (local sim or network)
        protected abstract ISimulationBridge Bridge { get; }

        [Header("Entities")]
        [SerializeField] protected PlayerRenderer _playerRenderer;

        [Header("Player Character Assets (drag-drop — fallback to Resources.Load if empty)")]
        [SerializeField] private GameObject _playerModelPrefab;
        [SerializeField] private CharacterAnimationConfig _playerAnimConfig;
        [SerializeField] private WeaponAttachConfig _playerWeaponConfig;

        [Header("Input")]
        [SerializeField] protected InputController _inputController;
        [SerializeField] protected CameraMount _cameraMount;
        [Header("Aiming")]
        [SerializeField] protected AimHandler _aimHandler;
        [SerializeField] protected HUDManager _hudManager;
        [SerializeField] private Texture2D _crosshairTexture;
        [SerializeField] private float _crosshairSize = 32f;

        // Read from MatchConfig so PvP can use the master-assigned entity ID
        // (issue #35); training leaves it at the default 1.
        protected static ulong PlayerEntityId => MatchConfig.LocalEntityId;

        protected bool _showCrosshair;
        protected CharacterDefinition _playerDef = null!;
        protected UnityEngine.Camera _mainCamera;
        protected MatchPauseMenu _pauseMenu;

        /// <summary>True while the in-match pause menu is open (issue #77).</summary>
        protected bool IsPaused => _pauseMenu != null && _pauseMenu.IsPaused;

        protected abstract void OnMatchStart();
        protected abstract void OnMatchFixedUpdate();

        private void Start()
        {
            _pauseMenu = gameObject.AddComponent<MatchPauseMenu>();
            _pauseMenu.Init(_cameraMount, _inputController, LeaveMatch);
            OnMatchStart();
        }
        private void FixedUpdate() => OnMatchFixedUpdate();

        // ── Leave match (pause menu) ─────────────────────────────────────────

        /// <summary>
        /// Pause-menu "LEAVE MATCH": return to the stage select screen. PvPMatch
        /// overrides this to tear down its SignalR lobby connection first; the UDP
        /// NetworkClient cleans itself up on scene unload.
        /// </summary>
        protected virtual void LeaveMatch()
        {
            // StageSelect needs a usable mode without a lobby — reset to the
            // offline picker so leaving an online match doesn't strand the player
            // on a dead PvP waiting screen.
            MatchConfig.Mode = GameMode.Training;
            UnityEngine.SceneManagement.SceneManager.LoadScene("StageSelect");
        }

        // ── Shared setup helpers ────────────────────────────────────────────

        protected void SetupPlayerRenderer(CharacterDefinition def, BakedAnimationData? baked)
        {
            _playerRenderer.ModelYOffset = def.ModelYOffset;
            _playerRenderer.CapsuleRadius = def.CapsuleRadius;
            _playerRenderer.CapsuleHeight = def.CapsuleHeight;
            _playerRenderer.HurtboxBoneDefs = def.HurtboxBoneDefs;
            _playerRenderer.SetBakedData(baked);
            _playerRenderer.SetCharacterDefinition(def);

            // Use drag-dropped assets, fall back to Resources.Load by convention
            if (_playerAnimConfig != null)
                _playerRenderer.SetAnimationConfig(_playerAnimConfig);
            _playerRenderer.LoadModel(def, _playerModelPrefab);

            var weaponConfig = _playerWeaponConfig != null
                ? _playerWeaponConfig
                : Resources.Load<WeaponAttachConfig>($"WeaponConfigs/{def.Class}");
            _playerRenderer.GetComponent<WeaponAttach>()
                ?.Init(_playerRenderer, weaponConfig);
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

        /// <summary>
        /// Instantiate the stage's visual prefab (Resources/Stages/&lt;arena.Name&gt;.prefab)
        /// under a "Stage" root. Collision comes from the baked .arena; the visual is cosmetic.
        /// </summary>
        protected void SpawnStageVisual(ArenaDefinition arena)
        {
            var prefab = Resources.Load<GameObject>($"Stages/{arena.Name}");
            if (prefab == null)
            {
                Debug.LogWarning($"[{GetType().Name}] No stage visual '{arena.Name}' (missing Resources/Stages/{arena.Name}.prefab) — running collision-only.");
                return;
            }
            var stageRoot = new GameObject("Stage");
            var visual = Instantiate(prefab, stageRoot.transform);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
        }

        protected void SetupHUD(CharacterDefinition def)
        {
            // One panel per player (local + opponents), sorted by entity ID so
            // panels read P1..PN left to right regardless of who is local (issue #38).
            var players = new List<HUDManager.HudPlayer>(MatchConfig.Opponents.Count + 1)
            {
                new(PlayerEntityId, $"P{PlayerEntityId}", isLocal: true)
            };
            foreach (var opp in MatchConfig.Opponents)
                players.Add(new HUDManager.HudPlayer(opp.EntityId, $"P{opp.EntityId}", isLocal: false));
            players.Sort((a, b) => a.EntityId.CompareTo(b.EntityId));

            // Stocks only in PvP (the game server's StockMatchRule); training has
            // no win condition, so stock display is hidden (maxStocks <= 0).
            int maxStocks = MatchConfig.Mode == GameMode.PvP ? MatchConfig.MaxStocks : 0;
            _hudManager?.Initialize(Bridge.GetState, players, maxStocks);
            _hudManager?.SetCharacterDefinition(def);
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
            if (IsPaused) return; // crosshair hidden behind the pause panel
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

        protected static BakedAnimationData? LoadBakedData(CharacterDefinition def)
        {
            if (string.IsNullOrEmpty(def.BakedDataPath)) return null;
            string? path = BakedContentPaths.ResolveBaked(def.BakedDataPath);
            if (path == null) return null;
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

        /// <summary>
        /// Apply the Ability Lab hurtbox override (spec #119) when one exists for the
        /// character: a per-character JSON next to the baked skeleton that fully
        /// replaces HurtboxBoneDefs. Returns the def unchanged when absent or invalid.
        /// Keeps local/training hurtboxes identical to the game server's.
        /// </summary>
        protected static CharacterDefinition ApplyHurtboxOverride(CharacterDefinition def, BakedAnimationData? baked)
        {
            var overridePath = HurtboxOverride.OverridePathFor(def);
            if (overridePath == null || baked == null) return def;
            string? sysPath = BakedContentPaths.ResolveBaked(overridePath);
            if (sysPath == null) return def;
            try
            {
                string json = File.ReadAllText(sysPath);
                if (!HurtboxOverride.TryParse(json, out _, out var defs) || defs == null) return def;
                if (!HurtboxOverride.ValidateOrder(defs, baked)) return def;
                Debug.Log($"[MatchBase] Applied hurtbox override: {sysPath} ({defs.Length} bones)");
                return HurtboxOverride.Apply(def, defs);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MatchBase] Failed to apply hurtbox override {sysPath}: {ex.Message} — using C# defs");
                return def;
            }
        }
    }
}
