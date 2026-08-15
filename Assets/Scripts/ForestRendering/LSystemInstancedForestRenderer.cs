using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Bosque.Contract;
using Bosque.LSystem;

namespace Bosque.ForestRendering
{
    [DisallowMultipleComponent]
    public class LSystemInstancedForestRenderer : MonoBehaviour
    {
        const int BATCH_SIZE = 1023;

        [Header("Input from L-System Tree")]
        public TreeSpecies species;
        public int baseTreeSeed = 425;

        [Header("Scene References")]
        public Terrain terrain;
        public Camera targetCamera;

        [Header("Materials")]
        public Material barkMaterial;
        public Material leafMaterial;
        public Material billboardMaterial;

        [Header("Forest Distribution")]
        [Range(1, 3000)] public int treeCount = 850;
        [Range(10f, 180f)] public float forestRadius = 72f;
        [Range(0f, 25f)] public float emptyCenterRadius = 7f;
        public Vector2 scaleRange = new Vector2(0.72f, 1.45f);
        public bool alignToTerrainNormal = true;

        [Header("LOD and Culling")]
        public bool useFrustumCulling = true;
        public bool useDistanceCulling = true;
        public float lod0Distance = 34f;
        public float lod1Distance = 82f;
        public float cullingDistance = 165f;
        public ShadowCastingMode shadowCasting = ShadowCastingMode.On;
        public bool receiveShadows = true;

        [Header("Automatic Showcase")]
        public bool automaticReveal = true;
        public float singleTreeSeconds = 4.5f;
        public float forestRevealSeconds = 17.0f;
        [Range(0.01f, 0.25f)] public float revealFeather = 0.075f;
        [Range(0.01f, 0.35f)] public float sproutMinScale = 0.035f;
        [Range(0f, 1f)] public float manualReveal = 1f;
        public bool showOverlay = true;

        [Header("Wind")]
        [Range(0f, 8f)] public float windStrengthDegrees = 1.8f;
        [Range(0.1f, 5f)] public float windSpeed = 0.95f;
        public Vector2 windDirection = new Vector2(1f, 0.35f);

        struct ForestInstance
        {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public Bounds worldBounds;
            public float revealOrder;
            public float windPhase;
        }

        readonly Matrix4x4[] branchLod0Batch = new Matrix4x4[BATCH_SIZE];
        readonly Matrix4x4[] leafLod0Batch = new Matrix4x4[BATCH_SIZE];
        readonly Matrix4x4[] branchLod1Batch = new Matrix4x4[BATCH_SIZE];
        readonly Matrix4x4[] leafLod1Batch = new Matrix4x4[BATCH_SIZE];
        readonly Matrix4x4[] billboardBatch = new Matrix4x4[BATCH_SIZE];

        readonly List<ForestInstance> instances = new List<ForestInstance>(1024);

        TreeRenderMeshes meshes;
        TreeSkeleton sourceSkeleton;
        Plane[] frustumPlanes;

        float startTime;
        float fpsSmooth;
        float revealProgress = -0.1f;
        bool generated;

        int revealedCount;
        int visibleCount;
        int culledCount;
        int lod0Count;
        int lod1Count;
        int lod2Count;
        int drawCallEstimate;

        string statusText = "Waiting for generation";

        public int RevealedCount => revealedCount;
        public int VisibleCount => visibleCount;
        public int CulledCount => culledCount;
        public int Lod0Count => lod0Count;
        public int Lod1Count => lod1Count;
        public int Lod2Count => lod2Count;
        public float RevealProgress01 => Mathf.Clamp01(revealProgress);

        void Awake()
        {
            EnsureGenerated();
            startTime = Time.time;
        }

        void OnEnable()
        {
            startTime = Time.time;
            EnsureGenerated();
        }

        void OnValidate()
        {
            treeCount = Mathf.Max(1, treeCount);
            forestRadius = Mathf.Max(10f, forestRadius);
            emptyCenterRadius = Mathf.Clamp(emptyCenterRadius, 0f, forestRadius * 0.5f);
            lod0Distance = Mathf.Max(1f, lod0Distance);
            lod1Distance = Mathf.Max(lod0Distance + 1f, lod1Distance);
            cullingDistance = Mathf.Max(lod1Distance + 1f, cullingDistance);
            scaleRange.y = Mathf.Max(scaleRange.x, scaleRange.y);
            revealFeather = Mathf.Clamp(revealFeather, 0.01f, 0.25f);
            sproutMinScale = Mathf.Clamp(sproutMinScale, 0.01f, 0.35f);
        }

        [ContextMenu("Regenerate Jean Forest")]
        public void Regenerate()
        {
            generated = false;
            EnsureGenerated();
            startTime = Time.time;
        }

        void EnsureGenerated()
        {
            if (generated)
                return;

            if (targetCamera == null)
                targetCamera = Camera.main;

            EnsureMaterials();

            TreeSpecies activeSpecies = species != null ? species : CreateRuntimeShowcaseSpecies();
            var generator = new LSystemTreeGenerator();
            sourceSkeleton = generator.Generate(activeSpecies, baseTreeSeed);

            if (sourceSkeleton == null || sourceSkeleton.BranchCount == 0)
            {
                activeSpecies = CreateRuntimeShowcaseSpecies();
                sourceSkeleton = generator.Generate(activeSpecies, baseTreeSeed);
            }

            meshes = TreeSkeletonMesher.Build(sourceSkeleton);
            GenerateForestInstances();
            generated = true;

            statusText = "Jean ready: skeleton -> mesh -> instanced forest";
        }

        void EnsureMaterials()
        {
            if (barkMaterial == null)
                barkMaterial = CreateRuntimeMaterial("Runtime_Bark_Instanced", new Color(0.32f, 0.18f, 0.10f), 0.45f);

            if (leafMaterial == null)
                leafMaterial = CreateRuntimeMaterial("Runtime_Leaves_Instanced", new Color(0.12f, 0.42f, 0.16f), 0.25f);

            if (billboardMaterial == null)
                billboardMaterial = CreateRuntimeMaterial("Runtime_Billboard_Instanced", new Color(0.10f, 0.33f, 0.12f), 0.15f);

            barkMaterial.enableInstancing = true;
            leafMaterial.enableInstancing = true;
            billboardMaterial.enableInstancing = true;
        }

        static Material CreateRuntimeMaterial(string name, Color color, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            var material = new Material(shader) { name = name, enableInstancing = true };
            SetMaterialColor(material, color);

            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", 0f);

            return material;
        }

        static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
        }

        static TreeSpecies CreateRuntimeShowcaseSpecies()
        {
            var sp = ScriptableObject.CreateInstance<TreeSpecies>();
            sp.speciesId = 2026;
            sp.displayName = "Jean Showcase L-System";
            sp.axiom = "X";
            sp.rules = new[]
            {
                new ProductionRule('X', "FF[&+X][&-X][/^X][\\^X]LL")
            };
            sp.iterations = 5;
            sp.angleDeg = 24.5f;
            sp.baseLength = 0.62f;
            sp.lengthScale = 0.78f;
            sp.baseRadius = 0.16f;
            sp.radiusScale = 0.68f;
            sp.pinchFactor = 0.82f;
            sp.angleJitter = 0.22f;
            sp.lengthJitter = 0.12f;
            sp.leafScale = 0.38f;
            sp.leafDensity = 1f;
            sp.minSpacing = 3.1f;
            sp.scaleRange = new Vector2(0.75f, 1.35f);
            return sp;
        }

        void GenerateForestInstances()
        {
            instances.Clear();
            if (treeCount <= 0)
                return;

            for (int i = 0; i < treeCount; i++)
            {
                Vector3 position;
                float revealOrder;

                if (i == 0)
                {
                    position = SampleTerrainPosition(Vector3.zero, out _);
                    revealOrder = 0f;
                }
                else
                {
                    float t = i / Mathf.Max(1f, treeCount - 1f);
                    float radius = Mathf.Lerp(emptyCenterRadius, forestRadius, Mathf.Sqrt(t));
                    float angle = (i * 137.50776f + baseTreeSeed * 0.37f) * Mathf.Deg2Rad;

                    float jitterR = Mathf.Lerp(-2.3f, 2.3f, Hash01(i * 193 + baseTreeSeed));
                    float jitterA = Mathf.Lerp(-0.18f, 0.18f, Hash01(i * 281 + baseTreeSeed));

                    Vector3 flat = new Vector3(
                        Mathf.Cos(angle + jitterA) * (radius + jitterR),
                        0f,
                        Mathf.Sin(angle + jitterA) * (radius + jitterR));

                    position = SampleTerrainPosition(flat, out _);
                    revealOrder = radius;
                }

                Vector3 terrainNormal;
                position = SampleTerrainPosition(position, out terrainNormal);

                float yaw = Hash01(i * 977 + baseTreeSeed) * 360f;
                float uniformScale = i == 0
                    ? 1.22f
                    : Mathf.Lerp(scaleRange.x, scaleRange.y, Hash01(i * 719 + baseTreeSeed));

                Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
                Quaternion terrainRotation = alignToTerrainNormal
                    ? Quaternion.FromToRotation(Vector3.up, terrainNormal)
                    : Quaternion.identity;

                var instance = new ForestInstance
                {
                    position = position,
                    rotation = terrainRotation * yawRotation,
                    scale = Vector3.one * uniformScale,
                    revealOrder = revealOrder,
                    windPhase = Hash01(i * 1543 + baseTreeSeed) * Mathf.PI * 2f
                };

                instance.worldBounds = TransformBounds(
                    meshes.localBounds,
                    Matrix4x4.TRS(instance.position, instance.rotation, instance.scale));

                instances.Add(instance);
            }

            instances.Sort((a, b) => a.revealOrder.CompareTo(b.revealOrder));
        }

        Vector3 SampleTerrainPosition(Vector3 worldPosition, out Vector3 normal)
        {
            normal = Vector3.up;
            if (terrain == null || terrain.terrainData == null)
                return worldPosition;

            TerrainData data = terrain.terrainData;
            Vector3 terrainPosition = terrain.transform.position;
            float u = Mathf.InverseLerp(terrainPosition.x, terrainPosition.x + data.size.x, worldPosition.x);
            float v = Mathf.InverseLerp(terrainPosition.z, terrainPosition.z + data.size.z, worldPosition.z);

            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);

            float y = terrain.SampleHeight(worldPosition) + terrainPosition.y;
            normal = data.GetInterpolatedNormal(u, v).normalized;
            return new Vector3(worldPosition.x, y, worldPosition.z);
        }

        void LateUpdate()
        {
            if (!Application.isPlaying)
                return;

            EnsureGenerated();
            UpdateRevealState();
            RenderVisibleForest();
            UpdateFps();
        }

        void UpdateRevealState()
        {
            if (!automaticReveal)
            {
                revealedCount = Mathf.Clamp(Mathf.RoundToInt(treeCount * manualReveal), 1, treeCount);
                revealProgress = Mathf.Clamp01(manualReveal);
                statusText = "Manual reveal: tuning the forest growth percentage";
                return;
            }

            float elapsed = Time.time - startTime;
            if (elapsed < singleTreeSeconds)
            {
                revealedCount = 1;
                revealProgress = -0.01f;
                statusText = "1. Base tree: one L-system skeleton is generated";
                return;
            }

            float t = Mathf.Clamp01((elapsed - singleTreeSeconds) / Mathf.Max(0.1f, forestRevealSeconds));
            float eased = t * t * (3f - 2f * t);
            revealProgress = eased;
            revealedCount = Mathf.Clamp(1 + Mathf.RoundToInt((treeCount - 1) * eased), 1, treeCount);

            statusText = t < 0.98f
                ? "2. Forest growth: instances sprout from the procedural terrain"
                : "3. Rendering: GPU instancing + Strahler LOD + culling + wind";
        }

        void RenderVisibleForest()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;

            if (targetCamera == null || meshes.branchesLod0 == null)
                return;

            frustumPlanes = useFrustumCulling
                ? GeometryUtility.CalculateFrustumPlanes(targetCamera)
                : null;

            int branch0Count = 0;
            int leaf0Count = 0;
            int branch1Count = 0;
            int leaf1Count = 0;
            int billboardCount = 0;

            visibleCount = 0;
            culledCount = 0;
            lod0Count = 0;
            lod1Count = 0;
            lod2Count = 0;
            drawCallEstimate = 0;

            Vector3 cameraPosition = targetCamera.transform.position;
            int activeCount = 0;

            for (int i = 0; i < instances.Count; i++)
            {
                ForestInstance instance = instances[i];
                float growth = ComputeGrowth01(instance, i);
                if (growth <= 0.01f)
                    continue;

                activeCount++;
                float distance = Vector3.Distance(cameraPosition, instance.position);

                bool culledByDistance = useDistanceCulling && distance > cullingDistance;
                bool culledByFrustum = useFrustumCulling &&
                                       frustumPlanes != null &&
                                       !GeometryUtility.TestPlanesAABB(frustumPlanes, instance.worldBounds);

                if (i != 0 && (culledByDistance || culledByFrustum))
                {
                    culledCount++;
                    continue;
                }

                visibleCount++;
                Matrix4x4 matrix = BuildWindMatrix(instance, growth);

                if (i == 0 || distance <= lod0Distance)
                {
                    lod0Count++;
                    QueueDraw(meshes.branchesLod0, barkMaterial, branchLod0Batch, ref branch0Count, matrix);
                    QueueDraw(meshes.leavesLod0, leafMaterial, leafLod0Batch, ref leaf0Count, matrix);
                }
                else if (distance <= lod1Distance)
                {
                    lod1Count++;
                    QueueDraw(meshes.branchesLod1, barkMaterial, branchLod1Batch, ref branch1Count, matrix);
                    QueueDraw(meshes.leavesLod1, leafMaterial, leafLod1Batch, ref leaf1Count, matrix);
                }
                else
                {
                    lod2Count++;
                    QueueDraw(meshes.billboardLod2, billboardMaterial, billboardBatch, ref billboardCount, matrix);
                }
            }

            Flush(meshes.branchesLod0, barkMaterial, branchLod0Batch, ref branch0Count);
            Flush(meshes.leavesLod0, leafMaterial, leafLod0Batch, ref leaf0Count);
            Flush(meshes.branchesLod1, barkMaterial, branchLod1Batch, ref branch1Count);
            Flush(meshes.leavesLod1, leafMaterial, leafLod1Batch, ref leaf1Count);
            Flush(meshes.billboardLod2, billboardMaterial, billboardBatch, ref billboardCount);

            revealedCount = activeCount;
        }

        float ComputeGrowth01(ForestInstance instance, int index)
        {
            if (index == 0)
                return 1f;

            float order01 = Mathf.InverseLerp(emptyCenterRadius, forestRadius, instance.revealOrder);
            float raw = Mathf.InverseLerp(order01, order01 + revealFeather, revealProgress);
            return Mathf.SmoothStep(0f, 1f, raw);
        }

        Matrix4x4 BuildWindMatrix(ForestInstance instance, float growth01)
        {
            Vector2 dir2 = windDirection.sqrMagnitude < 0.0001f
                ? Vector2.right
                : windDirection.normalized;

            Vector3 bendAxis = new Vector3(-dir2.y, 0f, dir2.x).normalized;
            float mature = Mathf.SmoothStep(0f, 1f, growth01);
            float sway = Mathf.Sin(Time.time * windSpeed + instance.windPhase) * windStrengthDegrees * mature;
            Quaternion windRotation = Quaternion.AngleAxis(sway, bendAxis);
            Vector3 growthScale = new Vector3(
                Mathf.Lerp(0.28f, 1f, mature),
                Mathf.Lerp(sproutMinScale, 1f, mature),
                Mathf.Lerp(0.28f, 1f, mature));
            Vector3 finalScale = Vector3.Scale(instance.scale, growthScale);

            return Matrix4x4.TRS(instance.position, windRotation * instance.rotation, finalScale);
        }

        void QueueDraw(Mesh mesh, Material material, Matrix4x4[] batch, ref int count, Matrix4x4 matrix)
        {
            if (mesh == null || mesh.vertexCount == 0 || material == null)
                return;

            batch[count] = matrix;
            count++;

            if (count >= BATCH_SIZE)
                Flush(mesh, material, batch, ref count);
        }

        void Flush(Mesh mesh, Material material, Matrix4x4[] batch, ref int count)
        {
            if (count <= 0 || mesh == null || material == null)
                return;

            Graphics.DrawMeshInstanced(
                mesh,
                0,
                material,
                batch,
                count,
                null,
                shadowCasting,
                receiveShadows,
                gameObject.layer,
                targetCamera);

            drawCallEstimate++;
            count = 0;
        }

        void UpdateFps()
        {
            float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            float current = 1f / dt;
            fpsSmooth = fpsSmooth <= 0f ? current : Mathf.Lerp(fpsSmooth, current, 0.08f);
        }

        void OnGUI()
        {
            if (!Application.isPlaying || !showOverlay)
                return;

            GUI.depth = -10;
            var rect = new Rect(18, 18, 520, 262);
            GUI.Box(rect, "");

            GUILayout.BeginArea(new Rect(rect.x + 16, rect.y + 10, rect.width - 32, rect.height - 20));
            GUILayout.Label("Procedural Forest - Jean Rendering");
            GUILayout.Space(4);
            GUILayout.Label(statusText);
            GUILayout.Space(8);
            GUILayout.Label("Source A: LSystemTreeGenerator -> TreeSkeleton");
            GUILayout.Label("Skeleton: " + (sourceSkeleton != null ? sourceSkeleton.BranchCount : 0) +
                            " branches | " + (sourceSkeleton != null ? sourceSkeleton.LeafCount : 0) +
                            " leaves | " + (sourceSkeleton != null ? sourceSkeleton.height.ToString("F1") : "0") + " m");
            GUILayout.Space(6);
            GUILayout.Label("FPS: " + fpsSmooth.ToString("F1") +
                            " | Active/Growing: " + revealedCount + "/" + treeCount +
                            " | Growth wave: " + (RevealProgress01 * 100f).ToString("F0") + "%");
            GUILayout.Label("Visible: " + visibleCount + " | Culled: " + culledCount);
            GUILayout.Label("LOD0 near: " + lod0Count +
                            " | LOD1 mid: " + lod1Count +
                            " | LOD2 billboard: " + lod2Count);
            GUILayout.Label("Estimated instanced draw calls: " + drawCallEstimate);
            GUILayout.Space(6);
            GUILayout.Label("Pipeline: terrain sampling, growth reveal, GPU instancing, LOD, culling, wind.");
            GUILayout.EndArea();
        }

        static Bounds TransformBounds(Bounds localBounds, Matrix4x4 matrix)
        {
            Vector3 center = matrix.MultiplyPoint3x4(localBounds.center);
            Vector3 extents = localBounds.extents;

            Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
            Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
            Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));

            extents.x = Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x);
            extents.y = Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y);
            extents.z = Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z);

            return new Bounds(center, extents * 2f);
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

    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class ForestGrowthWave : MonoBehaviour
    {
        public LSystemInstancedForestRenderer source;
        public float forestRadius = 74f;
        public float yOffset = 0.18f;
        public Color waveColor = new Color(0.62f, 0.95f, 0.44f, 0.30f);

        MeshRenderer meshRenderer;
        MaterialPropertyBlock block;

        void Awake()
        {
            meshRenderer = GetComponent<MeshRenderer>();
            block = new MaterialPropertyBlock();
            EnsureMesh();
        }

        void OnEnable()
        {
            if (source == null)
                source = FindAnyObjectByType<LSystemInstancedForestRenderer>();
        }

        void Update()
        {
            if (source == null)
            {
                if (meshRenderer != null)
                    meshRenderer.enabled = false;
                return;
            }

            float progress = source.RevealProgress01;
            bool active = Application.isPlaying && progress > 0.01f && progress < 0.995f;
            meshRenderer.enabled = active;
            if (!active)
                return;

            float radius = Mathf.Lerp(6f, forestRadius, progress);
            transform.position = Vector3.up * yOffset;
            transform.localScale = new Vector3(radius, 1f, radius);

            float alpha = Mathf.Sin(progress * Mathf.PI) * waveColor.a;
            Color color = new Color(waveColor.r, waveColor.g, waveColor.b, alpha);

            meshRenderer.GetPropertyBlock(block);
            if (meshRenderer.sharedMaterial != null && meshRenderer.sharedMaterial.HasProperty("_BaseColor"))
                block.SetColor("_BaseColor", color);
            if (meshRenderer.sharedMaterial != null && meshRenderer.sharedMaterial.HasProperty("_Color"))
                block.SetColor("_Color", color);
            meshRenderer.SetPropertyBlock(block);
        }

        void EnsureMesh()
        {
            var filter = GetComponent<MeshFilter>();
            if (filter.sharedMesh != null)
                return;

            const int segments = 128;
            const float inner = 0.94f;
            const float outer = 1.00f;

            var vertices = new Vector3[segments * 2];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];

            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                float ca = Mathf.Cos(angle);
                float sa = Mathf.Sin(angle);
                vertices[i * 2] = new Vector3(ca * inner, 0f, sa * inner);
                vertices[i * 2 + 1] = new Vector3(ca * outer, 0f, sa * outer);
                uvs[i * 2] = new Vector2(0f, i / (float)segments);
                uvs[i * 2 + 1] = new Vector2(1f, i / (float)segments);
            }

            int t = 0;
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int a = i * 2;
                int b = next * 2;
                int c = i * 2 + 1;
                int d = next * 2 + 1;

                triangles[t++] = a;
                triangles[t++] = c;
                triangles[t++] = b;
                triangles[t++] = c;
                triangles[t++] = d;
                triangles[t++] = b;
            }

            var mesh = new Mesh { name = "Forest_Growth_Wave_Ring" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;
        }
    }
}
