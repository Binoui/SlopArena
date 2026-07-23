// -- Human Soldier Animations 2.0 | Kevin Iglesias --
// This script is a secondary script that works with HumanSoldierController.cs script.
// You can freely edit, expand, and repurpose it as needed. To preserve your custom changes when updating
// to future versions, it is recommended to work from a duplicate of this script.

// Contact Support: support@keviniglesias.com

using UnityEngine;

namespace KevinIglesias
{
    public class HumanSoldierChangeWeaponSMB : StateMachineBehaviour
    {
        public SoldierWeapons weaponToDraw;
        
        private HumanSoldierController hSC;
        
        // OnStateEnter is called when a transition starts and the state machine starts to evaluate this state
        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if(hSC == null)
            {
                hSC = animator.GetComponent<HumanSoldierController>();
            }
            
            if(hSC)
            {
                hSC.ChangeWeapon(weaponToDraw);
            }
        }
    }
}
