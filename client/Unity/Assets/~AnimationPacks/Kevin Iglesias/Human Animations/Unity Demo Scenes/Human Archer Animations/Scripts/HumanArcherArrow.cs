// -- Human Archer Animations 2.0 | Kevin Iglesias --
// This script is a secondary script that works with HumanArcherController.cs script.
// It animates the bow when entering or exiting an AnimatorController state.
// You can freely edit, expand, and repurpose it as needed. To preserve your custom changes when updating
// to future versions, it is recommended to work from a duplicate of this script.

// Contact Support: support@keviniglesias.com

using UnityEngine;

namespace KevinIglesias
{
    public class HumanArcherArrow : MonoBehaviour
    {
        private float arrowSpeed = 30f;
        private float arrowLifetime = 2f;
        
        void OnEnable()
        {
            Destroy(this.gameObject, arrowLifetime);
        }

        void Update()
        {
            transform.Translate(transform.forward * arrowSpeed * Time.deltaTime, Space.World);
        }
    }
}
