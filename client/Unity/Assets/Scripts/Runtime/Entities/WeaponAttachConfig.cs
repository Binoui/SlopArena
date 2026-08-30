using UnityEngine;

namespace SlopArena.Client.Entities
{
    /// <summary>
    /// One weapon entry: which attack slot triggers it, which bone it follows,
    /// which prefab to spawn, and its bind-space attachment transform.
    /// </summary>

    [System.Serializable]
    public class WeaponEntry
    {
        [Tooltip("AttackSlot value that makes this weapon visible (0 = always visible; otherwise the CharacterState AttackSlot wire value, e.g. 11 = A).")]
        public byte AttackSlot;

        [Tooltip("Exact bone name from the SkinnedMeshRenderer (e.g. mixamorig:RightHand).")]
        public string BoneName;

        [Tooltip("Weapon prefab. Drag from Project view.")]
        public GameObject Prefab;

        [Tooltip("Position in the attached bone's local bind space.")]
        public Vector3 PositionOffset;

        [Tooltip("Rotation in the attached bone's local bind space.")]
        public Vector3 RotationOffset;
        [Tooltip("Hide the prop this many ticks after the attack starts (0 = keep for the whole attack).")]
        public int HideAfterTicks;

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
