// -- Human Soldier Animations 2.0 | Kevin Iglesias --
// This script is designed to showcase the animations included in the Unity demo scene for this asset.
// You can freely edit, expand, and repurpose it as needed. To preserve your custom changes when updating
// to future versions, it is recommended to work from a duplicate of this script.

// Contact Support: support@keviniglesias.com

using UnityEngine;
using System.Collections;

namespace KevinIglesias
{
    public enum SoldierWeapons
    {
        None,
        AssaultRifle,
        Bazooka,
        Rifle,
        Gun,
        DualGun
    }
    
    public enum SoldierPosition
    {
        StandUp,
        Crouch,
        Prone
    }
    
    public enum SoldierAction
    {
        Nothing,
        HoldWeapon,
        Salute,
        WeaponChange,
        Shoot01,
        Shoot02,
        Shoot03,
        PreciseShoot01,
        PreciseShoot02,
        PreciseShoot03,
        Grenade01L,
        Grenade02L,
        Grenade01R,
        Grenade02R,
        Reload,
        Aim,
        AimPrecise,
        Damage01,
        Damage02,
        Damage03,
        Damage04,
        Damage05,
        Death01,
        Death02,
        Death03,
        Death04,
        Death05,
        ProneDeath,
        ProneDamage,
        Jump,
        Roll,
        RunSlide,
        ChangeWeapons,
    }

    public enum SoldierMovement
    {
        NoMovement,
        Walk,
        Run,
        Sprint,
        StrafeL,
        StrafeR
    }
    
    public enum UnsheatheWeapons
    {
        GetAssaultRifle,
        GetRifle,
        GetBazooka,
        GetGun,
        GetGuns
    }

    public class HumanSoldierController : MonoBehaviour
    {
        public Animator animator;
        
        public SoldierWeapons equippedWeapon;
        
        public SoldierPosition position;
        
        public SoldierAction action;
        
        public SoldierMovement movement;

        public GameObject[] weapons;
        
        private IEnumerator changingWeaponsCoroutine;
        private int currentWeapon = 0;

        void Update()
        {
            animator.SetTrigger(equippedWeapon.ToString());
            
            animator.SetTrigger(position.ToString());
            
            if(action != SoldierAction.Nothing && action != SoldierAction.ChangeWeapons)
            {
                animator.SetTrigger(action.ToString());
            }

            if(action == SoldierAction.ChangeWeapons)
            {
                if(changingWeaponsCoroutine == null)
                {
                    changingWeaponsCoroutine = ChangingWeapons();
                    StartCoroutine(changingWeaponsCoroutine);
                }
            }else{
                if(changingWeaponsCoroutine != null)
                {
                    StopCoroutine(changingWeaponsCoroutine);
                    changingWeaponsCoroutine = null;
                }
            }

            animator.SetTrigger(movement.ToString());
        }
        
        private IEnumerator ChangingWeapons()
        {
            currentWeapon++;
            
            if(currentWeapon > 4)
            {
                currentWeapon = 0;
            }
            
            animator.SetTrigger(((UnsheatheWeapons)(currentWeapon)).ToString());
            
            yield return new WaitForSeconds(1.5f);
            
            changingWeaponsCoroutine = ChangingWeapons();
            StartCoroutine(changingWeaponsCoroutine);
        }
        
        public void ChangeWeapon(SoldierWeapons newWeapon)
        {
            for(int i = 0; i < weapons.Length; i++)
            {
                weapons[i].SetActive(false);
            }
            
            weapons[(int)newWeapon-1].SetActive(true);
            
            if(newWeapon == SoldierWeapons.DualGun)
            {
                weapons[(int)SoldierWeapons.Gun-1].SetActive(true);
            }
        }
    }
}
