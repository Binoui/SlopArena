using System;
using System.Collections.Generic;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using Unity.Pipeline.Editor.Testing;
using UnityEditor.TestTools.TestRunner.Api;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Regression coverage for UUM-149016 (InvalidOperationException from a stale collector
    /// re-delivered a later run's RunFinished).
    /// </summary>
    public class TestResultCollectorTests
    {
        [Test]
        public void RunFinished_CalledTwiceOnSameCollector_SecondCallDoesNotThrow()
        {
            var collector = new TestResultCollector();
            collector.Results.Add(new TestResult()); // skip the leaf-result rebuild path
            var result = new FakeTestResultAdaptor();

            Assert.DoesNotThrow(() => collector.RunFinished(result),
                "First RunFinished call should complete normally");
            Assert.DoesNotThrow(() => collector.RunFinished(result),
                "A second RunFinished delivered to an already-completed collector must not throw");
        }

        [Test]
        public void RunFinished_CalledAgainAfterInterveningRunStarted_SecondCallDoesNotThrow()
        {
            // RunStarted resets IsComplete on any non-cancelled collector, so the guard in
            // RunFinished must not rely on IsComplete.
            var collector = new TestResultCollector();
            collector.Results.Add(new TestResult());
            var result = new FakeTestResultAdaptor();

            collector.RunFinished(result);
            collector.RunStarted(new FakeTestAdaptor());

            Assert.DoesNotThrow(() => collector.RunFinished(result),
                "A stale collector redelivered RunFinished after an intervening RunStarted " +
                "broadcast must still not throw");
        }

        private sealed class FakeTestResultAdaptor : ITestResultAdaptor
        {
            public ITestAdaptor Test => throw new NotImplementedException();
            public string Name => "Fake";
            public string FullName => "Fake";
            public string ResultState => "Passed";
            public UnityEditor.TestTools.TestRunner.Api.TestStatus TestStatus => UnityEditor.TestTools.TestRunner.Api.TestStatus.Passed;
            public double Duration => 0;
            public DateTime StartTime => default;
            public DateTime EndTime => default;
            public string Message => null;
            public string StackTrace => null;
            public int AssertCount => 0;
            public int FailCount => 0;
            public int PassCount => 1;
            public int SkipCount => 0;
            public int InconclusiveCount => 0;
            public bool HasChildren => false;
            public IEnumerable<ITestResultAdaptor> Children => null;
            public string Output => null;
            public TNode ToXml() => throw new NotImplementedException();
        }

        private sealed class FakeTestAdaptor : ITestAdaptor
        {
            public string Id => "Fake";
            public string Name => "Fake";
            public string FullName => "Fake";
            public int TestCaseCount => 1;
            public bool HasChildren => false;
            public bool IsSuite => false;
            public IEnumerable<ITestAdaptor> Children => null;
            public ITestAdaptor Parent => null;
            public int TestCaseTimeout => 0;
            public ITypeInfo TypeInfo => null;
            public IMethodInfo Method => null;
            public object[] Arguments => Array.Empty<object>();
            public string[] Categories => Array.Empty<string>();
            public bool IsTestAssembly => false;
            public UnityEditor.TestTools.TestRunner.Api.RunState RunState => UnityEditor.TestTools.TestRunner.Api.RunState.Runnable;
            public string Description => null;
            public string SkipReason => null;
            public string ParentId => null;
            public string ParentFullName => null;
            public string UniqueName => "Fake";
            public string ParentUniqueName => null;
            public int ChildIndex => 0;
            public TestMode TestMode => TestMode.EditMode;
        }
    }
}
