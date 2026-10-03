using System;
using UnityEngine;

namespace Unity.Pipeline.Console
{
    /// <summary>
    /// Captures Unity console output into a process-wide <see cref="ConsoleLogBuffer"/> so the
    /// <c>console</c> command can serve it to CLI clients. This type lives in the runtime
    /// assembly, so it ships in player builds as well as the Editor.
    ///
    /// Lifecycle:
    ///  - In a Player, the <c>[RuntimeInitializeOnLoadMethod]</c> hook starts capture as the app boots.
    ///  - In the Editor, the editor-only <c>EditorConsoleCaptureBootstrap</c> starts capture on every
    ///    domain reload and layers on persistence so entries survive reloads (e.g. after a recompile),
    ///    and re-arms on every play-mode transition (Unity clears
    ///    <see cref="Application.logMessageReceivedThreaded"/>'s subscribers on play-mode exit even
    ///    though exiting play mode does not reload the domain — undocumented behavior, UUM-148802;
    ///    re-verify before retiring this). Both paths funnel through
    ///    <see cref="EnsureCapturing"/>, which is safe to call repeatedly: it always removes any
    ///    existing subscription before adding, so repeat calls (e.g. the runtime hook firing again
    ///    when Play mode is entered in the Editor) can never double-subscribe.
    ///  - <see cref="Application.logMessageReceivedThreaded"/> is used (not the non-threaded variant)
    ///    so logs emitted from background threads are captured too. The buffer is thread-safe.
    ///
    /// Capture starts when this type first initializes. Console entries produced before that — or
    /// before the package's first import — are not retroactively captured; this reads from the public
    /// log callback, not Unity's internal console store.
    /// </summary>
    public static class ConsoleLogCapture
    {
        static readonly ConsoleLogBuffer s_Buffer = new ConsoleLogBuffer();
        static readonly object s_SubscriptionLock = new object();

        /// <summary>The shared buffer holding captured console entries.</summary>
        public static ConsoleLogBuffer Buffer => s_Buffer;

        /// <summary>
        /// Player entry point. Runs as the application boots so console output is captured from the
        /// start. In the Editor this also fires when entering Play mode, but <see cref="EnsureCapturing"/>
        /// is idempotent so it does not double-subscribe.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RuntimeBootstrap()
        {
            EnsureCapturing();
        }

        /// <summary>
        /// Subscribe to Unity's log callback, removing any existing subscription first. Safe to call
        /// repeatedly and from either the runtime bootstrap or the Editor bootstrap.
        /// </summary>
        public static void EnsureCapturing()
        {
            lock (s_SubscriptionLock)
            {
                Application.logMessageReceivedThreaded -= OnLogMessage;
                Application.logMessageReceivedThreaded += OnLogMessage;
            }
        }

        static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            s_Buffer.Add(type, condition, stackTrace, DateTime.UtcNow);
        }
    }
}
