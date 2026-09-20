namespace SlopArena.Client.UI
{
    /// <summary>
    /// Tracks whether any modal surface is presented over the shared UI
    /// (issue #214). A conversation behind a modal is obscured, so chat must
    /// not treat it as read. Modal owners push/pop around their presentation;
    /// the counter is idempotent-safe via clamping so a missed pop cannot
    /// permanently wedge the read rule.
    /// </summary>
    public static class UiModalState
    {
        private static int _depth;

        /// <summary>True while at least one modal surface is presented.</summary>
        public static bool Presented => _depth > 0;

        public static void Push() => _depth++;

        public static void Pop() => _depth = System.Math.Max(0, _depth - 1);
    }
}
