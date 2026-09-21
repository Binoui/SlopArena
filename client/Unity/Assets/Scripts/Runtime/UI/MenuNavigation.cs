using System;
using SlopArena.Client.Input;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>Small shared focus/back contract for pre-match screens.</summary>
    public static class MenuNavigation
    {
        /// <summary>
        /// The active page's conventional initial focus, registered by
        /// Configure (issue #215): the Page/Social region switch falls back to
        /// it when the page region has no remembered focus. Null when a
        /// screen configures without an initial button (e.g. modals).
        /// </summary>
        public static VisualElement? PageInitialFocus { get; private set; }

        /// <summary>Focus the first action and route NavigationCancel to this screen's back action.</summary>
        public static void Configure(VisualElement root, Button initialButton, Action backAction)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));

            ConfigureShared(initialButton);

            root.RegisterCallback<ClickEvent>(_ => UISFX.PlayClick());
            root.RegisterCallback<NavigationCancelEvent>(evt => OnScreenCancel(evt, backAction));

            if (initialButton != null)
            {
                root.schedule.Execute(() =>
                {
                    if (initialButton.panel != null)
                        initialButton.Focus();
                });
            }
        }

        /// <summary>
        /// Fragment-mounted pages register on their page-owned section roots
        /// instead of one document root (issue #219): the mounted sections
        /// are no longer a single subtree, so shell-wide registration is
        /// impossible. The registrations live on the page context and are
        /// unregistered when the context releases, so repeated navigation
        /// never accumulates callbacks on the stable shell hosts.
        /// </summary>
        public static void Configure(FrontendPageContext context, Button initialButton, Action backAction)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            ConfigureShared(initialButton);

            var roots = context.OwnedRoots;
            EventCallback<ClickEvent> clickHandler = _ => UISFX.PlayClick();
            EventCallback<NavigationCancelEvent> cancelHandler = evt => OnScreenCancel(evt, backAction);
            for (int i = 0; i < roots.Count; i++)
            {
                roots[i].RegisterCallback(clickHandler);
                roots[i].RegisterCallback(cancelHandler);
            }

            context.AddReleaseAction(() =>
            {
                for (int i = 0; i < roots.Count; i++)
                {
                    roots[i].UnregisterCallback(clickHandler);
                    roots[i].UnregisterCallback(cancelHandler);
                }
            });

            if (initialButton != null)
            {
                var initial = initialButton;
                roots[0].schedule.Execute(() =>
                {
                    if (initial.panel != null)
                        initial.Focus();
                    // Focusing can leave the page body scrolled (issue #219);
                    // a fresh activation always starts at the top.
                    foreach (var root in roots)
                        root.Query<ScrollView>().ForEach(sv => sv.scrollOffset = Vector2.zero);
                });
            }
        }

        private static void ConfigureShared(Button? initialButton)
        {
            if (initialButton != null)
                PageInitialFocus = initialButton;

            // Pre-match screens own the pointer. Match cameras may have left it locked.
            UnityEngine.Cursor.lockState = UnityEngine.CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
            UISFX.PlayMenuMusic();
        }

        private static void OnScreenCancel(NavigationCancelEvent evt, Action? backAction)
        {
            // Stop here so a modal or nested screen cannot also invoke its parent back action.
            evt.StopImmediatePropagation();
            if (ChatInputGate.SuppressShortcuts)
                return;
            backAction?.Invoke();
        }
    }
}
