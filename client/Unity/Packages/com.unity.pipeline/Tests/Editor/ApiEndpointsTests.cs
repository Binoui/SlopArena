using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor;
using Unity.Pipeline.Models;
using Newtonsoft.Json.Linq;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Tests for HTTP API endpoints that CLI tools will consume.
    /// These test the complete server API surface for remote command execution.
    /// </summary>
    public class ApiEndpointsTests
    {
        private EditorPipelineServer m_Server;
        private Unity.Pipeline.Tests.Runtime.PipelineClient m_PipelineClient;

        [SetUp]
        public void SetUp()
        {
            // Setup command discovery for tests
            CommandRegistry.SetDiscovery(new TypeCacheCommandDiscovery());

            // Start an ISOLATED test server (ports 7850-7899, writes no descriptor) for endpoint
            // testing, so we never bind the live server's port (7800) or clobber its descriptor.
            m_Server = new TestEditorPipelineServer();
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
        public async Task ApiCommands_GetEndpoint_ReturnsCommandList()
        {
            // Act - Call /api/commands endpoint using unified Pipeline client
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert - Response structure
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"Commands endpoint should return success, got: {httpResponse.StatusCode}");
            Assert.AreEqual("application/json", httpResponse.Content.Headers.ContentType.MediaType,
                "Commands endpoint should return JSON content type");

            // Assert - JSON parsing
            var responseJson = JObject.Parse(jsonContent);
            Assert.IsNotNull(responseJson, "Should be able to parse commands JSON");

            // Verify response structure
            Assert.IsNotNull(responseJson["commands"], "Response should have commands array");
            Assert.IsNotNull(responseJson["count"], "Response should have count field");
            Assert.IsNotNull(responseJson["server"], "Response should have server info");

            // Verify commands array contains discovered commands
            var commands = responseJson["commands"] as JArray;
            Assert.Greater(commands.Count, 0, "Should have at least one discovered command");

            // Verify a specific test command is included
            var testCommand = commands.Cast<JObject>()
                .FirstOrDefault(cmd => cmd["name"]?.ToString() == "log_editor");
            Assert.IsNotNull(testCommand, "Should include log_editor test command");
            Assert.AreEqual("Log a message to Unity Editor console", testCommand["description"]?.ToString());
        }

        [Test]
        public async Task ApiCommands_OnEditorServer_ExcludesRuntimeOnlyCommands()
        {
            // Act - List commands from the Editor server
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();
            var responseJson = JObject.Parse(jsonContent);
            var commandNames = (responseJson["commands"] as JArray)
                .Cast<JObject>()
                .Select(cmd => cmd["name"]?.ToString())
                .ToList();

            // Assert - editor commands are listed, runtime-only commands are hidden
            Assert.Contains("editor_status", commandNames, "Editor command should be listed");
            CollectionAssert.DoesNotContain(commandNames, "runtime_status", "Runtime-only eval should be hidden on the Editor server");
            CollectionAssert.DoesNotContain(commandNames, "set_target_framerate", "Runtime-only reload_file_override should be hidden on the Editor server");
        }

        [Test]
        public async Task ApiCommands_DefaultDetail_ReturnsFullMetadata()
        {
            // Act - no detail parameter: full is the default (back-compat with pre-detail clients)
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"Commands endpoint should return success, got: {httpResponse.StatusCode}");
            var responseJson = JObject.Parse(jsonContent);
            var commands = responseJson["commands"] as JArray;
            Assert.Greater(commands.Count, 0, "Should have at least one discovered command");

            var logEditor = commands.Cast<JObject>()
                .First(cmd => cmd["name"]?.ToString() == "log_editor");
            Assert.IsNotNull(logEditor["parameters"], "default detail should include parameters");
            Assert.IsNotNull(logEditor["schema"], "default detail should include schema");
            Assert.IsNotNull(logEditor["tags"], "default detail should include tags");
            Assert.IsNotNull(logEditor["package"], "default detail should include package");
        }

        [Test]
        public async Task ApiCommands_DetailCompact_ReturnsLightweightIndex()
        {
            // Act - detail=compact opts into the lightweight browse/discovery projection
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?detail=compact");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"detail=compact should return success, got: {httpResponse.StatusCode}");
            var commands = JObject.Parse(jsonContent)["commands"] as JArray;
            Assert.Greater(commands.Count, 0, "Should have at least one discovered command");

            foreach (var cmd in commands.Cast<JObject>())
            {
                Assert.IsNotNull(cmd["name"], "compact entry should have name");
                Assert.IsNotNull(cmd["description"], "compact entry should have description");
                Assert.IsNotNull(cmd["tags"], "compact entry should have tags");
                Assert.IsNotNull(cmd["package"], "compact entry should have package");
                Assert.IsNull(cmd["parameters"], $"compact entry '{cmd["name"]}' should omit parameters");
                Assert.IsNull(cmd["schema"], $"compact entry '{cmd["name"]}' should omit schema");
            }
        }

        [Test]
        public async Task ApiCommands_QueryFilter_MatchesTagCaseInsensitively()
        {
            // Act - 'REGISTRATION' only appears in test_tagged's 'test/registration' tag
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?detail=compact&query=REGISTRATION");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"query filter should return success, got: {httpResponse.StatusCode}");
            var responseJson = JObject.Parse(jsonContent);
            var names = (responseJson["commands"] as JArray)
                .Cast<JObject>()
                .Select(cmd => cmd["name"]?.ToString())
                .ToList();
            Assert.Contains("test_tagged", names, "query should match a command via its tag, case-insensitively");
            CollectionAssert.DoesNotContain(names, "log_editor", "commands matching neither name, description nor tag should be filtered out");
        }

        [Test]
        public async Task ApiCommands_QueryFilter_MatchesNameByPrefix()
        {
            // Act - 'log_edi' is a strict prefix of the 'log_editor' command name
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?detail=compact&query=log_edi");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"query filter should return success, got: {httpResponse.StatusCode}");
            var names = (JObject.Parse(jsonContent)["commands"] as JArray)
                .Cast<JObject>()
                .Select(cmd => cmd["name"]?.ToString())
                .ToList();
            Assert.Contains("log_editor", names, "a query that is a prefix of a command name should match that command");
        }

        [Test]
        public async Task ApiCommands_QueryFilter_NoMatch_ReturnsEmptyNotError()
        {
            // Act
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?query=zzz_definitely_no_match_zzz");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert - empty result, not an error
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"a no-match query should still return success, got: {httpResponse.StatusCode}");
            var responseJson = JObject.Parse(jsonContent);
            Assert.AreEqual(0, (responseJson["commands"] as JArray).Count, "no-match query should return an empty commands array");
            Assert.AreEqual(0, responseJson["total"]?.ToObject<int>(), "no-match query should report total 0");
            Assert.AreEqual(0, responseJson["count"]?.ToObject<int>(), "no-match query should report count 0");
        }

        [Test]
        public async Task ApiCommands_TagFilter_MatchesSubtreeBySegmentPrefix()
        {
            // Act / Assert - 'test' matches the exact 'test' tag and the 'test/registration' subtree
            var jsonContent = await (await m_PipelineClient.GetHttpAsync("/api/commands?detail=compact&tag=test"))
                .Content.ReadAsStringAsync();
            var names = (JObject.Parse(jsonContent)["commands"] as JArray)
                .Cast<JObject>().Select(cmd => cmd["name"]?.ToString()).ToList();
            Assert.Contains("test_tagged", names, "tag=test should match the tagged test command");
            CollectionAssert.DoesNotContain(names, "log_editor", "untagged commands should be filtered out");

            // Drilling into a subtag still matches
            jsonContent = await (await m_PipelineClient.GetHttpAsync("/api/commands?detail=compact&tag=test/registration"))
                .Content.ReadAsStringAsync();
            names = (JObject.Parse(jsonContent)["commands"] as JArray)
                .Cast<JObject>().Select(cmd => cmd["name"]?.ToString()).ToList();
            Assert.Contains("test_tagged", names, "tag=test/registration should match the subtag directly");

            // Segment-aware: 'tes' is not a whole-segment prefix of 'test'
            jsonContent = await (await m_PipelineClient.GetHttpAsync("/api/commands?tag=tes"))
                .Content.ReadAsStringAsync();
            Assert.AreEqual(0, JObject.Parse(jsonContent)["total"]?.ToObject<int>(),
                "tag matching should respect '/' segment boundaries, not raw string prefixes");
        }

        [Test]
        public async Task ApiCommands_CombinedFilters_AreAnded()
        {
            // Act - log_editor matches the query but carries no tags, so the tag filter excludes it
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?query=log_editor&tag=test");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"combined filters should return success, got: {httpResponse.StatusCode}");
            Assert.AreEqual(0, JObject.Parse(jsonContent)["total"]?.ToObject<int>(),
                "filters should combine with AND");
        }

        [Test]
        public async Task ApiCommands_GroupByPackage_GroupsCommandsByOriginatingAssembly()
        {
            // Act
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?detail=compact&group_by=package&query=log_editor");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert - grouped responses carry 'groups' instead of 'commands'
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"group_by=package should return success, got: {httpResponse.StatusCode}");
            var responseJson = JObject.Parse(jsonContent);
            Assert.IsNull(responseJson["commands"], "grouped response should not have a flat commands array");
            var groups = responseJson["groups"] as JArray;
            Assert.IsNotNull(groups, "grouped response should have a groups array");

            var testGroup = groups.Cast<JObject>()
                .FirstOrDefault(g => g["package"]?.ToString() == "Unity.Pipeline.Tests.Editor");
            Assert.IsNotNull(testGroup, "log_editor's assembly should appear as a package group");
            var groupNames = (testGroup["commands"] as JArray)
                .Cast<JObject>().Select(cmd => cmd["name"]?.ToString()).ToList();
            Assert.Contains("log_editor", groupNames, "the group should contain the matching command");
        }

        [Test]
        public async Task ApiCommands_GroupByTag_ReturnsNestedTagTree()
        {
            // Act
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?detail=compact&group_by=tag&tag=test");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert - top-level 'test' node with a nested 'test/registration' child
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"group_by=tag should return success, got: {httpResponse.StatusCode}");
            var groups = JObject.Parse(jsonContent)["groups"] as JArray;
            Assert.IsNotNull(groups, "grouped response should have a groups array");

            var testNode = groups.Cast<JObject>().FirstOrDefault(g => g["tag"]?.ToString() == "test");
            Assert.IsNotNull(testNode, "should have a top-level 'test' tag node");
            var directNames = (testNode["commands"] as JArray)
                .Cast<JObject>().Select(cmd => cmd["name"]?.ToString()).ToList();
            Assert.Contains("test_tagged", directNames, "test_tagged carries the exact 'test' tag");

            var childNode = (testNode["children"] as JArray)?.Cast<JObject>()
                .FirstOrDefault(g => g["tag"]?.ToString() == "test/registration");
            Assert.IsNotNull(childNode, "'test/registration' should nest under 'test'");
            var childNames = (childNode["commands"] as JArray)
                .Cast<JObject>().Select(cmd => cmd["name"]?.ToString()).ToList();
            Assert.Contains("test_tagged", childNames, "test_tagged also carries the 'test/registration' tag");
        }

        [Test]
        public async Task ApiCommands_GroupByInvalid_ReturnsBadRequestListingAcceptedValues()
        {
            // Act
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?group_by=namespace");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert
            Assert.AreEqual(400, (int)httpResponse.StatusCode,
                $"Invalid group_by value should be rejected with 400. Response: {jsonContent}");
            StringAssert.Contains("flat", jsonContent, "error should list accepted value 'flat'");
            StringAssert.Contains("package", jsonContent, "error should list accepted value 'package'");
            StringAssert.Contains("tag", jsonContent, "error should list accepted value 'tag'");
        }

        [Test]
        public async Task ApiCommands_Pagination_SlicesNameSortedListDeterministically()
        {
            // Arrange - the unpaginated listing (name-sorted for deterministic pages)
            var allJson = await (await m_PipelineClient.GetHttpAsync("/api/commands?detail=compact"))
                .Content.ReadAsStringAsync();
            var allNames = (JObject.Parse(allJson)["commands"] as JArray)
                .Cast<JObject>().Select(cmd => cmd["name"]?.ToString()).ToList();
            CollectionAssert.AreEqual(allNames.OrderBy(n => n, System.StringComparer.Ordinal).ToList(), allNames,
                "commands should be name-sorted so pagination windows are deterministic");

            // Act
            var pageJson = await (await m_PipelineClient.GetHttpAsync("/api/commands?detail=compact&offset=1&limit=2"))
                .Content.ReadAsStringAsync();
            var pageResponse = JObject.Parse(pageJson);
            var pageNames = (pageResponse["commands"] as JArray)
                .Cast<JObject>().Select(cmd => cmd["name"]?.ToString()).ToList();

            // Assert
            CollectionAssert.AreEqual(allNames.Skip(1).Take(2).ToList(), pageNames,
                "offset/limit should slice the same ordering the unpaginated listing uses");
            Assert.AreEqual(2, pageResponse["count"]?.ToObject<int>(), "count should be the returned page size");
            Assert.AreEqual(allNames.Count, pageResponse["total"]?.ToObject<int>(), "total should be the match count before pagination");
            Assert.AreEqual(1, pageResponse["offset"]?.ToObject<int>(), "offset should be echoed");
            Assert.AreEqual(2, pageResponse["limit"]?.ToObject<int>(), "limit should be echoed");
        }

        [Test]
        public async Task ApiCommands_PaginationInvalid_ReturnsBadRequest()
        {
            // Act / Assert - negative offset
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?offset=-1");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();
            Assert.AreEqual(400, (int)httpResponse.StatusCode,
                $"negative offset should be rejected with 400. Response: {jsonContent}");
            StringAssert.Contains("offset", jsonContent, "error should name the offending parameter");

            // Non-numeric limit
            httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?limit=abc");
            jsonContent = await httpResponse.Content.ReadAsStringAsync();
            Assert.AreEqual(400, (int)httpResponse.StatusCode,
                $"non-numeric limit should be rejected with 400. Response: {jsonContent}");
            StringAssert.Contains("limit", jsonContent, "error should name the offending parameter");
        }

        [Test]
        public async Task ApiCommands_SortByPackage_OrdersByPackageThenName()
        {
            // Act
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?detail=compact&sort=package");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert - ordered by originating package, ties broken by name
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"sort=package should return success, got: {httpResponse.StatusCode}");
            var pairs = (JObject.Parse(jsonContent)["commands"] as JArray)
                .Cast<JObject>()
                .Select(cmd => (Package: cmd["package"]?.ToString(), Name: cmd["name"]?.ToString()))
                .ToList();
            var expected = pairs
                .OrderBy(p => p.Package, System.StringComparer.Ordinal)
                .ThenBy(p => p.Name, System.StringComparer.Ordinal)
                .ToList();
            CollectionAssert.AreEqual(expected, pairs,
                "sort=package should order by package with name as tiebreak");
        }

        [Test]
        public async Task ApiCommands_OrderDesc_ReversesNameSort()
        {
            // Act
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?detail=compact&order=desc");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"order=desc should return success, got: {httpResponse.StatusCode}");
            var names = (JObject.Parse(jsonContent)["commands"] as JArray)
                .Cast<JObject>().Select(cmd => cmd["name"]?.ToString()).ToList();
            CollectionAssert.AreEqual(names.OrderByDescending(n => n, System.StringComparer.Ordinal).ToList(), names,
                "order=desc should reverse the default name sort");
        }

        [Test]
        public async Task ApiCommands_SortInvalid_ReturnsBadRequestListingAcceptedValues()
        {
            // Act / Assert - unknown sort key
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?sort=alphabetical");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();
            Assert.AreEqual(400, (int)httpResponse.StatusCode,
                $"Invalid sort value should be rejected with 400. Response: {jsonContent}");
            StringAssert.Contains("name", jsonContent, "error should list accepted value 'name'");
            StringAssert.Contains("package", jsonContent, "error should list accepted value 'package'");

            // Unknown order direction
            httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?order=upside_down");
            jsonContent = await httpResponse.Content.ReadAsStringAsync();
            Assert.AreEqual(400, (int)httpResponse.StatusCode,
                $"Invalid order value should be rejected with 400. Response: {jsonContent}");
            StringAssert.Contains("asc", jsonContent, "error should list accepted value 'asc'");
            StringAssert.Contains("desc", jsonContent, "error should list accepted value 'desc'");
        }

        [Test]
        public async Task ApiCommands_DetailFull_ReturnsFullMetadataWithTagsAndPackage()
        {
            // Act - detail=full opts into the complete per-command metadata
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?detail=full");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"detail=full should return success, got: {httpResponse.StatusCode}");
            var commands = JObject.Parse(jsonContent)["commands"] as JArray;
            var logEditor = commands.Cast<JObject>()
                .First(cmd => cmd["name"]?.ToString() == "log_editor");
            Assert.IsNotNull(logEditor["parameters"], "full detail should include parameters");
            Assert.IsNotNull(logEditor["schema"], "full detail should include schema");
            Assert.IsNotNull(logEditor["tags"], "full detail should include tags");
            Assert.AreEqual("Unity.Pipeline.Tests.Editor", logEditor["package"]?.ToString(),
                "full detail should include the originating package");
        }

        [Test]
        public async Task ApiCommands_InvalidDetail_ReturnsBadRequestListingAcceptedValues()
        {
            // Act - an unsupported detail value
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands?detail=verbose");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert - rejected with a clear error naming the accepted values
            Assert.AreEqual(400, (int)httpResponse.StatusCode,
                $"Invalid detail value should be rejected with 400. Response: {jsonContent}");
            var responseJson = JObject.Parse(jsonContent);
            Assert.IsNotNull(responseJson["error"], "400 response should have error field");
            StringAssert.Contains("compact", jsonContent, "error should list accepted value 'compact'");
            StringAssert.Contains("full", jsonContent, "error should list accepted value 'full'");
        }

        [Test]
        public async Task ApiCommands_CommandStructure_ContainsRequiredFields()
        {
            // Act - Call /api/commands endpoint using unified Pipeline client
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/commands");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();
            var responseJson = JObject.Parse(jsonContent);

            // Assert - Command structure validation
            var commands = responseJson["commands"] as JArray;
            var firstCommand = commands[0] as JObject;

            // Required command fields
            Assert.IsNotNull(firstCommand["name"], "Command should have name field");
            Assert.IsNotNull(firstCommand["description"], "Command should have description field");
            Assert.IsNotNull(firstCommand["parameters"], "Command should have parameters array");
            Assert.IsNotNull(firstCommand["schema"], "Command should have JSON schema");
            Assert.IsNotNull(firstCommand["mainThreadRequired"], "Command should have mainThreadRequired field");

            // Verify schema is valid JSON
            var schema = firstCommand["schema"]?.ToString();
            var schemaJson = JObject.Parse(schema);
            Assert.AreEqual(firstCommand["name"]?.ToString(), schemaJson["title"]?.ToString(),
                "Schema title should match command name");
        }

        [Test]
        public async Task ApiStatus_GetBasicStatus_ReturnsServerInfo()
        {
            // Act - Call basic /api/status endpoint using unified Pipeline client
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/status");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert - Response structure
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"Basic status endpoint should return success, got: {httpResponse.StatusCode}");
            Assert.AreEqual("application/json", httpResponse.Content.Headers.ContentType.MediaType,
                "Basic status endpoint should return JSON content type");

            // Assert - JSON structure (basic server info only, no Editor APIs)
            var statusJson = JObject.Parse(jsonContent);
            Assert.IsNotNull(statusJson["status"], "Should have status field");

            // Verify basic values
            Assert.AreEqual("ready", statusJson["status"]?.ToString());
        }

        [Test]
        public async Task ApiEditorStatus_GetDetailedStatus_ReturnsEditorInfo()
        {
            // Act - Call rich /api/editor_status endpoint using unified Pipeline client
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/editor_status");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();

            // Assert - Response structure
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"Editor status endpoint should return success, got: {httpResponse.StatusCode}. Response: {jsonContent}");
            Assert.AreEqual("application/json", httpResponse.Content.Headers.ContentType.MediaType,
                "Editor status endpoint should return JSON content type");

            // Assert - Rich Editor status structure
            var statusJson = JObject.Parse(jsonContent);
            Assert.IsNotNull(statusJson["status"], "Should have overall status");
            Assert.IsNotNull(statusJson["compiling"], "Should have compiling state");
            Assert.IsNotNull(statusJson["domainReloadInProgress"], "Should have domain reload state");
            Assert.IsNotNull(statusJson["playMode"], "Should have play mode state");
            Assert.IsNotNull(statusJson["unityVersion"], "Should have Unity version");

            // Verify Editor-specific data is present
            Assert.Contains(statusJson["status"]?.ToString(), new[] { "ready", "compiling", "playing", "reloading" });
            Assert.Contains(statusJson["playMode"]?.ToString(), new[] { "stopped", "playing", "paused" });
            Assert.IsInstanceOf<bool>(statusJson["compiling"]?.ToObject<bool>());
        }

        [Test]
        public async Task ApiExec_PostCommand_ExecutesSuccessfully()
        {
            // Arrange
            var commandRequest = new CommandExecutionRequest("log_editor");
            commandRequest.Parameters["message"] = "Test message from CLI";

            // Act - Execute command via /api/exec endpoint using unified Pipeline client
            var response = await m_PipelineClient.PostJsonAsync("/api/exec", commandRequest);
            var responseContent = response.RawResponse;

            // Assert - Response structure
            Assert.IsTrue(response.IsSuccess,
                $"Exec endpoint should return success, got: {response.StatusCode}. Response: {responseContent}");

            // Assert - JSON parsing and structure
            Assert.IsTrue(response.HasValidJson, "Response should have valid JSON");
            var responseJson = response.JsonResponse;
            Assert.IsNotNull(responseJson["success"], "Response should have success field");
            Assert.IsNotNull(responseJson["command"], "Response should have command field");
            Assert.IsNotNull(responseJson["executedAt"], "Response should have executedAt timestamp");

            // Assert - Successful execution
            Assert.IsTrue(responseJson["success"].ToObject<bool>(), "Command should execute successfully");
            Assert.AreEqual("log_editor", responseJson["command"]?.ToString());
        }

        [Test]
        public async Task ApiExec_InvalidCommand_ReturnsError()
        {
            // Arrange
            var invalidRequest = new CommandExecutionRequest("nonexistent_command");

            // Act - Execute invalid command via /api/exec endpoint using unified Pipeline client
            var response = await m_PipelineClient.PostJsonAsync("/api/exec", invalidRequest);
            var responseContent = response.RawResponse;

            LogAssert.Expect(new Regex("^ExecuteCommandByName: No command named"));

            // Assert - Should return error
            Assert.IsFalse(response.IsSuccess, "Should return error for invalid command");

            Assert.IsTrue(response.HasValidJson, "Error response should have valid JSON");
            var responseJson = response.JsonResponse;
            Assert.IsNotNull(responseJson["error"], "Error response should have error field");
            Assert.IsNotNull(responseJson["message"], "Error response should have message field");
        }

        [Test]
        public async Task ApiExec_MissingRequiredParameter_ReturnsValidationError()
        {
            // Arrange - Try to execute log_editor without required message parameter
            var invalidRequest = new CommandExecutionRequest("log_editor");
            // Intentionally not setting the 'message' parameter to test validation

            // Act - Execute command with missing parameter via /api/exec endpoint using unified Pipeline client
            var response = await m_PipelineClient.PostJsonAsync("/api/exec", invalidRequest);
            var responseContent = response.RawResponse;

            LogAssert.Expect("ExecuteCommandByName: Parameter validation failed: Required parameter 'message' is missing or empty");

            // Assert - Should return validation error
            Assert.IsFalse(response.IsSuccess, "Should return error for missing required parameter");

            Assert.IsTrue(response.HasValidJson, "Error response should have valid JSON");
            var responseJson = response.JsonResponse;
            Assert.IsNotNull(responseJson["error"], "Should have error field");
            Assert.That(responseJson["errorDetails"]?.ToString(),
                Contains.Substring("message").IgnoreCase,
                "Error should mention missing message parameter");
        }

        [Test]
        public async Task ApiExec_OversizedBody_ReturnsPayloadTooLarge()
        {
            // Arrange - a request whose body exceeds the 1 MiB cap (Content-Length will advertise it).
            var oversized = new CommandExecutionRequest("log_editor");
            oversized.Parameters["message"] = new string('a', (1 * 1024 * 1024) + 1024);

            // Act
            var response = await m_PipelineClient.PostJsonAsync("/api/exec", oversized);

            // Assert - rejected with 413 before the command ever runs.
            Assert.AreEqual(413, response.StatusCode,
                $"Oversized body should be rejected with 413. Response: {response.RawResponse}");
            Assert.IsTrue(response.HasValidJson, "413 response should have valid JSON");
            Assert.That(response.JsonResponse["error"]?.ToString(),
                Contains.Substring("Payload Too Large"),
                "413 response should identify the payload-too-large error");
        }
    }
}