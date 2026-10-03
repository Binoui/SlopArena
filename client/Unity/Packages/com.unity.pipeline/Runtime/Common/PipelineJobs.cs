using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Unity.Pipeline
{
    /// <summary>
    /// Lifecycle states of a detached command job (CLI-335).
    /// Serialized to the wire as lower-case strings (see BasePipelineServer).
    /// </summary>
    internal enum PipelineJobState
    {
        Queued,
        Running,
        Completed,
        Failed,
        Canceled
    }

    /// <summary>One detached command execution tracked by <see cref="PipelineJobRegistry"/>.</summary>
    internal sealed class PipelineJobRecord
    {
        public string Id;
        public string Command;
        public PipelineJobState State;
        public object Result;
        public string Error;
        public string ErrorDetails;
        public DateTime EnqueuedAt;
        public DateTime? StartedAt;
        public DateTime? CompletedAt;
        public volatile bool CancellationRequested;

        /// <summary>Guards State/Result/Error/timestamps; reads and writes cross threads.</summary>
        public readonly object Gate = new object();
    }

    /// <summary>
    /// Registry of detached command jobs (CLI-335): a client can submit a command as a job
    /// (`/api/exec` with `"job": true`), get a job id back immediately, and later poll
    /// `/api/job?id=…` to reattach and collect the result — surviving the client's own HTTP
    /// timeout. Results are retained (bounded, see below) so a client that timed out or
    /// disconnected can still fetch the outcome.
    ///
    /// Owned one-per-<see cref="BasePipelineServer"/> (the same pattern as <c>Dispatcher</c>), so
    /// a test server's jobs can never cross-attribute state with the live server's.
    ///
    /// In-memory by design: a domain reload (script recompilation) discards pending jobs the
    /// same way it discards every other C# static. That limitation is inherent to in-Editor
    /// state and is tracked separately.
    /// </summary>
    internal sealed class PipelineJobRegistry
    {
        /// <summary>Completed/failed/canceled jobs retained beyond this count are pruned oldest-first.</summary>
        private const int MaxRetainedTerminalJobs = 100;

        /// <summary>
        /// Jobs allowed queued or running at once. Jobs execute strictly serially through the
        /// exec gate, so a deep backlog beyond this is pure queued work with no throughput
        /// benefit — an authorized client loop (or a retrying script) submitting faster than jobs
        /// drain would otherwise grow this without bound.
        /// </summary>
        private const int MaxQueuedJobs = 100;

        /// <summary>Terminal jobs older than this are pruned regardless of count.</summary>
        private static readonly TimeSpan m_RetentionTtl = TimeSpan.FromHours(1);

        private readonly ConcurrentDictionary<string, PipelineJobRecord> m_Jobs =
            new ConcurrentDictionary<string, PipelineJobRecord>();

        /// <summary>Guards the queued-job cap check against a concurrent create racing past it.</summary>
        private readonly object m_CreateGate = new object();

        /// <summary>
        /// The record currently executing. At most one job runs at a time (jobs share the
        /// server's exec gate), so a single reference suffices; read by
        /// <see cref="PipelineCancellation"/> from whatever thread the command runs on.
        /// </summary>
        private volatile PipelineJobRecord m_CurrentRunning;

        internal PipelineJobRecord CurrentRunning => m_CurrentRunning;

        /// <summary>
        /// Create a queued job for <paramref name="command"/>, unless <see cref="MaxQueuedJobs"/>
        /// queued-or-running jobs already exist (returns false; the caller should reject the
        /// submission rather than grow an unbounded backlog).
        /// </summary>
        internal bool TryCreate(string command, out PipelineJobRecord record)
        {
            Prune();

            lock (m_CreateGate)
            {
                var nonTerminal = m_Jobs.Values.Count(job =>
                {
                    lock (job.Gate)
                    {
                        return job.State == PipelineJobState.Queued || job.State == PipelineJobState.Running;
                    }
                });
                if (nonTerminal >= MaxQueuedJobs)
                {
                    record = null;
                    return false;
                }

                record = new PipelineJobRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Command = command,
                    State = PipelineJobState.Queued,
                    EnqueuedAt = DateTime.UtcNow
                };
                m_Jobs[record.Id] = record;
            }
            return true;
        }

        internal bool TryGet(string id, out PipelineJobRecord record)
        {
            Prune();
            return m_Jobs.TryGetValue(id ?? string.Empty, out record);
        }

        internal void MarkRunning(PipelineJobRecord record)
        {
            lock (record.Gate)
            {
                record.State = PipelineJobState.Running;
                record.StartedAt = DateTime.UtcNow;
            }
            m_CurrentRunning = record;
        }

        internal void MarkCompleted(PipelineJobRecord record, object result)
        {
            lock (record.Gate)
            {
                record.State = PipelineJobState.Completed;
                record.Result = result;
                record.CompletedAt = DateTime.UtcNow;
            }
            ClearCurrent(record);
        }

        internal void MarkFailed(PipelineJobRecord record, string error, string details)
        {
            lock (record.Gate)
            {
                record.State = PipelineJobState.Failed;
                record.Error = error;
                record.ErrorDetails = details;
                record.CompletedAt = DateTime.UtcNow;
            }
            ClearCurrent(record);
        }

        internal void MarkCanceled(PipelineJobRecord record)
        {
            lock (record.Gate)
            {
                record.State = PipelineJobState.Canceled;
                record.CompletedAt = DateTime.UtcNow;
            }
            ClearCurrent(record);
        }

        /// <summary>
        /// Request cancellation. A queued job is canceled before it ever starts (the runner
        /// checks the flag under the exec gate); a running job gets a cooperative flag that
        /// command/eval code can observe via <see cref="PipelineCancellation"/> — arbitrary
        /// synchronous main-thread code cannot be aborted safely, so cancellation of a running
        /// job takes effect only when the code cooperates (mirroring how test cancellation
        /// requires the runner's cooperation).
        /// </summary>
        internal bool RequestCancel(string id, out PipelineJobRecord record)
        {
            if (!TryGet(id, out record))
            {
                return false;
            }
            record.CancellationRequested = true;
            return true;
        }

        private void ClearCurrent(PipelineJobRecord record)
        {
            if (ReferenceEquals(m_CurrentRunning, record))
            {
                m_CurrentRunning = null;
            }
        }

        private void Prune()
        {
            var now = DateTime.UtcNow;
            var terminal = m_Jobs.Values
                .Where(job =>
                {
                    lock (job.Gate)
                    {
                        return job.State == PipelineJobState.Completed
                            || job.State == PipelineJobState.Failed
                            || job.State == PipelineJobState.Canceled;
                    }
                })
                .OrderBy(job => job.CompletedAt ?? job.EnqueuedAt)
                .ToList();

            // Oldest-first: drop everything beyond the retained count, plus anything expired.
            var excess = terminal.Count - MaxRetainedTerminalJobs;
            for (var i = 0; i < terminal.Count; i++)
            {
                var job = terminal[i];
                var expired = now - (job.CompletedAt ?? job.EnqueuedAt) > m_RetentionTtl;
                if (i < excess || expired)
                {
                    m_Jobs.TryRemove(job.Id, out _);
                }
            }
        }

        /// <summary>Test hook: forget every job.</summary>
        internal void Reset()
        {
            m_Jobs.Clear();
            m_CurrentRunning = null;
        }
    }

    /// <summary>
    /// <para>Cooperative cancellation for detached jobs (CLI-335). Long-running command or
    /// <c>eval</c> code checks this periodically; when a client has requested cancellation
    /// (<c>POST /api/job/cancel</c>), the code can wind down early:</para>
    /// <code>
    /// for (int i = 0; i &lt; n; i++)
    /// {
    ///     Unity.Pipeline.PipelineCancellation.ThrowIfCancellationRequested();
    ///     // … chunk of work …
    /// }
    /// </code>
    /// <para>Arbitrary synchronous code cannot be aborted safely from outside, so a running job
    /// that never checks this flag runs to completion (its state still reports canceled).</para>
    /// <para>This is a thin static façade over the executing server's own
    /// <see cref="PipelineJobRegistry"/> (resolved via <see cref="BasePipelineServer.CurrentServer"/>)
    /// so command code — which has no reference to "which server is running me" — can keep
    /// calling this the same way regardless of which server dispatched it.</para>
    /// </summary>
    public static class PipelineCancellation
    {
        /// <summary>True when the currently executing job has been asked to cancel.</summary>
        public static bool IsCancellationRequested
        {
            get
            {
                var current = BasePipelineServer.CurrentServer?.JobRegistry.CurrentRunning;
                return current != null && current.CancellationRequested;
            }
        }

        /// <summary>Throw <see cref="OperationCanceledException"/> when cancellation was requested.</summary>
        public static void ThrowIfCancellationRequested()
        {
            if (IsCancellationRequested)
            {
                throw new OperationCanceledException("Pipeline job cancellation was requested");
            }
        }
    }
}
