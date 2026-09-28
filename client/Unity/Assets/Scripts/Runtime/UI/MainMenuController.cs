using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>Home mode actions and announcement skin; the shell owns shared navigation and identity.</summary>
    public class MainMenuController : MonoBehaviour, IFrontendPageController
    {
        // Not serialized: the shell injects the per-activation context at
        // mount time (issue #219).
        private FrontendPageContext _context = null!;


        public void InjectPageContext(FrontendPageContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        private void OnEnable()
        {
            MatchConfig.Reset();
            if (_context == null)
            {
                Debug.LogError("[MainMenuController] No page context was injected.");
                enabled = false;
                return;
            }
            _context.Q<Button>("menu-online")!.clicked += () => FrontendController.StartMode(GameMode.PvP);
            _context.Q<Button>("menu-solo")!.clicked += () => FrontendController.StartMode(GameMode.Solo);
            _context.Q<Button>("menu-training")!.clicked += () => FrontendController.StartMode(GameMode.Training);
            MenuNavigation.Configure(_context, null, ReturnToMainMenu);
        }


        private static void ReturnToMainMenu()
        {
            // MainMenu is the root of this flow; Escape here is intentionally a no-op.
        }
    }
}
