using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Unity.Pipeline.Editor.Commands.PackageManager
{
    /// <summary>
    /// UPM package management over the Client API (CLI-203): list / search / add / remove / resolve.
    ///
    /// Threading: UPM <see cref="Client"/> operations are asynchronous (the <see cref="Request"/> only
    /// progresses while the editor ticks) and their members are main-thread only. Commands that wait on a
    /// request therefore run off the main thread (<c>MainThreadRequired = false</c>) and marshal every
    /// UPM touch back onto the main thread via the server dispatcher (<see cref="RunOnMain"/>), so they
    /// can block without freezing the editor.
    ///
    /// Command surface:
    ///  - <c>package_list</c> (installed scope) reads the resolved set inline; <c>available</c>/<c>all</c>
    ///    and <c>package_search</c> query the registry and wait for it — all return <b>synchronously</b>.
    ///  - <c>package_add</c>/<c>package_remove</c> mutate the manifest and trigger a <b>domain reload</b>
    ///    that tears down the in-flight request and the HTTP connection. They are <b>dual-mode</b>:
    ///    <list type="bullet">
    ///    <item><description><b>async (default)</b> — kick the op off, return <c>in_progress</c> immediately, and let
    ///    an <see cref="EditorApplication.update"/> poller finalize a Temp status file. Poll
    ///    <c>package_status</c> until <c>completed</c>/<c>failed</c>, then <c>recompile_status</c>. This
    ///    keeps the (single-request) server responsive and survives the reload.</description></item>
    ///    <item><description><b>synchronous</b> (<c>wait=true</c>) — block until the request completes and return the
    ///    full result. The result is captured in one main-thread hop the moment the request completes
    ///    (before the compile-triggered reload). A lost reply is recovered only from its correlated
    ///    terminal receipt or an operation-specific resolved-package postcondition.</description></item>
    ///    </list>
    ///    Both write the status file, so a lost reply remains observable via <c>package_status</c>.
    ///    An interrupted receipt that cannot prove success or failure stays <c>unknown</c>; ownership is
    ///    not settled from a reload or manifest-only change.
    ///  - <c>package_resolve</c> records its status too, so <c>package_status</c> validates it.
    ///
    /// Mutating commands (add / remove) follow the shared <c>confirm</c>/<c>dry_run</c> convention.
    /// UPM operations are not part of Unity's Undo.
    /// </summary>
    [InitializeOnLoad]
    public static class PackageManagerCommand
    {
        const string StatusFile = "Temp/pipeline_package_status.json";

        // Bound on a synchronous wait, kept under the CLI's default request timeout so a stuck operation
        // surfaces as a clean error rather than a dropped connection.
        const int WaitTimeoutSeconds = 25;
        const int PollIntervalMs = 50;

        static readonly object s_Lock = new object();

        // The native request and its receipt are one persisted package operation. The ownership host
        // activity deliberately outlives the HTTP command and is cleared only after a terminal result.
        static volatile bool s_InProgress;
        static Request s_Request;
        static string s_Operation;
        static string s_Argument;
        static string s_OperationId;
        static Func<Request, PackageSummary> s_ReadPackage;
        static PackageStatus s_Receipt;
        static EditorCommandOwnership s_TrackingOwnership;
        static bool s_RecoveryAttempted;

        static PackageManagerCommand()
        {
            RecoverInterruptedOperation();
        }

        static void RecoverWhenEditorReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            EditorApplication.update -= RecoverWhenEditorReady;
            RecoverInterruptedOperation();
        }

        // ---- List / search (synchronous) -------------------------------------------------------

        [CliCommand("package_list",
            "List packages by scope: installed (default) | available (registry) | all (both). Returns the full " +
            "result synchronously — available/all block until the registry query completes.",
            MainThreadRequired = false,
            Tags = new[] { "packages" })]
        public static object PackageList(
            [CliArg("scope", "Which packages to list: installed (default) | available | all.")] string scope = "installed",
            [CliArg("include_indirect", "Include indirect (transitive) installed dependencies (applies to scope=installed/all).")]
            bool includeIndirect = true,
            [CliArg("offline", "For available/all: query the local cache instead of the registry.")] bool offline = false)
        {
            switch ((scope ?? "installed").Trim().ToLowerInvariant())
            {
                case "installed":
                    return RunOnMain(() => ListInstalled(includeIndirect));
                case "available":
                    return ListFromRegistry("available", includeIndirect, offline);
                case "all":
                    return ListFromRegistry("all", includeIndirect, offline);
                default:
                    return new PackageListResponse
                    {
                        Success = false,
                        Scope = scope,
                        Message = $"Unknown scope '{scope}'. Use installed | available | all."
                    };
            }
        }

        /// <summary>
        /// Installed scope: <see cref="PackageInfo.GetAllRegisteredPackages"/> reflects the resolved
        /// set without a registry round-trip. Runs on the main thread (via <see cref="RunOnMain"/>).
        /// </summary>
        static PackageListResponse ListInstalled(bool includeIndirect)
        {
            var summaries = new List<PackageSummary>();
            foreach (var p in PackageInfo.GetAllRegisteredPackages())
            {
                if (includeIndirect || p.isDirectDependency)
                    summaries.Add(Map(p, isInstalled: true));
            }

            return new PackageListResponse
            {
                Success = true,
                Scope = "installed",
                Count = summaries.Count,
                Packages = summaries,
                Manifest = SafeReadManifest(),
                Message = $"{summaries.Count} installed package(s)."
            };
        }

        /// <summary>
        /// available / all scope: these need the registry, so a <c>Client.SearchAll</c> request is
        /// started on the main thread and awaited here (on the command's background thread). For
        /// <c>all</c>, the installed set is merged in (and marked) so callers see one unified list.
        /// </summary>
        static object ListFromRegistry(string scope, bool includeIndirect, bool offline)
        {
            var request = RunOnMain(() => Client.SearchAll(offline));

            if (!WaitForCompletion(request, out var waitError))
                return new PackageListResponse { Success = false, Scope = scope, Message = waitError };

            return RunOnMain<object>(() =>
            {
                if (request.Status != StatusCode.Success)
                    return new PackageListResponse
                    {
                        Success = false,
                        Scope = scope,
                        Message = $"Registry query failed: {request.Error?.message ?? "unknown error"}"
                    };

                var packages = BuildScopedList(scope, request.Result, includeIndirect);
                return new PackageListResponse
                {
                    Success = true,
                    Scope = scope,
                    Count = packages.Count,
                    Packages = packages,
                    Manifest = SafeReadManifest(),
                    Message = $"{packages.Count} package(s)."
                };
            });
        }

        /// <summary>
        /// Combine the registry search result with the installed set per <paramref name="scope"/>:
        /// <c>available</c> returns every registry package (flagged whether it is installed);
        /// <c>all</c> returns installed packages (resolved info) plus the registry packages that are
        /// not installed.
        /// </summary>
        static List<PackageSummary> BuildScopedList(string scope, PackageInfo[] available, bool includeIndirect)
        {
            var installed = new Dictionary<string, PackageInfo>();
            foreach (var p in PackageInfo.GetAllRegisteredPackages())
                installed[p.name] = p;

            var result = new List<PackageSummary>();

            if (scope == "available")
            {
                foreach (var p in available ?? Array.Empty<PackageInfo>())
                    result.Add(Map(p, isInstalled: installed.ContainsKey(p.name)));
                return result;
            }

            // scope == "all": installed entries first (with their resolved info), then registry-only ones.
            var seen = new HashSet<string>();
            foreach (var entry in installed.Values)
            {
                if (!includeIndirect && !entry.isDirectDependency)
                    continue;
                result.Add(Map(entry, isInstalled: true));
                seen.Add(entry.name);
            }
            foreach (var p in available ?? Array.Empty<PackageInfo>())
            {
                if (!seen.Contains(p.name))
                    result.Add(Map(p, isInstalled: false));
            }
            return result;
        }

        [CliCommand("package_search",
            "Search packages available in the registry. Provide a name (e.g. com.unity.foo) or omit to list all. " +
            "Returns the full result synchronously (blocks until the registry query completes).",
            MainThreadRequired = false,
            Tags = new[] { "packages" })]
        public static object PackageSearch(
            [CliArg("query", "Package name to search for. Omit/empty to list all available packages.")] string query = "",
            [CliArg("offline", "Search the local cache only.")] bool offline = false)
        {
            var trimmed = (query ?? string.Empty).Trim();

            var request = RunOnMain(() => string.IsNullOrEmpty(trimmed)
                ? Client.SearchAll(offline)
                : Client.Search(trimmed, offline));

            if (!WaitForCompletion(request, out var waitError))
                return new PackageSearchResponse { Success = false, Query = trimmed, Message = waitError };

            return RunOnMain<object>(() =>
            {
                if (request.Status != StatusCode.Success)
                    return new PackageSearchResponse
                    {
                        Success = false,
                        Query = trimmed,
                        Message = $"Search failed: {request.Error?.message ?? "unknown error"}"
                    };

                var packages = MapMarkingInstalled(request.Result);
                return new PackageSearchResponse
                {
                    Success = true,
                    Query = trimmed,
                    Count = packages.Count,
                    Packages = packages,
                    Message = $"{packages.Count} package(s)."
                };
            });
        }

        // ---- Mutating operations (dual-mode, CAT-2509 gated) -----------------------------------

        [CliCommand("package_add",
            "Add a UPM package by name@version, git URL, or 'file:' local path. Async by default (returns " +
            "in_progress; poll package_status); pass wait=true to block until added. A recompile/domain reload " +
            "follows — poll recompile_status. Requires confirm=true; use dry_run to preview.",
            MainThreadRequired = false,
            Tags = new[] { "packages" })]
        public static object PackageAdd(
            [CliArg("identifier", "Package to add: 'com.unity.foo@1.2.3', a git URL, or 'file:../Path'.", Required = true)] string identifier = "",
            [CliArg("confirm", "Apply the change. Without it the call is refused.")] bool confirm = false,
            [CliArg("dry_run", "Preview the change without applying it.")] bool dryRun = false,
            [CliArg("wait", "Block until the operation completes and return the result (synchronous). Default: return immediately and poll package_status.")] bool wait = false)
        {
            var ownershipContext = EditorCommandOwnershipContext.Current;
            if (!PackageIdentifier.TryParse(identifier, out var parsed, out var parseError))
                return PackageMutationResponse.Failed("add", identifier, parseError);

            if (IsBusy())
                return PackageMutationResponse.Busy("add");

            return ExecuteMutation(
                ownershipContext: ownershipContext,
                operation: "add",
                commandName: "package_add",
                argument: parsed.Identifier,
                planText: $"Add package {parsed.Description} [{parsed.Kind}]",
                confirm: confirm,
                dryRun: dryRun,
                wait: wait,
                start: () => Client.Add(parsed.Identifier),
                readPackage: req =>
                {
                    var added = (req as AddRequest)?.Result;
                    return added != null ? Map(added, isInstalled: true) : null;
                });
        }

        [CliCommand("package_remove",
            "Remove a UPM package by name. Async by default (returns in_progress; poll package_status); pass " +
            "wait=true to block until removed. A recompile/domain reload follows — poll recompile_status. " +
            "Requires confirm=true; use dry_run to preview.",
            MainThreadRequired = false,
            Tags = new[] { "packages" })]
        public static object PackageRemove(
            [CliArg("name", "Package name to remove (e.g. com.unity.foo).", Required = true)] string name = "",
            [CliArg("confirm", "Apply the change. Without it the call is refused.")] bool confirm = false,
            [CliArg("dry_run", "Preview the change without applying it.")] bool dryRun = false,
            [CliArg("wait", "Block until the operation completes and return the result (synchronous). Default: return immediately and poll package_status.")] bool wait = false)
        {
            var ownershipContext = EditorCommandOwnershipContext.Current;
            if (string.IsNullOrWhiteSpace(name))
                return PackageMutationResponse.Failed("remove", name, "A package name is required.");

            var packageName = name.Trim();

            if (IsBusy())
                return PackageMutationResponse.Busy("remove");

            return ExecuteMutation(
                ownershipContext: ownershipContext,
                operation: "remove",
                commandName: "package_remove",
                argument: packageName,
                planText: $"Remove package '{packageName}'",
                confirm: confirm,
                dryRun: dryRun,
                wait: wait,
                start: () => Client.Remove(packageName),
                readPackage: null);
        }

        [CliCommand("package_resolve",
            "Resolve/refresh packages from the manifest (re-fetch and re-link). May trigger a recompile/domain " +
            "reload — poll recompile_status. Its outcome is recorded for package_status.",
            MainThreadRequired = true,
            Tags = new[] { "packages" })]
        public static object PackageResolve()
        {
            var ownershipContext = EditorCommandOwnershipContext.Current;
            if (IsBusy())
                return PackageMutationResponse.Busy("resolve");
            var receipt = CreateReceipt("resolve", null, ownershipContext);
            if (ownershipContext != null
                && !ownershipContext.Ownership.BeginHostActivity(ownershipContext, "package"))
                return PackageMutationResponse.Failed("resolve", null,
                    "Editor ownership changed before package resolution could start.");

            receipt.Status = "in_progress";
            receipt.StartedAt = NowIso();
            receipt.Message = "Resolving packages...";
            WriteStatus(receipt);

            try
            {
                // Client.Resolve() is fire-and-forget; this terminal receipt means only that Unity
                // accepted the request, not that a package reload or resolution finished.
                Client.Resolve();
            }
            catch (Exception ex)
            {
                receipt.Status = "failed";
                receipt.Success = false;
                receipt.CompletionEvidence = "command_call";
                receipt.Error = ex.Message;
                receipt.Manifest = SafeReadManifest();
                receipt.CompletedAt = NowIso();
                receipt.Message = $"Resolve failed: {ex.Message}";
                WriteStatus(receipt);
                ownershipContext?.Ownership.CompleteHostActivity(receipt.OperationId);
                var response = PackageMutationResponse.Failed("resolve", null, receipt.Error);
                response.OperationId = receipt.OperationId;
                return response;
            }

            receipt.Status = "completed";
            receipt.Success = true;
            receipt.CompletionEvidence = "command_accepted";
            receipt.RequiresRecompile = true;
            receipt.Manifest = SafeReadManifest();
            receipt.CompletedAt = NowIso();
            receipt.Message = "Resolve requested. If assemblies changed, a domain reload follows — poll recompile_status.";
            WriteStatus(receipt);
            ownershipContext?.Ownership.CompleteHostActivity(receipt.OperationId);
            return ToMutationResponse(receipt, plan: null);
        }

        // ---- Status ----------------------------------------------------------------------------

        [CliCommand("package_status",
            "Status of the last package operation (add/remove/resolve): idle | in_progress | completed | failed | unknown, " +
            "with its correlated outcome, evidence, manifest, and any error.",
            MainThreadRequired = false,
            Tags = new[] { "packages" })]
        public static string GetPackageStatus()
        {
            if (File.Exists(StatusFile))
                return File.ReadAllText(StatusFile);
            return "{\"status\":\"idle\"}";
        }

        // ---- Mutation execution ----------------------------------------------------------------

        /// <summary>
        /// Gate (confirm / dry-run) and kick off the mutating op, then
        /// either return <c>in_progress</c> (async, default) or block until completion (<paramref name="wait"/>).
        /// Either way the operation is tracked and a status file is written, so the result is recoverable
        /// via <c>package_status</c> even if the domain reload severs a synchronous reply.
        /// </summary>
        static object ExecuteMutation(
            EditorCommandOwnership.OperationContext ownershipContext,
            string operation, string commandName, string argument, string planText,
            bool confirm, bool dryRun, bool wait,
            Func<Request> start, Func<Request, PackageSummary> readPackage)
        {
            if (dryRun)
                return PackageMutationResponse.DryRunPreview(operation, argument, planText, $"Dry run — {planText}");
            if (!confirm)
                return PackageMutationResponse.Rejected(operation, argument,
                    "Refused: this changes project packages. Re-run with confirm=true to apply, or dry_run=true to preview.");

            string startError = null;
            string operationId = null;
            var request = RunOnMain(() =>
            {
                try
                {
                    if (!BeginTracking(operation, argument, ownershipContext, readPackage))
                    {
                        startError = "Editor ownership changed before the package operation could start.";
                        return null;
                    }
                    operationId = s_OperationId;

                    // Persist ownership correlation and the in-progress receipt before UPM can mutate the
                    // manifest or trigger a reload.
                    var started = start();
                    if (started == null)
                    {
                        startError = "The package manager did not return a request.";
                        FailStartingRequest(startError);
                        return null;
                    }

                    lock (s_Lock)
                        s_Request = started;
                    if (!wait)
                        SubscribePoll();
                    return started;
                }
                catch (Exception ex)
                {
                    startError = ex.Message;
                    FailStartingRequest(ex.Message);
                    return null;
                }
            });

            if (request == null)
            {
                var response = PackageMutationResponse.Failed(operation, argument,
                    startError ?? "Operation was confirmed but failed to start.");
                response.OperationId = operationId;
                return response;
            }

            if (!wait)
            {
                var response = PackageMutationResponse.InProgress(operation, argument, planText);
                response.OperationId = operationId;
                return response;
            }

            // Synchronous: poll until complete, capturing status + result atomically on the main thread
            // (a reload can only fire between ticks, so the snapshot can't be interleaved).
            var deadline = DateTime.UtcNow.AddSeconds(WaitTimeoutSeconds);
            while (true)
            {
                var status = RunOnMain(TryFinalize);
                if (status != null)
                    return ToMutationResponse(status, planText);

                if (DateTime.UtcNow > deadline)
                {
                    // Hand off to the update-loop poller so package_status still settles after we bail.
                    RunOnMain<object>(() => { SubscribePoll(); return null; });
                    var response = PackageMutationResponse.Failed(operation, argument,
                        $"Timed out after {WaitTimeoutSeconds}s; the operation may still be in progress — poll package_status.");
                    response.OperationId = operationId;
                    return response;
                }

                Thread.Sleep(PollIntervalMs);
            }
        }

        /// <summary>Persist operation identity and pre-state before starting the native UPM request.</summary>
        static bool BeginTracking(string operation, string argument,
            EditorCommandOwnership.OperationContext ownershipContext, Func<Request, PackageSummary> readPackage)
        {
            var receipt = CreateReceipt(operation, argument, ownershipContext);
            if (ownershipContext != null
                && !ownershipContext.Ownership.BeginHostActivity(ownershipContext, "package"))
                return false;

            lock (s_Lock)
            {
                s_Request = null;
                s_Operation = operation;
                s_Argument = argument;
                s_OperationId = receipt.OperationId;
                s_ReadPackage = readPackage;
                s_Receipt = receipt;
                s_TrackingOwnership = ownershipContext?.Ownership;
                s_InProgress = true;
            }

            receipt.Status = "in_progress";
            receipt.StartedAt = NowIso();
            receipt.Message = $"{operation} in progress. Poll package_status.";
            WriteStatus(receipt);
            return true;
        }

        static void FailStartingRequest(string error)
        {
            lock (s_Lock)
            {
                if (s_Receipt == null)
                    return;
                var failed = CopyReceipt();
                failed.Status = "failed";
                failed.Success = false;
                failed.CompletionEvidence = "command_call";
                failed.Error = string.IsNullOrEmpty(error) ? "The package manager failed to start the request." : error;
                failed.Manifest = SafeReadManifest();
                failed.CompletedAt = NowIso();
                failed.Message = $"{failed.Operation} failed: {failed.Error}";
                CompleteTracking(failed);
            }
        }

        static void SubscribePoll()
        {
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        /// <summary>Update-loop poller for async mode (and a timed-out sync wait): finalize when done.</summary>
        static void Poll() => TryFinalize();

        /// <summary>
        /// If the tracked request has completed, write its final status, clear tracking, and return the
        /// status; otherwise return null. Main-thread only (reads Request members); guarded so the sync
        /// waiter and the update-loop poller can't double-finalize.
        /// </summary>
        static PackageStatus TryFinalize()
        {
            lock (s_Lock)
            {
                var request = s_Request;
                if (request == null)
                {
                    EditorApplication.update -= Poll;
                    return null;
                }
                if (!request.IsCompleted)
                    return null;

                var status = BuildStatus(s_Operation, s_Argument, request, s_ReadPackage);
                CompleteTracking(status);
                return status;
            }
        }

        static PackageStatus BuildStatus(string operation, string argument, Request request,
            Func<Request, PackageSummary> readPackage)
        {
            var status = CopyReceipt();
            status.Operation = operation;
            status.Argument = argument;
            status.CompletedAt = NowIso();
            status.Manifest = SafeReadManifest();
            status.CompletionEvidence = "upm_request";

            if (request.Status == StatusCode.Success)
            {
                status.Status = "completed";
                status.Success = true;
                status.RequiresRecompile = true;
                try { status.Package = readPackage?.Invoke(request); }
                catch (Exception ex) { Debug.LogWarning($"[pipeline] package result projection failed: {ex.Message}"); }
                status.Message = $"{operation} completed.";
            }
            else
            {
                status.Status = "failed";
                status.Success = false;
                status.Error = string.IsNullOrWhiteSpace(request.Error?.message)
                    ? "Unknown package manager error."
                    : request.Error.message;
                status.Message = $"{operation} failed: {status.Error}";
            }

            return status;
        }

        static PackageStatus CopyReceipt()
        {
            var receipt = s_Receipt;
            return new PackageStatus
            {
                Operation = receipt?.Operation ?? s_Operation,
                Argument = receipt?.Argument ?? s_Argument,
                OperationId = receipt?.OperationId ?? s_OperationId,
                PreviousManifest = receipt?.PreviousManifest,
                PreviousResolvedPackages = receipt?.PreviousResolvedPackages,
                StartedAt = receipt?.StartedAt
            };
        }

        static void CompleteTracking(PackageStatus status)
        {
            WriteStatus(status);
            s_TrackingOwnership?.CompleteHostActivity(s_OperationId);
            s_Request = null;
            s_Operation = null;
            s_Argument = null;
            s_OperationId = null;
            s_ReadPackage = null;
            s_Receipt = null;
            s_TrackingOwnership = null;
            s_InProgress = false;
            EditorApplication.update -= Poll;
        }

        static PackageMutationResponse ToMutationResponse(PackageStatus s, string plan) => new PackageMutationResponse
        {
            Success = s.Success,
            Operation = s.Operation,
            OperationId = s.OperationId,
            Argument = s.Argument,
            Status = s.Status,
            Applied = s.Success,
            Plan = plan,
            Package = s.Package,
            Manifest = s.Manifest,
            RequiresRecompile = s.RequiresRecompile,
            Message = s.Message
        };

        /// <summary>
        /// Recover only from a correlated terminal receipt or a changed, operation-specific resolved
        /// package postcondition. A reload alone, a manifest-only change, or a stale receipt is unknown.
        /// </summary>
        internal static void RecoverInterruptedOperation()
        {
            EditorApplication.update -= RecoverWhenEditorReady;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.update += RecoverWhenEditorReady;
                return;
            }
            if (s_RecoveryAttempted)
                return;
            s_RecoveryAttempted = true;

            try
            {
                var status = ReadStatus();
                if (status == null || status.Status != "in_progress")
                    return;

                var currentManifest = SafeReadManifest();
                var currentPackages = SnapshotResolvedPackages();
                status.Manifest = currentManifest;
                status.CompletedAt = NowIso();
                if (TryProveResolvedPostcondition(status, currentManifest, currentPackages))
                {
                    status.Status = "completed";
                    status.Success = true;
                    status.RequiresRecompile = true;
                    status.CompletionEvidence = "resolved_package_state";
                    status.Error = null;
                    status.Message = $"{status.Operation} completion recovered from its changed registered-package state.";
                }
                else
                {
                    status.Status = "unknown";
                    status.Success = false;
                    status.RequiresRecompile = false;
                    status.CompletionEvidence = "unproven";
                    status.Error = "UPM's terminal request result was not persisted and the operation-specific resolved package postcondition could not be verified.";
                    status.Message = $"{status.Operation ?? "Package"} outcome is unknown after domain reload; ownership remains blocked.";
                }
                WriteStatus(status);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[pipeline] package status recovery failed: {ex.Message}");
                var status = ReadStatus();
                if (status == null || status.Status != "in_progress")
                    return;
                status.Status = "unknown";
                status.Success = false;
                status.CompletionEvidence = "unproven";
                status.CompletedAt = NowIso();
                status.Error = $"Interrupted package outcome could not be verified: {ex.Message}";
                status.Message = $"{status.Operation ?? "Package"} outcome is unknown after domain reload; ownership remains blocked.";
                WriteStatus(status);
            }
        }

        internal static bool TryReconcileOwnershipActivity(string operationId,
            EditorCommandOwnership ownership, out string reason)
        {
            RecoverInterruptedOperation();
            return TryReconcileOwnershipActivity(ReadStatus(), operationId, ownership, out reason);
        }

        internal static bool TryReconcileOwnershipActivity(PackageStatus status, string operationId,
            EditorCommandOwnership ownership, out string reason)
        {
            reason = null;
            if (ownership == null || string.IsNullOrEmpty(operationId))
            {
                reason = "Package recovery has no matching ownership operation.";
                return false;
            }
            if (status == null || status.OperationId != operationId)
            {
                reason = "Package receipt is missing or belongs to a different operation; completion is unknown.";
                return false;
            }
            if (status.Operation != "add" && status.Operation != "remove" && status.Operation != "resolve")
            {
                reason = "Package receipt has an invalid operation; completion is unknown.";
                return false;
            }
            if ((status.Operation == "add" || status.Operation == "remove")
                && string.IsNullOrWhiteSpace(status.Argument))
            {
                reason = "Package receipt has no operation subject; completion is unknown.";
                return false;
            }
            if (!DateTime.TryParse(status.StartedAt, out _) || !DateTime.TryParse(status.CompletedAt, out _))
            {
                reason = "Package receipt is not a complete terminal record; completion is unknown.";
                return false;
            }

            var terminalSuccess = status.Status == "completed" && status.Success
                && ((status.Operation == "add" || status.Operation == "remove")
                    ? status.CompletionEvidence == "upm_request" || status.CompletionEvidence == "resolved_package_state"
                    : status.CompletionEvidence == "command_accepted");
            var terminalFailure = status.Status == "failed" && !status.Success
                && !string.IsNullOrWhiteSpace(status.Error)
                && (status.CompletionEvidence == "upm_request" || status.CompletionEvidence == "command_call");
            if (!terminalSuccess && !terminalFailure)
            {
                reason = string.IsNullOrWhiteSpace(status.Error)
                    ? $"Package operation '{status.Operation}' has no trustworthy terminal receipt; completion is unknown."
                    : status.Error;
                return false;
            }

            var state = ownership.SnapshotForPersistence();
            var expectedCommand = status.Operation == "add" ? "package_add"
                : status.Operation == "remove" ? "package_remove" : "package_resolve";
            var matchingOperation = state.Operations != null
                && state.Operations.Exists(operation => operation.Id == operationId && operation.Command == expectedCommand);
            var hasOperation = state.Operations != null
                && state.Operations.Exists(operation => operation.Id == operationId);
            var matchingActivity = state.HostActivities != null
                && state.HostActivities.Exists(activity => activity.Id == operationId
                    && activity.Kind == "package" && activity.Command == expectedCommand);
            if ((hasOperation && !matchingOperation) || (!matchingOperation && !matchingActivity))
            {
                reason = "Package ownership operation/activity does not match the recovered receipt.";
                return false;
            }

            ownership.CompleteHostActivity(operationId);
            return true;
        }

        // ---- Helpers ---------------------------------------------------------------------------

        static bool IsBusy()
        {
            if (s_InProgress)
                return true;
            if (!File.Exists(StatusFile))
                return false;
            var status = ReadStatus();
            return status == null || status.Status == "in_progress";
        }

        /// <summary>
        /// Block until <paramref name="request"/> completes, polling its (main-thread-only) state via
        /// <see cref="RunOnMain"/> while this thread sleeps between checks. Returns false with
        /// <paramref name="error"/> set on timeout. Used by the read-only registry queries.
        /// </summary>
        static bool WaitForCompletion(Request request, out string error)
        {
            error = null;
            var deadline = DateTime.UtcNow.AddSeconds(WaitTimeoutSeconds);
            while (!RunOnMain(() => request.IsCompleted))
            {
                if (DateTime.UtcNow > deadline)
                {
                    error = $"Timed out after {WaitTimeoutSeconds}s waiting for the package manager.";
                    return false;
                }
                Thread.Sleep(PollIntervalMs);
            }
            return true;
        }

        /// <summary>
        /// Run <paramref name="fn"/> on the Unity main thread. When invoked off-thread (the normal case
        /// for the waiting commands), it marshals through the live server's dispatcher; when already on
        /// the main thread (e.g. a direct in-process/test call, or no server running) it runs inline.
        /// </summary>
        static T RunOnMain<T>(Func<T> fn)
        {
            var dispatcher = PipelineServerStartup.Server?.Dispatcher;
            if (dispatcher != null && dispatcher.IsInitialized && !dispatcher.IsMainThread())
                return dispatcher.Invoke(fn);
            return fn();
        }

        static Dictionary<string, string> SafeReadManifest() =>
            PackageManifest.TryRead(out var deps, out _) ? deps : null;

        internal static string ManifestFingerprint(string value)
        {
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty)));
        }

        static Dictionary<string, string> SnapshotManifest()
        {
            var manifest = SafeReadManifest();
            if (manifest == null)
                return null;
            var fingerprints = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var sha = SHA256.Create())
                foreach (var dependency in manifest)
                    fingerprints[dependency.Key] = Convert.ToBase64String(
                        sha.ComputeHash(Encoding.UTF8.GetBytes(dependency.Value ?? "")));
            return fingerprints;
        }
        static PackageStatus CreateReceipt(string operation, string argument,
            EditorCommandOwnership.OperationContext ownershipContext) => new PackageStatus
        {
            Operation = operation,
            Argument = argument,
            OperationId = ownershipContext?.OperationId ?? Guid.NewGuid().ToString("N"),
            PreviousManifest = SnapshotManifest(),
            PreviousResolvedPackages = SnapshotResolvedPackages()
        };

        static Dictionary<string, string> SnapshotResolvedPackages()
        {
            var packages = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var package in PackageInfo.GetAllRegisteredPackages())
                if (!string.IsNullOrEmpty(package.name))
                    packages[package.name] = PackageState(package);
            return packages;
        }

        static string PackageState(PackageInfo package) =>
            $"{package.version}\u001f{package.source}\u001f{(package.isDirectDependency ? "1" : "0")}";

        static bool IsDirectPackageState(string state) =>
            !string.IsNullOrEmpty(state) && state[state.Length - 1] == '1';

        internal static bool TryProveResolvedPostcondition(PackageStatus status,
            Dictionary<string, string> currentManifest, Dictionary<string, string> currentPackages)
        {
            if (status == null || string.IsNullOrEmpty(status.OperationId)
                || status.PreviousManifest == null || status.PreviousResolvedPackages == null
                || currentManifest == null || currentPackages == null)
                return false;

            string packageName;
            if (status.Operation == "add")
            {
                if (!TryFindAddedPackage(status, currentManifest, out packageName)
                    || !currentManifest.TryGetValue(packageName, out var spec)
                    || !currentPackages.TryGetValue(packageName, out var currentState)
                    || !IsDirectPackageState(currentState))
                    return false;

                if (status.PreviousManifest.TryGetValue(packageName, out var oldSpec)
                    && string.Equals(oldSpec, ManifestFingerprint(spec), StringComparison.Ordinal))
                    return false;
                if (status.PreviousResolvedPackages.TryGetValue(packageName, out var oldState)
                    && string.Equals(oldState, currentState, StringComparison.Ordinal))
                    return false;

                if (!PackageIdentifier.TryParse(status.Argument, out var parsed, out _))
                    return false;
                var packageState = currentState.Split('\u001f');
                if (packageState.Length != 3)
                    return false;
                if (parsed.Kind == PackageSourceKind.Registry)
                {
                    if (!string.Equals(packageState[1], "Registry", StringComparison.Ordinal)
                        || !string.Equals(spec, packageState[0], StringComparison.Ordinal))
                        return false;
                    if (parsed.Version != null
                        && !string.Equals(spec, parsed.Version, StringComparison.Ordinal))
                        return false;
                }
                if ((parsed.Kind == PackageSourceKind.Git && packageState[1] != "Git")
                    || (parsed.Kind == PackageSourceKind.Local && packageState[1] != "Local"))
                    return false;
                return true;
            }

            if (status.Operation != "remove")
                return false;

            packageName = status.Argument;
            if (string.IsNullOrWhiteSpace(packageName)
                || !status.PreviousManifest.ContainsKey(packageName)
                || !status.PreviousResolvedPackages.TryGetValue(packageName, out var previousState)
                || !IsDirectPackageState(previousState)
                || currentManifest.ContainsKey(packageName))
                return false;

            if (currentPackages.TryGetValue(packageName, out var remainingState)
                && (IsDirectPackageState(remainingState)
                    || string.Equals(previousState, remainingState, StringComparison.Ordinal)))
                return false;
            return true;
        }

        static bool TryFindAddedPackage(PackageStatus status, Dictionary<string, string> currentManifest,
            out string packageName)
        {
            packageName = null;
            if (!PackageIdentifier.TryParse(status.Argument, out var parsed, out _))
                return false;
            if (parsed.Kind == PackageSourceKind.Registry)
            {
                packageName = parsed.Name;
                return currentManifest.ContainsKey(packageName);
            }

            foreach (var dependency in currentManifest)
            {
                if (!string.Equals(dependency.Value, status.Argument, StringComparison.Ordinal))
                    continue;
                if (status.PreviousManifest.TryGetValue(dependency.Key, out var previous)
                    && string.Equals(previous, ManifestFingerprint(dependency.Value), StringComparison.Ordinal))
                    continue;
                if (packageName != null)
                {
                    packageName = null;
                    return false;
                }
                packageName = dependency.Key;
            }
            return packageName != null;
        }


        static PackageStatus ReadStatus()
        {
            if (!File.Exists(StatusFile))
                return null;
            try
            {
                return JsonConvert.DeserializeObject<PackageStatus>(File.ReadAllText(StatusFile));
            }
            catch (Exception)
            {
                return null;
            }
        }

        static PackageSummary Map(PackageInfo p, bool isInstalled = false) => new PackageSummary
        {
            Name = p.name,
            Version = p.version,
            DisplayName = p.displayName,
            Source = p.source.ToString(),
            ResolvedPath = p.resolvedPath,
            IsDirectDependency = p.isDirectDependency,
            IsInstalled = isInstalled
        };

        /// <summary>Map registry results, flagging which are currently installed.</summary>
        static List<PackageSummary> MapMarkingInstalled(IEnumerable<PackageInfo> packages)
        {
            var installed = new HashSet<string>();
            foreach (var p in PackageInfo.GetAllRegisteredPackages())
                installed.Add(p.name);

            var list = new List<PackageSummary>();
            if (packages != null)
                foreach (var p in packages)
                    list.Add(Map(p, installed.Contains(p.name)));
            return list;
        }

        static string NowIso() => DateTime.UtcNow.ToString("o");

        static void WriteStatus(PackageStatus status)
        {
            try
            {
                File.WriteAllText(StatusFile, JsonConvert.SerializeObject(status));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Package] Failed to write status file: {ex.Message}");
            }
        }
    }
}
