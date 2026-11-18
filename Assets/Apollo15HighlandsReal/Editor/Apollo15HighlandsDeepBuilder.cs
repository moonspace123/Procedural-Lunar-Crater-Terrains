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

public static class Apollo15HighlandsDeepBuilder
{
    private const string Root = "Assets/Apollo15HighlandsReal";
    private const string BaseRoot =
        "Assets/MoonEnvironment/Terrain/Observed/Generated/Apollo15_300m_20261021_171319_322";
    private const string BaseScenePath = BaseRoot + "/Apollo15_300m.unity";
    // The source scene uses ContactHeightfield as its TerrainData and VisualSurface
    // as the visible render mesh.
    private const string BaseTerrainPath = BaseRoot + "/ContactHeightfield.asset";
    private const string OutputTerrainPath = Root + "/Terrain/Apollo15_Highlands_Deep_300m_513.asset";
    private const string OutputMeshPath = Root + "/Terrain/Apollo15_Highlands_Deep_Visual_513.asset";
    private const string OutputScenePath = Root + "/Scenes/Apollo15_Highlands_Deep.unity";
    private const string ReportPath = Root + "/Data/apollo15_highlands_deep_report.json";
    private const string BuildMarkerPath = Root + "/Data/build_v2_doublets.done";

    private const int Resolution = 513;
    private const int Seed = 150927;
    private const float TerrainSize = 300f;
    private const float PaddingBelow = 7.5f;
    private const float PaddingAbove = 2.5f;

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
        public float rimHeightMeters;
        public float axisRatio;
        public int pairId;
    }

    [Serializable]
    private sealed class BuildReport
    {
        public string outputScene;
        public string preservedSourceScene;
        public string baseTerrain;
        public int randomSeed;
        public int terrainResolution;
        public float gridSpacingMeters;
        public int totalSyntheticCraters;
        public int avoidanceCraters;
        public int backgroundCraters;
        public float measuredMaximumSlopeDegrees;
        public string observedBasis;
        public string modelingChoices;
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
        public float degradation;
        public string role;
        public string preservation;
        public int pairId;
    }

    [MenuItem("Tools/Lunar Terrain/Build Apollo 15 Highlands Deep Craters")]
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
                throw new InvalidOperationException("Apollo 15 source terrain or source scene is missing.");

            EnsureFolder(Root + "/Terrain");
            EnsureFolder(Root + "/Scenes");
            EnsureFolder(Root + "/Data");

            List<Crater> craters = GenerateCraterPopulation();
            float outputHeight;
            float[,] heights = BuildHeights(source, craters, out outputHeight);
            TerrainData terrainData = BuildTerrainAsset(source, heights, outputHeight);
            Mesh mesh = BuildMeshAsset(heights, outputHeight);
            BuildScene(terrainData, mesh, outputHeight, openAfterBuild);
            WriteReport(craters, heights, outputHeight);
            File.WriteAllText(BuildMarkerPath,
                "Apollo 15 highlands deep-crater scene generated with deterministic seed " +
                Seed + ".\n");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Apollo15HighlandsDeep] Created " + OutputScenePath);
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

        // A partly cropped, old crater mirrors the edge-truncated forms in LROC frames.
        craters.Add(CreateCrater(random, 292f, 214f, 44f, "avoidance-edge", "degraded"));

        // Two explicit pairs are included without changing the total population:
        // a moderate overlapping pair and a smaller degraded doublet.
        AddDoublet(craters, random, 1, new Vector2(230f, 185f),
            18f, 14f, 0.79f, "moderate");
        AddDoublet(craters, random, 2, new Vector2(70f, 220f),
            12f, 10f, 0.78f, "degraded");

        AddPopulation(craters, random, 3, 22f, 32f, "avoidance-deep", "fresh-deep", true);
        AddPopulation(craters, random, 3, 14f, 22f, "avoidance", "moderate", true);
        AddPopulation(craters, random, 8, 8f, 14f, "avoidance", "degraded", true);
        AddPopulation(craters, random, 18, 3.5f, 8f, "background", "degraded", false);

        // Four small companions create natural-looking doublets/secondary clusters.
        for (int i = 0; i < 4; ++i)
        {
            Crater parent = craters[8 + i];
            float diameter = Range(random, 4f, 7f);
            float separation = (parent.diameter + diameter) * Range(random, 0.28f, 0.48f);
            float direction = Range(random, 0f, Mathf.PI * 2f);
            float x = Mathf.Clamp(parent.x + Mathf.Cos(direction) * separation, 3f, 297f);
            float z = Mathf.Clamp(parent.z + Mathf.Sin(direction) * separation, 3f, 297f);
            craters.Add(CreateCrater(random, x, z, diameter, "background-paired", "degraded"));
        }
        return craters;
    }

    private static void AddDoublet(List<Crater> craters, System.Random random, int pairId,
        Vector2 midpoint, float diameterA, float diameterB, float separationFraction,
        string preservation)
    {
        float direction = Range(random, 0f, Mathf.PI * 2f);
        float separation = (diameterA + diameterB) * 0.5f * separationFraction;
        Vector2 offset = new Vector2(Mathf.Cos(direction), Mathf.Sin(direction)) *
            separation * 0.5f;
        Crater a = CreateCrater(random, midpoint.x - offset.x, midpoint.y - offset.y,
            diameterA, "avoidance-doublet", preservation);
        Crater b = CreateCrater(random, midpoint.x + offset.x, midpoint.y + offset.y,
            diameterB, "avoidance-doublet", preservation);
        a.pairId = pairId;
        b.pairId = pairId;
        craters.Add(a);
        craters.Add(b);
    }

    private static void AddPopulation(List<Crater> craters, System.Random random, int count,
        float minimumDiameter, float maximumDiameter, string role, string preservation,
        bool protectRoute)
    {
        for (int i = 0; i < count; ++i)
        {
            float u = (float)random.NextDouble();
            // Bias toward the small end, consistent with a crater size-frequency distribution.
            float diameter = Mathf.Lerp(minimumDiameter, maximumDiameter, u * u);
            float radius = diameter * 0.5f;
            bool placed = false;
            for (int attempt = 0; attempt < 800 && !placed; ++attempt)
            {
                float support = radius * 1.55f + 2f;
                float x = Range(random, support, TerrainSize - support);
                float z = Range(random, support, TerrainSize - support);

                // Reserve a narrow, curved traversable corridor. Small shallow background pits
                // are allowed inside it; task-relevant craters are not.
                float routeX = 150f + 43f * Mathf.Sin(z / TerrainSize * Mathf.PI * 1.65f - 0.55f);
                if (protectRoute && Mathf.Abs(x - routeX) < radius + 5.5f)
                    continue;

                bool conflicts = false;
                foreach (Crater existing in craters)
                {
                    if (existing.role == "avoidance-edge")
                        continue;
                    float minimumDistance = (radius + existing.diameter * 0.5f) *
                        (role == "background" ? 0.50f : 0.76f);
                    if (Vector2.Distance(new Vector2(x, z), new Vector2(existing.x, existing.z)) <
                        minimumDistance)
                    {
                        conflicts = true;
                        break;
                    }
                }
                if (conflicts)
                    continue;

                craters.Add(CreateCrater(random, x, z, diameter, role, preservation));
                placed = true;
            }
            if (!placed)
                Debug.LogWarning("[Apollo15HighlandsDeep] Could not place one " + role + " crater.");
        }
    }

    private static Crater CreateCrater(System.Random random, float x, float z, float diameter,
        string role, string preservation)
    {
        float depthRatio;
        float rimRatio;
        float degradation;
        switch (preservation)
        {
            case "fresh-deep":
                depthRatio = Range(random, 0.14f, 0.17f);
                rimRatio = Range(random, 0.026f, 0.038f);
                degradation = Range(random, 0.04f, 0.14f);
                break;
            case "moderate":
                depthRatio = Range(random, 0.07f, 0.10f);
                rimRatio = Range(random, 0.012f, 0.022f);
                degradation = Range(random, 0.30f, 0.55f);
                break;
            default:
                depthRatio = role.StartsWith("background", StringComparison.Ordinal)
                    ? Range(random, 0.03f, 0.055f)
                    : Range(random, 0.04f, 0.067f);
                rimRatio = Range(random, 0.005f, 0.012f);
                degradation = Range(random, 0.62f, 0.88f);
                break;
        }
        return new Crater
        {
            x = x,
            z = z,
            diameter = diameter,
            depthRatio = depthRatio,
            rimRatio = rimRatio,
            axisRatio = preservation == "fresh-deep"
                ? Range(random, 0.80f, 0.96f)
                : preservation == "moderate"
                    ? Range(random, 0.74f, 0.94f)
                    : Range(random, 0.70f, 0.92f),
            angle = Range(random, 0f, Mathf.PI * 2f),
            phase = Range(random, 0f, Mathf.PI * 2f),
            degradation = degradation,
            role = role,
            preservation = preservation
        };
    }

    private static float[,] BuildHeights(TerrainData source, List<Crater> craters,
        out float outputHeight)
    {
        outputHeight = source.size.y + PaddingBelow + PaddingAbove;
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
                float sourceMeters = Mathf.Lerp(lower, upper, tz) * source.size.y;
                heights[z, x] = (sourceMeters + PaddingBelow) / outputHeight;
            }
        }

        var stampedPairs = new HashSet<int>();
        foreach (Crater crater in craters)
        {
            if (crater.pairId <= 0)
            {
                StampCrater(heights, outputHeight, crater);
                continue;
            }
            if (!stampedPairs.Add(crater.pairId))
                continue;
            Crater mate = craters.Find(other => other != crater &&
                other.pairId == crater.pairId);
            if (mate != null)
                StampDoublet(heights, outputHeight, crater, mate);
            else
                StampCrater(heights, outputHeight, crater);
        }
        return heights;
    }

    private static void StampCrater(float[,] heights, float outputHeight, Crater crater)
    {
        float pixelsPerMeter = (Resolution - 1f) / TerrainSize;
        int centerX = Mathf.RoundToInt(crater.x * pixelsPerMeter);
        int centerZ = Mathf.RoundToInt(crater.z * pixelsPerMeter);
        float radius = crater.diameter * 0.5f;
        int range = Mathf.CeilToInt(radius * 1.65f * pixelsPerMeter);
        for (int z = Mathf.Max(0, centerZ - range);
             z <= Mathf.Min(Resolution - 1, centerZ + range); ++z)
        {
            for (int x = Mathf.Max(0, centerX - range);
                 x <= Mathf.Min(Resolution - 1, centerX + range); ++x)
            {
                float dx = (x - centerX) / pixelsPerMeter;
                float dz = (z - centerZ) / pixelsPerMeter;
                float depression, positive, normalizedRadius;
                EvaluateCraterParts(crater, dx, dz, out depression, out positive,
                    out normalizedRadius);
                if (normalizedRadius > 1.62f)
                    continue;
                heights[z, x] = Mathf.Clamp01(heights[z, x] +
                    (depression + positive) / outputHeight);
            }
        }
    }

    private static void StampDoublet(float[,] heights, float outputHeight,
        Crater first, Crater second)
    {
        float pixelsPerMeter = (Resolution - 1f) / TerrainSize;
        float support = Mathf.Max(first.diameter, second.diameter) * 0.5f * 1.8f;
        int minX = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(first.x, second.x) - support) *
            pixelsPerMeter));
        int maxX = Mathf.Min(Resolution - 1,
            Mathf.CeilToInt((Mathf.Max(first.x, second.x) + support) * pixelsPerMeter));
        int minZ = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(first.z, second.z) - support) *
            pixelsPerMeter));
        int maxZ = Mathf.Min(Resolution - 1,
            Mathf.CeilToInt((Mathf.Max(first.z, second.z) + support) * pixelsPerMeter));
        float blendDepth = Mathf.Min(first.diameter * first.depthRatio,
            second.diameter * second.depthRatio) * 0.12f;

        for (int z = minZ; z <= maxZ; ++z)
        {
            float sampleZ = z / pixelsPerMeter;
            for (int x = minX; x <= maxX; ++x)
            {
                float sampleX = x / pixelsPerMeter;
                float depressionA, positiveA, radiusA;
                float depressionB, positiveB, radiusB;
                EvaluateCraterParts(first, sampleX - first.x, sampleZ - first.z,
                    out depressionA, out positiveA, out radiusA);
                EvaluateCraterParts(second, sampleX - second.x, sampleZ - second.z,
                    out depressionB, out positiveB, out radiusB);
                if (radiusA > 1.62f && radiusB > 1.62f)
                    continue;

                float depression = Mathf.Min(depressionA, depressionB);
                if (depressionA < 0f && depressionB < 0f)
                    depression = SmoothMinimum(depressionA, depressionB, blendDepth);
                float sharedInterior = Mathf.Clamp01((1.14f - radiusA) / 0.30f) *
                    Mathf.Clamp01((1.14f - radiusB) / 0.30f);
                float positive = Mathf.Max(positiveA, positiveB) *
                    Mathf.Lerp(1f, 0.38f, sharedInterior);
                heights[z, x] = Mathf.Clamp01(heights[z, x] +
                    (depression + positive) / outputHeight);
            }
        }
    }

    private static void EvaluateCraterParts(Crater crater, float dx, float dz,
        out float depression, out float positive, out float normalizedRadius)
    {
        float radius = crater.diameter * 0.5f;
        float depth = crater.diameter * crater.depthRatio;
        float rimHeight = crater.diameter * crater.rimRatio;
        float cs = Mathf.Cos(crater.angle);
        float sn = Mathf.Sin(crater.angle);
        float ex = cs * dx - sn * dz;
        float ez = sn * dx + cs * dz;
        float theta = Mathf.Atan2(ez, ex);
        float irregularityAmplitude = Mathf.Lerp(0.05f, 0.13f, crater.degradation);
        float warpedRadius = radius * (1f + irregularityAmplitude *
            (0.55f * Mathf.Sin(theta * 2f + crater.phase * 0.37f) +
             0.72f * Mathf.Sin(theta * 3f + crater.phase) +
             0.30f * Mathf.Sin(theta * 5f - crater.phase * 0.63f)));
        normalizedRadius = Mathf.Sqrt(ex * ex +
            ez * ez / (crater.axisRatio * crater.axisRatio)) / warpedRadius;
        depression = 0f;
        positive = 0f;
        if (normalizedRadius > 1.62f)
            return;

        if (normalizedRadius < 1f)
        {
            float bowl = Mathf.Clamp01(1f - normalizedRadius * normalizedRadius);
            float floorFade = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(0.18f, 0.72f, normalizedRadius));
            float floorNoise = 0.90f + 0.10f * floorFade *
                Mathf.Sin(theta * 5f + crater.phase);
            depression = -depth * bowl * bowl * floorNoise;
        }

        float rimWidth = Mathf.Lerp(0.10f, 0.21f, crater.degradation);
        float rimDistance = (normalizedRadius - 1f) / rimWidth;
        float rimContinuity = Mathf.Lerp(0.94f, 0.45f, crater.degradation);
        float brokenRim = rimContinuity + (1f - rimContinuity) *
            Mathf.Clamp01(0.52f + 0.48f * Mathf.Sin(theta * 2f + crater.phase));
        positive += rimHeight * brokenRim * Mathf.Exp(-rimDistance * rimDistance);
        if (normalizedRadius > 1f)
        {
            float ejecta = rimHeight * Mathf.Lerp(0.11f, 0.025f,
                crater.degradation) * Mathf.Exp(-(normalizedRadius - 1f) *
                    Mathf.Lerp(4f, 6.5f, crater.degradation));
            positive += ejecta *
                (0.82f + 0.18f * Mathf.Sin(theta * 4f + crater.phase));
        }
    }

    private static float SmoothMinimum(float a, float b, float amount)
    {
        if (amount <= 0.0001f)
            return Mathf.Min(a, b);
        float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / amount);
        return Mathf.Lerp(b, a, h) - amount * h * (1f - h);
    }

    private static TerrainData BuildTerrainAsset(TerrainData source, float[,] heights,
        float outputHeight)
    {
        TerrainData terrainData = AssetDatabase.LoadAssetAtPath<TerrainData>(OutputTerrainPath);
        if (terrainData == null)
        {
            terrainData = new TerrainData();
            AssetDatabase.CreateAsset(terrainData, OutputTerrainPath);
        }
        terrainData.heightmapResolution = Resolution;
        terrainData.size = new Vector3(TerrainSize, outputHeight, TerrainSize);
        terrainData.name = "Apollo15_Highlands_Deep_300m_513";
        terrainData.SetHeights(0, 0, heights);
        terrainData.terrainLayers = source.terrainLayers;
        EditorUtility.SetDirty(terrainData);
        return terrainData;
    }

    private static Mesh BuildMeshAsset(float[,] heights, float outputHeight)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(OutputMeshPath);
        if (mesh == null)
        {
            mesh = new Mesh();
            AssetDatabase.CreateAsset(mesh, OutputMeshPath);
        }
        int vertexCount = Resolution * Resolution;
        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uv = new Vector2[vertexCount];
        int[] triangles = new int[(Resolution - 1) * (Resolution - 1) * 6];
        float step = TerrainSize / (Resolution - 1f);
        for (int z = 0; z < Resolution; ++z)
        {
            for (int x = 0; x < Resolution; ++x)
            {
                int index = z * Resolution + x;
                vertices[index] = new Vector3(-TerrainSize * 0.5f + x * step,
                    heights[z, x] * outputHeight + 0.001f,
                    -TerrainSize * 0.5f + z * step);
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
        mesh.name = "Apollo15_Highlands_Deep_Visual_513";
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

    private static void BuildScene(TerrainData terrainData, Mesh mesh, float outputHeight,
        bool openAfterBuild)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(OutputScenePath) == null &&
            !AssetDatabase.CopyAsset(BaseScenePath, OutputScenePath))
            throw new InvalidOperationException("Could not copy the Apollo 15 source scene.");

        Scene activeBefore = SceneManager.GetActiveScene();
        bool mayOpen = openAfterBuild && activeBefore.IsValid() && !activeBefore.isDirty;
        Scene output = SceneManager.GetSceneByPath(OutputScenePath);
        bool wasAlreadyLoaded = output.IsValid() && output.isLoaded;
        if (wasAlreadyLoaded && output.isDirty)
            throw new InvalidOperationException(
                "The generated Apollo 15 avoidance scene has unsaved changes. Save or discard " +
                "them before rebuilding the crater geometry.");
        if (!wasAlreadyLoaded)
            output = EditorSceneManager.OpenScene(OutputScenePath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(output);
        try
        {
            GameObject groundObject = Find(output, "Apollo15_MuJoCo_Ground") ??
                Find(output, "Apollo15_Highlands_Deep_MuJoCo_Ground");
            GameObject visualObject = Find(output, "Generated_Visible_Surface") ??
                Find(output, "Apollo15_Highlands_Deep_Visible_Surface");
            if (groundObject == null || visualObject == null)
                throw new InvalidOperationException("Apollo 15 central terrain objects were not found.");

            groundObject.name = "Apollo15_Highlands_Deep_MuJoCo_Ground";
            Terrain terrain = groundObject.GetComponentInChildren<Terrain>(true);
            MjGeom geom = groundObject.GetComponent<MjGeom>();
            if (terrain == null || geom == null)
                throw new InvalidOperationException("Central Terrain or MuJoCo HField is missing.");
            terrain.terrainData = terrainData;
            terrain.drawHeightmap = false;
            terrain.heightmapPixelError = 2f;
            geom.HField.Terrain = terrain;
            Vector3 groundPosition = groundObject.transform.position;
            groundObject.transform.position = new Vector3(groundPosition.x, -PaddingBelow,
                groundPosition.z);

            visualObject.name = "Apollo15_Highlands_Deep_Visible_Surface";
            MeshFilter filter = visualObject.GetComponent<MeshFilter>();
            if (filter == null)
                throw new InvalidOperationException("Central visible surface MeshFilter is missing.");
            filter.sharedMesh = mesh;
            Vector3 visualPosition = visualObject.transform.position;
            visualObject.transform.position = new Vector3(visualPosition.x, -PaddingBelow,
                visualPosition.z);

            GameObject configuration = Find(output, "Apollo15_Highlands_Deep_Configuration");
            if (configuration == null)
                configuration = new GameObject("Apollo15_Highlands_Deep_Configuration");

            GameObject sunObject = Find(output, "Sun");
            if (sunObject != null)
            {
                sunObject.transform.rotation = Quaternion.Euler(18f, -38f, 0f);
                Light sun = sunObject.GetComponent<Light>();
                if (sun != null)
                {
                    sun.intensity = 1.65f;
                    sun.shadows = LightShadows.Hard;
                    sun.shadowStrength = 1f;
                    sun.shadowBias = 0.015f;
                    sun.shadowNormalBias = 0.12f;
                    RenderSettings.sun = sun;
                }
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.006f, 0.007f, 0.009f, 1f);

            GameObject cameraObject = Find(output, "Apollo15_Highlands_Deep_Orbital_Camera");
            if (cameraObject == null)
            {
                cameraObject = new GameObject("Apollo15_Highlands_Deep_Orbital_Camera");
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = 154f;
                camera.nearClipPlane = 0.3f;
                camera.farClipPlane = 600f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                cameraObject.transform.position = new Vector3(0f, 260f, 0f);
                cameraObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }

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
                GameObject visual = Find(scene, "Apollo15_Highlands_Deep_Visible_Surface");
                if (visual != null)
                    Selection.activeGameObject = visual;
            };
        }
        else if (openAfterBuild)
        {
            Debug.LogWarning("[Apollo15HighlandsDeep] Output was built but not opened because the " +
                "current scene has unsaved changes.");
        }
    }

    private static void WriteReport(List<Crater> craters, float[,] heights, float outputHeight)
    {
        var records = new List<CraterRecord>();
        int avoidanceCount = 0;
        foreach (Crater crater in craters)
        {
            if (crater.role.StartsWith("avoidance", StringComparison.Ordinal))
                ++avoidanceCount;
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
                rimHeightMeters = crater.diameter * crater.rimRatio,
                axisRatio = crater.axisRatio,
                pairId = crater.pairId
            });
        }
        var report = new BuildReport
        {
            outputScene = OutputScenePath,
            preservedSourceScene = BaseScenePath,
            baseTerrain = "NAC_DTM_APOLLO15 v1.9, 151x151 native samples at 2 m, " +
                "existing project import resampled to 513 for this scene",
            randomSeed = Seed,
            terrainResolution = Resolution,
            gridSpacingMeters = TerrainSize / (Resolution - 1f),
            totalSyntheticCraters = craters.Count,
            avoidanceCraters = avoidanceCount,
            backgroundCraters = craters.Count - avoidanceCount,
            measuredMaximumSlopeDegrees = MeasureMaximumSlope(heights, outputHeight),
            observedBasis = "Apollo 15 local survey: common rounded subdued low-rim craters; " +
                "2024 catalogue scaling suggests about nine craters >=14 m per 0.09 km2; " +
                "Minton et al. equilibrium fit supports a steep size-frequency distribution.",
            modelingChoices = "Obstacle-training distribution: 19 task-relevant craters " +
                "(10 degraded 8-14 m, 5 moderate 14-22 m, 3 deliberately deep fresh " +
                "22-32 m craters, and one 44 m degraded edge crater) plus 22 shallow " +
                "3.5-8 m background or paired craters. The population contains one moderate " +
                "overlapping pair and one smaller degraded doublet; their shared depression " +
                "uses smooth-min blending and their facing rims are suppressed to form a saddle. " +
                "Exactly nine generated craters are " +
                ">=14 m. The deep fresh d/D range 0.14-0.17 is a training choice, not a " +
                "measured local Apollo 15 proportion. A curved corridor with 5.5 m center " +
                "clearance is protected from major craters.",
            evidenceBoundary = "The underlying macro relief is observed Apollo 15 DTM. " +
                "The scene name uses Highlands as a project label; the 300 m source crop is " +
                "the project's NAC_DTM_APOLLO15 landing-region terrain, not a separately " +
                "validated highland-only crop. Every added crater position and morphology is " +
                "deterministic synthetic geometry, not a reconstruction of crater locations " +
                "visible in the supplied LROC image.",
            craters = records.ToArray()
        };
        File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
    }

    private static float MeasureMaximumSlope(float[,] heights, float outputHeight)
    {
        float spacing = TerrainSize / (Resolution - 1f);
        float maximum = 0f;
        for (int z = 1; z < Resolution - 1; ++z)
        {
            for (int x = 1; x < Resolution - 1; ++x)
            {
                float dx = (heights[z, x + 1] - heights[z, x - 1]) * outputHeight /
                    (2f * spacing);
                float dz = (heights[z + 1, x] - heights[z - 1, x]) * outputHeight /
                    (2f * spacing);
                float slope = Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
                maximum = Mathf.Max(maximum, slope);
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
