using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using Bosque.Contract;
using Bosque.ForestRendering;

namespace Bosque.EditorTools
{
    public static class JeanSceneBuilder
    {
        const string GENERATED_FOLDER = "Assets/JeanGenerated";
        const string MATERIAL_FOLDER = "Assets/Materials";
        const string SCENE_PATH = "Assets/Scenes/Jean_Forest_Showcase.unity";
        const string SPECIES_PATH = "Assets/Species/Jean_ShowcaseSpecies.asset";
        const string TERRAIN_PATH = GENERATED_FOLDER + "/Jean_Terrain.asset";
        const string FLOOR_MESH_PATH = GENERATED_FOLDER + "/Jean_ForestFloor.asset";
        const string GRASS_MESH_PATH = GENERATED_FOLDER + "/Jean_GrassBlades.asset";
        const string PATH_MESH_PATH = GENERATED_FOLDER + "/Jean_ForestPath.asset";
        const string ROCK_MESH_PATH = GENERATED_FOLDER + "/Jean_LowPolyRock.asset";

        const float TERRAIN_SIZE = 190f;
        const float TERRAIN_HEIGHT = 14f;
        const float TERRAIN_BASE_Y = -0.7f;

        [MenuItem("Bosque/Jean/Build Forest Showcase Scene")]
        public static void BuildForestShowcaseScene()
        {
            EnsureFolders();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "Jean_Forest_Showcase";

            Material bark = GetOrCreateMaterial(MATERIAL_FOLDER + "/Jean_Bark_Instanced.mat",
                "Jean_Bark_Instanced", new Color(0.34f, 0.20f, 0.11f), 0.48f, false);

            Material leaves = GetOrCreateMaterial(MATERIAL_FOLDER + "/Jean_Leaves_Instanced.mat",
                "Jean_Leaves_Instanced", new Color(0.10f, 0.43f, 0.15f), 0.20f, true);

            Material billboard = GetOrCreateMaterial(MATERIAL_FOLDER + "/Jean_Billboard_Instanced.mat",
                "Jean_Billboard_Instanced", new Color(0.08f, 0.31f, 0.11f), 0.12f, true);

            Material ground = GetOrCreateMaterial(MATERIAL_FOLDER + "/Jean_ForestFloor.mat",
                "Jean_ForestFloor", new Color(0.20f, 0.27f, 0.17f), 0.62f, false);

            Material moss = GetOrCreateMaterial(MATERIAL_FOLDER + "/Jean_MossPatches.mat",
                "Jean_MossPatches", new Color(0.12f, 0.34f, 0.12f), 0.45f, false);

            Material leafLitter = GetOrCreateMaterial(MATERIAL_FOLDER + "/Jean_LeafLitter.mat",
                "Jean_LeafLitter", new Color(0.37f, 0.27f, 0.14f), 0.72f, false);

            Material path = GetOrCreateMaterial(MATERIAL_FOLDER + "/Jean_ForestPath.mat",
                "Jean_ForestPath", new Color(0.45f, 0.36f, 0.22f), 0.70f, false);

            Material rock = GetOrCreateMaterial(MATERIAL_FOLDER + "/Jean_Rocks.mat",
                "Jean_Rocks", new Color(0.32f, 0.35f, 0.32f), 0.66f, false);

            Material grass = GetOrCreateMaterial(MATERIAL_FOLDER + "/Jean_Grass.mat",
                "Jean_Grass", new Color(0.16f, 0.38f, 0.15f), 0.34f, true);

            Material wave = GetOrCreateTransparentMaterial(MATERIAL_FOLDER + "/Jean_GrowthWave.mat",
                "Jean_GrowthWave", new Color(0.62f, 0.95f, 0.42f, 0.32f), 0.10f);

            TreeSpecies species = GetOrCreateShowcaseSpecies();
            Terrain terrain = CreateSamplingTerrain();
            CreateForestFloor(ground);
            CreateCentralClearing(leafLitter, moss);
            CreateForestPath(path);
            CreateGrassField(grass);
            CreateRocksAndLogs(rock, bark);

            Transform pivot = CreatePivot();
            Camera camera = CreateCamera(pivot);
            CreateLighting();

            var rendererObject = new GameObject("Jean_GPU_Instanced_Forest");
            var renderer = rendererObject.AddComponent<LSystemInstancedForestRenderer>();
            renderer.species = species;
            renderer.baseTreeSeed = 425;
            renderer.terrain = terrain;
            renderer.targetCamera = camera;
            renderer.barkMaterial = bark;
            renderer.leafMaterial = leaves;
            renderer.billboardMaterial = billboard;
            renderer.treeCount = 1200;
            renderer.forestRadius = 82f;
            renderer.emptyCenterRadius = 8.5f;
            renderer.scaleRange = new Vector2(0.70f, 1.45f);
            renderer.lod0Distance = 36f;
            renderer.lod1Distance = 92f;
            renderer.cullingDistance = 178f;
            renderer.singleTreeSeconds = 4.5f;
            renderer.forestRevealSeconds = 21f;
            renderer.revealFeather = 0.085f;
            renderer.sproutMinScale = 0.025f;
            renderer.windStrengthDegrees = 2.0f;
            renderer.windSpeed = 0.85f;
            renderer.showOverlay = true;

            CreateGrowthWave(renderer, wave);
            Selection.activeGameObject = rendererObject;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Jean] Upgraded forest scene built at " + SCENE_PATH +
                      ". Press Play: one L-system tree appears first, then the terrain-born forest grows.");
        }

        public static void BuildForestShowcaseSceneFromBatch()
        {
            BuildForestShowcaseScene();
        }

        static void EnsureFolders()
        {
            EnsureFolder("Assets", "JeanGenerated");
            EnsureFolder("Assets", "Materials");
            EnsureFolder("Assets", "Species");
            EnsureFolder("Assets", "Scenes");
        }

        static void EnsureFolder(string parent, string name)
        {
            string path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, name);
        }

        static TreeSpecies GetOrCreateShowcaseSpecies()
        {
            JeanTreeSpeciesAsset species = AssetDatabase.LoadAssetAtPath<JeanTreeSpeciesAsset>(SPECIES_PATH);
            if (species == null)
            {
                if (AssetDatabase.LoadAssetAtPath<Object>(SPECIES_PATH) != null)
                    AssetDatabase.DeleteAsset(SPECIES_PATH);

                species = ScriptableObject.CreateInstance<JeanTreeSpeciesAsset>();
                AssetDatabase.CreateAsset(species, SPECIES_PATH);
            }

            species.speciesId = 2026;
            species.displayName = "L-System Showcase Species";
            species.axiom = "X";
            species.rules = new[]
            {
                new ProductionRule('X', "FF[&+X][&-X][/^X][\\^X]LL")
            };
            species.iterations = 5;
            species.angleDeg = 24.5f;
            species.baseLength = 0.62f;
            species.lengthScale = 0.78f;
            species.baseRadius = 0.16f;
            species.radiusScale = 0.68f;
            species.pinchFactor = 0.82f;
            species.angleJitter = 0.22f;
            species.lengthJitter = 0.12f;
            species.leafScale = 0.38f;
            species.leafDensity = 1f;
            species.altitudeRange = new Vector2(0f, 1000f);
            species.maxSlopeDeg = 36f;
            species.minSpacing = 3.1f;
            species.scaleRange = new Vector2(0.70f, 1.45f);

            EditorUtility.SetDirty(species);
            return species;
        }

        static Material GetOrCreateMaterial(string path, string name, Color color, float smoothness, bool doubleSided)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    shader = Shader.Find("Standard");

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.name = name;
            material.enableInstancing = true;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", doubleSided ? 0f : 2f);

            material.renderQueue = -1;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material GetOrCreateTransparentMaterial(string path, string name, Color color, float smoothness)
        {
            Material material = GetOrCreateMaterial(path, name, color, smoothness, true);
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;

            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_AlphaClip"))
                material.SetFloat("_AlphaClip", 0f);
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            EditorUtility.SetDirty(material);
            return material;
        }

        static Terrain CreateSamplingTerrain()
        {
            TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(TERRAIN_PATH);
            if (data == null)
            {
                data = new TerrainData();
                AssetDatabase.CreateAsset(data, TERRAIN_PATH);
            }

            int resolution = 257;
            data.heightmapResolution = resolution;
            data.size = new Vector3(TERRAIN_SIZE, TERRAIN_HEIGHT, TERRAIN_SIZE);

            var heights = new float[resolution, resolution];
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float u = x / (resolution - 1f);
                    float v = z / (resolution - 1f);
                    heights[z, x] = Height01(u, v);
                }
            }

            data.SetHeights(0, 0, heights);
            EditorUtility.SetDirty(data);

            GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
            terrainObject.name = "Sampling_Terrain_Hidden_For_Placement";
            terrainObject.transform.position = new Vector3(-TERRAIN_SIZE * 0.5f, TERRAIN_BASE_Y, -TERRAIN_SIZE * 0.5f);

            Terrain terrain = terrainObject.GetComponent<Terrain>();
            terrain.drawHeightmap = false;
            terrain.drawTreesAndFoliage = false;
            terrain.heightmapPixelError = 6f;
            terrain.basemapDistance = 800f;

            TerrainCollider collider = terrainObject.GetComponent<TerrainCollider>();
            if (collider != null)
                collider.terrainData = data;

            return terrain;
        }

        static void CreateForestFloor(Material material)
        {
            const int resolution = 129;
            var vertices = new Vector3[resolution * resolution];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[(resolution - 1) * (resolution - 1) * 6];

            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float u = x / (resolution - 1f);
                    float v = z / (resolution - 1f);
                    float wx = (u - 0.5f) * TERRAIN_SIZE;
                    float wz = (v - 0.5f) * TERRAIN_SIZE;
                    vertices[z * resolution + x] = new Vector3(wx, SampleGroundHeight(wx, wz) + 0.025f, wz);
                    uvs[z * resolution + x] = new Vector2(u * 12f, v * 12f);
                }
            }

            int t = 0;
            for (int z = 0; z < resolution - 1; z++)
            {
                for (int x = 0; x < resolution - 1; x++)
                {
                    int a = z * resolution + x;
                    int b = a + 1;
                    int c = a + resolution;
                    int d = c + 1;
                    triangles[t++] = a;
                    triangles[t++] = c;
                    triangles[t++] = b;
                    triangles[t++] = b;
                    triangles[t++] = c;
                    triangles[t++] = d;
                }
            }

            var mesh = new Mesh { name = "Jean_ForestFloor" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var floor = new GameObject("Visible_Forest_Floor");
            floor.AddComponent<MeshFilter>().sharedMesh = SaveMeshAsset(mesh, FLOOR_MESH_PATH);
            floor.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        static void CreateCentralClearing(Material leafLitter, Material moss)
        {
            CreateDisc("SourceTree_Clearing_Leaf_Litter", Vector3.zero, 10.5f, leafLitter, 80, 0.18f, 0.07f);
            CreateAnnulus("Mossy_Growth_Ring", Vector3.zero, 12.5f, 18.0f, moss, 96, 0.12f, 0.06f);

            for (int i = 0; i < 20; i++)
            {
                float angle = i * 137.50776f * Mathf.Deg2Rad;
                float radius = Mathf.Lerp(18f, 82f, Hash01(i * 73));
                Vector3 center = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                center.y = SampleGroundHeight(center.x, center.z) + 0.065f;

                float patchRadius = Mathf.Lerp(3.2f, 7.5f, Hash01(i * 131 + 9));
                Material material = Hash01(i * 41) > 0.42f ? moss : leafLitter;
                CreateDisc("Ground_Detail_Patch_" + i.ToString("00"), center, patchRadius, material, 36, 0.28f, 0.05f);
            }
        }

        static void CreateForestPath(Material material)
        {
            const int steps = 72;
            const float zStart = -92f;
            const float zEnd = 7f;
            const float width = 5.0f;

            var vertices = new Vector3[(steps + 1) * 2];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[steps * 6];

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float z = Mathf.Lerp(zStart, zEnd, t);
                float x = PathCenterX(z);
                float nextZ = Mathf.Lerp(zStart, zEnd, Mathf.Clamp01(t + 1f / steps));
                Vector3 tangent = new Vector3(PathCenterX(nextZ) - x, 0f, nextZ - z).normalized;
                Vector3 side = new Vector3(-tangent.z, 0f, tangent.x);
                float localWidth = width * Mathf.Lerp(1.15f, 0.78f, t);

                Vector3 left = new Vector3(x, 0f, z) - side * localWidth;
                Vector3 right = new Vector3(x, 0f, z) + side * localWidth;
                left.y = SampleGroundHeight(left.x, left.z) + 0.085f;
                right.y = SampleGroundHeight(right.x, right.z) + 0.085f;

                vertices[i * 2] = left;
                vertices[i * 2 + 1] = right;
                uvs[i * 2] = new Vector2(0f, t * 8f);
                uvs[i * 2 + 1] = new Vector2(1f, t * 8f);
            }

            int tri = 0;
            for (int i = 0; i < steps; i++)
            {
                int a = i * 2;
                int b = a + 1;
                int c = a + 2;
                int d = a + 3;
                triangles[tri++] = a;
                triangles[tri++] = c;
                triangles[tri++] = b;
                triangles[tri++] = b;
                triangles[tri++] = c;
                triangles[tri++] = d;
            }

            var mesh = new Mesh { name = "Jean_ForestPath" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var path = new GameObject("Presentation_Path_To_Source_Tree");
            path.AddComponent<MeshFilter>().sharedMesh = SaveMeshAsset(mesh, PATH_MESH_PATH);
            path.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        static void CreateGrassField(Material material)
        {
            const int blades = 950;
            var vertices = new List<Vector3>(blades * 6);
            var uvs = new List<Vector2>(blades * 6);
            var triangles = new List<int>(blades * 6);

            for (int i = 0; i < blades; i++)
            {
                float radius = Mathf.Lerp(11f, 90f, Mathf.Sqrt(Hash01(i * 101 + 5)));
                float angle = (i * 137.50776f + 18f) * Mathf.Deg2Rad;
                float x = Mathf.Cos(angle) * radius + Mathf.Lerp(-2.2f, 2.2f, Hash01(i * 211));
                float z = Mathf.Sin(angle) * radius + Mathf.Lerp(-2.2f, 2.2f, Hash01(i * 271));

                if (Mathf.Abs(x - PathCenterX(z)) < 4.8f && z < 10f)
                    continue;

                Vector3 root = new Vector3(x, SampleGroundHeight(x, z) + 0.09f, z);
                float yaw = Hash01(i * 401) * Mathf.PI * 2f;
                float width = Mathf.Lerp(0.10f, 0.28f, Hash01(i * 541));
                float height = Mathf.Lerp(0.55f, 1.75f, Hash01(i * 641));
                Vector3 side = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)) * width;
                Vector3 tip = root + Vector3.up * height + new Vector3(side.z, 0f, -side.x) * Mathf.Lerp(-0.22f, 0.22f, Hash01(i * 811));

                int start = vertices.Count;
                vertices.Add(root - side);
                vertices.Add(root + side);
                vertices.Add(tip);
                vertices.Add(root + side);
                vertices.Add(root - side);
                vertices.Add(tip);

                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(0.5f, 1f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(0.5f, 1f));

                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
                triangles.Add(start + 3);
                triangles.Add(start + 4);
                triangles.Add(start + 5);
            }

            var mesh = new Mesh { name = "Jean_GrassBlades" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var grass = new GameObject("Combined_Grass_Understory");
            grass.AddComponent<MeshFilter>().sharedMesh = SaveMeshAsset(mesh, GRASS_MESH_PATH);
            grass.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        static void CreateRocksAndLogs(Material rockMaterial, Material barkMaterial)
        {
            Mesh rockMesh = SaveMeshAsset(CreateRockMesh(), ROCK_MESH_PATH);

            for (int i = 0; i < 34; i++)
            {
                float radius = Mathf.Lerp(14f, 86f, Mathf.Sqrt(Hash01(i * 37 + 14)));
                float angle = (i * 97.91f + 11f) * Mathf.Deg2Rad;
                Vector3 position = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                if (Mathf.Abs(position.x - PathCenterX(position.z)) < 5.5f && position.z < 12f)
                    position.x += 7f;

                position.y = SampleGroundHeight(position.x, position.z) + 0.13f;

                var rock = new GameObject("Terrain_Rock_" + i.ToString("00"));
                rock.transform.position = position;
                rock.transform.rotation = Quaternion.Euler(
                    Hash01(i * 61) * 20f,
                    Hash01(i * 83) * 360f,
                    Hash01(i * 109) * 20f);
                rock.transform.localScale = new Vector3(
                    Mathf.Lerp(0.65f, 1.8f, Hash01(i * 131)),
                    Mathf.Lerp(0.25f, 0.78f, Hash01(i * 151)),
                    Mathf.Lerp(0.65f, 1.7f, Hash01(i * 173)));

                rock.AddComponent<MeshFilter>().sharedMesh = rockMesh;
                rock.AddComponent<MeshRenderer>().sharedMaterial = rockMaterial;
            }

            for (int i = 0; i < 5; i++)
            {
                float z = Mathf.Lerp(-48f, 48f, Hash01(i * 401 + 2));
                float x = Mathf.Lerp(-45f, 45f, Hash01(i * 521 + 7));
                if (Vector2.Distance(new Vector2(x, z), Vector2.zero) < 16f)
                    x += 20f;

                var log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                log.name = "Fallen_Log_" + i.ToString("00");
                Object.DestroyImmediate(log.GetComponent<Collider>());
                log.transform.position = new Vector3(x, SampleGroundHeight(x, z) + 0.35f, z);
                log.transform.rotation = Quaternion.Euler(84f, Hash01(i * 631) * 360f, Hash01(i * 733) * 12f);
                log.transform.localScale = new Vector3(0.35f, Mathf.Lerp(2.4f, 4.6f, Hash01(i * 877)), 0.35f);
                log.GetComponent<MeshRenderer>().sharedMaterial = barkMaterial;
            }
        }

        static void CreateGrowthWave(LSystemInstancedForestRenderer source, Material material)
        {
            var waveObject = new GameObject("Visible_Growth_Wave_On_Terrain");
            var filter = waveObject.AddComponent<MeshFilter>();
            var renderer = waveObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            var wave = waveObject.AddComponent<ForestGrowthWave>();
            wave.source = source;
            wave.forestRadius = source.forestRadius;
            wave.yOffset = SampleGroundHeight(0f, 0f) + 0.20f;
            wave.waveColor = new Color(0.62f, 0.95f, 0.42f, 0.32f);

            filter.sharedMesh = null;
        }

        static Transform CreatePivot()
        {
            var pivot = new GameObject("Showcase_Pivot");
            pivot.transform.position = Vector3.zero;
            return pivot.transform;
        }

        static Camera CreateCamera(Transform pivot)
        {
            var cameraObject = new GameObject("Presentation_Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(-36f, 15f, -32f);
            cameraObject.transform.LookAt(Vector3.up * 8f);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.67f, 0.76f);
            camera.fieldOfView = 47f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 450f;

            var presentation = cameraObject.AddComponent<ForestPresentationCamera>();
            presentation.pivot = pivot;
            presentation.orbitRadius = 104f;
            presentation.orbitHeight = 31f;
            presentation.lookAtHeight = 8f;
            presentation.orbitDegreesPerSecond = 5.8f;
            presentation.breathingZoom = 6f;
            presentation.automaticPullback = true;
            presentation.closeRadius = 30f;
            presentation.closeHeight = 12f;
            presentation.wideRadius = 104f;
            presentation.wideHeight = 31f;
            presentation.closeSeconds = 4.5f;
            presentation.pullbackSeconds = 18f;

            return camera;
        }

        static void CreateLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.64f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.28f, 0.34f, 0.25f);
            RenderSettings.ambientGroundColor = new Color(0.13f, 0.12f, 0.09f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.52f, 0.62f, 0.68f);
            RenderSettings.fogDensity = 0.010f;

            var sunObject = new GameObject("Low_Warm_Sun_Key");
            sunObject.transform.rotation = Quaternion.Euler(42f, -34f, 0f);
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.87f, 0.62f);
            sun.intensity = 1.65f;
            sun.shadows = LightShadows.Soft;

            var fillObject = new GameObject("Cool_Canopy_Fill");
            fillObject.transform.rotation = Quaternion.Euler(25f, 150f, 0f);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.37f, 0.52f, 0.76f);
            fill.intensity = 0.42f;
            fill.shadows = LightShadows.None;
        }

        static void CreateDisc(string name, Vector3 center, float radius, Material material, int segments, float irregularity, float yOffset)
        {
            var vertices = new Vector3[segments + 1];
            var triangles = new int[segments * 3];
            vertices[0] = new Vector3(center.x, SampleGroundHeight(center.x, center.z) + yOffset, center.z);

            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                float r = radius * Mathf.Lerp(1f - irregularity, 1f + irregularity, Hash01(i * 97 + name.GetHashCode()));
                float x = center.x + Mathf.Cos(angle) * r;
                float z = center.z + Mathf.Sin(angle) * r;
                vertices[i + 1] = new Vector3(x, SampleGroundHeight(x, z) + yOffset, z);

                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i == segments - 1 ? 1 : i + 2;
            }

            var mesh = new Mesh { name = name + "_Mesh" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var obj = new GameObject(name);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            obj.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        static void CreateAnnulus(string name, Vector3 center, float innerRadius, float outerRadius, Material material, int segments, float irregularity, float yOffset)
        {
            var vertices = new Vector3[segments * 2];
            var triangles = new int[segments * 6];

            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                float noise = Mathf.Lerp(1f - irregularity, 1f + irregularity, Hash01(i * 113 + name.GetHashCode()));
                float ca = Mathf.Cos(angle);
                float sa = Mathf.Sin(angle);
                float ix = center.x + ca * innerRadius * noise;
                float iz = center.z + sa * innerRadius * noise;
                float ox = center.x + ca * outerRadius * noise;
                float oz = center.z + sa * outerRadius * noise;

                vertices[i * 2] = new Vector3(ix, SampleGroundHeight(ix, iz) + yOffset, iz);
                vertices[i * 2 + 1] = new Vector3(ox, SampleGroundHeight(ox, oz) + yOffset, oz);
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

            var mesh = new Mesh { name = name + "_Mesh" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var obj = new GameObject(name);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            obj.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        static Mesh CreateRockMesh()
        {
            const int sides = 10;
            const int rings = 5;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            for (int r = 0; r <= rings; r++)
            {
                float v = r / (float)rings;
                float phi = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, v);
                float y = Mathf.Sin(phi);
                float ringRadius = Mathf.Cos(phi);

                for (int s = 0; s < sides; s++)
                {
                    float angle = Mathf.PI * 2f * s / sides;
                    float rough = Mathf.Lerp(0.80f, 1.18f, Hash01(r * 53 + s * 17));
                    vertices.Add(new Vector3(Mathf.Cos(angle) * ringRadius * rough, y * 0.72f, Mathf.Sin(angle) * ringRadius * rough));
                }
            }

            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int next = (s + 1) % sides;
                    int a = r * sides + s;
                    int b = r * sides + next;
                    int c = (r + 1) * sides + s;
                    int d = (r + 1) * sides + next;
                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(d);
                }
            }

            var mesh = new Mesh { name = "Jean_LowPolyRock" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh SaveMeshAsset(Mesh mesh, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            EditorUtility.CopySerialized(mesh, existing);
            existing.name = mesh.name;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static float Height01(float u, float v)
        {
            float broad = Mathf.PerlinNoise(u * 2.1f + 17.1f, v * 2.1f + 3.4f);
            float detail = Mathf.PerlinNoise(u * 8.2f + 11.7f, v * 8.2f + 29.1f);
            float micro = Mathf.PerlinNoise(u * 22.0f + 4.5f, v * 22.0f + 9.3f);
            float radial = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f));
            float centralClearing = Mathf.SmoothStep(0.10f, 0.0f, radial) * 0.08f;
            float basin = Mathf.SmoothStep(0.0f, 0.42f, radial) * 0.035f;
            return Mathf.Clamp01(broad * 0.23f + detail * 0.07f + micro * 0.018f + centralClearing + basin);
        }

        static float SampleGroundHeight(float x, float z)
        {
            float u = Mathf.Clamp01((x + TERRAIN_SIZE * 0.5f) / TERRAIN_SIZE);
            float v = Mathf.Clamp01((z + TERRAIN_SIZE * 0.5f) / TERRAIN_SIZE);
            return TERRAIN_BASE_Y + Height01(u, v) * TERRAIN_HEIGHT;
        }

        static float PathCenterX(float z)
        {
            float t = Mathf.InverseLerp(-92f, 7f, z);
            return Mathf.Lerp(-15f, 0f, t) + Mathf.Sin(t * Mathf.PI * 2.3f) * 4.2f;
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
