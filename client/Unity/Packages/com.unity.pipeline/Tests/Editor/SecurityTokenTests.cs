using NUnit.Framework;
using Unity.Pipeline.Security;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Tests for security token generation and comparison.
    /// </summary>
    public class SecurityTokenTests
    {
        [SetUp]
        public void SetUp() => SecurityTokenManager.ClearCache();

        [TearDown]
        public void TearDown() => SecurityTokenManager.ClearCache();

        [Test]
        public void GetOrCreateToken_FirstCall_GeneratesToken()
        {
            var token = SecurityTokenManager.GetOrCreateToken();

            Assert.IsNotNull(token, "Token should not be null");
            Assert.IsNotEmpty(token, "Token should not be empty");
            Assert.GreaterOrEqual(token.Length, 32, "Token should be at least 32 characters (256-bit base64)");
        }

        [Test]
        public void GetOrCreateToken_SecondCall_ReturnsSameToken()
        {
            var firstToken = SecurityTokenManager.GetOrCreateToken();
            var secondToken = SecurityTokenManager.GetOrCreateToken();

            Assert.AreEqual(firstToken, secondToken, "Should return the same token on subsequent calls");
        }

        [Test]
        public void ClearCache_AfterTokenGeneration_GeneratesDifferentToken()
        {
            var originalToken = SecurityTokenManager.GetOrCreateToken();

            SecurityTokenManager.ClearCache();
            var newToken = SecurityTokenManager.GetOrCreateToken();

            Assert.IsNotNull(newToken);
            Assert.AreNotEqual(originalToken, newToken, "A fresh token should be generated after the cache is cleared");
        }

        [Test]
        public void GetOrCreateToken_AfterSimulatedDomainReload_ReturnsSameToken()
        {
            var before = SecurityTokenManager.GetOrCreateToken();

            // A domain reload clears in-memory statics but leaves SessionState intact.
            SecurityTokenManager.ResetInMemoryCacheForTests();
            var after = SecurityTokenManager.GetOrCreateToken();

            Assert.AreEqual(before, after,
                "Token must survive a domain reload (rehydrated from SessionState) so long-lived clients don't get 401.");
        }

        [Test]
        public void ClearCache_ThenSimulatedReload_GeneratesDifferentToken()
        {
            var original = SecurityTokenManager.GetOrCreateToken();

            // Explicit rotation erases the persisted copy, so even the reload rehydrate path
            // cannot bring the old token back.
            SecurityTokenManager.ClearCache();
            SecurityTokenManager.ResetInMemoryCacheForTests();
            var rotated = SecurityTokenManager.GetOrCreateToken();

            Assert.AreNotEqual(original, rotated,
                "ClearCache must erase the persisted token so a fresh one is generated, not rehydrated.");
        }

        [Test]
        public void RotateToken_GeneratesNewTokenAndPersistsIt()
        {
            var original = SecurityTokenManager.GetOrCreateToken();

            var rotated = SecurityTokenManager.RotateToken();

            Assert.AreNotEqual(original, rotated, "RotateToken should produce a new token.");
            Assert.AreEqual(rotated, SecurityTokenManager.GetOrCreateToken(),
                "The rotated token should be the one served on subsequent calls.");

            // And it must survive a subsequent domain reload like any session token.
            SecurityTokenManager.ResetInMemoryCacheForTests();
            Assert.AreEqual(rotated, SecurityTokenManager.GetOrCreateToken(),
                "The rotated token should persist across domain reloads.");
        }

        [Test]
        public void GetOrCreateToken_WarmedThenConcurrentReads_AreConsistent()
        {
            // Warm on the main thread, then hammer the fast path from background threads — warmed
            // reads must be consistent and must not touch SessionState off-thread.
            var expected = SecurityTokenManager.GetOrCreateToken();
            Assert.IsNotEmpty(expected);

            var results = new System.Collections.Concurrent.ConcurrentBag<string>();
            System.Threading.Tasks.Parallel.For(0, 32, _ => results.Add(SecurityTokenManager.GetOrCreateToken()));

            Assert.AreEqual(32, results.Count);
            CollectionAssert.AreEquivalent(System.Linq.Enumerable.Repeat(expected, 32), results,
                "All concurrent reads should return the single warmed token.");
        }

        [Test]
        public void ConstantTimeEquals_IdenticalTokens_ReturnsTrue()
        {
            var token = SecurityTokenManager.GetOrCreateToken();
            Assert.IsTrue(SecurityTokenManager.ConstantTimeEquals(token, token));
        }

        [Test]
        public void ConstantTimeEquals_DifferentTokens_ReturnsFalse()
        {
            var token = SecurityTokenManager.GetOrCreateToken();
            Assert.IsFalse(SecurityTokenManager.ConstantTimeEquals(token, "not-the-token"));
        }

        [TestCase(null, TestName = "ConstantTimeEquals_NullToken_ReturnsFalse")]
        [TestCase("", TestName = "ConstantTimeEquals_EmptyToken_ReturnsFalse")]
        public void ConstantTimeEquals_NullOrEmpty_ReturnsFalse(string token)
        {
            var expected = SecurityTokenManager.GetOrCreateToken();
            Assert.IsFalse(SecurityTokenManager.ConstantTimeEquals(token, expected));
        }
    }
}
