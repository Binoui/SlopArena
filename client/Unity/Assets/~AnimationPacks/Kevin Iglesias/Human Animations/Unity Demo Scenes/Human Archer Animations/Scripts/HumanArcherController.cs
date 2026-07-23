// -- Human Archer Animations 2.0 | Kevin Iglesias --
// This script is designed to showcase the animations included in the Unity demo scene for this asset.
// You can freely edit, expand, and repurpose it as needed. To preserve your custom changes when updating
// to future versions, it is recommended to work from a duplicate of this script.

// Contact Support: support@keviniglesias.com

using UnityEngine;
using System.Collections;

namespace KevinIglesias
{
    public enum ArcherAnimation
    {
        Nothing,
        Idles,
        Shoot,
        ShootUp,
        ShootDown,
        ShootFast,
        ShootRunning,
        ShootMovingBackwards,
        StrafeShooting_L,
        StrafeShooting_R,
        SetTrap,
        ThrowTrap,
        Damage,
        Death,
        Unsheathe,
    }
    
    public class HumanArcherController : MonoBehaviour
    {
        public Animator archerAnimator;
        
        [Header("ANIMATION TO PLAY")]
        public ArcherAnimation animationToPlay;

        [Header("BOW")]
        public LineRenderer bowstringLine;
        
        public Transform limb01;
        public Transform limb02;
        
        public Transform tip01;
        public Transform tip02;
        public Transform nockPoint;
        
        public Transform bowstringAnchorPoint;
        
        public AnimationCurve bowReleaseCurve;

        private Vector3 nockPointRestLocalPosition;
        private Vector3 initialLimb01LocalEulerAngles;
        private Vector3 initialLimb02LocalEulerAngles;
        
        private IEnumerator bowAnimation;
        private IEnumerator bowSheathAnimation;
        
        [Header("ARROW")]
        public GameObject arrowInHand;
        public GameObject arrowToShoot;
        
        private IEnumerator getArrowAnimation;

        [Header("UNSHEATHE")]
        public GameObject bowSheathed;
        public GameObject bowInHand;
        
        //Initialize values
        void OnEnable()
        {
            if(nockPoint)
            {
                nockPointRestLocalPosition = nockPoint.localPosition;
            }
            
            if(limb01 && limb02)
            {
                initialLimb01LocalEulerAngles = limb01.localEulerAngles;
                initialLimb02LocalEulerAngles = limb02.localEulerAngles;
            }
        }
        
        void Update()
        {
            //Apply selected animation
            archerAnimator.SetTrigger(animationToPlay.ToString());
        }

        void CreateBowstring()
        {
            if(!bowstringLine || !tip01 || !tip02 || !nockPoint)
            {
                return;
            }
            
            bowstringLine.positionCount = 3;
            bowstringLine.SetPosition(0, tip01.position);
            bowstringLine.SetPosition(1, nockPoint.position);
            bowstringLine.SetPosition(2, tip02.position);
        }
        
        void LateUpdate()
        {
            CreateBowstring();
        }

#if UNITY_EDITOR
        //Places bowstring even in Edit mode
        void OnValidate()
        {
            CreateBowstring();
        }
#endif 

        ///BOW PULL/RELEASE ANIMATION
        public void LoadBow(float delay, float duration)
        {
            if(bowAnimation != null)
            {
                StopCoroutine(bowAnimation);
                nockPoint.localPosition = nockPointRestLocalPosition;
            }
            bowAnimation = LoadBowCoroutine(delay, duration);
            StartCoroutine(bowAnimation);
        }
        public void ShootArrow(float delay, float duration)
        {
            if(bowAnimation != null)
            {
                StopCoroutine(bowAnimation);
                nockPoint.position = bowstringAnchorPoint.position;
            }
            bowAnimation = ShootArrowCoroutine(delay, duration);
            StartCoroutine(bowAnimation);
        }
        public void CancelLoadBow(float delay, float cancelDuration)
        {
            if(bowAnimation != null)
            {
                StopCoroutine(bowAnimation);
            }
            bowAnimation = CancelLoadBowCoroutine(delay, cancelDuration);
            StartCoroutine(bowAnimation);
        }
        
        private IEnumerator LoadBowCoroutine(float delay, float duration)
        {
            yield return new WaitForSeconds(delay);
            
            Vector3 limb01LoadLocalEulerAngles = 
            new Vector3(initialLimb01LocalEulerAngles.x, initialLimb01LocalEulerAngles.y, initialLimb01LocalEulerAngles.z-15f);
            Vector3 limb02LoadLocalEulerAngles = 
            new Vector3(initialLimb02LocalEulerAngles.x, initialLimb02LocalEulerAngles.y, initialLimb02LocalEulerAngles.z-15f);
            
            nockPoint.localPosition = nockPointRestLocalPosition;
            
            float t = 0;
            while(t < 1)
            {
                t += Time.deltaTime / duration;
                limb01.localEulerAngles = 
                Vector3.Lerp(initialLimb01LocalEulerAngles, limb01LoadLocalEulerAngles, t);
                limb02.localEulerAngles = 
                Vector3.Lerp(initialLimb02LocalEulerAngles, limb02LoadLocalEulerAngles, t);
                
                nockPoint.position = Vector3.Lerp(nockPoint.position, bowstringAnchorPoint.position, t);
                
                yield return null;
            }
        }
        
        private IEnumerator ShootArrowCoroutine(float delay, float duration)
        {
            yield return new WaitForSeconds(delay);
            
            Vector3 limb01LoadLocalEulerAngles = 
            new Vector3(initialLimb01LocalEulerAngles.x, initialLimb01LocalEulerAngles.y, initialLimb01LocalEulerAngles.z-15f);
            Vector3 limb02LoadLocalEulerAngles = 
            new Vector3(initialLimb02LocalEulerAngles.x, initialLimb02LocalEulerAngles.y, initialLimb02LocalEulerAngles.z-15f);
            
            Vector3 initialNockRestLocalPosition = nockPoint.localPosition;
            
            arrowInHand.SetActive(false);

            Instantiate(arrowToShoot, bowstringAnchorPoint.position, bowstringAnchorPoint.rotation);

            float t = 0;
            while(t < 1)
            {
                t += Time.deltaTime / duration;
                limb01.localEulerAngles = 
                Vector3.LerpUnclamped(limb01LoadLocalEulerAngles, initialLimb01LocalEulerAngles, bowReleaseCurve.Evaluate(t));
                limb02.localEulerAngles = 
                Vector3.LerpUnclamped(limb02LoadLocalEulerAngles, initialLimb02LocalEulerAngles, bowReleaseCurve.Evaluate(t));
                
                nockPoint.localPosition = Vector3.LerpUnclamped(initialNockRestLocalPosition, nockPointRestLocalPosition, bowReleaseCurve.Evaluate(t));
                
                yield return null;
            }
        }
        
        private IEnumerator CancelLoadBowCoroutine(float delay, float duration)
        {
            yield return new WaitForSeconds(delay);
            
            Vector3 limb01LoadLocalEulerAngles = 
            new Vector3(initialLimb01LocalEulerAngles.x, initialLimb01LocalEulerAngles.y, initialLimb01LocalEulerAngles.z-15f);
            Vector3 limb02LoadLocalEulerAngles = 
            new Vector3(initialLimb02LocalEulerAngles.x, initialLimb02LocalEulerAngles.y, initialLimb02LocalEulerAngles.z-15f);
            
            Vector3 initialNockRestLocalPosition = nockPoint.localPosition;
            
            float t = 0;
            while(t < 1)
            {
                t += Time.deltaTime / duration;
                limb01.localEulerAngles = 
                Vector3.LerpUnclamped(limb01LoadLocalEulerAngles, initialLimb01LocalEulerAngles, t);
                limb02.localEulerAngles = 
                Vector3.LerpUnclamped(limb02LoadLocalEulerAngles, initialLimb02LocalEulerAngles, t);
                
                nockPoint.localPosition = Vector3.LerpUnclamped(initialNockRestLocalPosition, nockPointRestLocalPosition, t);
                
                yield return null;
            }
        }
        
        ///GET ARROW AFTER SHOOTING
        public void GetArrow(float delay)
        {
            if(getArrowAnimation != null)
            {
                StopCoroutine(getArrowAnimation);
            }
            getArrowAnimation = GetArrowCoroutine(delay);
            StartCoroutine(getArrowAnimation);
        }
        
        private IEnumerator GetArrowCoroutine(float delay)
        {
            yield return new WaitForSeconds(delay);
            
            arrowInHand.SetActive(true);
        }
        
        ///BOW UNSHEATHE / SHEATHE
        public void UnsheatheBow(float delay)
        {
            if(bowSheathAnimation != null)
            {
                StopCoroutine(bowSheathAnimation);
            }
            bowSheathAnimation = UnsheatheBowCoroutine(delay);
            StartCoroutine(bowSheathAnimation);
        }
        
        private IEnumerator UnsheatheBowCoroutine(float delay)
        {
            yield return new WaitForSeconds(delay);
            
            bowSheathed.SetActive(false);
            bowInHand.SetActive(true);
            
        }
        
        public void SheatheBow(float delay)
        {
            if(bowSheathAnimation != null)
            {
                StopCoroutine(bowSheathAnimation);
            }
            bowSheathAnimation = SheatheBowCoroutine(delay);
            StartCoroutine(bowSheathAnimation);
        }
        
        private IEnumerator SheatheBowCoroutine(float delay)
        {
            yield return new WaitForSeconds(delay);
            
            bowSheathed.SetActive(true);
            bowInHand.SetActive(false);
        }
        
    }
}
