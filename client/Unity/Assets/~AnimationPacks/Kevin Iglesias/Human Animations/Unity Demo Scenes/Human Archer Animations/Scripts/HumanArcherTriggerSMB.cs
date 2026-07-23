// -- Human Archer Animations 2.0 | Kevin Iglesias --
// This script is a secondary script that works with HumanArcherController.cs script.
// It animates the bow when entering or exiting an AnimatorController state.
// You can freely edit, expand, and repurpose it as needed. To preserve your custom changes when updating
// to future versions, it is recommended to work from a duplicate of this script.

// Contact Support: support@keviniglesias.com

using UnityEngine;

namespace KevinIglesias
{
    public class HumanArcherTriggerSMB : StateMachineBehaviour
    {
        public string triggerToAdd;
        
        // OnStateEnter is called when a transition starts and the state machine starts to evaluate this state
        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            animator.SetTrigger(triggerToAdd);
        }
    }
}
