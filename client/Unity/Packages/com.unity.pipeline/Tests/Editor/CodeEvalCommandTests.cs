using NUnit.Framework;
using System.Text.RegularExpressions;
using Unity.Pipeline.Models;
using Unity.Pipeline.Runtime.Commands;
using Unity.Pipeline.Tests;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Tests for the eval command (CodeEvalCommand), exercised directly and via PipelineClient.
    /// Compiler-level behavior (EvalCodeCompiler) is covered by EvalCodeCompilerTests.
    /// </summary>
    public class CodeEvalCommandTests
    {
        #region Direct

        [Test]
        public void EvaluateCode_SimpleArithmetic_ReturnsResult()
        {
            var r = CodeEvalCommand.EvaluateCode("return 2 + 2;");
            Assert.IsTrue(r.Success, r.Error);
            Assert.AreEqual(4, r.Result);
        }

        [Test]
        public void EvaluateCode_StringExpression_ReturnsString()
        {
            var r = CodeEvalCommand.EvaluateCode("return \"Hello World\";");
            Assert.IsTrue(r.Success, r.Error);
            Assert.AreEqual("Hello World", r.Result);
        }

        [Test]
        public void EvaluateCode_UnityApi_ReturnsVersion()
        {
            var r = CodeEvalCommand.EvaluateCode("return Application.unityVersion;");
            Assert.IsTrue(r.Success, r.Error);
            Assert.IsInstanceOf<string>(r.Result);
        }

        [Test]
        public void EvaluateCode_DebugLog_ReturnsExplicitValue()
        {
            var r = CodeEvalCommand.EvaluateCode("Debug.Log(\"x\"); return \"logged\";");
            Assert.IsTrue(r.Success, r.Error);
            Assert.AreEqual("logged", r.Result);
        }

        [Test]
        public void EvaluateCode_EmptyCode_BadRequest()
        {
            var r = CodeEvalCommand.EvaluateCode("");
            Assert.IsFalse(r.Success);
            Assert.AreEqual("Bad Request", r.Error);
        }

        [Test]
        public void EvaluateCode_NullCode_BadRequest()
        {
            var r = CodeEvalCommand.EvaluateCode(null);
            Assert.IsFalse(r.Success);
            Assert.AreEqual("Bad Request", r.Error);
        }

        [Test]
        public void EvaluateCode_SyntaxError_CompilationFailed()
        {
            var r = CodeEvalCommand.EvaluateCode("return 2 +;");
            Assert.IsFalse(r.Success);
            Assert.AreEqual("Compilation Failed", r.Error);
            Assert.Greater(r.Diagnostics.Count, 0);
        }

        [Test]
        public void EvaluateCode_RuntimeException_Fails()
        {
            var r = CodeEvalCommand.EvaluateCode("throw new System.Exception(\"boom\");");
            Assert.IsFalse(r.Success);
            Assert.IsNotNull(r.ErrorDetails);
        }

        [Test]
        public void EvaluateCode_ZeroTimeout_BadRequest()
        {
            var r = CodeEvalCommand.EvaluateCode("return 1;", timeout: 0);
            Assert.IsFalse(r.Success);
            Assert.AreEqual("Bad Request", r.Error);
        }

        [Test]
        public void EvaluateCode_ExcessiveTimeout_BadRequest()
        {
            // Cap raised to 24h (CLI-335): 40s is legal now; beyond the cap still rejects.
            var r = CodeEvalCommand.EvaluateCode("return 1;", timeout: 86_400_001);
            Assert.IsFalse(r.Success);
            Assert.AreEqual("Bad Request", r.Error);
        }

        [Test]
        public void EvaluateCode_LongRunningTimeoutRequest_ShouldNotBeRejectedOutright()
        {
            // Repro for the reported bug (a legitimately slow eval needed a timeout above 30s):
            // EvaluateSource's hardcoded ceiling rejects any `timeout` above 30000ms outright,
            // before even attempting compilation, so a caller can't even ask for more time
            // regardless of whether their code would actually need it.
            var r = CodeEvalCommand.EvaluateCode("return 1;", timeout: 35000);
            Assert.IsTrue(r.Success, $"Expected a timeout request above 30000ms to be accepted, got: {r.Error} / {r.ErrorDetails}");
        }

        [Test]
        public void EvaluateCode_RecordsExecutionTime()
        {
            var r = CodeEvalCommand.EvaluateCode("return 42;", timeout: 5000);
            Assert.IsTrue(r.Success, r.Error);
            Assert.Greater(r.ExecutionTimeMs, 0);
        }

        [Test]
        public void EvaluateCode_Success_PopulatesEnvelopeMetadata()
        {
            var before = System.DateTime.UtcNow;
            var r = CodeEvalCommand.EvaluateCode("return 42;");
            Assert.IsTrue(r.Success, r.Error);
            Assert.AreEqual("eval", r.Command);
            Assert.AreNotEqual(default(System.DateTime), r.ExecutedAt, "executedAt must be a real timestamp");
            Assert.GreaterOrEqual(r.ExecutedAt, before);
        }

        [Test]
        public void EvaluateCode_Failure_PopulatesEnvelopeMetadata()
        {
            var before = System.DateTime.UtcNow;
            var r = CodeEvalCommand.EvaluateCode("return 2 +;");
            Assert.IsFalse(r.Success);
            Assert.AreEqual("eval", r.Command);
            Assert.AreNotEqual(default(System.DateTime), r.ExecutedAt, "executedAt must be a real timestamp");
            Assert.GreaterOrEqual(r.ExecutedAt, before);
        }

        #endregion

        #region EvalFile

        [Test]
        public void EvaluateFile_FromFile_ReturnsResult()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "eval_test_" + System.Guid.NewGuid().ToString("N") + ".cs");
            System.IO.File.WriteAllText(path, "return 2 + 2;");
            try
            {
                var r = CodeEvalCommand.EvaluateFile(path);
                Assert.IsTrue(r.Success, r.Error);
                Assert.AreEqual(4, r.Result);
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Test]
        public void EvaluateFile_FileNotFound_BadRequest()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "eval_missing_" + System.Guid.NewGuid().ToString("N") + ".cs");
            var r = CodeEvalCommand.EvaluateFile(path);
            Assert.IsFalse(r.Success);
            Assert.AreEqual("Bad Request", r.Error);
        }

        [Test]
        public void EvaluateFile_NonCsFile_BadRequest()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "eval_test_" + System.Guid.NewGuid().ToString("N") + ".txt");
            System.IO.File.WriteAllText(path, "return 2 + 2;");
            try
            {
                var r = CodeEvalCommand.EvaluateFile(path);
                Assert.IsFalse(r.Success);
                Assert.AreEqual("Bad Request", r.Error);
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Test]
        public void EvaluateFile_EmptyFile_BadRequest()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "eval_test_" + System.Guid.NewGuid().ToString("N") + ".cs");
            System.IO.File.WriteAllText(path, "   ");
            try
            {
                var r = CodeEvalCommand.EvaluateFile(path);
                Assert.IsFalse(r.Success);
                Assert.AreEqual("Bad Request", r.Error);
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Test]
        public void EvaluateFile_NullFile_BadRequest()
        {
            var r = CodeEvalCommand.EvaluateFile(null);
            Assert.IsFalse(r.Success);
            Assert.AreEqual("Bad Request", r.Error);
        }

        #endregion

        #region ViaClient

        [Test]
        public void Eval_ViaClient_ReturnsResult()
        {
            using (var server = new PipelineTestServer())
            {
                // The test client authenticates with the server's bearer token.
                var response = server.Execute("eval", new { code = "return Application.platform.ToString();", timeout = 5000 });
                Assert.IsTrue(response.IsSuccess, response.Error);

                var r = response.GetTypedResponse<EvalResponse>();
                Assert.IsNotNull(r, "Should deserialize an EvalResponse");
                Assert.IsTrue(r.Success, r.Error);
                Assert.IsInstanceOf<string>(r.Result);
            }
        }

        [Test]
        public void Eval_ViaClient_SyntaxError_CompilationFailed()
        {
            using (var server = new PipelineTestServer())
            {
                var response = server.Execute("eval", new { code = "return 2 +;", timeout = 5000 });

                var r = response.GetTypedResponse<EvalResponse>();
                Assert.IsNotNull(r, "Should deserialize an EvalResponse");
                Assert.IsFalse(r.Success);
                Assert.AreEqual("Compilation Failed", r.Error);
            }
        }

        [Test]
        public void Eval_ViaClient_RequestedTimeoutBelowDispatcherDefault_TimesOutEarly()
        {
            // Repro for the dispatcher half of the reported bug: the Dispatcher.Invoke wrapping every
            // MainThreadRequired command (including eval) uses a hardcoded 60000ms wait, ignoring
            // whatever `timeout` the caller actually asked for. So a caller who asks for far less than
            // 60s currently just waits for the code to finish anyway; only the eval's own `timeout`
            // value, once threaded through, should be the deadline that governs the wait.
            using (var server = new PipelineTestServer())
            {
                LogAssert.Expect(LogType.Error, new Regex("Failed to handle /api/exec request.*timed out after 150ms"));

                // PipelineTestServer.Execute's own pump loop dequeues and runs the eval work item
                // synchronously once it starts, so it can't observe the request's completion until
                // that (compile + 600ms sleep) finishes — give it enough headroom for that, on top of
                // the compile itself, even though the actual dispatcher timeout below fires in ~150ms.
                var response = server.Execute("eval",
                    new { code = "System.Threading.Thread.Sleep(600); return 1;", timeout = 150 },
                    timeoutMs: 20000);

                Assert.IsFalse(response.IsSuccess,
                    $"Expected the 150ms request to time out instead of waiting for the 600ms sleep to finish: {response.RawResponse}");
            }
        }

        [Test]
        public void EvalFile_ViaClient_ReturnsResult()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "eval_test_" + System.Guid.NewGuid().ToString("N") + ".cs");
            System.IO.File.WriteAllText(path, "return 6 * 7;");
            try
            {
                using (var server = new PipelineTestServer())
                {
                    var response = server.Execute("eval_file", new { file = path, timeout = 5000 });
                    Assert.IsTrue(response.IsSuccess, response.Error);

                    var r = response.GetTypedResponse<EvalResponse>();
                    Assert.IsNotNull(r, "Should deserialize an EvalResponse");
                    Assert.IsTrue(r.Success, r.Error);
                    Assert.AreEqual(42, r.Result);
                }
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        #endregion
    }
}
