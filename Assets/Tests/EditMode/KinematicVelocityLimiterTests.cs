using NUnit.Framework;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation;
using SwarmECS.Simulation.Systems;

namespace SwarmECS.Tests.EditMode
{
    public sealed class KinematicVelocityLimiterTests
    {
        private static readonly FP HighAcceleration = FP.FromInt(100);

        [Test]
        public void Limit_AccelerationDeltaNeverExceedsConfiguredPerTickBudget()
        {
            SwarmConfig config = SwarmConfig.DemoDefault(1);
            FP expectedDelta = config.MaxAcceleration * config.FixedDeltaTime;

            FPVector2 velocity = KinematicVelocityLimiter.Limit(
                0,
                FPVector2.Zero,
                V(6, 0),
                config.MaxSpeed,
                config.MaxAcceleration,
                config.FixedDeltaTime,
                config.MaxTurnStep,
                out bool accelerationLimited,
                out bool turnLimited);

            Assert.That(accelerationLimited, Is.True);
            Assert.That(turnLimited, Is.False);
            Assert.That(velocity.X, Is.EqualTo(expectedDelta));
            Assert.That(velocity.Y, Is.EqualTo(FP.Zero));
            Assert.That(velocity.SqrMagnitude, Is.LessThanOrEqualTo(expectedDelta * expectedDelta));
        }

        [Test]
        public void Limit_TurnsAtConfiguredRawStepWithoutFloatingPointTrig()
        {
            FPVector2 turnStep = SwarmConfig.DefaultMaxTurnStep;

            FPVector2 velocity = KinematicVelocityLimiter.Limit(
                0,
                V(3, 0),
                V(0, 3),
                FP.FromInt(6),
                HighAcceleration,
                FP.One,
                turnStep,
                out bool accelerationLimited,
                out bool turnLimited);

            Assert.That(turnLimited, Is.True);
            Assert.That(accelerationLimited, Is.False);
            Assert.That(velocity.X, Is.EqualTo(turnStep.X * FP.FromInt(3)));
            Assert.That(velocity.Y, Is.EqualTo(turnStep.Y * FP.FromInt(3)));
        }

        [Test]
        public void Limit_TargetInsideTurnConeRemainsBitIdentical()
        {
            FPVector2 targetInsideTwelveDegrees = new(
                FP.FromRaw(65177),
                FP.FromRaw(6850));

            FPVector2 velocity = KinematicVelocityLimiter.Limit(
                4,
                FPVector2.UnitX,
                targetInsideTwelveDegrees,
                FP.FromInt(6),
                HighAcceleration,
                FP.One,
                SwarmConfig.DefaultMaxTurnStep,
                out bool accelerationLimited,
                out bool turnLimited);

            Assert.That(turnLimited, Is.False);
            Assert.That(accelerationLimited, Is.False);
            Assert.That(velocity, Is.EqualTo(targetInsideTwelveDegrees));
        }

        [Test]
        public void Limit_ExactReverseUsesStableEntityIdAsTurnTieBreak()
        {
            FPVector2 evenVelocity = KinematicVelocityLimiter.Limit(
                2,
                FPVector2.UnitX,
                -FPVector2.UnitX,
                FP.FromInt(6),
                HighAcceleration,
                FP.One,
                SwarmConfig.DefaultMaxTurnStep,
                out _,
                out bool evenTurnLimited);
            FPVector2 oddVelocity = KinematicVelocityLimiter.Limit(
                3,
                FPVector2.UnitX,
                -FPVector2.UnitX,
                FP.FromInt(6),
                HighAcceleration,
                FP.One,
                SwarmConfig.DefaultMaxTurnStep,
                out _,
                out bool oddTurnLimited);

            Assert.That(evenTurnLimited, Is.True);
            Assert.That(oddTurnLimited, Is.True);
            Assert.That(evenVelocity.X, Is.EqualTo(oddVelocity.X));
            Assert.That(evenVelocity.Y, Is.EqualTo(-oddVelocity.Y));
            Assert.That(evenVelocity.Y, Is.GreaterThan(FP.Zero));
        }

        [Test]
        public void Limit_StoppedAgentHasNoHeadingButStillRespectsAcceleration()
        {
            FP maxAcceleration = FP.FromInt(4);
            FP deltaTime = FP.FromRatio(1, 2);

            FPVector2 velocity = KinematicVelocityLimiter.Limit(
                7,
                FPVector2.Zero,
                V(0, -6),
                FP.FromInt(6),
                maxAcceleration,
                deltaTime,
                SwarmConfig.DefaultMaxTurnStep,
                out bool accelerationLimited,
                out bool turnLimited);

            Assert.That(turnLimited, Is.False);
            Assert.That(accelerationLimited, Is.True);
            Assert.That(velocity, Is.EqualTo(V(0, -2)));
        }

        [Test]
        public void Limit_ZeroTargetDeceleratesWithoutIntroducingTurn()
        {
            FPVector2 velocity = KinematicVelocityLimiter.Limit(
                0,
                V(2, 0),
                FPVector2.Zero,
                FP.FromInt(6),
                FP.FromInt(3),
                FP.Half,
                SwarmConfig.DefaultMaxTurnStep,
                out bool accelerationLimited,
                out bool turnLimited);

            Assert.That(turnLimited, Is.False);
            Assert.That(accelerationLimited, Is.True);
            Assert.That(velocity, Is.EqualTo(new FPVector2(FP.Half, FP.Zero)));
        }

        [Test]
        public void Limit_TurnAndAccelerationCanBothConstrainSameTick()
        {
            FPVector2 current = V(3, 0);

            FPVector2 velocity = KinematicVelocityLimiter.Limit(
                8,
                current,
                V(0, 6),
                FP.FromInt(6),
                FP.One,
                FP.One,
                SwarmConfig.DefaultMaxTurnStep,
                out bool accelerationLimited,
                out bool turnLimited);

            Assert.That(turnLimited, Is.True);
            Assert.That(accelerationLimited, Is.True);
            FPVector2 delta = velocity - current;
            Assert.That(delta.SqrMagnitude, Is.LessThanOrEqualTo(FP.One));
            Assert.That(velocity.Y, Is.GreaterThan(FP.Zero));
        }

        [Test]
        public void Limit_AlwaysClampsTargetToMaximumSpeed()
        {
            FP maxSpeed = FP.FromInt(6);

            FPVector2 velocity = KinematicVelocityLimiter.Limit(
                1,
                V(6, 0),
                V(100, 100),
                maxSpeed,
                FP.FromInt(1000),
                FP.One,
                SwarmConfig.DefaultMaxTurnStep,
                out _,
                out _);

            Assert.That(velocity.SqrMagnitude, Is.LessThanOrEqualTo(maxSpeed * maxSpeed));
        }

        [Test]
        public void Limit_RepeatedInputsProduceIdenticalRawVelocity()
        {
            FPVector2 expected = KinematicVelocityLimiter.Limit(
                17,
                new FPVector2(FP.FromRatio(7, 3), FP.FromRatio(-5, 4)),
                new FPVector2(FP.FromRatio(-11, 5), FP.FromRatio(13, 7)),
                FP.FromInt(6),
                FP.FromInt(24),
                FP.FromRatio(1, 30),
                SwarmConfig.DefaultMaxTurnStep,
                out _,
                out _);

            for (int i = 0; i < 128; ++i)
            {
                FPVector2 actual = KinematicVelocityLimiter.Limit(
                    17,
                    new FPVector2(FP.FromRatio(7, 3), FP.FromRatio(-5, 4)),
                    new FPVector2(FP.FromRatio(-11, 5), FP.FromRatio(13, 7)),
                    FP.FromInt(6),
                    FP.FromInt(24),
                    FP.FromRatio(1, 30),
                    SwarmConfig.DefaultMaxTurnStep,
                    out _,
                    out _);
                Assert.That(actual.X.Raw, Is.EqualTo(expected.X.Raw));
                Assert.That(actual.Y.Raw, Is.EqualTo(expected.Y.Raw));
            }
        }

        [Test]
        public void ConfigHash_IsStableAndCoversKinematicLimitsAndInitialMode()
        {
            SwarmConfig baseline = SwarmConfig.DemoDefault(128);
            SwarmConfig same = SwarmConfig.DemoDefault(128);
            SwarmConfig differentAcceleration = CreateConfig(
                baseline,
                FP.FromInt(23),
                baseline.MaxTurnStep,
                baseline.SpatialIndexMode);
            SwarmConfig differentTurn = CreateConfig(
                baseline,
                baseline.MaxAcceleration,
                new FPVector2(baseline.MaxTurnStep.X, FP.FromRaw(baseline.MaxTurnStep.Y.Raw + 1)),
                baseline.SpatialIndexMode);
            SwarmConfig differentMode = baseline.WithSpatialIndexMode(SpatialIndexMode.KdTree);

            Assert.That(baseline.MaxTurnStep.SqrMagnitude, Is.EqualTo(FP.One));
            Assert.That(baseline.ConfigHash, Is.EqualTo(0xA58082FCCAEDD1C9UL));
            Assert.That(same.ConfigHash, Is.EqualTo(baseline.ConfigHash));
            Assert.That(differentAcceleration.ConfigHash, Is.Not.EqualTo(baseline.ConfigHash));
            Assert.That(differentTurn.ConfigHash, Is.Not.EqualTo(baseline.ConfigHash));
            Assert.That(differentMode.ConfigHash, Is.Not.EqualTo(baseline.ConfigHash));
            Assert.That(
                CloneConfig(baseline, capacity: baseline.Capacity + 1).ConfigHash,
                Is.Not.EqualTo(baseline.ConfigHash));
            Assert.That(
                CloneConfig(baseline, fixedDeltaTime: baseline.FixedDeltaTime + FP.Epsilon).ConfigHash,
                Is.Not.EqualTo(baseline.ConfigHash));
            Assert.That(
                CloneConfig(baseline, agentRadius: baseline.AgentRadius + FP.Epsilon).ConfigHash,
                Is.Not.EqualTo(baseline.ConfigHash));
            Assert.That(
                CloneConfig(baseline, maxSpeed: baseline.MaxSpeed + FP.Epsilon).ConfigHash,
                Is.Not.EqualTo(baseline.ConfigHash));
            Assert.That(
                CloneConfig(baseline, neighborDistance: baseline.NeighborDistance + FP.Epsilon).ConfigHash,
                Is.Not.EqualTo(baseline.ConfigHash));
            Assert.That(
                CloneConfig(baseline, maxNeighbors: baseline.MaxNeighbors + 1).ConfigHash,
                Is.Not.EqualTo(baseline.ConfigHash));
            Assert.That(
                CloneConfig(baseline, timeHorizon: baseline.TimeHorizon + FP.Epsilon).ConfigHash,
                Is.Not.EqualTo(baseline.ConfigHash));
            Assert.That(
                CloneConfig(baseline, worldHalfExtent: baseline.WorldHalfExtent + FP.Epsilon).ConfigHash,
                Is.Not.EqualTo(baseline.ConfigHash));
            Assert.That(differentMode.MaxAcceleration, Is.EqualTo(baseline.MaxAcceleration));
            Assert.That(differentMode.MaxTurnStep, Is.EqualTo(baseline.MaxTurnStep));
        }

        [Test]
        public void Config_RejectsInvalidKinematicLimits()
        {
            SwarmConfig baseline = SwarmConfig.DemoDefault(8);

            Assert.That(
                () => CreateConfig(
                    baseline,
                    -FP.One,
                    baseline.MaxTurnStep,
                    baseline.SpatialIndexMode),
                Throws.TypeOf<System.ArgumentOutOfRangeException>());
            Assert.That(
                () => CreateConfig(
                    baseline,
                    baseline.MaxAcceleration,
                    FPVector2.Zero,
                    baseline.SpatialIndexMode),
                Throws.TypeOf<System.ArgumentException>());
            Assert.That(
                () => CreateConfig(
                    baseline,
                    baseline.MaxAcceleration,
                    new FPVector2(FP.One, -FP.Epsilon),
                    baseline.SpatialIndexMode),
                Throws.TypeOf<System.ArgumentOutOfRangeException>());
            Assert.That(
                () => CreateConfig(
                    baseline,
                    baseline.MaxAcceleration,
                    new FPVector2(FP.One, FP.One),
                    baseline.SpatialIndexMode),
                Throws.TypeOf<System.ArgumentException>());
        }

        [Test]
        public void LegacyConfigConstructor_UsesV022MotionDefaults()
        {
            SwarmConfig baseline = SwarmConfig.DemoDefault(8);
            SwarmConfig legacy = new(
                baseline.Capacity,
                baseline.FixedDeltaTime,
                baseline.AgentRadius,
                baseline.MaxSpeed,
                baseline.NeighborDistance,
                baseline.MaxNeighbors,
                baseline.TimeHorizon,
                baseline.WorldHalfExtent,
                baseline.SpatialIndexMode);

            Assert.That(legacy.MaxAcceleration, Is.EqualTo(SwarmConfig.DefaultMaxAcceleration));
            Assert.That(legacy.MaxTurnStep, Is.EqualTo(SwarmConfig.DefaultMaxTurnStep));
            Assert.That(legacy.ConfigHash, Is.EqualTo(baseline.ConfigHash));
        }

        private static SwarmConfig CreateConfig(
            SwarmConfig baseline,
            FP maxAcceleration,
            FPVector2 maxTurnStep,
            SpatialIndexMode mode)
        {
            return new SwarmConfig(
                baseline.Capacity,
                baseline.FixedDeltaTime,
                baseline.AgentRadius,
                baseline.MaxSpeed,
                maxAcceleration,
                maxTurnStep,
                baseline.NeighborDistance,
                baseline.MaxNeighbors,
                baseline.TimeHorizon,
                baseline.WorldHalfExtent,
                mode);
        }

        private static SwarmConfig CloneConfig(
            SwarmConfig baseline,
            int? capacity = null,
            FP? fixedDeltaTime = null,
            FP? agentRadius = null,
            FP? maxSpeed = null,
            FP? neighborDistance = null,
            int? maxNeighbors = null,
            FP? timeHorizon = null,
            FP? worldHalfExtent = null)
        {
            return new SwarmConfig(
                capacity ?? baseline.Capacity,
                fixedDeltaTime ?? baseline.FixedDeltaTime,
                agentRadius ?? baseline.AgentRadius,
                maxSpeed ?? baseline.MaxSpeed,
                baseline.MaxAcceleration,
                baseline.MaxTurnStep,
                neighborDistance ?? baseline.NeighborDistance,
                maxNeighbors ?? baseline.MaxNeighbors,
                timeHorizon ?? baseline.TimeHorizon,
                worldHalfExtent ?? baseline.WorldHalfExtent,
                baseline.SpatialIndexMode);
        }

        private static FPVector2 V(int x, int y)
        {
            return new FPVector2(FP.FromInt(x), FP.FromInt(y));
        }
    }
}
