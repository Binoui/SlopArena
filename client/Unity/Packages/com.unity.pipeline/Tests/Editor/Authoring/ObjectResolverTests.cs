using NUnit.Framework;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Unity.Pipeline;

namespace Unity.Pipeline.Tests.Editor.Authoring
{
    /// <summary>
    /// Tests for the object-reference resolver foundation (CLI-190): each handle form resolves back
    /// to the same object, and Describe produces a canonical identity.
    /// </summary>
    public class ObjectResolverTests
    {
        private const string AssetFolder = "Assets/AUTHAPI9_Res";

        private GameObject m_SceneObject;

        [TearDown]
        public void TearDown()
        {
            if (m_SceneObject != null)
                Object.DestroyImmediate(m_SceneObject);
            m_SceneObject = null;

            if (AssetDatabase.IsValidFolder(AssetFolder))
            {
                AssetDatabase.DeleteAsset(AssetFolder);
                AssetDatabase.Refresh();
            }
        }

        private static string CreateMaterialAsset(string name)
        {
            if (!AssetDatabase.IsValidFolder(AssetFolder))
                AssetDatabase.CreateFolder("Assets", "AUTHAPI9_Res");

            var shader = Shader.Find("Standard")
                ?? Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            var path = $"{AssetFolder}/{name}.mat";
            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return path;
        }

        [Test]
        public void Describe_SceneObject_ProducesInstanceIdAndHierarchyPath()
        {
            m_SceneObject = new GameObject("CLI190_Root");
            var child = new GameObject("Child");
            child.transform.SetParent(m_SceneObject.transform);

            var info = ObjectResolver.Describe(child);

            Assert.IsNotNull(info);
            Assert.AreEqual(PipelineUtils.GetObjectId(child), info.InstanceId);
            Assert.AreEqual("/CLI190_Root/Child", info.HierarchyPath);
            Assert.AreEqual("GameObject", info.Type);
            Assert.IsNull(info.AssetPath, "A scene object should not report an asset path");
        }

        [Test]
        public void Resolve_ByInstanceId_ReturnsSameObject()
        {
            m_SceneObject = new GameObject("CLI190_ById");
            var handle = new ObjectRef { InstanceId = PipelineUtils.GetObjectId(m_SceneObject) };

            Assert.IsTrue(ObjectResolver.TryResolve(handle, out var obj, out var error), error);
            Assert.AreSame(m_SceneObject, obj);
        }

        [Test]
        public void Resolve_ByHierarchyPath_ReturnsSameObject()
        {
            m_SceneObject = new GameObject("CLI190_ByPath");
            var handle = new ObjectRef { HierarchyPath = "/CLI190_ByPath" };

            Assert.IsTrue(ObjectResolver.TryResolve(handle, out var obj, out var error), error);
            Assert.AreSame(m_SceneObject, obj);
        }

        [Test]
        public void Resolve_EmptyHandle_FailsWithError()
        {
            Assert.IsFalse(ObjectResolver.TryResolve(new ObjectRef(), out _, out var error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void Resolve_UnknownGuid_FailsWithError()
        {
            var handle = new ObjectRef { Guid = "00000000000000000000000000000000" };
            Assert.IsFalse(ObjectResolver.TryResolve(handle, out _, out var error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void Resolve_ByRelativeAssetPath_NormalizesUnderAuthoringRoot()
        {
            // A path without the "Assets/" prefix (AUTHAPI-9) resolves under the authoring root.
            var full = CreateMaterialAsset("Floor");
            var expected = AssetDatabase.LoadMainAssetAtPath(full);
            var handle = new ObjectRef { Path = "AUTHAPI9_Res/Floor.mat" };

            Assert.IsTrue(ObjectResolver.TryResolve(handle, out var obj, out var error), error);
            Assert.AreSame(expected, obj);
        }

        [Test]
        public void Resolve_ExplicitAssetsPath_StillResolves()
        {
            var full = CreateMaterialAsset("Wall");
            var handle = new ObjectRef { Path = full };

            Assert.IsTrue(ObjectResolver.TryResolve(handle, out var obj, out var error), error);
            Assert.AreSame(AssetDatabase.LoadMainAssetAtPath(full), obj);
        }

        [Test]
        public void Resolve_DottedGameObjectName_FallsBackToHierarchy()
        {
            // A dotted name (e.g. "Cube.001") is routed to Path by the string converter but is really a
            // scene object; the Path branch falls back to a hierarchy lookup.
            m_SceneObject = new GameObject("Cube.001");
            var handle = new ObjectRef { Path = "Cube.001" };

            Assert.IsTrue(ObjectResolver.TryResolve(handle, out var obj, out var error), error);
            Assert.AreSame(m_SceneObject, obj);
        }

        [Test]
        public void Resolve_UnresolvableRelativePath_ErrorListsEveryStrategy()
        {
            var handle = new ObjectRef { Path = "AUTHAPI9_Res/Missing.mat" };

            Assert.IsFalse(ObjectResolver.TryResolve(handle, out _, out var error));
            StringAssert.Contains("no asset at", error);
            StringAssert.Contains("no GameObject at hierarchy path", error);
        }
    }
}
