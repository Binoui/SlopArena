using System;
using SlopArena.Client.Input;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>Small shared focus/back contract for pre-match screens.</summary>
    public static class MenuNavigation
    {
        /// <summary>Focus the first action and route NavigationCancel to this screen's back action.</summary>
        public static void Configure(VisualElement root, Button initialButton, Action backAction)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));

            // Pre-match screens own the pointer. Match cameras may have left it locked.
            UnityEngine.Cursor.lockState = UnityEngine.CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
            root.RegisterCallback<ClickEvent>(_ => UISFX.PlayClick());
            UISFX.PlayMenuMusic();


            root.RegisterCallback<NavigationCancelEvent>(evt =>
            {
                // Stop here so a modal or nested screen cannot also invoke its parent back action.
                evt.StopImmediatePropagation();
                if (ChatInputGate.SuppressShortcuts)
                    return;
                backAction?.Invoke();
            });

            if (initialButton != null)
            {
                root.schedule.Execute(() =>
                {
                    if (initialButton.panel != null)
                        initialButton.Focus();
                });
            }
        }
    }
}
