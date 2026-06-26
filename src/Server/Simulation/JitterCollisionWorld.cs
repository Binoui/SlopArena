using System;
using System.Linq;
using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.LinearMath;
using SlopArena.Shared;

namespace SlopArena.Server
{
    /// <summary>
    /// Jitter2-based collision world for server-side character-vs-arena collision.
    /// Builds a TriangleMesh from arena CollisionTriangles and tests a capsule
    /// against candidate triangles each tick using MPR+EPA narrowphase.
    /// </summary>
    public class JitterCollisionWorld : IDisposable
    {
        private TriangleMesh _mesh;
        private CapsuleShape _capsule;
        private int _triangleCount;
        private bool _disposed;
        private TriangleShape[] _triangleShapes;

        public bool IsAvailable => _mesh != null && _triangleCount > 0;

        public JitterCollisionWorld(ArenaDefinition arena)
        {
            if (arena.CollisionTriangles == null || arena.CollisionTriangles.Length == 0)
            {
                _mesh = null;
                _triangleCount = 0;
                _triangleShapes = null;
                return;
            }

            var tris = arena.CollisionTriangles;
            var soup = new JTriangle[tris.Length];

            for (int i = 0; i < tris.Length; i++)
            {
                var t = tris[i];
                soup[i] = new JTriangle(
                    new JVector(t.AX, t.AY, t.AZ),
                    new JVector(t.BX, t.BY, t.BZ),
                    new JVector(t.CX, t.CY, t.CZ));
            }

            _mesh = new TriangleMesh(soup.AsSpan(), ignoreDegenerated: true);
            _triangleCount = _mesh.Indices.Length / 3;
            _triangleShapes = TriangleShape.CreateAllShapes(_mesh).ToArray();
            _capsule = new CapsuleShape(0.3f, 0.5f);

            Console.WriteLine($"[JitterCollision] {_triangleCount} triangles, {_mesh.Vertices.Length} unique verts");
        }

        /// <summary>
        /// Test capsule against candidate triangles and push character out of overlaps.
        /// </summary>
        public bool CollideCharacter(
            ref float px, ref float py, ref float pz,
            ref float vx, ref float vy, ref float vz,
            int[] candidateIndices, int candidateCount)
        {
            if (_mesh == null || candidateCount == 0 || _triangleShapes == null)
                return false;

            bool hit = false;
            var capsulePos = new JVector(px, py, pz);
            var capsuleOrient = JQuaternion.Identity;

            for (int ci = 0; ci < candidateCount; ci++)
            {
                int ti = candidateIndices[ci];
                if (ti < 0 || ti >= _triangleShapes.Length)
                    continue;

                if (NarrowPhase.Collision(
                        _triangleShapes[ti], _capsule,
                        JQuaternion.Identity, capsuleOrient,
                        JVector.Zero, capsulePos,
                        out _, out _,
                        out JVector normal, out float penetration))
                {
                    if (penetration > 0f && !float.IsNaN(normal.X))
                    {
                        px += normal.X * penetration;
                        py += normal.Y * penetration;
                        pz += normal.Z * penetration;

                        float vDotN = vx * normal.X + vy * normal.Y + vz * normal.Z;
                        if (vDotN < 0f)
                        {
                            vx -= vDotN * normal.X;
                            vy -= vDotN * normal.Y;
                            vz -= vDotN * normal.Z;
                        }

                        capsulePos = new JVector(px, py, pz);
                        hit = true;
                    }
                }
            }

            if (float.IsNaN(px) || float.IsNaN(py) || float.IsNaN(pz) ||
                float.IsInfinity(px) || float.IsInfinity(py) || float.IsInfinity(pz))
            {
                Console.WriteLine("[JitterCollision] NaN — resetting");
                px = 0; py = 1; pz = 0;
                vx = 0; vy = 0; vz = 0;
                hit = true;
            }

            return hit;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _mesh = null;
                _triangleShapes = null;
                _capsule = null;
                _disposed = true;
            }
        }
    }
}
