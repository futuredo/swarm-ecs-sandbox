using NUnit.Framework;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation;
using SwarmECS.Simulation.Collision;
using SwarmECS.Simulation.Netcode;
using SwarmECS.Simulation.Systems;

namespace SwarmECS.Tests.EditMode
{
    public sealed class StaticObstacleRuntimeIntegrationTests
    {
        [Test]
        public void Avoidance_QueriesOnlyVisibleObstacleFacesAndReportsSeparateMetrics()
        {
            SwarmConfig config = CreateConfig(
                1,
                FP.FromRatio(1, 10),
                FP.Half,
                FP.FromInt(3),
                FP.FromInt(100),
                FP.FromInt(4),
                0,
                FP.FromInt(2));
            SwarmWorld world = CreateWorld(config, 1);
            world.Positions[0] = FPVector2.Zero;
            world.Velocities[0] = FPVector2.Zero;
            world.PreferredVelocities[0] = V(0, 2);
            StaticObstacleCollisionSystem obstacles = new(new[]
            {
                new FPOrientedBox2(V(0, 2), V(4, 1)),
            });

            using NeighborAvoidanceSystem avoidance = new(config, obstacles);
            avoidance.Execute(world);

            Assert.That(avoidance.LastObstacleOrcaLines, Is.EqualTo(1));
            Assert.That(avoidance.LastAgentOrcaLines, Is.Zero);
            Assert.That(avoidance.LastOrcaLines, Is.EqualTo(1));
            Assert.That(world.NextVelocities[0].Y, Is.LessThan(FP.FromInt(2)));
        }

        [Test]
        public void Movement_HighSpeedSweepCannotTunnelThroughWall()
        {
            SwarmConfig config = CreateConfig(
                1,
                FP.One,
                FP.Half,
                FP.FromInt(30),
                FP.FromInt(1000),
                FP.FromInt(8),
                0,
                FP.FromInt(2));
            SwarmWorld world = CreateWorld(config, 1);
            world.Positions[0] = V(-10, 0);
            world.Velocities[0] = V(30, 0);
            world.NextVelocities[0] = V(30, 0);
            StaticObstacleCollisionSystem obstacles = CreateUnitBoxSystem();
            MovementIntegrationSystem movement = new();

            movement.Execute(world, obstacles);

            Assert.That(world.Positions[0].X, Is.LessThanOrEqualTo(FP.FromRatio(-3, 2)));
            Assert.That(world.Velocities[0].X, Is.EqualTo(FP.Zero));
            Assert.That(obstacles.LastSweepHits, Is.EqualTo(1));
            Assert.That(obstacles.LastPenetrationRecoveries, Is.Zero);
            Assert.That(obstacles.LastMaxResidualDepth, Is.EqualTo(FP.Zero));
        }

        [Test]
        public void PublicObstacleSnapshotsCannotMutateRuntimeGeometry()
        {
            StaticObstacleCollisionSystem obstacles = CreateUnitBoxSystem();
            FPOrientedBox2[] obstacleSnapshot = obstacles.Obstacles;
            obstacleSnapshot[0] = new FPOrientedBox2(V(50, 50), V(1, 1));
            var segmentSnapshot = obstacles.ObstacleSegments;
            segmentSnapshot[0] = default;
            obstacles.BeginTick();

            obstacles.MoveAgent(
                V(-4, 0),
                V(8, 0),
                FP.Half,
                FP.One,
                out FPVector2 position,
                out _);

            Assert.That(position.X, Is.LessThanOrEqualTo(FP.FromRatio(-3, 2)));
            Assert.That(obstacles.LastSweepHits, Is.EqualTo(1));
        }

        [Test]
        public void Movement_WallImpactPreservesTangentialSlide()
        {
            SwarmConfig config = CreateConfig(
                1,
                FP.One,
                FP.Half,
                FP.FromInt(10),
                FP.FromInt(1000),
                FP.FromInt(8),
                0,
                FP.FromInt(2));
            SwarmWorld world = CreateWorld(config, 1);
            world.Positions[0] = V(-3, -2);
            world.Velocities[0] = V(6, 3);
            world.NextVelocities[0] = V(6, 3);
            StaticObstacleCollisionSystem obstacles = new(new[]
            {
                new FPOrientedBox2(FPVector2.Zero, V(1, 5)),
            });
            MovementIntegrationSystem movement = new();

            movement.Execute(world, obstacles);

            Assert.That(world.Positions[0].X, Is.LessThanOrEqualTo(FP.FromRatio(-3, 2)));
            Assert.That(world.Positions[0].Y, Is.GreaterThan(FP.Zero));
            Assert.That(world.Velocities[0].X, Is.EqualTo(FP.Zero));
            Assert.That(world.Velocities[0].Y, Is.GreaterThan(FP.Zero));
            Assert.That(obstacles.LastSweepHits, Is.GreaterThanOrEqualTo(1));
            Assert.That(obstacles.LastPenetrationRecoveries, Is.Zero);
            Assert.That(obstacles.LastMaxResidualDepth, Is.EqualTo(FP.Zero));
        }

        [Test]
        public void Movement_ConcaveCornerUsesTwoStableImpactsWithoutSatPushOut()
        {
            SwarmConfig config = CreateConfig(
                1,
                FP.One,
                FP.Half,
                FP.FromInt(10),
                FP.FromInt(1000),
                FP.FromInt(8),
                0,
                FP.FromInt(2));
            SwarmWorld world = CreateWorld(config, 1);
            world.Positions[0] = V(-3, -3);
            world.Velocities[0] = V(5, 5);
            world.NextVelocities[0] = V(5, 5);
            StaticObstacleCollisionSystem obstacles = new(new[]
            {
                new FPOrientedBox2(V(0, 2), V(1, 4)),
                new FPOrientedBox2(V(2, 0), V(4, 1)),
            });
            MovementIntegrationSystem movement = new();

            movement.Execute(world, obstacles);

            Assert.That(world.Positions[0].X, Is.LessThanOrEqualTo(FP.FromRatio(-3, 2)));
            Assert.That(world.Positions[0].Y, Is.LessThanOrEqualTo(FP.FromRatio(-3, 2)));
            Assert.That(world.Velocities[0], Is.EqualTo(FPVector2.Zero));
            Assert.That(obstacles.LastSweepHits, Is.EqualTo(2));
            Assert.That(obstacles.LastPenetrationRecoveries, Is.Zero);
            Assert.That(obstacles.LastMaxResidualDepth, Is.EqualTo(FP.Zero));
        }

        [Test]
        public void Movement_EntranceNarrowerThanDiameterBlocksWithoutTunneling()
        {
            SwarmConfig config = CreateConfig(
                1,
                FP.One,
                FP.Half,
                FP.FromInt(8),
                FP.FromInt(1000),
                FP.FromInt(8),
                0,
                FP.FromInt(2));
            SwarmWorld world = CreateWorld(config, 1);
            world.Positions[0] = V(0, -3);
            world.Velocities[0] = V(0, 6);
            world.NextVelocities[0] = V(0, 6);
            FP halfWidth = FP.FromRatio(13, 5);
            StaticObstacleCollisionSystem obstacles = new(new[]
            {
                new FPOrientedBox2(V(-3, 0), new FPVector2(halfWidth, FP.Half)),
                new FPOrientedBox2(V(3, 0), new FPVector2(halfWidth, FP.Half)),
            });
            MovementIntegrationSystem movement = new();

            movement.Execute(world, obstacles);

            Assert.That(world.Positions[0].Y, Is.LessThanOrEqualTo(-FP.One));
            Assert.That(world.Velocities[0].Y, Is.EqualTo(FP.Zero));
            Assert.That(obstacles.LastSweepHits, Is.GreaterThanOrEqualTo(1));
            Assert.That(obstacles.LastPenetrationRecoveries, Is.Zero);
            Assert.That(obstacles.LastMaxResidualDepth, Is.EqualTo(FP.Zero));
        }

        [Test]
        public void AvoidanceAndMovement_TwoAgentsCrossCorridorWithoutFallbackRecovery()
        {
            SwarmConfig config = CreateConfig(
                2,
                FP.FromRatio(1, 30),
                FP.FromRatio(7, 20),
                FP.FromInt(2),
                FP.FromInt(24),
                FP.FromInt(4),
                1,
                FP.FromInt(2));
            SwarmWorld world = CreateWorld(config, 2);
            world.Positions[0] = new FPVector2(FP.FromRatio(-2, 5), FP.FromInt(-3));
            world.Positions[1] = new FPVector2(FP.FromRatio(2, 5), FP.FromInt(3));
            world.Velocities[0] = FPVector2.Zero;
            world.Velocities[1] = FPVector2.Zero;
            FP wallX = FP.FromRatio(7, 4);
            StaticObstacleCollisionSystem obstacles = new(new[]
            {
                new FPOrientedBox2(
                    new FPVector2(-wallX, FP.Zero),
                    new FPVector2(FP.FromRatio(1, 4), FP.FromInt(6))),
                new FPOrientedBox2(
                    new FPVector2(wallX, FP.Zero),
                    new FPVector2(FP.FromRatio(1, 4), FP.FromInt(6))),
            });
            MovementIntegrationSystem movement = new();
            int fallbackRecoveries = 0;

            using NeighborAvoidanceSystem avoidance = new(config, obstacles);
            for (int tick = 0; tick < 120; ++tick)
            {
                world.PreferredVelocities[0] = V(0, 2);
                world.PreferredVelocities[1] = V(0, -2);
                avoidance.Execute(world);
                movement.Execute(world, obstacles);
                fallbackRecoveries += obstacles.LastPenetrationRecoveries;
                world.AdvanceTick();
            }

            Assert.That(world.Positions[0].Y, Is.GreaterThan(FP.FromInt(2)));
            Assert.That(world.Positions[1].Y, Is.LessThan(FP.FromInt(-2)));
            Assert.That(avoidance.LastObstacleOrcaLines, Is.GreaterThan(0));
            Assert.That(fallbackRecoveries, Is.Zero);
            Assert.That(obstacles.LastMaxResidualDepth, Is.EqualTo(FP.Zero));
        }

        [Test]
        public void Movement_AppliesTurnLimitBeforePositionIntegration()
        {
            SwarmConfig baseline = CreateConfig(
                1,
                FP.One,
                FP.Half,
                FP.FromInt(6),
                FP.FromInt(100),
                FP.FromInt(4),
                0,
                FP.FromInt(2));
            SwarmWorld world = CreateWorld(baseline, 1);
            world.Positions[0] = FPVector2.Zero;
            world.Velocities[0] = V(3, 0);
            world.NextVelocities[0] = V(0, 3);
            MovementIntegrationSystem movement = new();
            StaticObstacleCollisionSystem emptyObstacles = new(new FPOrientedBox2[0]);

            movement.Execute(world, emptyObstacles);

            FPVector2 expected = new(
                baseline.MaxTurnStep.X * FP.FromInt(3),
                baseline.MaxTurnStep.Y * FP.FromInt(3));
            Assert.That(world.Velocities[0], Is.EqualTo(expected));
            Assert.That(world.Positions[0], Is.EqualTo(expected));
            Assert.That(movement.LastTurnLimitedAgents, Is.EqualTo(1));
            Assert.That(movement.LastAccelerationLimitedAgents, Is.Zero);
        }

        [Test]
        public void CcdCollision_ReexecutionFromSnapshotProducesSameStateHash()
        {
            SwarmConfig config = CreateConfig(
                1,
                FP.One,
                FP.Half,
                FP.FromInt(30),
                FP.FromInt(1000),
                FP.FromInt(8),
                0,
                FP.FromInt(2));
            SwarmWorld world = CreateWorld(config, 1);
            world.Positions[0] = V(-10, 0);
            world.Velocities[0] = V(30, 0);
            world.NextVelocities[0] = V(30, 0);
            StaticObstacleCollisionSystem obstacles = CreateUnitBoxSystem();
            MovementIntegrationSystem movement = new();
            WorldSnapshotRing snapshots = new(config.Capacity, 2);
            snapshots.Save(world);

            movement.Execute(world, obstacles);
            world.AdvanceTick();
            ulong firstHash = world.ComputeStateHash();

            Assert.That(snapshots.TryRestore(world, 0), Is.True);
            world.NextVelocities[0] = V(30, 0);
            movement.Execute(world, obstacles);
            world.AdvanceTick();

            Assert.That(world.ComputeStateHash(), Is.EqualTo(firstHash));
        }

        [Test]
        public void InitialOverlap_UsesExplicitSatFallbackAndClearsResidualDepth()
        {
            StaticObstacleCollisionSystem obstacles = CreateUnitBoxSystem();
            obstacles.BeginTick();

            obstacles.MoveAgent(
                FPVector2.Zero,
                V(1, 0),
                FP.Half,
                FP.One,
                out FPVector2 position,
                out _);

            Assert.That(position.X, Is.GreaterThan(FP.One));
            Assert.That(obstacles.LastPenetrationRecoveries, Is.GreaterThan(0));
            Assert.That(obstacles.LastMaxResidualDepth, Is.EqualTo(FP.Zero));
        }

        private static StaticObstacleCollisionSystem CreateUnitBoxSystem()
        {
            return new StaticObstacleCollisionSystem(new[]
            {
                new FPOrientedBox2(FPVector2.Zero, V(1, 1)),
            });
        }

        private static SwarmWorld CreateWorld(SwarmConfig config, int count)
        {
            SwarmWorld world = new(config);
            world.InitializeDeterministicFormation(count, 0xCCD022u);
            return world;
        }

        private static SwarmConfig CreateConfig(
            int capacity,
            FP deltaTime,
            FP radius,
            FP maxSpeed,
            FP maxAcceleration,
            FP neighborDistance,
            int maxNeighbors,
            FP timeHorizon)
        {
            return new SwarmConfig(
                capacity,
                deltaTime,
                radius,
                maxSpeed,
                maxAcceleration,
                SwarmConfig.DefaultMaxTurnStep,
                neighborDistance,
                maxNeighbors,
                timeHorizon,
                FP.FromInt(100),
                SpatialIndexMode.UniformGrid);
        }

        private static FPVector2 V(int x, int y)
        {
            return new FPVector2(FP.FromInt(x), FP.FromInt(y));
        }
    }
}
