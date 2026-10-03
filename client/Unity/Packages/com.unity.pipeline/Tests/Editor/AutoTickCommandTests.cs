using NUnit.Framework;
using Unity.Pipeline.Editor.Commands;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Tests for auto-tick's SessionState persistence across (simulated) domain reloads.
    /// The live editor's own server may already have auto-tick enabled, so SetUp/TearDown save and
    /// restore whatever state was live before each test instead of assuming it starts off.
    /// </summary>
    public class AutoTickCommandTests
    {
        private bool m_PrevEnabled;
        private long m_PrevIntervalMs;
        private bool m_HadPersisted;
        private bool m_PrevPersistedEnabled;
        private long m_PrevPersistedIntervalMs;

        [SetUp]
        public void SetUp()
        {
            m_PrevEnabled = AutoTickCommand.IsEnabled;
            m_PrevIntervalMs = AutoTickCommand.CurrentIntervalMs;
            m_HadPersisted = AutoTickCommand.TryGetPersistedSessionForTests(out m_PrevPersistedEnabled, out m_PrevPersistedIntervalMs);
            AutoTickCommand.EraseSessionForTests();
        }

        [TearDown]
        public void TearDown()
        {
            // persist:false so this only restores the live in-memory state, not SessionState -
            // the persisted baseline (or lack thereof) is restored/erased separately below.
            AutoTickCommand.SetAutoTick(m_PrevEnabled, (int)m_PrevIntervalMs, persist: false);

            if (m_HadPersisted)
                AutoTickCommand.SetPersistedSessionForTests(m_PrevPersistedEnabled, m_PrevPersistedIntervalMs);
            else
                AutoTickCommand.EraseSessionForTests();
        }

        [TestCase(true, 5)]
        [TestCase(true, 250)]
        [TestCase(false, 40)]
        public void RestoreFromSession_AfterSimulatedReload_RestoresExactPriorState(bool enable, int intervalMs)
        {
            AutoTickCommand.SetAutoTick(enable, intervalMs);

            // A domain reload clears the statics (and the update-loop subscription with them) but
            // leaves the SessionState copy this call just wrote intact.
            AutoTickCommand.ResetForTests();

            // Pass the opposite of the persisted value as the default, so a passing assertion proves
            // the persisted state won, not the default falling through by coincidence.
            AutoTickCommand.RestoreFromSession(defaultEnabled: !enable);

            Assert.AreEqual(enable, AutoTickCommand.IsEnabled,
                "Restore must bring back the exact enabled state set via SetAutoTick, not the startup default.");
            if (enable)
            {
                Assert.AreEqual(intervalMs, AutoTickCommand.CurrentIntervalMs,
                    "Restore must bring back the exact interval set via SetAutoTick, not the default interval.");
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RestoreFromSession_NoPriorExplicitCallThisSession_AppliesDefault(bool defaultEnabled)
        {
            // SetUp already erased SessionState and this simulates a reload on top of that, so there
            // is no persisted choice for RestoreFromSession to find.
            AutoTickCommand.ResetForTests();

            AutoTickCommand.RestoreFromSession(defaultEnabled);

            Assert.AreEqual(defaultEnabled, AutoTickCommand.IsEnabled);
            if (defaultEnabled)
            {
                Assert.AreEqual(AutoTickCommand.DefaultIntervalMs, AutoTickCommand.CurrentIntervalMs,
                    "With no explicit prior choice, restoring 'enabled' must use the default interval.");
            }
        }

        [Test]
        public void SetAutoTick_WithPersistFalse_RevertsToPriorPersistedBaselineAfterSimulatedReload()
        {
            // Establish a persisted baseline, as if the user had configured a normal auto-tick rate.
            AutoTickCommand.SetAutoTick(true, 200);

            // A temporary, expensive override (interval_ms=0 pegs a CPU core) that must not overwrite
            // the baseline above.
            AutoTickCommand.SetAutoTick(true, 0, persist: false);
            Assert.AreEqual(0, AutoTickCommand.CurrentIntervalMs, "The temporary override should still apply immediately.");

            AutoTickCommand.ResetForTests();
            AutoTickCommand.RestoreFromSession(defaultEnabled: false);

            Assert.AreEqual(200, AutoTickCommand.CurrentIntervalMs,
                "persist:false must not clobber the previously persisted baseline.");
        }

        [Test]
        public void SetAutoTick_WithPersistFalse_NoPriorBaseline_DoesNotSurviveSimulatedReload()
        {
            AutoTickCommand.SetAutoTick(true, 0, persist: false);
            Assert.IsTrue(AutoTickCommand.IsEnabled);

            AutoTickCommand.ResetForTests();
            AutoTickCommand.RestoreFromSession(defaultEnabled: false);

            Assert.IsFalse(AutoTickCommand.IsEnabled,
                "With no persisted baseline, a session-only (persist:false) call must not survive a reload.");
        }
    }
}
