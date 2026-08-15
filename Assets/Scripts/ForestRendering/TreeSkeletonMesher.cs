using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Bosque.Contract;

namespace Bosque.ForestRendering
{
    public struct TreeRenderMeshes
    {
        public Mesh branchesLod0;
        public Mesh leavesLod0;
        public Mesh branchesLod1;
        public Mesh leavesLod1;
        public Mesh billboardLod2;
        public Bounds localBounds;
        public int sourceBranchCount;
        public int sourceLeafCount;
    }

    /// <summary>
    /// Jean: converts the L-system skeleton delivered by A into renderable meshes.
    /// The forest renderer reuses these meshes through GPU instancing.
    /// </summary>
    public static class TreeSkeletonMesher
    {
        const float MIN_BRANCH_RADIUS = 0.018f;

        public static TreeRenderMeshes Build(TreeSkeleton skeleton)
        {
            var result = new TreeRenderMeshes
            {
                localBounds = skeleton != null ? skeleton.bounds : new Bounds(Vector3.up, Vector3.one),
                sourceBranchCount = skeleton != null ? skeleton.BranchCount : 0,
                sourceLeafCount = skeleton != null ? skeleton.LeafCount : 0
            };

            if (skeleton == null || skeleton.BranchCount == 0)
            {
                result.branchesLod0 = EmptyMesh("Empty_Branches_LOD0");
                result.leavesLod0 = EmptyMesh("Empty_Leaves_LOD0");
                result.branchesLod1 = EmptyMesh("Empty_Branches_LOD1");
                result.leavesLod1 = EmptyMesh("Empty_Leaves_LOD1");
                result.billboardLod2 = EmptyMesh("Empty_Billboard_LOD2");
                return result;
            }

            result.branchesLod0 = BuildBranchMesh(skeleton, 1, 7, 1.45f, "LSystem_Branches_LOD0");
            result.leavesLod0 = BuildLeafMesh(skeleton, 1, 1, 1.00f, 3, "LSystem_Leaves_LOD0");

            result.branchesLod1 = BuildBranchMesh(skeleton, 2, 5, 1.65f, "LSystem_Branches_LOD1");
            result.leavesLod1 = BuildLeafMesh(skeleton, 2, 2, 1.35f, 2, "LSystem_Leaves_LOD1");

            result.billboardLod2 = BuildBillboardMesh(skeleton, "LSystem_Billboard_LOD2");
            return result;
        }

        static Mesh BuildBranchMesh(TreeSkeleton skeleton, int minOrder, int sides, float radiusMultiplier, string name)
        {
            var vertices = new List<Vector3>(skeleton.BranchCount * sides * 2);
            var normals = new List<Vector3>(skeleton.BranchCount * sides * 2);
            var uvs = new List<Vector2>(skeleton.BranchCount * sides * 2);
            var triangles = new List<int>(skeleton.BranchCount * sides * 6);

            var branches = skeleton.branches;
            for (int i = 0; i < branches.Length; i++)
            {
                BranchSegment branch = branches[i];
                if (branch.order < minOrder)
                    continue;

                Vector3 axis = branch.end - branch.start;
                float length = axis.magnitude;
                if (length < 0.0001f)
                    continue;

                Vector3 dir = axis / length;
                Vector3 tangent = Vector3.Cross(dir, Vector3.up);
                if (tangent.sqrMagnitude < 0.0001f)
                    tangent = Vector3.Cross(dir, Vector3.right);
                tangent.Normalize();

                Vector3 bitangent = Vector3.Cross(dir, tangent).normalized;
                int baseIndex = vertices.Count;

                float startRadius = Mathf.Max(MIN_BRANCH_RADIUS, branch.radiusStart * radiusMultiplier);
                float endRadius = Mathf.Max(MIN_BRANCH_RADIUS * 0.45f, branch.radiusEnd * radiusMultiplier);

                for (int s = 0; s < sides; s++)
                {
                    float angle = (Mathf.PI * 2f * s) / sides;
                    Vector3 radial = tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle);

                    vertices.Add(branch.start + radial * startRadius);
                    vertices.Add(branch.end + radial * endRadius);
                    normals.Add(radial);
                    normals.Add(radial);
                    uvs.Add(new Vector2((float)s / sides, 0f));
                    uvs.Add(new Vector2((float)s / sides, length));
                }

                for (int s = 0; s < sides; s++)
                {
                    int n = (s + 1) % sides;
                    int a = baseIndex + s * 2;
                    int b = baseIndex + n * 2;
                    int c = baseIndex + s * 2 + 1;
                    int d = baseIndex + n * 2 + 1;

                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(d);
                    triangles.Add(b);
                }
            }

            return FinishMesh(name, vertices, normals, uvs, triangles);
        }

        static Mesh BuildLeafMesh(
            TreeSkeleton skeleton,
            int minBranchOrder,
            int sampleStep,
            float sizeMultiplier,
            int crossedQuads,
            string name)
        {
            var anchors = CollectLeafAnchors(skeleton);
            var vertices = new List<Vector3>(anchors.Count * crossedQuads * 8);
            var normals = new List<Vector3>(anchors.Count * crossedQuads * 8);
            var uvs = new List<Vector2>(anchors.Count * crossedQuads * 8);
            var triangles = new List<int>(anchors.Count * crossedQuads * 12);

            for (int i = 0; i < anchors.Count; i += Mathf.Max(1, sampleStep))
            {
                LeafAnchor leaf = anchors[i];
                if (leaf.branchIndex >= 0 && leaf.branchIndex < skeleton.branches.Length)
                {
                    if (skeleton.branches[leaf.branchIndex].order < minBranchOrder)
                        continue;
                }

                float scale = Mathf.Max(0.06f, leaf.scale * sizeMultiplier);
                for (int q = 0; q < crossedQuads; q++)
                {
                    float roll = q * (180f / crossedQuads) + Hash01(i * 41 + q * 17) * 24f;
                    Quaternion planeRotation =
                        Quaternion.AngleAxis(roll, leaf.orientation * Vector3.forward) * leaf.orientation;

                    AddDoubleSidedLeaf(
                        vertices,
                        normals,
                        uvs,
                        triangles,
                        leaf.position,
                        planeRotation,
                        scale);
                }
            }

            return FinishMesh(name, vertices, normals, uvs, triangles);
        }

        static List<LeafAnchor> CollectLeafAnchors(TreeSkeleton skeleton)
        {
            var anchors = new List<LeafAnchor>();
            if (skeleton.leaves != null && skeleton.leaves.Length > 0)
            {
                anchors.AddRange(skeleton.leaves);
                return anchors;
            }

            // Fallback for grammars that draw branches but did not emit L symbols yet.
            for (int i = 0; i < skeleton.branches.Length; i++)
            {
                BranchSegment branch = skeleton.branches[i];
                if (branch.order != 1)
                    continue;

                Vector3 dir = branch.Direction;
                Vector3 normal = Vector3.Cross(dir, Vector3.right);
                if (normal.sqrMagnitude < 0.0001f)
                    normal = Vector3.Cross(dir, Vector3.forward);
                normal.Normalize();

                anchors.Add(new LeafAnchor
                {
                    position = branch.end,
                    orientation = Quaternion.LookRotation(normal, dir),
                    scale = Mathf.Lerp(0.18f, 0.34f, Hash01(i * 97)),
                    branchIndex = i
                });
            }

            return anchors;
        }

        static void AddDoubleSidedLeaf(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> triangles,
            Vector3 center,
            Quaternion rotation,
            float scale)
        {
            int start = vertices.Count;
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            Vector3 normal = rotation * Vector3.forward;

            float halfWidth = scale * 0.42f;
            float halfHeight = scale * 0.72f;

            vertices.Add(center + up * halfHeight);
            vertices.Add(center + right * halfWidth);
            vertices.Add(center - up * halfHeight);
            vertices.Add(center - right * halfWidth);

            vertices.Add(center + up * halfHeight);
            vertices.Add(center - right * halfWidth);
            vertices.Add(center - up * halfHeight);
            vertices.Add(center + right * halfWidth);

            for (int i = 0; i < 4; i++)
                normals.Add(normal);
            for (int i = 0; i < 4; i++)
                normals.Add(-normal);

            uvs.Add(new Vector2(0.5f, 1f));
            uvs.Add(new Vector2(1f, 0.5f));
            uvs.Add(new Vector2(0.5f, 0f));
            uvs.Add(new Vector2(0f, 0.5f));
            uvs.Add(new Vector2(0.5f, 1f));
            uvs.Add(new Vector2(0f, 0.5f));
            uvs.Add(new Vector2(0.5f, 0f));
            uvs.Add(new Vector2(1f, 0.5f));

            triangles.Add(start + 0);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start + 0);
            triangles.Add(start + 2);
            triangles.Add(start + 3);

            triangles.Add(start + 4);
            triangles.Add(start + 5);
            triangles.Add(start + 6);
            triangles.Add(start + 4);
            triangles.Add(start + 6);
            triangles.Add(start + 7);
        }

        static Mesh BuildBillboardMesh(TreeSkeleton skeleton, string name)
        {
            float height = Mathf.Max(4f, skeleton.height);
            float width = Mathf.Max(3f, Mathf.Max(skeleton.bounds.size.x, skeleton.bounds.size.z) * 1.35f);
            float centerY = height * 0.52f;

            var vertices = new List<Vector3>(32);
            var normals = new List<Vector3>(32);
            var uvs = new List<Vector2>(32);
            var triangles = new List<int>(48);

            for (int i = 0; i < 3; i++)
            {
                Quaternion rot = Quaternion.Euler(0f, i * 60f, 0f);
                AddBillboardPlane(vertices, normals, uvs, triangles, rot, width, height, centerY);
            }

            return FinishMesh(name, vertices, normals, uvs, triangles);
        }

        static void AddBillboardPlane(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> triangles,
            Quaternion rotation,
            float width,
            float height,
            float centerY)
        {
            int start = vertices.Count;
            Vector3 right = rotation * Vector3.right;
            Vector3 normal = rotation * Vector3.forward;
            Vector3 center = Vector3.up * centerY;

            vertices.Add(center - right * width * 0.5f - Vector3.up * height * 0.5f);
            vertices.Add(center + right * width * 0.5f - Vector3.up * height * 0.5f);
            vertices.Add(center + right * width * 0.5f + Vector3.up * height * 0.5f);
            vertices.Add(center - right * width * 0.5f + Vector3.up * height * 0.5f);

            vertices.Add(vertices[start + 0]);
            vertices.Add(vertices[start + 3]);
            vertices.Add(vertices[start + 2]);
            vertices.Add(vertices[start + 1]);

            for (int i = 0; i < 4; i++)
                normals.Add(normal);
            for (int i = 0; i < 4; i++)
                normals.Add(-normal);

            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(0f, 1f));
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(0f, 1f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(1f, 0f));

            triangles.Add(start + 0);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start + 0);
            triangles.Add(start + 2);
            triangles.Add(start + 3);

            triangles.Add(start + 4);
            triangles.Add(start + 5);
            triangles.Add(start + 6);
            triangles.Add(start + 4);
            triangles.Add(start + 6);
            triangles.Add(start + 7);
        }

        static Mesh FinishMesh(
            string name,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> triangles)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65000)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh EmptyMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(new List<Vector3>());
            mesh.SetTriangles(new int[0], 0);
            return mesh;
        }

        static float Hash01(int value)
        {
            unchecked
            {
                uint x = (uint)value;
                x ^= x >> 16;
                x *= 0x7feb352du;
                x ^= x >> 15;
                x *= 0x846ca68bu;
                x ^= x >> 16;
                return (x & 0x00ffffff) / 16777216f;
            }
        }
    }
}
