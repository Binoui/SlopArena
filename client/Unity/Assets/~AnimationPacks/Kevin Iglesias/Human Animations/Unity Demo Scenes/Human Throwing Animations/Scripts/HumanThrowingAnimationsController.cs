// -- Human Throwing Animations 2.0 | Kevin Iglesias --
// This script is designed to showcase the animations included in the Unity demo scene for this asset.
// You can freely edit, expand, and repurpose it as needed. To preserve your custom changes when updating
// to future versions, it is recommended to work from a duplicate of this script.

// Contact Support: support@keviniglesias.com

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace KevinIglesias
{
    public enum ThrowingHandGrip
    {
        None,
        GripObjectL,
        GripObjectR,
        GripObjectBothHands,
        GripBallL,
        GripBallR,
        GripBallBothHands
    }
    
    public enum ThrowingAction
    {
        Idle,
        IdleActionL,
        IdleActionR,
        Damages,
        Deaths,
        ThrowBall01L,
        ThrowBall02L,
        ThrowBall01R,
        ThrowBall02R,
        ThrowBigRock01,
        ThrowBoomerang01L,
        ThrowBoomerang01R,
        ThrowSpear01L,
        ThrowSpear01R,
        ThrowSpear02L,
        ThrowSpear02R,
        SpearChange01L,
        SpearChange01R,
        ThrowWeapon01L,
        ThrowWeapon01R,
        ThrowWeapon02L,
        ThrowWeapon02R,
        ThrowWeapon03L,
        ThrowWeapon03R,
        ThrowWeapon04L,
        ThrowWeapon04R,
    }

    public enum ThrowingMovement
    {
        NoMovement,
        Walk,
        Run,
        StrafeL,
        StrafeR
    }
    
    [System.Serializable]
    public class ThrowingProps
    {
        public string propName;
        public GameObject prop;
        public bool active;
    }

    public class HumanThrowingAnimationsController : MonoBehaviour
    {
        public Animator animator;
        
        public ThrowingHandGrip handPose;
        private ThrowingHandGrip lastHandPose;
        
        public ThrowingAction action;
        private ThrowingAction lastAction;
        
        public ThrowingMovement movement;
        private ThrowingMovement lastMovement;
        
        public List<ThrowingProps> props = new List<ThrowingProps>();
        
        void Awake()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 30;
        }
        
        void Start()
        {
            lastHandPose = ThrowingHandGrip.None;
            lastAction = ThrowingAction.Idle;
            lastMovement = ThrowingMovement.NoMovement;
        }

        void Update()
        {
            if(handPose != lastHandPose)
            {
                animator.SetTrigger(handPose.ToString());
                lastHandPose = handPose;
            }

            if(action != lastAction)
            {
                if(action != ThrowingAction.Idle)
                {
                    animator.SetTrigger(action.ToString());
                }
                lastAction = action;
            }

            if(movement != lastMovement)
            {
                animator.SetTrigger(movement.ToString());
                lastMovement = movement;
            }
        }
        
        #if UNITY_EDITOR
            void OnValidate()
            {
                // Delay the SetActive call to avoid "SendMessage cannot be called..." warnings
                EditorApplication.delayCall += () =>
                {
                    // Check object still exists (Unity can destroy it between validation and delay)
                    if (this == null) return;

                    for (int i = 0; i < props.Count; i++)
                    {
                        if (props[i].prop)
                        {
                            props[i].propName = props[i].prop.name;
                            props[i].prop.SetActive(props[i].active);
                        }
                    }
                };
            }
        #endif
    }
        
}

