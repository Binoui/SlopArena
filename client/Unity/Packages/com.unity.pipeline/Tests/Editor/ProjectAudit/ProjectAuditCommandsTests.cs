using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor;
using Unity.Pipeline.Editor.Commands.ProjectAudit;

namespace Unity.Pipeline.Tests.Editor.ProjectAudit
{
    /// <summary>
    /// Unit tests for the <c>audit</c> / <c>audit_status</c> commands. These cover discovery/schema
    /// and the two pieces of pure logic — CSV escaping and category-name validation — without running
    /// a real Project Auditor scan (slow, and Project Auditor may be absent from the test project;
    /// the reflection-driven scan is exercised live against an Editor that has it installed).
    /// </summary>
    public class ProjectAuditCommandsTests
    {
        [SetUp]
        public void SetUp() => CommandRegistry.SetDiscovery(new TypeCacheCommandDiscovery());

        [Test]
        public void Audit_And_AuditStatus_AreDiscovered_WithExpectedSchema()
        {
            var commands = CommandRegistry.DiscoverCommands().ToList();

            var audit = commands.FirstOrDefault(c => c.Name == "audit");
            Assert.IsNotNull(audit, "Should discover the audit command");
            Assert.IsFalse(audit.MainThreadRequired,
                "audit must not require the main thread (it only enqueues; the scan runs on a later tick)");
            CollectionAssert.AreEquivalent(new[] { "categories", "output" },
                audit.Parameters.Select(p => p.Name).ToList());
            Assert.IsTrue(audit.Parameters.All(p => !p.Required), "audit parameters should be optional");

            var status = commands.FirstOrDefault(c => c.Name == "audit_status");
            Assert.IsNotNull(status, "Should discover the audit_status command");
            Assert.IsFalse(status.MainThreadRequired,
                "audit_status must be off-main-thread so it answers while a scan holds the main thread");
            Assert.AreEqual(0, status.Parameters.Count, "audit_status takes no parameters");
        }

        [Test]
        public void AuditStatus_ReturnsValidJsonWithStatusField()
        {
            // Returns idle/scanning/completed when Project Auditor is present, or unavailable when not;
            // in every case a 'status' field must be present.
            var json = JObject.Parse(ProjectAuditCommands.AuditStatus());
            Assert.IsNotNull(json["status"], "audit_status should always include a 'status' field");
        }

        [Test]
        public void EscapeCsv_LeavesPlainFieldsUnquoted()
        {
            Assert.AreEqual("Code", ProjectAuditCommands.EscapeCsv("Code"));
            Assert.AreEqual("", ProjectAuditCommands.EscapeCsv(null));
        }

        [Test]
        public void EscapeCsv_QuotesFieldsWithCommasQuotesOrNewlines()
        {
            Assert.AreEqual("\"a,b\"", ProjectAuditCommands.EscapeCsv("a,b"));
            Assert.AreEqual("\"line1\nline2\"", ProjectAuditCommands.EscapeCsv("line1\nline2"));
            Assert.AreEqual("\"has\r\ncrlf\"", ProjectAuditCommands.EscapeCsv("has\r\ncrlf"));
        }

        [Test]
        public void EscapeCsv_DoublesEmbeddedQuotes()
        {
            // Cache the result instead of calling "every frame".  ->  quoted, with the inner quotes doubled.
            Assert.AreEqual("\"say \"\"hi\"\"\"", ProjectAuditCommands.EscapeCsv("say \"hi\""));
        }

        [Test]
        public void ValidateCategories_ReturnsNull_WhenNoneRequested()
        {
            var valid = new[] { "Code", "ProjectSetting", "Texture" };
            Assert.IsNull(ProjectAuditCommands.ValidateCategories(new string[0], valid));
            Assert.IsNull(ProjectAuditCommands.ValidateCategories(null, valid));
        }

        [Test]
        public void ValidateCategories_ReturnsNull_WhenAllValid_CaseInsensitive()
        {
            var valid = new[] { "Code", "ProjectSetting", "Texture" };
            Assert.IsNull(ProjectAuditCommands.ValidateCategories(new[] { "Code", "texture" }, valid));
        }

        [Test]
        public void ValidateCategories_ReportsUnknown_AndListsValid()
        {
            var valid = new[] { "Code", "ProjectSetting", "Texture" };
            var error = ProjectAuditCommands.ValidateCategories(new[] { "Code", "Bogus" }, valid);

            Assert.IsNotNull(error);
            // The unknown name is named before the "Valid categories:" list; the valid one is not.
            var unknownPart = error.Substring(0, error.IndexOf("Valid categories:", System.StringComparison.Ordinal));
            StringAssert.Contains("Bogus", unknownPart);
            StringAssert.DoesNotContain("Code", unknownPart, "the valid category 'Code' should not be reported as unknown");
            StringAssert.Contains("ProjectSetting", error, "the error should list the valid categories");
        }

        /// <summary>Stand-in for Project Auditor's IssueCategory.</summary>
        enum FakeCategory { Code, Texture }

        /// <summary>
        /// Stand-in for the serialization wrapper the built-in editor module stores categories in
        /// (SerializableEnum&lt;IssueCategory&gt;): a struct constructed from the enum value.
        /// </summary>
        struct FakeWrappedCategory
        {
            public readonly FakeCategory Value;
            public FakeWrappedCategory(FakeCategory value) => Value = value;
        }

        [Test]
        public void BuildCategoryArray_BuildsBareEnumArray_CaseInsensitively()
        {
            var array = ProjectAuditCommands.BuildCategoryArray(
                typeof(FakeCategory), null, typeof(FakeCategory), new[] { "texture", "Code" });

            Assert.AreEqual(typeof(FakeCategory[]), array.GetType());
            CollectionAssert.AreEqual(new[] { FakeCategory.Texture, FakeCategory.Code }, array);
        }

        [Test]
        public void BuildCategoryArray_WrapsElements_WhenFieldHoldsAWrapperType()
        {
            var wrapperCtor = typeof(FakeWrappedCategory).GetConstructor(new[] { typeof(FakeCategory) });

            var array = ProjectAuditCommands.BuildCategoryArray(
                typeof(FakeWrappedCategory), wrapperCtor, typeof(FakeCategory), new[] { "Texture" });

            // Assigning a bare-enum array into a wrapper-typed field throws at runtime, so the element
            // type must be the wrapper itself.
            Assert.AreEqual(typeof(FakeWrappedCategory[]), array.GetType());
            Assert.AreEqual(FakeCategory.Texture, ((FakeWrappedCategory[])array)[0].Value);
        }
    }
}
