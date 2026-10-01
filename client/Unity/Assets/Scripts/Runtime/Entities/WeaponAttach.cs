using System.Collections.Generic;
using UnityEngine;
using SlopArena.Shared;

namespace SlopArena.Client.Entities
{
    [ExecuteAlways]
    public class WeaponAttach : MonoBehaviour
    {
        private const int HistoryCapacity = 64;
        private PlayerRenderer _owner;
        private CharacterDefinition _characterDefinition;
        private SkinnedMeshRenderer _skin;
        private bool _previewStateActive;
        private byte _previewAttackSlot;
        private int _previewAttackElapsedTicks;
        private Transform[] _bones;
        private Transform[] _hilts;
        private Transform[] _tips;
        private GameObject[] _instances;
        private SwordSweepTrail[] _sweepTrails;
        private WeaponEntry[] _entries;
        private WeaponAttachConfig _config;
        private readonly CapturedState[] _history = new CapturedState[HistoryCapacity];
        private int _historyStart;
        private int _historyCount;
        private int _captureSerial;
        private int _processedSerial;
        private bool _hitboxTrailActive;
        private bool _hasAttackIdentity;
        private byte _attackSequence;
        private byte _attackSlot;
        private byte _comboStage;
        private ushort _attackElapsedTicks;
        private byte _deaths;
        private bool _hasCapturedPosition;
        private Vector3 _lastPosition;
        private float _runtimeClock;
        private float _lastSampleTime;
        private int _lastSampleSequence = -1;
        private int _lastSampleStage = -1;
        private int _lastSampleElapsed = -1;
        private float _previewTimelineSeconds = -1f;
        public bool HasSwordTrail { get; private set; }

        private struct CapturedState
        {
            public CharacterState State;
            public bool Airborne;
            public int Serial;
            public bool HasPose;
            public Vector3 Hilt;
            public Vector3 Tip;
        }

        public void Init(PlayerRenderer owner, WeaponAttachConfig config)
        {
            Cleanup();
            _owner = owner;
            _config = config;
            _characterDefinition = owner != null ? owner.CharacterDef : null;
            if (config == null || config.Entries == null || config.Entries.Length == 0)
                return;

            _skin = owner != null ? owner.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            if (_skin == null)
            {
                Debug.LogWarning($"[WeaponAttach] No SkinnedMeshRenderer under {owner?.name}");
                return;
            }
            _entries = config.Entries;
            int count = _entries.Length;
            _bones = new Transform[count];
            _hilts = new Transform[count];
            _tips = new Transform[count];
            _instances = new GameObject[count];
            _sweepTrails = new SwordSweepTrail[count];

            for (int i = 0; i < count; i++)
            {
                _bones[i] = FindBone(_entries[i].BoneName);
                if (_entries[i].Prefab == null)
                {
                    Debug.LogWarning($"[WeaponAttach] Prefab is null for entry {i} ({_entries[i].BoneName})");
                    continue;
                }
                _instances[i] = Instantiate(_entries[i].Prefab);
                _instances[i].SetActive(false);
                if (_entries[i].TrailStylePrefab == null)
                    continue;

                var styleSystem = _entries[i].TrailStylePrefab.GetComponentInChildren<ParticleSystem>(true);
                var styleRenderer = styleSystem != null
                    ? styleSystem.GetComponent<ParticleSystemRenderer>() : null;
                if (styleRenderer == null || styleRenderer.sharedMaterial == null)
                    continue;
                Transform weapon = _instances[i].transform;
                _hilts[i] = weapon.Find(_entries[i].TrailHiltAnchor);
                _tips[i] = weapon.Find(_entries[i].TrailTipAnchor);
                _sweepTrails[i] = new SwordSweepTrail(
                    weapon, _entries[i].TrailStylePrefab, _entries[i].HitboxMotionTime,
                    _entries[i].TrailBladeWidth);
                HasSwordTrail = true;
            }
        }

        public void SetPreviewState(byte attackSlot, int attackElapsedTicks)
        {
            _previewStateActive = true;
            _previewAttackSlot = attackSlot;
            _previewAttackElapsedTicks = attackElapsedTicks;
        }

        public void SetHitboxTrailActive(bool active, float previewSeconds = -1f)
        {
            if (_sweepTrails == null)
                return;
            _hitboxTrailActive = active;
            foreach (var trail in _sweepTrails)
                trail?.SetActive(active);
            if (_previewStateActive && !active)
            {
                ClearTrails();
                _previewTimelineSeconds = -1f;
            }
            if (active && previewSeconds < 0f)
                ProcessPendingHistory();
            else if (!active)
                ClearPendingHistory();
        }

        /// <summary>Capture every authoritative presentation tick, not just rendered frames.</summary>
        public void CaptureState(
            in CharacterState state, bool airborne, bool hasPose, Vector3 hilt, Vector3 tip)
        {
            if (_owner == null || _entries == null || _sweepTrails == null)
                return;
            if (_owner.CharacterDef != _characterDefinition)
            {
                ClearTrails();
                ClearPendingHistory();
                _hasAttackIdentity = false;
                _characterDefinition = _owner.CharacterDef;
            }
            Vector3 position = new(state.PX, state.PY, state.PZ);
            bool teleported = _hasCapturedPosition && (state.Deaths != _deaths
                || (position - _lastPosition).sqrMagnitude > 9f);
            bool attacking = state.State == ActionState.Attacking && state.AttackSlot != 0;
            bool newIdentity = attacking && (!_hasAttackIdentity
                || state.AttackSequence != _attackSequence
                || state.AttackSlot != _attackSlot
                || state.ComboStage != _comboStage
                || state.AttackElapsedTicks < _attackElapsedTicks);

            if (teleported || newIdentity)
                ClearTrails();
            if (!attacking)
            {
                _hasAttackIdentity = false;
                ClearPendingHistory();
                _deaths = state.Deaths;
                _lastPosition = position;
                _hasCapturedPosition = true;
                return;
            }

            _hasAttackIdentity = true;
            _attackSequence = state.AttackSequence;
            _attackSlot = state.AttackSlot;
            _comboStage = state.ComboStage;
            _attackElapsedTicks = state.AttackElapsedTicks;
            _deaths = state.Deaths;
            _lastPosition = position;
            _hasCapturedPosition = true;
            if (_historyCount == HistoryCapacity)
            {
                _historyStart = (_historyStart + 1) % HistoryCapacity;
                _historyCount--;
            }
            int index = (_historyStart + _historyCount++) % HistoryCapacity;
            _history[index] = new CapturedState
            {
                State = state,
                Airborne = airborne,
                Serial = ++_captureSerial,
                HasPose = hasPose,
                Hilt = hilt,
                Tip = tip,
            };
        }

        /// <summary>
        /// Rebuild Ability Lab's recent trail from authoritative preview states. The
        /// caller supplies the entire simulation history through the selected tick.
        /// </summary>
        public void SetPreviewTrailHistory(
            IReadOnlyList<CharacterState> states, int targetTick, bool airborne)
        {
            if (!HasSwordTrail || states == null || targetTick < 0)
                return;
            UpdateTrailWidths();
            float targetTime = targetTick / 60f;
            _previewTimelineSeconds = targetTime;
            float oldestTime = targetTime - TrailLifetime();
            ClearTrails();
            foreach (var trail in _sweepTrails)
                trail?.SetActive(true);

            int firstTick = Mathf.Max(0, targetTick - Mathf.CeilToInt(TrailLifetime() * 60f));
            bool wasSampling = false;
            for (int tick = firstTick; tick < states.Count && tick <= targetTick; tick++)
            {
                CharacterState state = states[tick];
                float sampleTime = tick / 60f;
                Vector3 hilt = default, tip = default;
                bool sampleable = sampleTime >= oldestTime
                    && _owner.IsSwordWindowTick(state, airborne);
                if (sampleable)
                    sampleable = _owner.TrySampleBakedWeaponPath(
                        state, airborne, out hilt, out tip);
                if (!sampleable)
                {
                    if (wasSampling)
                    {
                        foreach (var trail in _sweepTrails)
                            trail?.SetActive(false);
                        wasSampling = false;
                    }
                    continue;
                }
                if (!wasSampling)
                {
                    foreach (var trail in _sweepTrails)
                        trail?.SetActive(true);
                    wasSampling = true;
                }
                foreach (var trail in _sweepTrails)
                    trail?.SampleAt(sampleTime, hilt, tip);
            }

            foreach (var trail in _sweepTrails)
            {
                trail?.SetActive(_hitboxTrailActive);
                trail?.DrawAt(targetTime);
            }
        }

        private float TrailLifetime()
        {
            float lifetime = 0f;
            if (_sweepTrails != null)
                foreach (var trail in _sweepTrails)
                    if (trail != null)
                        lifetime = Mathf.Max(lifetime, trail.VisibleLifetime);
            return lifetime;
        }

        private void ProcessPendingHistory()
        {
            if (!_hitboxTrailActive || _owner == null)
                return;

            for (int i = 0; i < _historyCount; i++)
            {
                CapturedState captured = _history[(_historyStart + i) % HistoryCapacity];
                if (captured.Serial <= _processedSerial)
                    continue;
                _processedSerial = captured.Serial;
                CharacterState state = captured.State;
                if (!_owner.IsSwordWindowTick(state, captured.Airborne)
                    || (state.AttackSequence == _lastSampleSequence
                        && state.ComboStage == _lastSampleStage
                        && state.AttackElapsedTicks == _lastSampleElapsed))
                    continue;
                if (!captured.HasPose)
                    continue;
                int deltaTicks = _lastSampleElapsed < 0
                    ? 0 : Mathf.Max(0, state.AttackElapsedTicks - _lastSampleElapsed);
                float currentTime = Application.isPlaying ? Time.unscaledTime : _runtimeClock;
                float sampleTime = Mathf.Max(currentTime, _lastSampleTime + deltaTicks / 60f);
                _runtimeClock = Mathf.Max(_runtimeClock, sampleTime);
                _lastSampleTime = sampleTime;
                foreach (var trail in _sweepTrails)
                    trail?.SampleAt(sampleTime, captured.Hilt, captured.Tip);
                _lastSampleSequence = state.AttackSequence;
                _lastSampleStage = state.ComboStage;
                _lastSampleElapsed = state.AttackElapsedTicks;
            }
        }

        private void ClearPendingHistory()
        {
            _historyStart = 0;
            _historyCount = 0;
            _processedSerial = _captureSerial;
        }

        private void ClearTrails()
        {
            foreach (var trail in _sweepTrails)
                trail?.Clear();
            ClearPendingHistory();
            _lastSampleSequence = -1;
            _lastSampleStage = -1;
            _lastSampleElapsed = -1;
        }

        private void UpdateTrailWidths()
        {
            var entries = _config != null ? _config.Entries : null;
            if (entries == null || _sweepTrails == null) return;
            for (int i = 0; i < entries.Length && i < _sweepTrails.Length; i++)
            {
                var entry = entries[i];
                if (_instances[i] == null || entry == null) continue;
                if (_sweepTrails[i]?.StylePrefab != entry.TrailStylePrefab)
                {
                    var replacement = entry.TrailStylePrefab != null
                        ? new SwordSweepTrail(_instances[i].transform, entry.TrailStylePrefab,
                            entry.HitboxMotionTime, entry.TrailBladeWidth)
                        : null;
                    var previous = _sweepTrails[i];
                    _sweepTrails[i] = replacement;
                    previous?.Dispose();
                    replacement?.SetActive(_hitboxTrailActive);
                }
                _sweepTrails[i]?.SetBladeWidth(entry.TrailBladeWidth);
            }
            HasSwordTrail = false;
            foreach (var trail in _sweepTrails)
                if (trail != null) { HasSwordTrail = true; break; }
        }

        private void LateUpdate() => RefreshPresentation();

        /// <summary>Apply attachment placement and trail drawing after an Ability Lab scrub, without waiting for a frame.</summary>
        public void RefreshPresentation()
        {
            if (_owner == null || _entries == null || _instances == null || _sweepTrails == null)
                return;
            UpdateTrailWidths();
            if (Application.isPlaying)
                _runtimeClock = Mathf.Max(_runtimeClock, Time.unscaledTime);
            else if (!_previewStateActive)
                _runtimeClock += 1f / 60f;
            float drawTime = _previewStateActive && _previewTimelineSeconds >= 0f
                ? _previewTimelineSeconds : _runtimeClock;

            byte slot = _previewStateActive ? _previewAttackSlot : _owner.CurrentAttackSlot;
            bool isAttacking = _previewStateActive || _owner.CurrentActionState == ActionState.Attacking;
            bool isAiming = !_previewStateActive && _owner.CurrentActionState == ActionState.Aiming;
            int elapsedTicks = _previewStateActive
                ? _previewAttackElapsedTicks : _owner.CurrentAttackElapsedTicks;
            for (int i = 0; i < _entries.Length; i++)
            {
                GameObject go = _instances[i];
                if (go == null)
                    continue;
                byte entrySlot = _entries[i].AttackSlot;
                bool withinHold = _previewStateActive || _entries[i].HideAfterTicks <= 0
                    || !isAttacking || elapsedTicks < _entries[i].HideAfterTicks;
                bool visible = (entrySlot == 0 || (isAttacking || isAiming) && slot == entrySlot)
                    && withinHold;
                if (go.activeSelf != visible)
                    go.SetActive(visible);
                if ((visible || _hitboxTrailActive) && _bones[i] != null)
                {
                    go.transform.position = _bones[i].TransformPoint(_entries[i].PositionOffset);
                    go.transform.rotation = _bones[i].rotation
                        * Quaternion.Euler(_entries[i].RotationOffset);
                }
                if (_hitboxTrailActive && _owner.BakedData == null
                    && _hilts[i] != null && _tips[i] != null && visible)
                    _sweepTrails[i]?.SampleAt(drawTime, _hilts[i].position, _tips[i].position);
                _sweepTrails[i]?.DrawAt(drawTime);
            }
        }

        private void OnDestroy() => Cleanup();

        private void Cleanup()
        {
            if (_sweepTrails != null)
                foreach (var trail in _sweepTrails)
                    trail?.Dispose();
            if (_instances != null)
                foreach (var go in _instances)
                {
                    if (go == null) continue;
                    if (Application.isPlaying) Destroy(go);
                    else DestroyImmediate(go);
                }
            _owner = null;
            _config = null;
            _characterDefinition = null;
            _skin = null;
            _bones = null;
            _hilts = null;
            _tips = null;
            _instances = null;
            _sweepTrails = null;
            _entries = null;
            _hitboxTrailActive = false;
            HasSwordTrail = false;
            _previewStateActive = false;
            _previewAttackSlot = 0;
            _previewAttackElapsedTicks = 0;
            _hasAttackIdentity = false;
            _hasCapturedPosition = false;
            _historyStart = 0;
            _historyCount = 0;
            _captureSerial = 0;
            _processedSerial = 0;
        }

        private Transform FindBone(string boneName)
        {
            if (_skin == null) return null;
            foreach (var bone in _skin.bones)
                if (bone != null && bone.name == boneName)
                    return bone;
            if (boneName == "mixamorig:RightHand")
            {
                var animator = _skin.GetComponentInParent<Animator>();
                var humanoidHand = animator?.GetBoneTransform(HumanBodyBones.RightHand);
                if (humanoidHand != null)
                    return humanoidHand;
            }
            Debug.LogWarning($"[WeaponAttach] Bone '{boneName}' not found on {_owner.name}");
            return null;
        }
    }
}
