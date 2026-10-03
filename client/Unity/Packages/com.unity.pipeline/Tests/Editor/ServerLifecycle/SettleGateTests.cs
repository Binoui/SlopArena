using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor;
using Unity.Pipeline.Security;
using Newtonsoft.Json.Linq;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Tests for the post-startup settle gate (AUTHAPI-35): while the Editor is settling after a
    /// cold start (still importing/compiling when the server comes up), main-thread commands must
    /// be rejected with a distinguishable, retryable "busy" signal (HTTP 503) instead of executing
    /// into a half-ready Editor and failing with an opaque null-data envelope. Status surfaces
    /// (background commands, editor_status, /api/status, /api/editor_status) must stay servable so
    /// callers can observe the settling state and know when to retry.
    /// </summary>
    public class SettleGateTests
    {
        /// <summary>
        /// Isolated editor server (test port range, no descriptor — same isolation as
        /// <see cref="TestEditorPipelineServer"/>) whose settle latch is pinned via
        /// <see cref="Settled"/>, since an EditMode test cannot put the real Editor into its
        /// cold-import busy state.
        /// </summary>
        private sealed class SettleGateTestServer : EditorPipelineServer
        {
            /// <summary>false simulates the post-cold-start settle window; true a settled server.</summary>
            public bool Settled { get; set; }

            protected override bool WritesDescriptor => false;

            protected override (int basePort, int maxPort) GetPortRange() => (7850, 7899);

            protected override string GetToken() => SecurityTokenManager.GetOrCreateToken();

            protected override bool IsSettled => Settled;
        }

        /// <summary>
        /// Isolated editor server exposing the REAL settle latch, to verify a server started after
        /// the session has settled (mid-session restarts and test servers — every server except
        /// the cold-import one) settles immediately and never gates anything, even if the Editor
        /// happens to be importing/compiling at Start() (as CI fixtures that create scripts do).
        /// </summary>
        private sealed class LatchProbeServer : EditorPipelineServer
        {
            public bool RealSettled => IsSettled;

            /// <summary>The session-settled SessionState marker, surfaced so the latch test can
            /// assert its precondition explicitly instead of assuming it.</summary>
            public static bool SessionSettled => IsSessionSettled;

            protected override bool WritesDescriptor => false;

            protected override (int basePort, int maxPort) GetPortRange() => (7850, 7899);

            protected override string GetToken() => SecurityTokenManager.GetOrCreateToken();
        }

        private SettleGateTestServer m_Server;
        private Unity.Pipeline.Tests.Runtime.PipelineClient m_PipelineClient;

        [SetUp]
        public void SetUp()
        {
            CommandRegistry.SetDiscovery(new TypeCacheCommandDiscovery());

            m_Server = new SettleGateTestServer { Settled = false };
            m_Server.Start();

            m_PipelineClient = new Unity.Pipeline.Tests.Runtime.PipelineClient(m_Server);
        }

        [TearDown]
        public void TearDown()
        {
            m_PipelineClient?.Dispose();
            m_Server?.Stop();
        }

        [Test]
        public async Task ApiExec_MainThreadCommand_WhileSettling_Returns503BusyEnvelope()
        {
            // Act - log_editor is MainThreadRequired (the attribute default), like create_scene /
            // instantiate_prefab from the report. Parameters are valid so only the gate can reject.
            var response = await m_PipelineClient.ExecuteCommandAsync("log_editor",
                new { message = "should not run while settling" });

            // Assert - rejected with 503 and the distinguishable, retryable busy envelope
            Assert.AreEqual(503, response.StatusCode,
                $"Main-thread command should be rejected with 503 while settling. Response: {response.RawResponse}");
            Assert.IsTrue(response.HasValidJson, "Busy response should have valid JSON");

            var json = response.JsonResponse;
            Assert.IsFalse(json["success"].ToObject<bool>(), "Busy response should have success=false");
            Assert.AreEqual("log_editor", json["command"]?.ToString(), "Busy response should echo the command");
            Assert.AreEqual("Server Busy", json["error"]?.ToString(), "Busy response should carry the busy error");
            Assert.AreEqual("busy", json["status"]?.ToString(), "Busy response should carry the machine-readable status marker");
            Assert.IsTrue(json["retryable"].ToObject<bool>(), "Busy response should be marked retryable");
            StringAssert.Contains("settling", json["errorDetails"]?.ToString(),
                "Busy details should explain the Editor is settling");
        }

        [Test]
        public async Task ApiExec_DetachedJobSubmission_WhileSettling_Returns503AndCreatesNoJob()
        {
            // Act - the gate must apply BEFORE a detached job is created: a queued job would
            // otherwise run into the half-ready Editor in the background (the contract called
            // out in the changelog and connectivity.md).
            var response = await m_PipelineClient.PostJsonAsync("/api/exec", new
            {
                command = "log_editor",
                parameters = new { message = "should not be queued while settling" },
                job = true
            });

            // Assert - same busy envelope as the sync path, and no job handle was handed out
            Assert.AreEqual(503, response.StatusCode,
                $"Detached job submission should be rejected with 503 while settling. Response: {response.RawResponse}");
            Assert.IsTrue(response.HasValidJson, "Busy response should have valid JSON");

            var json = response.JsonResponse;
            Assert.IsFalse(json["success"].ToObject<bool>(), "Busy response should have success=false");
            Assert.AreEqual("Server Busy", json["error"]?.ToString(), "Busy response should carry the busy error");
            Assert.AreEqual("busy", json["status"]?.ToString(), "Busy response should carry the status marker");
            Assert.IsTrue(json["retryable"].ToObject<bool>(), "Busy response should be marked retryable");
            Assert.IsNull(json.SelectToken("result.jobId"),
                "No job may be created while settling — the busy reply must carry no job handle");
        }

        [Test]
        public async Task ApiExec_BackgroundCommand_WhileSettling_ExecutesNormally()
        {
            // Act - recompile_status is MainThreadRequired=false: exactly the polling clients rely
            // on while the Editor is busy, so it must never be gated.
            var response = await m_PipelineClient.ExecuteCommandAsync("recompile_status", new { });

            // Assert
            Assert.IsTrue(response.IsSuccess,
                $"Background command should execute while settling, got: {response.StatusCode}. Response: {response.RawResponse}");
            Assert.IsTrue(response.IsCommandSuccess, "Background command should succeed while settling");
        }

        [Test]
        public async Task ApiExec_EditorStatus_WhileSettling_RemainsServiceable()
        {
            // Act - editor_status is main-thread but explicitly exempt: it is the status surface
            // callers poll to observe the settling state itself.
            var response = await m_PipelineClient.ExecuteCommandAsync("editor_status", new { });

            // Assert
            Assert.IsTrue(response.IsSuccess,
                $"editor_status should stay servable while settling, got: {response.StatusCode}. Response: {response.RawResponse}");
            Assert.IsTrue(response.IsCommandSuccess, "editor_status should succeed while settling");
        }

        [Test]
        public async Task ApiStatus_WhileSettling_ReportsSettlingInsteadOfReady()
        {
            // Act
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/status");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert - ready is withheld until the Editor is actually serviceable
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"/api/status should respond while settling, got: {httpResponse.StatusCode}");
            var statusJson = JObject.Parse(jsonContent);
            Assert.AreEqual("settling", statusJson["status"]?.ToString(),
                "/api/status should report 'settling' until the Editor is first seen idle");
        }

        [Test]
        public async Task ApiEditorStatus_Endpoint_WhileSettling_BypassesBusyGate()
        {
            // Act - the dedicated status endpoint must keep working so the busy state is observable
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/editor_status");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"/api/editor_status should respond while settling, got: {httpResponse.StatusCode}. Response: {jsonContent}");
            var statusJson = JObject.Parse(jsonContent);
            Assert.IsNotNull(statusJson["status"], "/api/editor_status should report the editor state");
        }

        [Test]
        public async Task ApiExec_AfterSettling_PreviouslyGatedCommandSucceeds()
        {
            // Arrange - the settle window ends (one-way latch releases)
            m_Server.Settled = true;

            // Act - the same command that was gated in the settling tests
            var response = await m_PipelineClient.ExecuteCommandAsync("log_editor",
                new { message = "runs after settling" });

            // Assert
            Assert.IsTrue(response.IsSuccess,
                $"Main-thread command should execute once settled, got: {response.StatusCode}. Response: {response.RawResponse}");
            Assert.IsTrue(response.IsCommandSuccess, "Main-thread command should succeed once settled");
        }

        [Test]
        public void ServerStartedInSettledSession_SettlesImmediately()
        {
            // Precondition, asserted explicitly rather than assumed: this test probes the
            // "session already settled ⇒ immediate settle" path, which is only meaningful once
            // the session-settled marker is set. It always is by the time tests run (the test
            // runner doesn't execute while the initial import/compile is still pending, and the
            // live server settles then), but if a test-runner change ever violates that, fail
            // loudly on the precondition instead of confusingly on the latch assertion below.
            Assert.IsTrue(LatchProbeServer.SessionSettled,
                "PRECONDITION: the Editor session should have settled before EditMode tests run — " +
                "if this fails, the test ran before the session's first idle moment and the assertion below would be meaningless");

            // Act - the session-scoped latch must release during Start() even if the Editor
            // happens to be importing/compiling at that instant (as it is on CI when fixtures
            // create scripts). Mid-session servers must never gate anything (AUTHAPI-35 gates the
            // cold-import window only).
            var server = new LatchProbeServer();
            try
            {
                server.Start();

                // Assert
                Assert.IsTrue(server.RealSettled,
                    "A server started after the session has settled should settle immediately (no behavior change outside the cold-import window)");
            }
            finally
            {
                server.Stop();
            }
        }
    }
}
