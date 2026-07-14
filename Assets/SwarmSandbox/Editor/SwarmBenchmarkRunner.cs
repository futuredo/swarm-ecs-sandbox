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
            public string stateHash;
        }

        [MenuItem("Swarm ECS/Run 10k Headless Benchmark")]
        public static void RunFromMenu()
        {
            Run(10_000, 8, 24);
        }

        public static void RunFromCommandLine()
        {
            int agents = ReadIntEnvironment("SWARM_AGENT_COUNT", 10_000);
            int warmup = ReadIntEnvironment("SWARM_WARMUP_TICKS", 8);
            int samples = ReadIntEnvironment("SWARM_SAMPLE_TICKS", 24);
            Run(agents, warmup, samples);
        }

        private static void Run(int agents, int warmupTicks, int sampleTicks)
        {
            SwarmConfig config = SwarmConfig.PortfolioDefault(agents);
            SwarmWorld world = new(config);
            world.InitializeDeterministicFormation(agents, 0x5EED1234u);
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

                int p95Index = Mathf.Clamp(Mathf.CeilToInt(samples.Length * 0.95f) - 1, 0, samples.Length - 1);
                BenchmarkReport report = new()
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
                    spatialIndex = simulation.Avoidance.Mode.ToString(),
                    stateHash = "0x" + world.ComputeStateHash().ToString("X16"),
                };

                string outputDirectory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "BenchmarkResults");
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllText(Path.Combine(outputDirectory, "latest.json"), JsonUtility.ToJson(report, true));
                File.WriteAllText(Path.Combine(outputDirectory, "latest.md"), BuildMarkdown(report));
                Debug.Log($"[SwarmECS] Benchmark complete: {report.agents:N0} agents, avg {report.averageMilliseconds:F3} ms, p95 {report.p95Milliseconds:F3} ms, GC {report.managedBytesAcrossSamples} B");
            }
            finally
            {
                simulation.Dispose();
            }
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
                "- Warmup/sample ticks: " + report.warmupTicks + "/" + report.sampleTicks + "\n" +
                "- Average: " + report.averageMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + " ms/tick\n" +
                "- P95: " + report.p95Milliseconds.ToString("F3", CultureInfo.InvariantCulture) + " ms/tick\n" +
                "- Min/max: " + report.minMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + "/" + report.maxMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + " ms\n" +
                "- Managed allocation across samples: " + report.managedBytesAcrossSamples + " B\n" +
                "- Final deterministic hash: `" + report.stateHash + "`\n";
        }

        private static int ReadIntEnvironment(string key, int fallback)
        {
            string value = Environment.GetEnvironmentVariable(key);
            return int.TryParse(value, out int parsed) && parsed > 0 ? parsed : fallback;
        }
    }
}
