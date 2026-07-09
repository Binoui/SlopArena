using UnityEngine;

namespace SlopArena.Client.Entities
{
    /// <summary>
    /// One weapon entry: which attack slot triggers it, which bone it follows,
    /// which prefab to spawn (Resources-relative path), and an optional rotation offset.
    /// </summary>
    [System.Serializable]
    public class WeaponEntry
    {
        [Tooltip("AttackSlot value that makes this weapon visible (1-6). 0 = always visible.")]
        public byte AttackSlot;

        [Tooltip("Exact bone name from the SkinnedMeshRenderer (e.g. mixamorig:RightHand).")]
        public string BoneName;

        [Tooltip("Resources-relative path to the weapon prefab (e.g. Weapons/manki_bazooka).")]
        public string PrefabResourcePath;

        [Tooltip("Local rotation offset applied on top of the bone's rotation.")]
        public Vector3 RotationOffset;
    }

    /// <summary>
    /// Data asset that describes all weapon props for one character.
    /// Place in Resources/WeaponConfigs/ named after the CharacterClass enum value
    /// (e.g. Resources/WeaponConfigs/Manki.asset).
    /// </summary>
    [CreateAssetMenu(fileName = "NewWeaponAttachConfig", menuName = "SlopArena/Weapon Attach Config")]
    public class WeaponAttachConfig : ScriptableObject
    {
        public WeaponEntry[] Entries = System.Array.Empty<WeaponEntry>();
    }
}
