using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation;
using SwarmECS.Simulation.Determinism;
using SwarmECS.Simulation.Netcode;
using SwarmECS.Simulation.Replay;
using SwarmECS.Simulation.Systems;
using UnityEngine;
using DiagnosticsProcess = System.Diagnostics.Process;

namespace SwarmECS.Editor
{
    /// <summary>
    /// Generates and verifies a deterministic replay in separate Unity batchmode
    /// processes. The tracked reports are intended to be reproducible evidence,
    /// not a replacement for cross-backend and cross-architecture validation.
    /// </summary>
    public static class SwarmReplayProcessRunner
    {
        private const string ReplayFileName = "cross-process.swarmreplay";
        private const string CaptureFileName = "capture.json";
        private const string VerifyFileName = "verify.json";
        private const string MarkdownFileName = "latest.md";
        private const int AgentCount = 256;
        private const int FinalTick = 120;
        private const int CheckpointInterval = 30;
        private const uint FormationSeed = 0xC0DEC0DEu;

        [Serializable]
        private sealed class LayerHashReport
        {
            public string config;
            public string worldMetadata;
            public string groupTargets;
            public string groupPathStates;
            public string navigationRequestSequence;
            public string agentPositions;
            public string agentVelocities;
            public string agentPathCursors;
            public string full;
        }

        [Serializable]
        private sealed class CaptureReport
        {
            public string unityVersion;
            public string timestampUtc;
            public int processId;
            public int replaySchemaVersion;
            public string logicHash;
            public string configHash;
            public string replayFile;
            public string replaySha256;
            public int replayBytes;
            public string formationSeed;
            public int agents;
            public int commands;
            public int checkpoints;
            public int finalTick;
            public LayerHashReport finalAuthorityHashes;
        }

        [Serializable]
        private sealed class DesyncProbeReport
        {
            public string component;
            public int entityIndex;
            public string field;
            public string rawKind;
            public string expectedRaw;
            public string actualRaw;
        }

        [Serializable]
        private sealed class VerifyReport
        {
            public string unityVersion;
            public string timestampUtc;
            public int captureProcessId;
            public int verifyProcessId;
            public bool independentProcess;
            public bool crcAndSchemaValidated;
            public bool allCheckpointsMatched;
            public int matchedCheckpoints;
            public string replaySha256;
            public string logicHash;
            public string configHash;
            public int finalTick;
            public LayerHashReport finalAuthorityHashes;
            public DesyncProbeReport desyncProbe;
        }

        public static void CaptureFromCommandLine()
        {
            string outputDirectory = GetOutputDirectory();
            string replayPath = Path.Combine(outputDirectory, ReplayFileName);
            SwarmConfig config = SwarmConfig.DemoDefault(AgentCount);
            SimulationCommand[] commands = BuildCommands();
            SwarmReplayCheckpoint[] checkpoints = CaptureCheckpoints(
                config,
                commands,
                out WorldAuthorityHashes finalHashes);
            var replay = new SwarmReplay(
                SimulationBuildIdentity.CurrentLogicHash,
                config,
                FormationSeed,
                AgentCount,
                commands,
                checkpoints);

            byte[] bytes = SwarmReplaySerializer.Serialize(replay);
            File.WriteAllBytes(replayPath, bytes);

            var report = new CaptureReport
            {
                unityVersion = Application.unityVersion,
                timestampUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                processId = DiagnosticsProcess.GetCurrentProcess().Id,
                replaySchemaVersion = SwarmReplaySerializer.CurrentSchemaVersion,
                logicHash = FormatHash(replay.LogicHash),
                configHash = FormatHash(config.ConfigHash),
                replayFile = ReplayFileName,
                replaySha256 = ComputeSha256(bytes),
                replayBytes = bytes.Length,
                formationSeed = "0x" + FormationSeed.ToString("X8", CultureInfo.InvariantCulture),
                agents = AgentCount,
                commands = commands.Length,
                checkpoints = checkpoints.Length,
                finalTick = FinalTick,
                finalAuthorityHashes = BuildLayerHashReport(finalHashes),
            };
            WriteJson(Path.Combine(outputDirectory, CaptureFileName), report);
            Debug.Log(
                "[SwarmECS] Replay capture complete: " + replayPath +
                ", final authority hash " + report.finalAuthorityHashes.full);
        }

        public static void VerifyFromCommandLine()
        {
            string outputDirectory = GetOutputDirectory();
            string replayPath = Path.Combine(outputDirectory, ReplayFileName);
            string capturePath = Path.Combine(outputDirectory, CaptureFileName);
            if (!File.Exists(replayPath) || !File.Exists(capturePath))
            {
                throw new FileNotFoundException(
                    "Replay verification requires capture.json and cross-process.swarmreplay.");
            }

            CaptureReport capture = JsonUtility.FromJson<CaptureReport>(File.ReadAllText(capturePath));
            if (capture == null)
            {
                throw new InvalidDataException("Replay capture report cannot be parsed.");
            }

            int verifyProcessId = DiagnosticsProcess.GetCurrentProcess().Id;
            if (capture.processId == verifyProcessId)
            {
                throw new InvalidOperationException(
                    "Replay capture and verification must run in separate processes.");
            }

            byte[] replayBytes = File.ReadAllBytes(replayPath);
            string replaySha256 = ComputeSha256(replayBytes);
            if (!string.Equals(replaySha256, capture.replaySha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Replay SHA-256 does not match the capture report.");
            }

            SwarmReplay replay = SwarmReplaySerializer.Deserialize(replayBytes);
            SwarmWorld verifiedWorld = PlayAndVerify(replay, out int matchedCheckpoints);
            WorldAuthorityHashes verifiedHashes = WorldAuthorityDiagnostics.ComputeHashes(verifiedWorld);
            if (!string.Equals(
                    FormatHash(verifiedHashes.Full),
                    capture.finalAuthorityHashes.full,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Final replay authority hash does not match the capture process.");
            }

            SwarmWorld diagnosticProbeWorld = PlayAndVerify(replay, out _);
            FPVector2 original = diagnosticProbeWorld.Positions[0];
            diagnosticProbeWorld.Positions[0] = new FPVector2(
                FP.FromRaw(original.X.Raw + 1),
                original.Y);
            if (!WorldAuthorityDiagnostics.TryFindFirstDifference(
                    verifiedWorld,
                    diagnosticProbeWorld,
                    out WorldDesyncDifference difference))
            {
                throw new InvalidOperationException("The deterministic desync probe produced no difference.");
            }

            var report = new VerifyReport
            {
                unityVersion = Application.unityVersion,
                timestampUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                captureProcessId = capture.processId,
                verifyProcessId = verifyProcessId,
                independentProcess = true,
                crcAndSchemaValidated = true,
                allCheckpointsMatched = matchedCheckpoints == replay.CheckpointCount,
                matchedCheckpoints = matchedCheckpoints,
                replaySha256 = replaySha256,
                logicHash = FormatHash(replay.LogicHash),
                configHash = FormatHash(replay.Config.ConfigHash),
                finalTick = verifiedWorld.Tick,
                finalAuthorityHashes = BuildLayerHashReport(verifiedHashes),
                desyncProbe = new DesyncProbeReport
                {
                    component = difference.ComponentName,
                    entityIndex = difference.EntityIndex,
                    field = difference.FieldName,
                    rawKind = difference.RawKind.ToString(),
                    expectedRaw = FormatRaw(difference.ExpectedRaw, difference.RawKind),
                    actualRaw = FormatRaw(difference.ActualRaw, difference.RawKind),
                },
            };

            WriteJson(Path.Combine(outputDirectory, VerifyFileName), report);
            File.WriteAllText(
                Path.Combine(outputDirectory, MarkdownFileName),
                BuildMarkdown(capture, report));
            Debug.Log(
                "[SwarmECS] Cross-process replay verified: " + matchedCheckpoints +
                "/" + replay.CheckpointCount + " checkpoints, final authority hash " +
                report.finalAuthorityHashes.full);
        }

        private static SimulationCommand[] BuildCommands()
        {
            return new[]
            {
                new SimulationCommand(
                    15,
                    0,
                    SimulationCommandType.SetGroupTarget,
                    0,
                    new FPVector2(FP.FromInt(34), FP.FromInt(51))),
                new SimulationCommand(
                    45,
                    1,
                    SimulationCommandType.SetSpatialIndexMode,
                    (byte)SpatialIndexMode.KdTree,
                    FPVector2.Zero),
                new SimulationCommand(
                    60,
                    2,
                    SimulationCommandType.SetGroupTarget,
                    3,
                    new FPVector2(FP.FromInt(-37), FP.FromInt(29))),
                new SimulationCommand(
                    90,
                    3,
                    SimulationCommandType.SetSpatialIndexMode,
                    (byte)SpatialIndexMode.UniformGrid,
                    FPVector2.Zero),
                new SimulationCommand(
                    105,
                    4,
                    SimulationCommandType.SetGroupTarget,
                    1,
                    new FPVector2(FP.FromInt(-42), FP.FromInt(38))),
            };
        }

        private static SwarmReplayCheckpoint[] CaptureCheckpoints(
            SwarmConfig config,
            SimulationCommand[] commands,
            out WorldAuthorityHashes finalHashes)
        {
            int checkpointCount = (FinalTick / CheckpointInterval) + 1;
            var checkpoints = new SwarmReplayCheckpoint[checkpointCount];
            SwarmWorld world = CreateWorld(config);
            CommandTimeline timeline = BuildTimeline(commands);
            using (var simulation = new SwarmSimulation(world))
            {
                int checkpointIndex = 0;
                while (true)
                {
                    if (world.Tick % CheckpointInterval == 0)
                    {
                        WorldAuthorityHashes hashes = WorldAuthorityDiagnostics.ComputeHashes(world);
                        checkpoints[checkpointIndex++] = new SwarmReplayCheckpoint(world.Tick, hashes.Full);
                    }

                    if (world.Tick == FinalTick)
                    {
                        break;
                    }

                    Step(world, simulation, timeline);
                }

                if (checkpointIndex != checkpoints.Length)
                {
                    throw new InvalidOperationException("Replay checkpoint capture count is inconsistent.");
                }
            }

            finalHashes = WorldAuthorityDiagnostics.ComputeHashes(world);
            return checkpoints;
        }

        private static SwarmWorld PlayAndVerify(SwarmReplay replay, out int matchedCheckpoints)
        {
            if (replay.CheckpointCount == 0)
            {
                throw new InvalidDataException("Replay verification requires at least one checkpoint.");
            }

            int finalTick = replay.GetCheckpoint(replay.CheckpointCount - 1).Tick;
            for (int commandIndex = 0; commandIndex < replay.CommandCount; commandIndex++)
            {
                if (replay.GetCommand(commandIndex).Tick >= finalTick)
                {
                    throw new InvalidDataException(
                        "Replay commands must execute before the final checkpoint.");
                }
            }

            SwarmWorld world = CreateWorld(replay.Config, replay.AgentCount, replay.Seed);
            CommandTimeline timeline = BuildTimeline(replay.CopyCommands());
            matchedCheckpoints = 0;
            using (var simulation = new SwarmSimulation(world))
            {
                while (true)
                {
                    if (matchedCheckpoints < replay.CheckpointCount &&
                        replay.GetCheckpoint(matchedCheckpoints).Tick == world.Tick)
                    {
                        SwarmReplayCheckpoint expected = replay.GetCheckpoint(matchedCheckpoints);
                        ulong actual = WorldAuthorityDiagnostics.ComputeHashes(world).Full;
                        if (actual != expected.StateHash)
                        {
                            throw new InvalidDataException(
                                "Replay checkpoint mismatch at tick " + world.Tick +
                                ": expected " + FormatHash(expected.StateHash) +
                                ", actual " + FormatHash(actual) + ".");
                        }

                        matchedCheckpoints++;
                    }

                    if (world.Tick == finalTick)
                    {
                        break;
                    }

                    Step(world, simulation, timeline);
                }
            }

            if (matchedCheckpoints != replay.CheckpointCount)
            {
                throw new InvalidDataException("Replay contains an unreachable checkpoint tick.");
            }

            return world;
        }

        private static SwarmWorld CreateWorld(SwarmConfig config)
        {
            return CreateWorld(config, AgentCount, FormationSeed);
        }

        private static SwarmWorld CreateWorld(SwarmConfig config, int agentCount, uint seed)
        {
            var world = new SwarmWorld(config);
            world.InitializeDeterministicFormation(agentCount, seed);
            return world;
        }

        private static CommandTimeline BuildTimeline(SimulationCommand[] commands)
        {
            var timeline = new CommandTimeline(commands.Length + 1);
            for (int i = 0; i < commands.Length; i++)
            {
                if (!timeline.AppendOrdered(commands[i]))
                {
                    throw new InvalidDataException(
                        "Replay command timeline is not a strictly ordered canonical stream.");
                }
            }

            return timeline;
        }

        private static void Step(
            SwarmWorld world,
            SwarmSimulation simulation,
            CommandTimeline timeline)
        {
            timeline.ApplyAtTick(world, world.Tick);
            simulation.Step(world);
            world.AdvanceTick();
        }

        private static LayerHashReport BuildLayerHashReport(WorldAuthorityHashes hashes)
        {
            return new LayerHashReport
            {
                config = FormatHash(hashes.Config),
                worldMetadata = FormatHash(hashes.WorldMetadata),
                groupTargets = FormatHash(hashes.GroupTargets),
                groupPathStates = FormatHash(hashes.GroupPathStates),
                navigationRequestSequence = FormatHash(hashes.NavigationRequestSequence),
                agentPositions = FormatHash(hashes.AgentPositions),
                agentVelocities = FormatHash(hashes.AgentVelocities),
                agentPathCursors = FormatHash(hashes.AgentPathCursors),
                full = FormatHash(hashes.Full),
            };
        }

        private static string GetOutputDirectory()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string configured = Environment.GetEnvironmentVariable("SWARM_REPLAY_OUTPUT_DIR");
            string outputDirectory = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(projectRoot, "ReplayResults")
                : Path.GetFullPath(
                    Path.IsPathRooted(configured)
                        ? configured
                        : Path.Combine(projectRoot, configured));
            Directory.CreateDirectory(outputDirectory);
            return outputDirectory;
        }

        private static void WriteJson(string path, object report)
        {
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] digest = sha256.ComputeHash(bytes);
                return BitConverter.ToString(digest).Replace("-", string.Empty);
            }
        }

        private static string FormatHash(ulong value)
        {
            return "0x" + value.ToString("X16", CultureInfo.InvariantCulture);
        }

        private static string FormatRaw(ulong value, WorldAuthorityRawKind kind)
        {
            return kind == WorldAuthorityRawKind.Int32
                ? unchecked((int)(uint)value).ToString(CultureInfo.InvariantCulture)
                : value.ToString(CultureInfo.InvariantCulture);
        }

        private static string BuildMarkdown(CaptureReport capture, VerifyReport verify)
        {
            return
                "# Cross-process deterministic replay\n\n" +
                "- Unity: " + verify.unityVersion + "\n" +
                "- Capture/verify process IDs: " + capture.processId + "/" +
                verify.verifyProcessId + " (independent: " + verify.independentProcess + ")\n" +
                "- Replay schema / logic hash: " + capture.replaySchemaVersion +
                " / `" + verify.logicHash + "`\n" +
                "- Config hash: `" + verify.configHash + "`\n" +
                "- Replay SHA-256: `" + verify.replaySha256 + "`\n" +
                "- Agents / commands / final tick: " + capture.agents + " / " +
                capture.commands + " / " + verify.finalTick + "\n" +
                "- Matched checkpoints: " + verify.matchedCheckpoints + "/" +
                capture.checkpoints + "\n" +
                "- Final authority hash: `" + verify.finalAuthorityHashes.full + "`\n" +
                "- Desync probe: `" + verify.desyncProbe.component + "[" +
                verify.desyncProbe.entityIndex + "]." + verify.desyncProbe.field +
                "` raw " + verify.desyncProbe.expectedRaw + " -> " +
                verify.desyncProbe.actualRaw + "\n\n" +
                "The capture and verification commands run in separate Unity batchmode processes. " +
                "This evidence covers the same macOS host, Unity version, scripting backend, and CPU architecture; " +
                "it does not claim Mono/IL2CPP or ARM64/x64 equivalence.\n";
        }
    }
}
