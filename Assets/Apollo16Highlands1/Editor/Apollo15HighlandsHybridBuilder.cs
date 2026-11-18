#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Mujoco;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class Apollo15HighlandsHybridBuilder
{
    private const string Root = "Assets/Apollo15Highlands";
    private const string SourceRoot = "Assets/Apollo16Highlands";
    private const string BaseScenePath = SourceRoot + "/Scenes/Apollo16_Highlands_Realistic.unity";
    private const string BaseTerrainPath = SourceRoot + "/Terrain/Apollo16_Highlands_300m.asset";
    private const string OutputScenePath = Root + "/Scenes/Apollo15_Highlands.unity";
    private const string OutputTerrainPath = Root + "/Terrain/Apollo15_Highlands_300m_513.asset";
    private const string OutputMeshPath = Root + "/Terrain/Apollo15_Highlands_Visual_513.asset";
    private const string ReportPath = Root + "/Data/apollo15_highlands_report.json";
    private const string MarkerPath = Root + "/Data/build_v1.done";

    private const int Resolution = 513;
    private const int Seed = 150927;
    private const float TerrainSize = 300f;

    [Serializable]
    private sealed class CraterRecord
    {
        public int id;
        public string role;
        public string preservation;
        public float xMeters;
        public float zMeters;
        public float diameterMeters;
        public float depthMeters;
        public float depthDiameterRatio;
        public float axisRatio;
    }

    [Serializable]
    private sealed class BuildReport
    {
        public string outputScene;
        public string preservedSourceScene;
        public string macroTerrainSource;
        public int randomSeed;
        public int terrainResolution;
        public float gridSpacingMeters;
        public int totalSyntheticCraters;
        public int avoidanceCraters;
        public int backgroundCraters;
        public float measuredMaximumSlopeDegrees;
        public string distributionBasis;
        public string morphologyBasis;
        public string evidenceBoundary;
        public CraterRecord[] craters;
    }

    private sealed class Crater
    {
        public float x;
        public float z;
        public float diameter;
        public float depthRatio;
        public float rimRatio;
        public float axisRatio;
        public float angle;
        public float phase;
        public float erosion;
        public string role;
        public string preservation;
    }

    [MenuItem("Tools/Lunar Terrain/Build Apollo 15 Highlands Hybrid")]
    public static void BuildFromMenu()
    {
        Build(true);
    }

    private static void Build(bool openAfterBuild)
    {
        try
        {
            TerrainData source = AssetDatabase.LoadAssetAtPath<TerrainData>(BaseTerrainPath);
            if (source == null || AssetDatabase.LoadAssetAtPath<SceneAsset>(BaseScenePath) == null)
                throw new InvalidOperationException("Apollo 16 realistic source scene or base terrain is missing.");

            EnsureFolder(Root + "/Terrain");
            EnsureFolder(Root + "/Scenes");
            EnsureFolder(Root + "/Data");

            List<Crater> craters = GenerateCraterPopulation();
            float[,] heights = ResampleAndStamp(source, craters);
            TerrainData terrainData = BuildTerrainAsset(source, heights);
            Mesh mesh = BuildMeshAsset(terrainData, heights);
            BuildScene(terrainData, mesh, openAfterBuild);
            WriteReport(craters, heights, source.size);

            File.WriteAllText(MarkerPath,
                "Apollo15 Highlands hybrid built with deterministic seed " + Seed + ".\n");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Apollo15Highlands] Created " + OutputScenePath);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static List<Crater> GenerateCraterPopulation()
    {
        var random = new System.Random(Seed);
        var craters = new List<Crater>();

        // Same task-oriented population as Apollo15_Avoidance_300m.
        craters.Add(CreateCrater(random, 292f, 214f, 44f,
            "avoidance-edge", "degraded"));
        AddPopulation(craters, random, 2, 20f, 30f, "avoidance", "fresh", true);
        AddPopulation(craters, random, 6, 14f, 22f, "avoidance", "moderate", true);
        AddPopulation(craters, random, 12, 8f, 14f, "avoidance", "degraded", true);
        AddPopulation(craters, random, 24, 3.5f, 8f, "background", "degraded", false);

        // Four companions form doublets or small secondary clusters.
        for (int i = 0; i < 4; ++i)
        {
            Crater parent = craters[3 + i];
            float diameter = Range(random, 4f, 7f);
            float separation = (parent.diameter + diameter) * Range(random, 0.28f, 0.48f);
            float direction = Range(random, 0f, Mathf.PI * 2f);
            float x = Mathf.Clamp(parent.x + Mathf.Cos(direction) * separation, 3f, 297f);
            float z = Mathf.Clamp(parent.z + Mathf.Sin(direction) * separation, 3f, 297f);
            craters.Add(CreateCrater(random, x, z, diameter,
                "background-paired", "degraded"));
        }
        return craters;
    }

    private static void AddPopulation(List<Crater> craters, System.Random random, int count,
        float minimumDiameter, float maximumDiameter, string role, string preservation,
        bool protectRoute)
    {
        for (int i = 0; i < count; ++i)
        {
            float u = (float)random.NextDouble();
            float diameter = Mathf.Lerp(minimumDiameter, maximumDiameter, u * u);
            float radius = diameter * 0.5f;
            bool placed = false;
            for (int attempt = 0; attempt < 800 && !placed; ++attempt)
            {
                float support = radius * 1.65f + 2f;
                float x = Range(random, support, TerrainSize - support);
                float z = Range(random, support, TerrainSize - support);

                // Preserve the same curved route opportunity used by the avoidance scene.
                float routeX = 150f + 43f * Mathf.Sin(z / TerrainSize * Mathf.PI * 1.65f - 0.55f);
                if (protectRoute && Mathf.Abs(x - routeX) < radius + 5.5f)
                    continue;

                bool conflict = false;
                foreach (Crater existing in craters)
                {
                    if (existing.role == "avoidance-edge")
                        continue;
                    float separation = (radius + existing.diameter * 0.5f) *
                        (role == "background" ? 0.50f : 0.76f);
                    if (Vector2.Distance(new Vector2(x, z),
                            new Vector2(existing.x, existing.z)) < separation)
                    {
                        conflict = true;
                        break;
                    }
                }
                if (conflict)
                    continue;

                craters.Add(CreateCrater(random, x, z, diameter, role, preservation));
                placed = true;
            }
            if (!placed)
                Debug.LogWarning("[Apollo15Highlands] Could not place one " + role + " crater.");
        }
    }

    private static Crater CreateCrater(System.Random random, float x, float z,
        float diameter, string role, string preservation)
    {
        float depthRatio;
        float rimRatio;
        float erosion;
        float axisMinimum;
        float axisMaximum;
        switch (preservation)
        {
            case "fresh":
                depthRatio = Range(random, 0.11f, 0.135f);
                rimRatio = Range(random, 0.025f, 0.035f);
                erosion = Range(random, 0.08f, 0.20f);
                axisMinimum = 0.82f;
                axisMaximum = 0.97f;
                break;
            case "moderate":
                depthRatio = Range(random, 0.07f, 0.10f);
                rimRatio = Range(random, 0.012f, 0.022f);
                erosion = Range(random, 0.34f, 0.58f);
                axisMinimum = 0.75f;
                axisMaximum = 0.94f;
                break;
            default:
                depthRatio = role.StartsWith("background", StringComparison.Ordinal)
                    ? Range(random, 0.03f, 0.055f)
                    : Range(random, 0.04f, 0.067f);
                rimRatio = Range(random, 0.005f, 0.012f);
                erosion = Range(random, 0.64f, 0.90f);
                axisMinimum = 0.70f;
                axisMaximum = 0.92f;
                break;
        }

        return new Crater
        {
            x = x,
            z = z,
            diameter = diameter,
            depthRatio = depthRatio,
            rimRatio = rimRatio,
            erosion = erosion,
            axisRatio = Range(random, axisMinimum, axisMaximum),
            angle = Range(random, 0f, Mathf.PI * 2f),
            phase = Range(random, 0f, Mathf.PI * 2f),
            role = role,
            preservation = preservation
        };
    }

    private static float[,] ResampleAndStamp(TerrainData source, List<Crater> craters)
    {
        int sourceResolution = source.heightmapResolution;
        float[,] sourceHeights = source.GetHeights(0, 0, sourceResolution, sourceResolution);
        float[,] heights = new float[Resolution, Resolution];
        for (int z = 0; z < Resolution; ++z)
        {
            float sourceZ = z * (sourceResolution - 1f) / (Resolution - 1f);
            int z0 = Mathf.Min(Mathf.FloorToInt(sourceZ), sourceResolution - 2);
            int z1 = z0 + 1;
            float tz = sourceZ - z0;
            for (int x = 0; x < Resolution; ++x)
            {
                float sourceX = x * (sourceResolution - 1f) / (Resolution - 1f);
                int x0 = Mathf.Min(Mathf.FloorToInt(sourceX), sourceResolution - 2);
                int x1 = x0 + 1;
                float tx = sourceX - x0;
                float lower = Mathf.Lerp(sourceHeights[z0, x0], sourceHeights[z0, x1], tx);
                float upper = Mathf.Lerp(sourceHeights[z1, x0], sourceHeights[z1, x1], tx);
                heights[z, x] = Mathf.Lerp(lower, upper, tz);
            }
        }

        foreach (Crater crater in craters)
            StampApollo16Interior(heights, source.size, crater);
        return heights;
    }

    private static void StampApollo16Interior(float[,] heights, Vector3 size, Crater crater)
    {
        float pixelsPerMeter = (Resolution - 1f) / size.x;
        int centerX = Mathf.RoundToInt(crater.x * pixelsPerMeter);
        int centerZ = Mathf.RoundToInt(crater.z * pixelsPerMeter);
        float radius = crater.diameter * 0.5f;
        int range = Mathf.CeilToInt(radius * 1.8f * pixelsPerMeter);
        float depth = crater.diameter * crater.depthRatio;
        float rimHeight = crater.diameter * crater.rimRatio;
        float cs = Mathf.Cos(crater.angle);
        float sn = Mathf.Sin(crater.angle);

        for (int z = Mathf.Max(0, centerZ - range);
             z <= Mathf.Min(Resolution - 1, centerZ + range); ++z)
        {
            for (int x = Mathf.Max(0, centerX - range);
                 x <= Mathf.Min(Resolution - 1, centerX + range); ++x)
            {
                float dx = (x - centerX) / pixelsPerMeter;
                float dz = (z - centerZ) / pixelsPerMeter;
                float ex = cs * dx - sn * dz;
                float ez = sn * dx + cs * dz;
                float theta = Mathf.Atan2(ez, ex);

                // Degraded craters receive stronger low-order rim warping. Fresh craters
                // remain closer to circular, while no two outlines are exact circles.
                float irregularityAmplitude = Mathf.Lerp(0.05f, 0.13f, crater.erosion);
                float irregularity = 1f + irregularityAmplitude *
                    (0.55f * Mathf.Sin(theta * 2f + crater.phase * 0.37f) +
                     0.72f * Mathf.Sin(theta * 3f + crater.phase) +
                     0.30f * Mathf.Sin(theta * 5f - crater.phase * 0.63f));
                float normalizedRadius = Mathf.Sqrt(ex * ex +
                    ez * ez / (crater.axisRatio * crater.axisRatio)) /
                    (radius * Mathf.Max(0.68f, irregularity));
                if (normalizedRadius > 1.8f)
                    continue;

                float deltaMeters = 0f;
                if (normalizedRadius < 1f)
                {
                    // Apollo16_Highlands_Realistic's bowl-squared interior profile.
                    float bowl = Mathf.Clamp01(1f - normalizedRadius * normalizedRadius);
                    // Fade azimuthal floor relief to zero at the center so it retains the
                    // Apollo16 interior character without creating a pinched center seam.
                    float floorFade = Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(0.18f, 0.72f, normalizedRadius));
                    float floorNoise = 0.90f + 0.10f * floorFade *
                        Mathf.Sin(theta * 5f + crater.phase);
                    deltaMeters -= depth * bowl * bowl * floorNoise;
                }

                float rimWidth = Mathf.Lerp(0.11f, 0.20f, crater.erosion);
                float rimDistance = (normalizedRadius - 1f) / rimWidth;
                float rimContinuity = Mathf.Lerp(0.96f, 0.42f, crater.erosion);
                float brokenRim = rimContinuity + (1f - rimContinuity) *
                    Mathf.Clamp01(0.52f + 0.48f *
                        Mathf.Sin(theta * 2f + crater.phase));
                deltaMeters += rimHeight * brokenRim *
                    Mathf.Exp(-rimDistance * rimDistance);

                if (normalizedRadius > 1f)
                {
                    float ejecta = rimHeight * Mathf.Lerp(0.10f, 0.025f, crater.erosion) *
                        Mathf.Exp(-(normalizedRadius - 1f) *
                            Mathf.Lerp(3.4f, 6.2f, crater.erosion));
                    deltaMeters += ejecta *
                        (0.82f + 0.18f * Mathf.Sin(theta * 4f + crater.phase));
                }

                heights[z, x] = Mathf.Clamp01(heights[z, x] + deltaMeters / size.y);
            }
        }
    }

    private static TerrainData BuildTerrainAsset(TerrainData source, float[,] heights)
    {
        TerrainData output = AssetDatabase.LoadAssetAtPath<TerrainData>(OutputTerrainPath);
        if (output == null)
        {
            output = new TerrainData();
            AssetDatabase.CreateAsset(output, OutputTerrainPath);
        }
        output.heightmapResolution = Resolution;
        output.size = source.size;
        output.name = "Apollo15_Highlands_300m_513";
        output.SetHeights(0, 0, heights);
        output.terrainLayers = source.terrainLayers;
        EditorUtility.SetDirty(output);
        return output;
    }

    private static Mesh BuildMeshAsset(TerrainData terrainData, float[,] heights)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(OutputMeshPath);
        if (mesh == null)
        {
            mesh = new Mesh();
            AssetDatabase.CreateAsset(mesh, OutputMeshPath);
        }

        Vector3[] vertices = new Vector3[Resolution * Resolution];
        Vector2[] uv = new Vector2[Resolution * Resolution];
        int[] triangles = new int[(Resolution - 1) * (Resolution - 1) * 6];
        float stepX = terrainData.size.x / (Resolution - 1f);
        float stepZ = terrainData.size.z / (Resolution - 1f);
        for (int z = 0; z < Resolution; ++z)
        {
            for (int x = 0; x < Resolution; ++x)
            {
                int index = z * Resolution + x;
                vertices[index] = new Vector3(-terrainData.size.x * 0.5f + x * stepX,
                    heights[z, x] * terrainData.size.y + 0.001f,
                    -terrainData.size.z * 0.5f + z * stepZ);
                uv[index] = new Vector2(x / (Resolution - 1f), z / (Resolution - 1f));
            }
        }

        int triangle = 0;
        for (int z = 0; z < Resolution - 1; ++z)
        {
            for (int x = 0; x < Resolution - 1; ++x)
            {
                int lowerLeft = z * Resolution + x;
                int lowerRight = lowerLeft + 1;
                int upperLeft = lowerLeft + Resolution;
                int upperRight = upperLeft + 1;
                triangles[triangle++] = lowerLeft;
                triangles[triangle++] = upperLeft;
                triangles[triangle++] = lowerRight;
                triangles[triangle++] = lowerRight;
                triangles[triangle++] = upperLeft;
                triangles[triangle++] = upperRight;
            }
        }

        mesh.Clear();
        mesh.name = "Apollo15_Highlands_Visual_513";
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    private static void BuildScene(TerrainData terrainData, Mesh mesh, bool openAfterBuild)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(OutputScenePath) == null &&
            !AssetDatabase.CopyAsset(BaseScenePath, OutputScenePath))
            throw new InvalidOperationException("Could not copy Apollo16_Highlands_Realistic.");

        Scene activeBefore = SceneManager.GetActiveScene();
        bool mayOpen = openAfterBuild && activeBefore.IsValid() && !activeBefore.isDirty;
        Scene output = SceneManager.GetSceneByPath(OutputScenePath);
        bool wasAlreadyLoaded = output.IsValid() && output.isLoaded;
        if (wasAlreadyLoaded && output.isDirty)
            throw new InvalidOperationException(
                "Apollo15_Highlands has unsaved changes. Save or discard them before rebuilding.");
        if (!wasAlreadyLoaded)
            output = EditorSceneManager.OpenScene(OutputScenePath, OpenSceneMode.Additive);

        SceneManager.SetActiveScene(output);
        try
        {
            GameObject terrainObject = Find(output,
                "Apollo16_Highlands_Terrain_Realistic_513") ??
                Find(output, "Apollo15_Highlands_Terrain_513");
            GameObject visualObject = Find(output,
                "Apollo16_Highlands_Visual_Realistic") ??
                Find(output, "Apollo15_Highlands_Visual");
            if (terrainObject == null || visualObject == null)
                throw new InvalidOperationException("Realistic terrain or visual object was not found.");

            terrainObject.name = "Apollo15_Highlands_Terrain_513";
            Terrain terrain = terrainObject.GetComponent<Terrain>();
            if (terrain == null)
                throw new InvalidOperationException("Terrain component is missing.");
            terrain.terrainData = terrainData;
            terrain.drawHeightmap = false;
            terrain.heightmapPixelError = 2f;
            MjGeom ground = terrainObject.GetComponentInParent<MjGeom>();
            if (ground != null)
            {
                ground.HField.Terrain = terrain;
                ground.gameObject.name = "Apollo15_Highlands_HField";
            }

            visualObject.name = "Apollo15_Highlands_Visual";
            MeshFilter filter = visualObject.GetComponent<MeshFilter>();
            if (filter == null)
                throw new InvalidOperationException("Visual MeshFilter is missing.");
            filter.sharedMesh = mesh;

            GameObject root = Find(output, "Apollo16_Highlands_Test_Root");
            if (root != null)
                root.name = "Apollo15_Highlands_Root";
            GameObject oldConfiguration = Find(output, "Apollo16_Realistic_Surface_Configuration");
            if (oldConfiguration != null)
                oldConfiguration.name = "Apollo15_Highlands_Configuration";
            else if (Find(output, "Apollo15_Highlands_Configuration") == null)
                new GameObject("Apollo15_Highlands_Configuration");

            EditorSceneManager.MarkSceneDirty(output);
            EditorSceneManager.SaveScene(output, OutputScenePath);
        }
        finally
        {
            if (!wasAlreadyLoaded)
                EditorSceneManager.CloseScene(output, true);
            if (activeBefore.IsValid() && activeBefore.isLoaded)
                SceneManager.SetActiveScene(activeBefore);
        }

        if (mayOpen && !wasAlreadyLoaded)
        {
            EditorApplication.delayCall += () =>
            {
                Scene scene = EditorSceneManager.OpenScene(OutputScenePath, OpenSceneMode.Single);
                SceneManager.SetActiveScene(scene);
                GameObject visual = Find(scene, "Apollo15_Highlands_Visual");
                if (visual != null)
                    Selection.activeGameObject = visual;
            };
        }
        else if (openAfterBuild && !mayOpen)
        {
            Debug.LogWarning("[Apollo15Highlands] Built but not opened because the current " +
                "scene has unsaved changes.");
        }
    }

    private static void WriteReport(List<Crater> craters, float[,] heights, Vector3 size)
    {
        var records = new List<CraterRecord>();
        int avoidance = 0;
        foreach (Crater crater in craters)
        {
            if (crater.role.StartsWith("avoidance", StringComparison.Ordinal))
                ++avoidance;
            records.Add(new CraterRecord
            {
                id = records.Count,
                role = crater.role,
                preservation = crater.preservation,
                xMeters = crater.x - TerrainSize * 0.5f,
                zMeters = crater.z - TerrainSize * 0.5f,
                diameterMeters = crater.diameter,
                depthMeters = crater.diameter * crater.depthRatio,
                depthDiameterRatio = crater.depthRatio,
                axisRatio = crater.axisRatio
            });
        }

        var report = new BuildReport
        {
            outputScene = OutputScenePath,
            preservedSourceScene = BaseScenePath,
            macroTerrainSource = "NAC_DTM_APOLLO16-derived 300 m terrain retained from the " +
                "project's current realistic scene workflow.",
            randomSeed = Seed,
            terrainResolution = Resolution,
            gridSpacingMeters = size.x / (Resolution - 1f),
            totalSyntheticCraters = craters.Count,
            avoidanceCraters = avoidance,
            backgroundCraters = craters.Count - avoidance,
            measuredMaximumSlopeDegrees = MeasureMaximumSlope(heights, size),
            distributionBasis = "Apollo15_Avoidance_300m task distribution: 21 obstacle-scale " +
                "craters and 28 shallow background craters, including one edge crater, doublets, " +
                "and a protected curved route corridor.",
            morphologyBasis = "Bowl-squared interior profile adapted from " +
                "Apollo16_Highlands_Realistic; center-safe floor modulation, stronger degraded " +
                "rim warping, elliptical outlines, broken rims and weak ejecta were added.",
            evidenceBoundary = "Apollo15_Highlands is a synthetic hybrid project label, not a " +
                "geographic reconstruction of an Apollo 15 highland site. Crater positions and " +
                "shapes are generated; the macro terrain is Apollo 16-derived.",
            craters = records.ToArray()
        };
        File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
    }

    private static float MeasureMaximumSlope(float[,] heights, Vector3 size)
    {
        float spacingX = size.x / (Resolution - 1f);
        float spacingZ = size.z / (Resolution - 1f);
        float maximum = 0f;
        for (int z = 1; z < Resolution - 1; ++z)
        {
            for (int x = 1; x < Resolution - 1; ++x)
            {
                float dx = (heights[z, x + 1] - heights[z, x - 1]) * size.y /
                    (2f * spacingX);
                float dz = (heights[z + 1, x] - heights[z - 1, x]) * size.y /
                    (2f * spacingZ);
                maximum = Mathf.Max(maximum,
                    Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg);
            }
        }
        return maximum;
    }

    private static GameObject Find(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                if (transform.name == name)
                    return transform.gameObject;
        return null;
    }

    private static float Range(System.Random random, float minimum, float maximum)
    {
        return Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int index = 1; index < parts.Length; ++index)
        {
            string next = current + "/" + parts[index];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[index]);
            current = next;
        }
    }
}
#endif
