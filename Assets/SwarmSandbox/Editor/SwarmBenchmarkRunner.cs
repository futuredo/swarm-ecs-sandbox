using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using SwarmECS.Simulation;
using SwarmECS.Simulation.Systems;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SwarmECS.Editor
{
    public static class SwarmBenchmarkRunner
    {
        private const uint FormationSeed = 0x5EED1234u;

        private static readonly SpatialIndexMode[] SpatialIndexMatrixModes =
        {
            SpatialIndexMode.UniformGrid,
            SpatialIndexMode.KdTree,
            SpatialIndexMode.KdTreeKNearest,
        };

        [Serializable]
        private sealed class BenchmarkReport
        {
            public string unityVersion;
            public string timestampUtc;
            public string processor;
            public int processorCount;
            public string graphicsDevice;
            public int agents;
            public int warmupTicks;
            public int sampleTicks;
            public double averageMilliseconds;
            public double p95Milliseconds;
            public double minMilliseconds;
            public double maxMilliseconds;
            public long managedBytesAcrossSamples;
            public string spatialIndex;
            public string avoidanceExecution;
            public int backgroundWorkersUsed;
            public int pathRequestBudgetPerTick;
            public int pathCacheCapacity;
            public int navigationIslandCount;
            public int sharedWaypointCount;
            public string stateHash;
            public string canonicalSpatialComparisonHash;
        }

        [Serializable]
        private sealed class BenchmarkMatrixReport
        {
            public string unityVersion;
            public string timestampUtc;
            public string processor;
            public int processorCount;
            public string graphicsDevice;
            public int agents;
            public int warmupTicks;
            public int sampleTicks;
            public string formationSeed;
            public int fixedDeltaTimeRaw;
            public int neighborDistanceRaw;
            public int maxNeighbors;
            public string[] runOrder;
            public BenchmarkReport[] results;
        }

        [MenuItem("Swarm ECS/Run 10k Headless Benchmark")]
        public static void RunFromMenu()
        {
            BenchmarkReport report = ExecuteBenchmark(
                10_000,
                8,
                24,
                SpatialIndexMode.UniformGrid);
            WriteSingleReport(report);
            LogCompletedReport(report);
        }

        [MenuItem("Swarm ECS/Run 10k Spatial Index Matrix")]
        public static void RunSpatialIndexMatrixFromMenu()
        {
            RunSpatialIndexMatrix(10_000, 8, 24);
        }

        public static void RunFromCommandLine()
        {
            int agents = ReadIntEnvironment("SWARM_AGENT_COUNT", 10_000);
            int warmup = ReadIntEnvironment("SWARM_WARMUP_TICKS", 8);
            int samples = ReadIntEnvironment("SWARM_SAMPLE_TICKS", 24);
            SpatialIndexMode mode = ReadSpatialIndexMode();

            BenchmarkReport report = ExecuteBenchmark(agents, warmup, samples, mode);
            WriteSingleReport(report);
            LogCompletedReport(report);
        }

        public static void RunSpatialIndexMatrixFromCommandLine()
        {
            int agents = ReadIntEnvironment("SWARM_AGENT_COUNT", 10_000);
            int warmup = ReadIntEnvironment("SWARM_WARMUP_TICKS", 8);
            int samples = ReadIntEnvironment("SWARM_SAMPLE_TICKS", 24);
            RunSpatialIndexMatrix(agents, warmup, samples);
        }

        private static void RunSpatialIndexMatrix(int agents, int warmupTicks, int sampleTicks)
        {
            BenchmarkReport[] reports = new BenchmarkReport[SpatialIndexMatrixModes.Length];
            for (int i = 0; i < SpatialIndexMatrixModes.Length; i++)
            {
                reports[i] = ExecuteBenchmark(
                    agents,
                    warmupTicks,
                    sampleTicks,
                    SpatialIndexMatrixModes[i]);
                LogCompletedReport(reports[i]);
            }

            BenchmarkMatrixReport matrix = BuildMatrixReport(
                agents,
                warmupTicks,
                sampleTicks,
                reports);
            WriteMatrixReport(matrix);
            Debug.Log(
                $"[SwarmECS] Spatial index matrix complete: {agents:N0} agents, " +
                $"{reports.Length} modes, {sampleTicks} samples per mode");
        }

        private static BenchmarkReport ExecuteBenchmark(
            int agents,
            int warmupTicks,
            int sampleTicks,
            SpatialIndexMode mode)
        {
            SwarmConfig config = CreateBenchmarkConfig(agents, mode);
            SwarmWorld world = new(config);
            world.InitializeDeterministicFormation(agents, FormationSeed);
            SwarmSimulation simulation = new(world);
            try
            {
                for (int i = 0; i < warmupTicks; i++)
                {
                    simulation.Step(world);
                    world.AdvanceTick();
                }

                GC.Collect();
                double[] samples = new double[sampleTicks];
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < sampleTicks; i++)
                {
                    long start = Stopwatch.GetTimestamp();
                    simulation.Step(world);
                    world.AdvanceTick();
                    samples[i] = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
                }

                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                Array.Sort(samples);
                double sum = 0d;
                for (int i = 0; i < samples.Length; i++)
                {
                    sum += samples[i];
                }

                int p95Index = Mathf.Clamp(
                    Mathf.CeilToInt(samples.Length * 0.95f) - 1,
                    0,
                    samples.Length - 1);
                int backgroundWorkersUsed =
                    mode == SpatialIndexMode.UniformGrid
                        ? simulation.Avoidance.BackgroundWorkerCount
                        : 0;
                return new BenchmarkReport
                {
                    unityVersion = Application.unityVersion,
                    timestampUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    processor = SystemInfo.processorType,
                    processorCount = SystemInfo.processorCount,
                    graphicsDevice = SystemInfo.graphicsDeviceName,
                    agents = agents,
                    warmupTicks = warmupTicks,
                    sampleTicks = sampleTicks,
                    averageMilliseconds = sum / samples.Length,
                    p95Milliseconds = samples[p95Index],
                    minMilliseconds = samples[0],
                    maxMilliseconds = samples[samples.Length - 1],
                    managedBytesAcrossSamples = allocated,
                    spatialIndex = GetSpatialIndexLabel(world.SpatialIndexMode),
                    avoidanceExecution = GetAvoidanceExecutionLabel(backgroundWorkersUsed),
                    backgroundWorkersUsed = backgroundWorkersUsed,
                    pathRequestBudgetPerTick = simulation.Navigation.MaxPathRequestsPerTick,
                    pathCacheCapacity = simulation.Navigation.PathCacheCapacity,
                    navigationIslandCount = simulation.Navigation.Islands.RegionCount,
                    sharedWaypointCount = simulation.Navigation.TotalSharedWaypoints,
                    stateHash = "0x" + world.ComputeStateHash().ToString("X16"),
                    canonicalSpatialComparisonHash =
                        "0x" + ComputeCanonicalSpatialComparisonHash(world).ToString("X16"),
                };
            }
            finally
            {
                simulation.Dispose();
            }
        }

        private static SwarmConfig CreateBenchmarkConfig(int agents, SpatialIndexMode mode)
        {
            SwarmConfig baseline = SwarmConfig.PortfolioDefault(agents);
            return new SwarmConfig(
                baseline.Capacity,
                baseline.FixedDeltaTime,
                baseline.AgentRadius,
                baseline.MaxSpeed,
                baseline.NeighborDistance,
                baseline.MaxNeighbors,
                baseline.TimeHorizon,
                baseline.WorldHalfExtent,
                mode);
        }

        private static BenchmarkMatrixReport BuildMatrixReport(
            int agents,
            int warmupTicks,
            int sampleTicks,
            BenchmarkReport[] reports)
        {
            SwarmConfig baseline = SwarmConfig.PortfolioDefault(agents);
            string[] runOrder = new string[SpatialIndexMatrixModes.Length];
            for (int i = 0; i < SpatialIndexMatrixModes.Length; i++)
            {
                runOrder[i] = GetSpatialIndexLabel(SpatialIndexMatrixModes[i]);
            }

            return new BenchmarkMatrixReport
            {
                unityVersion = Application.unityVersion,
                timestampUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                processor = SystemInfo.processorType,
                processorCount = SystemInfo.processorCount,
                graphicsDevice = SystemInfo.graphicsDeviceName,
                agents = agents,
                warmupTicks = warmupTicks,
                sampleTicks = sampleTicks,
                formationSeed = "0x" + FormationSeed.ToString("X8"),
                fixedDeltaTimeRaw = baseline.FixedDeltaTime.Raw,
                neighborDistanceRaw = baseline.NeighborDistance.Raw,
                maxNeighbors = baseline.MaxNeighbors,
                runOrder = runOrder,
                results = reports,
            };
        }

        private static void WriteSingleReport(BenchmarkReport report)
        {
            string outputDirectory = GetOutputDirectory();
            File.WriteAllText(
                Path.Combine(outputDirectory, "latest.json"),
                JsonUtility.ToJson(report, true));
            File.WriteAllText(
                Path.Combine(outputDirectory, "latest.md"),
                BuildMarkdown(report));
        }

        private static void WriteMatrixReport(BenchmarkMatrixReport report)
        {
            string outputDirectory = GetOutputDirectory();
            File.WriteAllText(
                Path.Combine(outputDirectory, "spatial-index-matrix.json"),
                JsonUtility.ToJson(report, true));
            File.WriteAllText(
                Path.Combine(outputDirectory, "spatial-index-matrix.md"),
                BuildMatrixMarkdown(report));
        }

        private static string GetOutputDirectory()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string configuredPath = Environment.GetEnvironmentVariable("SWARM_BENCHMARK_OUTPUT_DIR");
            string outputDirectory = string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(projectRoot, "BenchmarkResults")
                : Path.GetFullPath(
                    Path.IsPathRooted(configuredPath)
                        ? configuredPath
                        : Path.Combine(projectRoot, configuredPath));
            Directory.CreateDirectory(outputDirectory);
            return outputDirectory;
        }

        private static string BuildMarkdown(BenchmarkReport report)
        {
            return
                "# Swarm ECS benchmark\n\n" +
                "- UTC: " + report.timestampUtc + "\n" +
                "- Unity: " + report.unityVersion + "\n" +
                "- CPU: " + report.processor + " (" + report.processorCount + " logical cores)\n" +
                "- GPU: " + report.graphicsDevice + "\n" +
                "- Agents: " + report.agents.ToString("N0") + "\n" +
                "- Spatial index: " + report.spatialIndex + "\n" +
                "- Avoidance execution: " + report.avoidanceExecution + "\n" +
                "- Path requests/tick: " + report.pathRequestBudgetPerTick + "\n" +
                "- Path cache capacity: " + report.pathCacheCapacity + "\n" +
                "- Navigation islands/shared waypoints: " + report.navigationIslandCount + "/" + report.sharedWaypointCount + "\n" +
                "- Warmup/sample ticks: " + report.warmupTicks + "/" + report.sampleTicks + "\n" +
                "- Average: " + report.averageMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + " ms/tick\n" +
                "- P95: " + report.p95Milliseconds.ToString("F3", CultureInfo.InvariantCulture) + " ms/tick\n" +
                "- Min/max: " + report.minMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + "/" + report.maxMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + " ms\n" +
                "- Current-thread managed allocation across samples: " + report.managedBytesAcrossSamples + " B\n" +
                "- Final full state hash: `" + report.stateHash + "`\n" +
                "- Canonical spatial comparison hash: `" + report.canonicalSpatialComparisonHash + "`\n";
        }

        private static string BuildMatrixMarkdown(BenchmarkMatrixReport report)
        {
            string markdown =
                "# Swarm ECS spatial-index benchmark matrix\n\n" +
                "- UTC: " + report.timestampUtc + "\n" +
                "- Unity: " + report.unityVersion + "\n" +
                "- CPU: " + report.processor + " (" + report.processorCount + " logical cores)\n" +
                "- GPU: " + report.graphicsDevice + "\n" +
                "- Agents: " + report.agents.ToString("N0") + "\n" +
                "- Formation seed: `" + report.formationSeed + "`\n" +
                "- Fixed delta / neighbor distance (Q16.16 raw): " +
                report.fixedDeltaTimeRaw + "/" + report.neighborDistanceRaw + "\n" +
                "- Max neighbors: " + report.maxNeighbors + "\n" +
                "- Warmup/sample ticks per mode: " + report.warmupTicks + "/" + report.sampleTicks + "\n\n" +
                "| Spatial index | Avoidance execution | Average (ms) | P95 (ms) | Min (ms) | Max (ms) | Current-thread GC (B) | Full state hash | Canonical comparison hash |\n" +
                "|---|---|---:|---:|---:|---:|---:|---|---|\n";

            for (int i = 0; i < report.results.Length; i++)
            {
                BenchmarkReport result = report.results[i];
                markdown +=
                    "| " + result.spatialIndex +
                    " | " + result.avoidanceExecution +
                    " | " + result.averageMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
                    " | " + result.p95Milliseconds.ToString("F3", CultureInfo.InvariantCulture) +
                    " | " + result.minMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
                    " | " + result.maxMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
                    " | " + result.managedBytesAcrossSamples +
                    " | `" + result.stateHash + "`" +
                    " | `" + result.canonicalSpatialComparisonHash + "` |\n";
            }

            return markdown +
                "\nThis is an end-to-end logic-tick comparison of the current runtime modes, not an isolated spatial-query microbenchmark. " +
                "UniformGrid uses its persistent worker pool when available; both KD modes currently execute avoidance on the caller thread.\n\n" +
                "The full state hash includes the authoritative `SpatialIndexMode`, so hashes from different modes are expected to differ. " +
                "The canonical comparison hash temporarily normalizes only that field to `UniformGrid`, computes the hash, and restores the original mode. " +
                "It is evidence for algorithm-result equivalence: UniformGrid and KdTreeRadius should match, while exact KNN may legitimately differ because it has different neighbor-selection semantics.\n";
        }

        private static ulong ComputeCanonicalSpatialComparisonHash(SwarmWorld world)
        {
            SpatialIndexMode mode = world.SpatialIndexMode;
            try
            {
                world.SetSpatialIndexMode(SpatialIndexMode.UniformGrid);
                return world.ComputeStateHash();
            }
            finally
            {
                world.SetSpatialIndexMode(mode);
            }
        }

        private static void LogCompletedReport(BenchmarkReport report)
        {
            Debug.Log(
                $"[SwarmECS] Benchmark complete: {report.spatialIndex}, " +
                $"{report.agents:N0} agents, avg {report.averageMilliseconds:F3} ms, " +
                $"p95 {report.p95Milliseconds:F3} ms, " +
                $"current-thread GC {report.managedBytesAcrossSamples} B");
        }

        private static SpatialIndexMode ReadSpatialIndexMode()
        {
            string commandLineValue = ReadCommandLineValue(
                "-swarmSpatialIndex",
                "--swarm-spatial-index");
            string requested = string.IsNullOrWhiteSpace(commandLineValue)
                ? Environment.GetEnvironmentVariable("SWARM_SPATIAL_INDEX")
                : commandLineValue;

            if (string.IsNullOrWhiteSpace(requested))
            {
                return SpatialIndexMode.UniformGrid;
            }

            if (TryParseSpatialIndexMode(requested, out SpatialIndexMode mode))
            {
                return mode;
            }

            throw new ArgumentException(
                $"Unsupported spatial index '{requested}'. " +
                "Expected UniformGrid, KdTreeRadius, or KdTreeKNearest.");
        }

        private static bool TryParseSpatialIndexMode(string value, out SpatialIndexMode mode)
        {
            string normalized = value
                .Trim()
                .Replace("-", string.Empty)
                .Replace("_", string.Empty)
                .Replace(" ", string.Empty)
                .ToLowerInvariant();
            switch (normalized)
            {
                case "uniformgrid":
                case "grid":
                    mode = SpatialIndexMode.UniformGrid;
                    return true;
                case "kdtree":
                case "kdtreeradius":
                case "kdradius":
                    mode = SpatialIndexMode.KdTree;
                    return true;
                case "kdtreeknearest":
                case "kdknn":
                case "knn":
                    mode = SpatialIndexMode.KdTreeKNearest;
                    return true;
                default:
                    mode = default;
                    return false;
            }
        }

        private static string GetSpatialIndexLabel(SpatialIndexMode mode)
        {
            return mode switch
            {
                SpatialIndexMode.UniformGrid => "UniformGrid",
                SpatialIndexMode.KdTree => "KdTreeRadius",
                SpatialIndexMode.KdTreeKNearest => "KdTreeKNearest",
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
        }

        private static string GetAvoidanceExecutionLabel(int backgroundWorkersUsed)
        {
            return backgroundWorkersUsed > 0
                ? $"Caller + {backgroundWorkersUsed} background workers"
                : "Caller thread";
        }

        private static string ReadCommandLineValue(params string[] keys)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int argumentIndex = 0; argumentIndex < arguments.Length; argumentIndex++)
            {
                string argument = arguments[argumentIndex];
                for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                {
                    string key = keys[keyIndex];
                    if (string.Equals(argument, key, StringComparison.OrdinalIgnoreCase))
                    {
                        return argumentIndex + 1 < arguments.Length
                            ? arguments[argumentIndex + 1]
                            : string.Empty;
                    }

                    string prefix = key + "=";
                    if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return argument.Substring(prefix.Length);
                    }
                }
            }

            return null;
        }

        private static int ReadIntEnvironment(string key, int fallback)
        {
            string value = Environment.GetEnvironmentVariable(key);
            return int.TryParse(value, out int parsed) && parsed > 0 ? parsed : fallback;
        }
    }
}
