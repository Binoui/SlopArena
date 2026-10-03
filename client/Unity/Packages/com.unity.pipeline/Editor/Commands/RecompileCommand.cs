using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands
{
    /// <summary>
    /// Forces a script recompile that works even when the editor is unfocused or minimized.
    ///
    /// Why this is non-trivial:
    ///  - Unity only runs script compilation while the editor is the active OS application. Pass
    ///    focus=true to bring it to the foreground (editor_focus) before AssetDatabase.Refresh();
    ///    it is off by default because the server keeps the editor ticking while unfocused, so
    ///    compilation still proceeds without stealing the user's foreground window.
    ///  - A successful compile triggers a domain reload, which destroys the managed AppDomain
    ///    (HTTP server, in-flight requests, statics). The triggering request therefore cannot stay
    ///    open and return when done.
    ///
    /// Pattern (mirrors the test runner): completion is reported via a status file that survives the
    /// domain reload. Call "recompile" to trigger, then poll "recompile_status" until status is
    /// "completed" or "up_to_date". The client must tolerate connection errors during the reload.
    /// </summary>
    [InitializeOnLoad]
    public static class RecompileCommand
    {
        const string StatusFile = "Temp/pipeline_recompile_status.json";

        static readonly List<string> s_Errors = new List<string>();
        private static string s_ActiveOperationId;
        private static EditorCommandOwnership s_ActiveOwnership;
        private static bool s_CompletionPending;

        // Focus action, indirected so tests can observe whether focus was performed without
        // actually stealing the OS foreground window.
        internal static Action s_FocusAction = () => FocusEditorCommand.FocusEditor();

        static RecompileCommand()
        {
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
        }

        [CliCommand("recompile", "Force a script recompile (works while unfocused/minimized). Poll recompile_status for completion.", MainThreadRequired = true, Tags = new[] { "scripts/compile" })]
        public static object Recompile(
            [CliArg("focus", "If true, bring the Editor to the foreground before compiling. Off by default.")] bool focus = false)
        {
            var context = EditorCommandOwnershipContext.Current;
            s_ActiveOperationId = context?.OperationId;
            s_ActiveOwnership = context?.Ownership;
            if (context != null && !context.Ownership.BeginHostActivity(context, "compile"))
            {
                s_ActiveOperationId = null;
                s_ActiveOwnership = null;
                return new { status = "ownership_rejected", message = "Editor ownership changed before compilation could start." };
            }

            if (focus)
                s_FocusAction();

            WriteStatus("triggered", false, null, s_ActiveOperationId);
            AssetDatabase.Refresh();

            if (EditorApplication.isCompiling)
                return new { status = "compiling", message = "Recompilation started. Poll recompile_status until completed." };

            WriteStatus("up_to_date", false, null, s_ActiveOperationId);
            context?.Ownership.CompleteHostActivity(context.OperationId);
            s_ActiveOperationId = null;
            s_ActiveOwnership = null;
            return new { status = "up_to_date", message = "No scripts needed recompilation." };
        }

        [CliCommand("recompile_status", "Get the status of the last recompile: idle | triggered | compiling | completed | up_to_date.", MainThreadRequired = false, Tags = new[] { "scripts/compile" })]
        public static string RecompileStatus()
        {
            if (File.Exists(StatusFile))
                return File.ReadAllText(StatusFile);
            return "{\"status\":\"idle\"}";
        }

        static void OnCompilationStarted(object _)
        {
            var context = EditorCommandOwnershipContext.Current;
            if (context != null)
            {
                s_ActiveOperationId = context.OperationId;
                s_ActiveOwnership = context.Ownership;
            }
            s_Errors.Clear();
            WriteStatus("compiling", false, null, s_ActiveOperationId);
        }

        static void OnAssemblyCompilationFinished(string assembly, CompilerMessage[] messages)
        {
            if (messages == null) return;
            foreach (var m in messages)
                if (m.type == CompilerMessageType.Error)
                    s_Errors.Add(m.message);
        }

        static void OnCompilationFinished(object _)
        {
            var failed = s_Errors.Count > 0;
            WriteStatus("completed", failed, s_Errors.ToArray(), s_ActiveOperationId);
            // Successful Editor script compilation remains owned until its new domain is ready.
            if (!failed)
                return;
            if (s_CompletionPending)
                return;

            s_CompletionPending = true;
            EditorApplication.update += CompleteCompilationWhenReady;
        }

        // Failed compilation does not produce a new domain. Keep its activity until an idle
        // Editor update; successful compilation is reconciled after the actual domain reload.
        static void CompleteCompilationWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            EditorApplication.update -= CompleteCompilationWhenReady;
            s_CompletionPending = false;
            if (s_ActiveOwnership != null && !string.IsNullOrEmpty(s_ActiveOperationId))
                s_ActiveOwnership.CompleteHostActivity(s_ActiveOperationId);
            s_ActiveOwnership = null;
            s_ActiveOperationId = null;
        }

        internal static bool IsOperationComplete(string operationId)
        {
            var status = ReadStatus();
            var state = status?["status"]?.Value<string>();
            return status?["operationId"]?.Value<string>() == operationId
                   && (state == "completed" || state == "up_to_date");
        }

        internal static bool ResumeOwnershipActivity(EditorCommandOwnership ownership, string operationId)
        {
            var status = ReadStatus();
            if (!EditorApplication.isCompiling
                || status?["operationId"]?.Value<string>() != operationId
                || status["status"]?.Value<string>() != "compiling")
                return false;
            s_ActiveOperationId = operationId;
            s_ActiveOwnership = ownership;
            return true;
        }

        private static JObject ReadStatus()
        {
            if (!File.Exists(StatusFile))
                return null;
            try
            {
                return JObject.Parse(File.ReadAllText(StatusFile));
            }
            catch (Exception)
            {
                return null;
            }
        }

        static void WriteStatus(string status, bool failed, string[] errors, string operationId = null)
        {
            try
            {
                var payload = new { status, failed, errors = errors ?? Array.Empty<string>(), operationId };
                File.WriteAllText(StatusFile, JsonConvert.SerializeObject(payload));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Recompile] Failed to write status file: {ex.Message}");
            }
        }
}
}
