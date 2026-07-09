using UnityEngine;
using SlopArena.Shared;

namespace SlopArena.Client.Characters
{
    /// <summary>
    /// Manki-specific weapon props: bazooka (R), bomb (Q), aerosol (RMB).
    /// Add this component to the PlayerRenderer GameObject in the scene/prefab.
    /// Wire the three weapon prefabs in the Inspector.
    /// Call Init(playerRenderer) from TrainingMatch after LoadModel().
    /// </summary>
    public class MankiWeaponAttach : SlopArena.Client.Entities.WeaponAttach
    {
        [Header("Weapon Prefabs")]
        [SerializeField] private GameObject _bazookaPrefab;   // R  — slot 5
        [SerializeField] private GameObject _bombPrefab;      // Q  — slot 3
        [SerializeField] private GameObject _aerosolPrefab;   // RMB — slot 2

        [Header("Local Rotation Offsets (tunable)")]
        [SerializeField] private Vector3 _bazookaRotOffset = Vector3.zero;
        [SerializeField] private Vector3 _bombRotOffset    = Vector3.zero;
        [SerializeField] private Vector3 _aerosolRotOffset = Vector3.zero;

        private Transform _rightHand;
        private Transform _leftHand;

        private GameObject _bazookaInstance;
        private GameObject _bombInstance;
        private GameObject _aerosolInstance;

        protected override void OnCleanup()
        {
            if (_bazookaInstance != null) Destroy(_bazookaInstance);
            if (_bombInstance    != null) Destroy(_bombInstance);
            if (_aerosolInstance != null) Destroy(_aerosolInstance);
            _bazookaInstance = _bombInstance = _aerosolInstance = null;
        }

        protected override void OnInit()
        {
            _rightHand = FindBone("mixamorig:RightHand");
            _leftHand  = FindBone("mixamorig:LeftHand");

            if (_bazookaPrefab  != null) _bazookaInstance  = Instantiate(_bazookaPrefab);
            if (_bombPrefab     != null) _bombInstance     = Instantiate(_bombPrefab);
            if (_aerosolPrefab  != null) _aerosolInstance  = Instantiate(_aerosolPrefab);

            // Start hidden
            SetVisible(_bazookaInstance,  false);
            SetVisible(_bombInstance,     false);
            SetVisible(_aerosolInstance,  false);
        }

        protected override void OnUpdate()
        {
            byte slot = Owner.CurrentAttackSlot;
            bool isAttacking = Owner.CurrentActionState == ActionState.Attacking;

            bool showBazooka = isAttacking && slot == 5;
            bool showBomb    = isAttacking && slot == 3;
            bool showAerosol = isAttacking && slot == 2;

            UpdateProp(_bazookaInstance,  _rightHand, _bazookaRotOffset,  showBazooka);
            UpdateProp(_bombInstance,     _leftHand,  _bombRotOffset,     showBomb);
            UpdateProp(_aerosolInstance,  _rightHand, _aerosolRotOffset,  showAerosol);
        }

        private static void UpdateProp(GameObject prop, Transform bone, Vector3 rotOffset, bool visible)
        {
            if (prop == null) return;

            bool wasVisible = prop.activeSelf;
            if (wasVisible != visible)
                prop.SetActive(visible);

            if (visible && bone != null)
            {
                prop.transform.position = bone.position;
                prop.transform.rotation = bone.rotation * Quaternion.Euler(rotOffset);
            }
        }

        private static void SetVisible(GameObject go, bool visible)
        {
            if (go != null) go.SetActive(visible);
        }
    }
}
