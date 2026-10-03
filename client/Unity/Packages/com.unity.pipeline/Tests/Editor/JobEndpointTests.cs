using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Pipeline;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Tests for the detached-job surface (CLI-335): POST /api/exec with "job": true returns a
    /// job id immediately; GET /api/job?id=… polls state/progress and retains the result for
    /// reattach after a client timeout; POST /api/job/cancel cancels a queued job outright and
    /// requests cooperative cancellation (PipelineCancellation) of a running one.
    /// </summary>
    public class JobEndpointTests
    {
        private EditorPipelineServer m_Server;
        private Unity.Pipeline.Tests.Runtime.PipelineClient m_PipelineClient;

        /// <summary>Gates job_test_wait so tests control exactly when a job completes.</summary>
        private static readonly ManualResetEventSlim m_ReleaseJobCommand = new ManualResetEventSlim(false);

        /// <summary>Gates job_test_delayed_progress so a test can observe it running before it reports anything.</summary>
        private static readonly ManualResetEventSlim m_ReleaseDelayedProgressCommand = new ManualResetEventSlim(false);

        [SetUp]
        public void SetUp()
        {
            CommandRegistry.SetDiscovery(new TypeCacheCommandDiscovery());

            m_ReleaseJobCommand.Reset();
            m_ReleaseDelayedProgressCommand.Reset();

            m_Server = new TestEditorPipelineServer();
            m_Server.Start();
            m_Server.JobRegistry.Reset();
            m_Server.Progress.Clear();

            m_PipelineClient = new Unity.Pipeline.Tests.Runtime.PipelineClient(m_Server);
        }

        [TearDown]
        public void TearDown()
        {
            m_ReleaseJobCommand.Set();
            m_ReleaseDelayedProgressCommand.Set();
            m_PipelineClient?.Dispose();
            m_Server?.JobRegistry.Reset();
            m_Server?.Progress.Clear();
            m_Server?.Stop();
        }

        [CliCommand("job_test_wait", "Test command: report progress and wait for release", MainThreadRequired = false)]
        public static string JobTestWait()
        {
            CliProgress.Report("Job Test", "Waiting", 1, 2, 0.5);
            m_ReleaseJobCommand.Wait(TimeSpan.FromSeconds(15));
            return "job done";
        }

        /// <summary>Test command: runs (State becomes Running) but reports nothing until released.</summary>
        [CliCommand("job_test_delayed_progress", "Test command: wait for release, then report progress", MainThreadRequired = false)]
        public static string JobTestDelayedProgress()
        {
            m_ReleaseDelayedProgressCommand.Wait(TimeSpan.FromSeconds(15));
            CliProgress.Report("Job B", "Reporting late");
            return "job b done";
        }

        [CliCommand("job_test_cancellable", "Test command: loop until cooperatively canceled", MainThreadRequired = false)]
        public static string JobTestCancellable()
        {
            for (var i = 0; i < 300; i++)
            {
                PipelineCancellation.ThrowIfCancellationRequested();
                Thread.Sleep(50);
            }
            return "ran to completion";
        }

        private async Task<JObject> SubmitJobAsync(string command)
        {
            var response = await m_PipelineClient.PostJsonAsync("/api/exec", new
            {
                command,
                parameters = new { },
                job = true
            });
            Assert.IsTrue(response.IsSuccess, $"Job submission should succeed: {response.Error}");
            var json = response.JsonResponse;
            Assert.IsNotNull(json, "Job submission should return JSON");
            Assert.AreEqual(true, json["success"]?.Value<bool>());
            // Standard exec envelope: the job handle is the command's result.
            var result = json["result"];
            Assert.IsNotNull(result?["jobId"], "Submission must return a job id immediately");
            Assert.AreEqual("queued", result["state"]?.ToString());
            return (JObject)result;
        }

        private async Task<JObject> GetJobAsync(string jobId)
        {
            var httpResponse = await m_PipelineClient.GetHttpAsync($"/api/job?id={jobId}");
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"/api/job should return success for known id, got: {httpResponse.StatusCode}");
            return JObject.Parse(await httpResponse.Content.ReadAsStringAsync());
        }

        private async Task<JObject> WaitForStateAsync(string jobId, string state, int attempts = 200)
        {
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                var json = await GetJobAsync(jobId);
                if (json["state"]?.ToString() == state)
                {
                    return json;
                }
                await Task.Delay(50);
            }
            Assert.Fail($"Job {jobId} never reached state '{state}'");
            return null;
        }

        [Test]
        public async Task DetachedJob_ReturnsIdImmediately_RunsAndRetainsResult()
        {
            var submitted = await SubmitJobAsync("job_test_wait");
            var jobId = submitted["jobId"].ToString();

            // While the command is gated open, the job must report running — with the
            // command's CliProgress snapshot attached.
            var running = await WaitForStateAsync(jobId, "running");
            Assert.IsNotNull(running["progress"], "Running job should carry the progress snapshot");
            Assert.AreEqual("Job Test", running["progress"]["title"]?.ToString());

            m_ReleaseJobCommand.Set();
            var completed = await WaitForStateAsync(jobId, "completed");
            Assert.AreEqual("job done", completed["result"]?.ToString());

            // Reattach semantics: the result is retained and can be fetched again.
            var again = await GetJobAsync(jobId);
            Assert.AreEqual("completed", again["state"]?.ToString());
            Assert.AreEqual("job done", again["result"]?.ToString());
        }

        [Test]
        public async Task QueuedJob_DoesNotInheritPreviousJobsProgress()
        {
            // Job A reports progress and holds the exec gate open; job B queues behind it and,
            // once it starts, deliberately reports nothing until released — the window this
            // test inspects is exactly the one where A's leftover progress could otherwise leak.
            var jobA = (await SubmitJobAsync("job_test_wait"))["jobId"].ToString();
            await WaitForStateAsync(jobA, "running");
            var jobB = (await SubmitJobAsync("job_test_delayed_progress"))["jobId"].ToString();

            m_ReleaseJobCommand.Set();
            await WaitForStateAsync(jobA, "completed");

            var runningB = await WaitForStateAsync(jobB, "running");
            Assert.IsNull(runningB["progress"],
                "Job B is running but hasn't reported yet — it must not surface job A's stale progress");

            m_ReleaseDelayedProgressCommand.Set();
            var completedB = await WaitForStateAsync(jobB, "completed");
            Assert.AreEqual("job b done", completedB["result"]?.ToString());
        }

        [Test]
        public async Task CancelQueuedJob_NeverStarts()
        {
            // Job A holds the exec gate open; job B queues behind it.
            var jobA = (await SubmitJobAsync("job_test_wait"))["jobId"].ToString();
            await WaitForStateAsync(jobA, "running");
            var jobB = (await SubmitJobAsync("job_test_wait"))["jobId"].ToString();

            var cancelResponse = await m_PipelineClient.PostJsonAsync("/api/job/cancel", new { id = jobB });
            Assert.IsTrue(cancelResponse.IsSuccess, $"Cancel should succeed: {cancelResponse.Error}");
            Assert.AreEqual(true, cancelResponse.JsonResponse["cancellationRequested"]?.Value<bool>());

            m_ReleaseJobCommand.Set();
            var canceled = await WaitForStateAsync(jobB, "canceled");
            Assert.IsNull(canceled["startedAt"], "A job canceled while queued must never start");
            await WaitForStateAsync(jobA, "completed");
        }

        [Test]
        public async Task CancelRunningJob_CooperativeCancellationTakesEffect()
        {
            // The command surfaces cancellation by throwing OperationCanceledException,
            // which the command layer logs as an error before the job runner marks the
            // job canceled — expected here, not a test failure.
            LogAssert.Expect(LogType.Error, new Regex("cancellation was requested"));
            var jobId = (await SubmitJobAsync("job_test_cancellable"))["jobId"].ToString();
            await WaitForStateAsync(jobId, "running");

            var cancelResponse = await m_PipelineClient.PostJsonAsync("/api/job/cancel", new { id = jobId });
            Assert.IsTrue(cancelResponse.IsSuccess, $"Cancel should succeed: {cancelResponse.Error}");

            var canceled = await WaitForStateAsync(jobId, "canceled");
            Assert.IsNull(canceled["result"], "A cooperatively canceled job must not report a result");
        }

        [Test]
        public async Task UnknownJobId_Returns404()
        {
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/job?id=does-not-exist");
            Assert.AreEqual(404, (int)httpResponse.StatusCode);
        }

        [Test]
        public async Task Eval_AcceptsTimeoutAboveThirtySeconds()
        {
            // CLI-335: the server-side eval cap was a hard 30000ms; long timeouts are now legal.
            var response = await m_PipelineClient.ExecuteCommandAsync("eval", new
            {
                code = "return 21 * 2;",
                timeout = 120000
            });
            Assert.IsTrue(response.IsSuccess, $"eval with a 120s timeout should be accepted: {response.Error}");
        }
    }
}
