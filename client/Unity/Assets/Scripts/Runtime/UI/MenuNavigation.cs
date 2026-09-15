using System;
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

            root.RegisterCallback<NavigationCancelEvent>(evt =>
            {
                // Stop here so a modal or nested screen cannot also invoke its parent back action.
                evt.StopImmediatePropagation();
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
