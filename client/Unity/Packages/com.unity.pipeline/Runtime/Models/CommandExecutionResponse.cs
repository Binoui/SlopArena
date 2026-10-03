using System;
using Newtonsoft.Json;

namespace Unity.Pipeline.Models
{
    /// <summary>
    /// Response model for /api/exec endpoint.
    /// Contains execution result and metadata for remote command execution.
    /// </summary>
    [Serializable]
    public class CommandExecutionResponse : BaseResponse
    {
        /// <summary>
        /// Whether the command executed successfully.
        /// </summary>
        [JsonProperty("success")]
        public bool Success { get; set; }

        /// <summary>
        /// Name of the command that was executed.
        /// </summary>
        [JsonProperty("command")]
        public string Command { get; set; }


        /// <summary>
        /// Result returned by the command (if any).
        /// </summary>
        [JsonProperty("result")]
        public object Result { get; set; }

        /// <summary>
        /// Execution duration in milliseconds.
        /// </summary>
        [JsonProperty("executionTimeMs")]
        public long? ExecutionTimeMs { get; set; }

        /// <summary>
        /// Coarse machine-readable server state marker. Set to "busy" when the command was not
        /// executed because the server's host cannot service it yet (e.g. the Editor is still
        /// settling after a cold start). Omitted from the JSON on ordinary success/failure
        /// responses, so existing envelopes are unchanged.
        /// </summary>
        [JsonProperty("status", NullValueHandling = NullValueHandling.Ignore)]
        public string Status { get; set; }

        /// <summary>
        /// True when the failure is transient and the same request is expected to succeed after a
        /// short wait (poll /api/status until it reports "ready"). Omitted from the JSON on
        /// ordinary success/failure responses.
        /// </summary>
        [JsonProperty("retryable", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Retryable { get; set; }

        /// <summary>
        /// Create a successful execution response.
        /// </summary>
        public static CommandExecutionResponse CmdSuccess(string command, object result = null, long? executionTimeMs = null)
        {
            return new CommandExecutionResponse
            {
                Success = true,
                Command = command,
                ExecutedAt = DateTime.UtcNow,
                Result = result,
                ExecutionTimeMs = executionTimeMs
            };
        }

        /// <summary>
        /// Create a failed execution response.
        /// </summary>
        public static CommandExecutionResponse CmdFailure(string cmd, string error, string errorDetails)
        {
            return new CommandExecutionResponse
            {
                Command = cmd,
                Success = false,
                ExecutedAt = DateTime.UtcNow,
                Error = error,
                ErrorDetails = errorDetails
            };
        }

        /// <summary>
        /// Create the retryable "server busy" response (sent with HTTP 503): the command was not
        /// executed because the host cannot service it yet — distinguishable from a genuine
        /// command failure via status/retryable so callers know to retry shortly instead of
        /// guessing (AUTHAPI-35).
        /// </summary>
        public static CommandExecutionResponse CmdBusy(string cmd, string details)
        {
            return new CommandExecutionResponse
            {
                Command = cmd,
                Success = false,
                ExecutedAt = DateTime.UtcNow,
                Error = "Server Busy",
                ErrorDetails = details,
                Status = "busy",
                Retryable = true
            };
        }
    }
}