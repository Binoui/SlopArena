// -- Human Archer Animations 2.0 | Kevin Iglesias --
// This script is a secondary script that works with HumanArcherController.cs script.
// It animates the bow when entering or exiting an AnimatorController state.
// You can freely edit, expand, and repurpose it as needed. To preserve your custom changes when updating
// to future versions, it is recommended to work from a duplicate of this script.

// Contact Support: support@keviniglesias.com

using UnityEngine;

namespace KevinIglesias
{
    public enum UnsheatheAction
    {
        Unsheathe,
        Sheathe
    }

    public class HumanArcherUnsheatheBowSMB : StateMachineBehaviour
    {
        public UnsheatheAction action;

        public float delay;
 
        private HumanArcherController hAC;
        
        // OnStateEnter is called when a transition starts and the state machine starts to evaluate this state
        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if(!hAC)
            {
                hAC = animator.GetComponent<HumanArcherController>();
            }
            
            if(action == UnsheatheAction.Unsheathe)
            {
                hAC.UnsheatheBow(delay);
            }else{
                hAC.SheatheBow(delay);
            }
        }
    }
}
