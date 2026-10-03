using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Pipeline.Console;
using Unity.Pipeline.Runtime.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using ObservabilityConsoleLogBuffer = Unity.Pipeline.Editor.Commands.Observability.ConsoleLogBuffer;
using ObservabilityConsoleCommands = Unity.Pipeline.Editor.Commands.Observability.ConsoleCommands;
using ObservabilityConsoleLogEntryDto = Unity.Pipeline.Editor.Commands.Observability.ConsoleLogEntryDto;

namespace Unity.Pipeline.Tests.Editor.Console
{
    public class ConsoleCaptureResubscribeTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (EditorApplication.isPlaying)
                yield return new ExitPlayMode();

            RearmObservability();
            RearmConsoleLogCapture();
        }

        [UnityTest]
        public IEnumerator ConsoleCapture_SurvivesPlayModeExit()
        {
            yield return new EnterPlayMode();
            yield return new ExitPlayMode();

            var marker = NewMarker("survives_exit");
            EmitError(marker);

            var observabilityResult = ObservabilityConsoleCommands.GetConsoleLogs("error", 50);
            var observabilityLogs = ReadObservabilityLogs(observabilityResult);
            Assert.IsTrue(observabilityLogs.Any(e => e.Message != null && e.Message.Contains(marker)),
                "get_console_logs must still capture entries logged after exiting play mode");

            var consoleResponse = ConsoleCommand.GetConsole(level: "error");
            Assert.IsTrue(consoleResponse.Entries.Any(e => e.Message != null && e.Message.Contains(marker)),
                "console must still capture entries logged after exiting play mode");
        }

        private static IEnumerable TestCases()
        {
            yield return new TestCaseData(
                    (Action)StripObservabilitySubscription,
                    (Action)RearmObservability,
                    (Func<string, int>)CountObservabilityCaptures)
                .SetName("{m}(Observability_get_console_logs)");
            yield return new TestCaseData(
                    (Action)StripConsoleLogCaptureSubscription,
                    (Action)RearmConsoleLogCapture,
                    (Func<string, int>)CountConsoleLogCaptureCaptures)
                .SetName("{m}(ConsoleLogCapture_console)");
        }

        [TestCaseSource(nameof(TestCases))]
        public void CaptureSurvivesLostSubscription_WhenRearmed(Action strip, Action rearm, Func<string, int> countCaptures)
        {
            strip();

            var deadMarker = NewMarker("dead");
            EmitError(deadMarker);
            Assert.AreEqual(0, countCaptures(deadMarker),
                "Stripping the subscription should stop capture (sanity check on the strip step)");

            rearm();

            var liveMarker = NewMarker("live");
            EmitError(liveMarker);
            Assert.AreEqual(1, countCaptures(liveMarker),
                "Re-arming the subscription must restore capture");

            rearm();

            var idempotentMarker = NewMarker("idempotent");
            EmitError(idempotentMarker);
            Assert.AreEqual(1, countCaptures(idempotentMarker),
                "Re-arming an already-live subscription must not double-record a single log");
        }

        private static string NewMarker(string tag) => $"consolecapture_{tag}_{Guid.NewGuid():N}";

        private static void EmitError(string marker)
        {
            LogAssert.Expect(LogType.Error, new Regex(".*" + Regex.Escape(marker) + ".*"));
            Debug.LogError(marker);
        }

        private static List<ObservabilityConsoleLogEntryDto> ReadObservabilityLogs(object result)
        {
            var logsProp = result.GetType().GetProperty("logs");
            Assert.IsNotNull(logsProp, "Result should expose a 'logs' property");
            return (List<ObservabilityConsoleLogEntryDto>)logsProp.GetValue(result);
        }

        private static void StripObservabilitySubscription()
        {
            var method = typeof(ObservabilityConsoleLogBuffer).GetMethod("OnLogMessageThreaded", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "ConsoleLogBuffer.OnLogMessageThreaded must exist for this test to strip its subscription");
            var handler = (Application.LogCallback)Delegate.CreateDelegate(typeof(Application.LogCallback), method);
            Application.logMessageReceivedThreaded -= handler;
        }

        private static void RearmObservability() => ObservabilityConsoleLogBuffer.EnsureCapturing();

        private static int CountObservabilityCaptures(string marker) =>
            ObservabilityConsoleLogBuffer.Snapshot().Count(e => e.Message != null && e.Message.Contains(marker));

        private static void StripConsoleLogCaptureSubscription()
        {
            var method = typeof(ConsoleLogCapture).GetMethod("OnLogMessage", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "ConsoleLogCapture.OnLogMessage must exist for this test to strip its subscription");
            var handler = (Application.LogCallback)Delegate.CreateDelegate(typeof(Application.LogCallback), method);
            Application.logMessageReceivedThreaded -= handler;
        }

        private static void RearmConsoleLogCapture() => ConsoleLogCapture.EnsureCapturing();

        private static int CountConsoleLogCaptureCaptures(string marker) =>
            ConsoleLogCapture.Buffer.Query(-1, 0, ConsoleLogBuffer.SeverityLog)
                .Entries.Count(e => e.Message != null && e.Message.Contains(marker));
    }
}
