using System.Reflection;
using NUnit.Framework;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation;
using SwarmECS.Simulation.Determinism;
using SwarmECS.Simulation.Pathfinding;

namespace SwarmECS.Tests.EditMode
{
    public sealed class WorldAuthorityDiagnosticsTests
    {
        private const uint Seed = 0xD35A11CEu;

        [Test]
        public void IdenticalWorlds_HaveIdenticalLayerHashesAndNoDifference()
        {
            SwarmWorld expected = CreateWorld();
            SwarmWorld actual = CreateWorld();

            WorldAuthorityHashes expectedHashes = WorldAuthorityDiagnostics.ComputeHashes(expected);
            WorldAuthorityHashes actualHashes = WorldAuthorityDiagnostics.ComputeHashes(actual);

            AssertAllHashesEqual(expectedHashes, actualHashes);
            Assert.That(
                expectedHashes.Full,
                Is.EqualTo(0x6C20FEE27CABF37EUL),
                "Changing the schema-ordered reference hash requires an explicit schema review.");
            Assert.That(
                WorldAuthorityDiagnostics.TryFindFirstDifference(expected, actual, out WorldDesyncDifference difference),
                Is.False);
            Assert.That(difference.HasDifference, Is.False);
            Assert.That(difference.ComponentName, Is.EqualTo("None"));
            Assert.That(difference.ToString(), Is.EqualTo("No authoritative difference"));
        }

        [Test]
        public void ConfigHashDifference_IsReportedBeforeMutableWorldState()
        {
            SwarmConfig expectedConfig = SwarmConfig.DemoDefault(16);
            SwarmConfig actualConfig = new(
                expectedConfig.Capacity,
                expectedConfig.FixedDeltaTime,
                expectedConfig.AgentRadius,
                expectedConfig.MaxSpeed,
                expectedConfig.MaxAcceleration + FP.Epsilon,
                expectedConfig.MaxTurnStep,
                expectedConfig.NeighborDistance,
                expectedConfig.MaxNeighbors,
                expectedConfig.TimeHorizon,
                expectedConfig.WorldHalfExtent,
                expectedConfig.SpatialIndexMode);
            SwarmWorld expected = CreateWorld(expectedConfig, 8, Seed);
            SwarmWorld actual = CreateWorld(actualConfig, 8, Seed);
            actual.Positions[0] = new FPVector2(FP.MaxValue, FP.MinValue);

            AssertDifference(
                expected,
                actual,
                WorldAuthorityComponent.Config,
                -1,
                "ConfigHash",
                WorldAuthorityRawKind.UInt64,
                expectedConfig.ConfigHash,
                actualConfig.ConfigHash);
        }

        [TestCase("Tick")]
        [TestCase("Count")]
        [TestCase("Seed")]
        [TestCase("SpatialIndexMode")]
        public void WorldMetadataDifference_ReportsExactField(string fieldName)
        {
            SwarmConfig config = SwarmConfig.DemoDefault(16);
            SwarmWorld expected = CreateWorld(config, 8, Seed);
            SwarmWorld actual = CreateWorld(config, 8, Seed);
            WorldAuthorityRawKind rawKind;
            ulong expectedRaw;
            ulong actualRaw;

            switch (fieldName)
            {
                case "Tick":
                    actual.AdvanceTick();
                    rawKind = WorldAuthorityRawKind.Int32;
                    expectedRaw = Int32Raw(expected.Tick);
                    actualRaw = Int32Raw(actual.Tick);
                    break;
                case "Count":
                    actual.InitializeDeterministicFormation(9, Seed);
                    rawKind = WorldAuthorityRawKind.Int32;
                    expectedRaw = Int32Raw(expected.Count);
                    actualRaw = Int32Raw(actual.Count);
                    break;
                case "Seed":
                    actual = CreateWorld(config, 8, Seed + 1u);
                    rawKind = WorldAuthorityRawKind.UInt32;
                    expectedRaw = expected.Seed;
                    actualRaw = actual.Seed;
                    break;
                case "SpatialIndexMode":
                    actual.SetSpatialIndexMode(SpatialIndexMode.KdTree);
                    rawKind = WorldAuthorityRawKind.UInt32;
                    expectedRaw = (uint)expected.SpatialIndexMode;
                    actualRaw = (uint)actual.SpatialIndexMode;
                    break;
                default:
                    throw new AssertionException("Unknown metadata field " + fieldName);
            }

            AssertDifference(
                expected,
                actual,
                WorldAuthorityComponent.WorldMetadata,
                -1,
                fieldName,
                rawKind,
                expectedRaw,
                actualRaw);
        }

        [TestCase("X.Raw")]
        [TestCase("Y.Raw")]
        public void GroupTargetDifference_ReportsGroupAndRawAxis(string fieldName)
        {
            const int group = 2;
            SwarmWorld expected = CreateWorld();
            SwarmWorld actual = CreateWorld();
            FPVector2 original = actual.GroupTargets[group];
            actual.GroupTargets[group] = fieldName == "X.Raw"
                ? new FPVector2(FP.FromRaw(original.X.Raw + 7), original.Y)
                : new FPVector2(original.X, FP.FromRaw(original.Y.Raw + 7));
            int expectedValue = fieldName == "X.Raw"
                ? expected.GroupTargets[group].X.Raw
                : expected.GroupTargets[group].Y.Raw;
            int actualValue = fieldName == "X.Raw"
                ? actual.GroupTargets[group].X.Raw
                : actual.GroupTargets[group].Y.Raw;

            AssertDifference(
                expected,
                actual,
                WorldAuthorityComponent.GroupTargets,
                group,
                fieldName,
                WorldAuthorityRawKind.Int32,
                Int32Raw(expectedValue),
                Int32Raw(actualValue));
        }

        [TestCase("ResolvedStartIndex")]
        [TestCase("ResolvedGoalIndex")]
        [TestCase("ResolvedMapRevision")]
        [TestCase("Status")]
        [TestCase("PendingStartIndex")]
        [TestCase("PendingGoalIndex")]
        [TestCase("PendingMapRevision")]
        [TestCase("PendingSequence")]
        public void GroupPathStateDifference_ReportsEveryAuthoritativeField(string fieldName)
        {
            const int group = 1;
            SwarmWorld expected = CreateWorld();
            SwarmWorld actual = CreateWorld();
            GroupPathState expectedState = expected.GroupPathStates[group];
            GroupPathState actualState = actual.GroupPathStates[group];
            MutateGroupPathField(ref actualState, fieldName);
            actual.GroupPathStates[group] = actualState;

            bool isStatus = fieldName == "Status";
            ulong expectedRaw = isStatus
                ? (uint)expectedState.Status
                : Int32Raw(ReadGroupPathInt32(expectedState, fieldName));
            ulong actualRaw = isStatus
                ? (uint)actualState.Status
                : Int32Raw(ReadGroupPathInt32(actualState, fieldName));

            AssertDifference(
                expected,
                actual,
                WorldAuthorityComponent.GroupPathStates,
                group,
                fieldName,
                isStatus ? WorldAuthorityRawKind.UInt32 : WorldAuthorityRawKind.Int32,
                expectedRaw,
                actualRaw);
        }

        [Test]
        public void NavigationSequenceDifference_ReportsSingletonField()
        {
            SwarmWorld expected = CreateWorld();
            SwarmWorld actual = CreateWorld();
            SetNextPathRequestSequence(actual, 41);

            AssertDifference(
                expected,
                actual,
                WorldAuthorityComponent.NavigationRequestSequence,
                -1,
                "NextPathRequestSequence",
                WorldAuthorityRawKind.Int32,
                Int32Raw(0),
                Int32Raw(41));
        }

        [TestCase(WorldAuthorityComponent.AgentPositions, "X.Raw")]
        [TestCase(WorldAuthorityComponent.AgentPositions, "Y.Raw")]
        [TestCase(WorldAuthorityComponent.AgentVelocities, "X.Raw")]
        [TestCase(WorldAuthorityComponent.AgentVelocities, "Y.Raw")]
        public void AgentVectorDifference_ReportsComponentEntityAndRawAxis(
            WorldAuthorityComponent component,
            string fieldName)
        {
            const int entity = 3;
            SwarmWorld expected = CreateWorld();
            SwarmWorld actual = CreateWorld();
            FPVector2[] values = component == WorldAuthorityComponent.AgentPositions
                ? actual.Positions
                : actual.Velocities;
            FPVector2 original = values[entity];
            values[entity] = fieldName == "X.Raw"
                ? new FPVector2(FP.FromRaw(original.X.Raw + 1), original.Y)
                : new FPVector2(original.X, FP.FromRaw(original.Y.Raw + 1));
            FPVector2 expectedValue = component == WorldAuthorityComponent.AgentPositions
                ? expected.Positions[entity]
                : expected.Velocities[entity];
            FPVector2 actualValue = values[entity];
            int expectedRaw = fieldName == "X.Raw" ? expectedValue.X.Raw : expectedValue.Y.Raw;
            int actualRaw = fieldName == "X.Raw" ? actualValue.X.Raw : actualValue.Y.Raw;

            WorldDesyncDifference difference = AssertDifference(
                expected,
                actual,
                component,
                entity,
                fieldName,
                WorldAuthorityRawKind.Int32,
                Int32Raw(expectedRaw),
                Int32Raw(actualRaw));
            Assert.That(
                difference.ToString(),
                Does.StartWith(difference.ComponentName + "[entity=3]." + fieldName + " expected="));
        }

        [Test]
        public void AgentPathCursorDifference_ReportsUnsignedRawValue()
        {
            const int entity = 4;
            SwarmWorld expected = CreateWorld();
            SwarmWorld actual = CreateWorld();
            actual.PathCursors[entity]++;

            AssertDifference(
                expected,
                actual,
                WorldAuthorityComponent.AgentPathCursors,
                entity,
                "PathCursor",
                WorldAuthorityRawKind.UInt32,
                expected.PathCursors[entity],
                actual.PathCursors[entity]);
        }

        [Test]
        public void ComponentHashes_IsolateVelocityMutationAndFullHashChanges()
        {
            SwarmWorld expected = CreateWorld();
            SwarmWorld actual = CreateWorld();
            actual.Velocities[5] = new FPVector2(FP.FromRaw(17), FP.FromRaw(-29));

            WorldAuthorityHashes expectedHashes = WorldAuthorityDiagnostics.ComputeHashes(expected);
            WorldAuthorityHashes actualHashes = WorldAuthorityDiagnostics.ComputeHashes(actual);

            Assert.That(actualHashes.Config, Is.EqualTo(expectedHashes.Config));
            Assert.That(actualHashes.WorldMetadata, Is.EqualTo(expectedHashes.WorldMetadata));
            Assert.That(actualHashes.GroupTargets, Is.EqualTo(expectedHashes.GroupTargets));
            Assert.That(actualHashes.GroupPathStates, Is.EqualTo(expectedHashes.GroupPathStates));
            Assert.That(actualHashes.NavigationRequestSequence, Is.EqualTo(expectedHashes.NavigationRequestSequence));
            Assert.That(actualHashes.AgentPositions, Is.EqualTo(expectedHashes.AgentPositions));
            Assert.That(actualHashes.AgentVelocities, Is.Not.EqualTo(expectedHashes.AgentVelocities));
            Assert.That(actualHashes.AgentPathCursors, Is.EqualTo(expectedHashes.AgentPathCursors));
            Assert.That(actualHashes.Full, Is.Not.EqualTo(expectedHashes.Full));
            Assert.That(
                actualHashes.Get(WorldAuthorityComponent.AgentVelocities),
                Is.EqualTo(actualHashes.AgentVelocities));
        }

        [Test]
        public void StableComponentOrder_PrioritizesTargetsThenPositionsBeforeLaterComponents()
        {
            SwarmWorld expected = CreateWorld();
            SwarmWorld actual = CreateWorld();
            GroupPathState state = actual.GroupPathStates[0];
            state.ResolvedStartIndex = 99;
            actual.GroupPathStates[0] = state;
            FPVector2 target = actual.GroupTargets[3];
            actual.GroupTargets[3] = new FPVector2(target.X, FP.FromRaw(target.Y.Raw + 1));
            actual.Positions[7] = new FPVector2(FP.FromRaw(actual.Positions[7].X.Raw + 1), actual.Positions[7].Y);
            actual.Velocities[0] = new FPVector2(FP.Epsilon, FP.Zero);

            AssertDifference(
                expected,
                actual,
                WorldAuthorityComponent.GroupTargets,
                3,
                "Y.Raw",
                WorldAuthorityRawKind.Int32,
                Int32Raw(expected.GroupTargets[3].Y.Raw),
                Int32Raw(actual.GroupTargets[3].Y.Raw));

            actual.GroupTargets[3] = expected.GroupTargets[3];
            actual.GroupPathStates[0] = expected.GroupPathStates[0];
            AssertDifference(
                expected,
                actual,
                WorldAuthorityComponent.AgentPositions,
                7,
                "X.Raw",
                WorldAuthorityRawKind.Int32,
                Int32Raw(expected.Positions[7].X.Raw),
                Int32Raw(actual.Positions[7].X.Raw));
        }

        private static SwarmWorld CreateWorld()
        {
            return CreateWorld(SwarmConfig.DemoDefault(16), 8, Seed);
        }

        private static SwarmWorld CreateWorld(SwarmConfig config, int count, uint seed)
        {
            SwarmWorld world = new(config);
            world.InitializeDeterministicFormation(count, seed);
            return world;
        }

        private static WorldDesyncDifference AssertDifference(
            SwarmWorld expected,
            SwarmWorld actual,
            WorldAuthorityComponent component,
            int entityIndex,
            string fieldName,
            WorldAuthorityRawKind rawKind,
            ulong expectedRaw,
            ulong actualRaw)
        {
            Assert.That(
                WorldAuthorityDiagnostics.TryFindFirstDifference(expected, actual, out WorldDesyncDifference difference),
                Is.True);
            Assert.That(difference.HasDifference, Is.True);
            Assert.That(difference.Component, Is.EqualTo(component));
            Assert.That(difference.ComponentName, Is.EqualTo(component.ToString()));
            Assert.That(difference.EntityIndex, Is.EqualTo(entityIndex));
            Assert.That(difference.FieldName, Is.EqualTo(fieldName));
            Assert.That(difference.RawKind, Is.EqualTo(rawKind));
            Assert.That(difference.ExpectedRaw, Is.EqualTo(expectedRaw));
            Assert.That(difference.ActualRaw, Is.EqualTo(actualRaw));
            return difference;
        }

        private static void AssertAllHashesEqual(
            WorldAuthorityHashes expected,
            WorldAuthorityHashes actual)
        {
            Assert.That(actual.Config, Is.EqualTo(expected.Config));
            Assert.That(actual.WorldMetadata, Is.EqualTo(expected.WorldMetadata));
            Assert.That(actual.GroupTargets, Is.EqualTo(expected.GroupTargets));
            Assert.That(actual.GroupPathStates, Is.EqualTo(expected.GroupPathStates));
            Assert.That(actual.NavigationRequestSequence, Is.EqualTo(expected.NavigationRequestSequence));
            Assert.That(actual.AgentPositions, Is.EqualTo(expected.AgentPositions));
            Assert.That(actual.AgentVelocities, Is.EqualTo(expected.AgentVelocities));
            Assert.That(actual.AgentPathCursors, Is.EqualTo(expected.AgentPathCursors));
            Assert.That(actual.Full, Is.EqualTo(expected.Full));
        }

        private static void SetNextPathRequestSequence(SwarmWorld world, int value)
        {
            PropertyInfo property = typeof(SwarmWorld).GetProperty(
                nameof(SwarmWorld.NextPathRequestSequence),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo setter = property?.GetSetMethod(true);
            Assert.That(setter, Is.Not.Null);
            setter.Invoke(world, new object[] { value });
        }

        private static void MutateGroupPathField(ref GroupPathState state, string fieldName)
        {
            switch (fieldName)
            {
                case "ResolvedStartIndex":
                    state.ResolvedStartIndex = 11;
                    break;
                case "ResolvedGoalIndex":
                    state.ResolvedGoalIndex = 12;
                    break;
                case "ResolvedMapRevision":
                    state.ResolvedMapRevision = 13;
                    break;
                case "Status":
                    state.Status = GroupPathStatus.Unreachable;
                    break;
                case "PendingStartIndex":
                    state.PendingStartIndex = 15;
                    break;
                case "PendingGoalIndex":
                    state.PendingGoalIndex = 16;
                    break;
                case "PendingMapRevision":
                    state.PendingMapRevision = 17;
                    break;
                case "PendingSequence":
                    state.PendingSequence = 18;
                    break;
                default:
                    throw new AssertionException("Unknown path-state field " + fieldName);
            }
        }

        private static int ReadGroupPathInt32(GroupPathState state, string fieldName)
        {
            return fieldName switch
            {
                "ResolvedStartIndex" => state.ResolvedStartIndex,
                "ResolvedGoalIndex" => state.ResolvedGoalIndex,
                "ResolvedMapRevision" => state.ResolvedMapRevision,
                "PendingStartIndex" => state.PendingStartIndex,
                "PendingGoalIndex" => state.PendingGoalIndex,
                "PendingMapRevision" => state.PendingMapRevision,
                "PendingSequence" => state.PendingSequence,
                _ => throw new AssertionException("Field is not an Int32 path-state field: " + fieldName),
            };
        }

        private static ulong Int32Raw(int value)
        {
            return unchecked((uint)value);
        }
    }
}
