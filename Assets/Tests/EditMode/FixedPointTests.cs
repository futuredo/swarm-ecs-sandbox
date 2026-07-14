using System;
using NUnit.Framework;
using SwarmECS.Determinism;
using SwarmECS.FixedPoint;

namespace SwarmECS.Tests.EditMode
{
    public sealed class FixedPointTests
    {
        [Test]
        public void Construction_UsesExpectedQ16_16Encoding()
        {
            Assert.That(FP.FromInt(1).Raw, Is.EqualTo(65536));
            Assert.That(FP.FromInt(-2).Raw, Is.EqualTo(-131072));
            Assert.That(FP.FromRatio(1, 2), Is.EqualTo(FP.Half));
            Assert.That(FP.FromRatio(-3, 2).Raw, Is.EqualTo(-98304));
            Assert.That(FP.FromDouble(1.25).Raw, Is.EqualTo(81920));
            Assert.That(FP.FromDouble(-1.25).Raw, Is.EqualTo(-81920));
        }

        [Test]
        public void FromRatio_HandlesExtremeLongInputsWithoutIntermediateOverflow()
        {
            Assert.That(FP.FromRatio(long.MinValue, long.MaxValue).Raw, Is.EqualTo(-65536));
            Assert.That(FP.FromRatio(long.MaxValue, long.MinValue).Raw, Is.EqualTo(-65535));
            Assert.That(FP.FromRatio(long.MaxValue, 1), Is.EqualTo(FP.MaxValue));
            Assert.That(FP.FromRatio(long.MinValue, 1), Is.EqualTo(FP.MinValue));
        }

        [Test]
        public void NegativeConversions_HaveExplicitFloorCeilTruncateAndRoundBehavior()
        {
            FP value = FP.FromRatio(-7, 4);

            Assert.That(value.ToIntTruncated(), Is.EqualTo(-1));
            Assert.That(value.FloorToInt(), Is.EqualTo(-2));
            Assert.That(value.CeilToInt(), Is.EqualTo(-1));
            Assert.That(value.RoundToInt(), Is.EqualTo(-2));
        }

        [Test]
        public void AddSubtractAndNegate_SaturateDeterministically()
        {
            Assert.That(FP.MaxValue + FP.Epsilon, Is.EqualTo(FP.MaxValue));
            Assert.That(FP.MinValue - FP.Epsilon, Is.EqualTo(FP.MinValue));
            Assert.That(-FP.MinValue, Is.EqualTo(FP.MaxValue));
            Assert.That(FP.Abs(FP.MinValue), Is.EqualTo(FP.MaxValue));
        }

        [Test]
        public void MultiplyAndDivide_HandleSignsAndTruncateTowardZero()
        {
            FP oneAndHalf = FP.FromRatio(3, 2);
            FP twoAndQuarter = FP.FromRatio(9, 4);

            Assert.That(oneAndHalf * twoAndQuarter, Is.EqualTo(FP.FromRatio(27, 8)));
            Assert.That((-oneAndHalf) * twoAndQuarter, Is.EqualTo(FP.FromRatio(-27, 8)));
            Assert.That(FP.FromInt(7) / FP.FromInt(2), Is.EqualTo(FP.FromRatio(7, 2)));
            Assert.That(FP.FromInt(-7) / FP.FromInt(2), Is.EqualTo(FP.FromRatio(-7, 2)));

            FP smallestNegativeProduct = FP.FromRaw(-1) * FP.Half;
            Assert.That(smallestNegativeProduct, Is.EqualTo(FP.Zero));
        }

        [Test]
        public void MultiplyAndDivide_SaturateAndRejectZeroDivisor()
        {
            Assert.That(FP.MaxValue * FP.Two, Is.EqualTo(FP.MaxValue));
            Assert.That(FP.MinValue * FP.Two, Is.EqualTo(FP.MinValue));
            Assert.That(FP.MaxValue / FP.Epsilon, Is.EqualTo(FP.MaxValue));
            Assert.Throws<DivideByZeroException>(() => _ = FP.One / FP.Zero);
            Assert.Throws<DivideByZeroException>(() => FP.FromRatio(1, 0));
        }

        [Test]
        public void Remainder_DefinesIntegralOverflowEdgeCase()
        {
            Assert.That(FP.MinValue % FP.FromRaw(-1), Is.EqualTo(FP.Zero));
            Assert.That(FP.FromRatio(-7, 2) % FP.FromInt(2), Is.EqualTo(FP.FromRatio(-3, 2)));
        }

        [TestCase(0, 0)]
        [TestCase(1, 65536)]
        [TestCase(4, 131072)]
        [TestCase(9, 196608)]
        [TestCase(144, 786432)]
        public void Sqrt_PerfectSquares_ReturnExactResults(int input, int expectedRaw)
        {
            Assert.That(FPMath.Sqrt(FP.FromInt(input)).Raw, Is.EqualTo(expectedRaw));
        }

        [Test]
        public void Sqrt_FloorsAtRawPrecision_AndRejectsNegativeValues()
        {
            FP rootTwo = FPMath.Sqrt(FP.FromInt(2));
            FP nextRaw = FP.FromRaw(rootTwo.Raw + 1);

            Assert.That(rootTwo * rootTwo, Is.LessThanOrEqualTo(FP.FromInt(2)));
            Assert.That(nextRaw * nextRaw, Is.GreaterThanOrEqualTo(FP.FromInt(2)));
            Assert.Throws<ArgumentOutOfRangeException>(() => FPMath.Sqrt(-FP.One));
        }

        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(3, 2)]
        [TestCase(4, 2)]
        [TestCase(5, 3)]
        [TestCase(15, 4)]
        [TestCase(16, 4)]
        [TestCase(17, 5)]
        [TestCase(int.MaxValue, 46341)]
        public void CeilingIntegerSquareRoot_CoversPerfectAdjacentAndMaximumValues(int value, int expected)
        {
            Assert.That(FPMath.CeilingIntegerSquareRoot(value), Is.EqualTo(expected));
        }

        [Test]
        public void CeilingIntegerSquareRoot_RejectsNegativeValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => FPMath.CeilingIntegerSquareRoot(-1));
        }

        [Test]
        public void Vector2_DotDetAndNormalize_AreDeterministic()
        {
            FPVector2 vector = new FPVector2(FP.FromInt(3), FP.FromInt(4));
            FPVector2 normalized = FPMath.NormalizeSafe(vector);

            Assert.That(vector.SqrMagnitude, Is.EqualTo(FP.FromInt(25)));
            Assert.That(vector.Magnitude, Is.EqualTo(FP.FromInt(5)));
            Assert.That(normalized.X, Is.EqualTo(FP.FromRatio(3, 5)));
            Assert.That(normalized.Y, Is.EqualTo(FP.FromRatio(4, 5)));
            Assert.That(FPMath.Dot(FPVector2.UnitX, FPVector2.UnitY), Is.EqualTo(FP.Zero));
            Assert.That(FPMath.Det(FPVector2.UnitX, FPVector2.UnitY), Is.EqualTo(FP.One));
        }

        [Test]
        public void NormalizeSafe_UsesFallbackForZeroOrTinyVectors()
        {
            FPVector2 fallback = FPVector2.UnitY;

            Assert.That(FPMath.NormalizeSafe(FPVector2.Zero), Is.EqualTo(FPVector2.Zero));
            Assert.That(FPMath.NormalizeSafe(FPVector2.Zero, fallback), Is.EqualTo(fallback));
            Assert.That(
                FPMath.NormalizeSafe(new FPVector2(FP.Epsilon, FP.Zero), fallback, FP.FromRaw(2)),
                Is.EqualTo(fallback));
        }

        [Test]
        public void NormalizeSafe_LargeAndUnevenVectorsStayInsideUnitCircle()
        {
            FPVector2 uneven = FPMath.NormalizeSafe(
                new FPVector2(FP.FromInt(1), FP.FromInt(4)));
            FPVector2 extreme = FPMath.NormalizeSafe(
                new FPVector2(FP.MaxValue, FP.MaxValue));

            Assert.That(uneven.SqrMagnitude, Is.LessThanOrEqualTo(FP.One));
            Assert.That(extreme.SqrMagnitude, Is.LessThanOrEqualTo(FP.One));
            Assert.That(extreme.X, Is.GreaterThan(FP.Zero));
            Assert.That(extreme.Y, Is.EqualTo(extreme.X));
        }

        [Test]
        public void Vector3_CrossAndNormalize_ProduceExpectedBasis()
        {
            FPVector3 cross = FPMath.Cross(FPVector3.UnitX, FPVector3.UnitY);
            FPVector3 normalized = FPMath.NormalizeSafe(new FPVector3(FP.Zero, FP.Zero, FP.FromInt(7)));

            Assert.That(cross, Is.EqualTo(FPVector3.UnitZ));
            Assert.That(normalized, Is.EqualTo(FPVector3.UnitZ));
        }

        [Test]
        public void Lerp_IsUnclamped_AndLerpClampedRestrictsT()
        {
            Assert.That(FPMath.Lerp(FP.Zero, FP.FromInt(10), FP.FromRatio(3, 2)), Is.EqualTo(FP.FromInt(15)));
            Assert.That(FPMath.LerpClamped(FP.Zero, FP.FromInt(10), FP.FromRatio(3, 2)), Is.EqualTo(FP.FromInt(10)));
        }

        [Test]
        public void XorShift32_SameSeedProducesIdenticalSequence()
        {
            XorShift32 first = new XorShift32(0x12345678U);
            XorShift32 second = new XorShift32(0x12345678U);

            for (int index = 0; index < 1024; index++)
            {
                Assert.That(first.NextUInt(), Is.EqualTo(second.NextUInt()));
            }

            Assert.That(first.State, Is.EqualTo(second.State));
        }

        [Test]
        public void XorShift32_KnownSeedMatchesReferenceSequence()
        {
            XorShift32 random = new XorShift32(0x12345678U);

            Assert.That(random.NextUInt(), Is.EqualTo(0x87985AA5U));
            Assert.That(random.NextUInt(), Is.EqualTo(0x155B24A3U));
            Assert.That(random.NextUInt(), Is.EqualTo(0x4820F4C4U));
            Assert.That(random.NextUInt(), Is.EqualTo(0x81B3AC98U));
            Assert.That(random.NextUInt(), Is.EqualTo(0x703A0788U));
        }

        [Test]
        public void XorShift32_ZeroSeedIsRemappedAndStateCanBeRestored()
        {
            XorShift32 random = new XorShift32(0U);
            uint initialState = random.State;
            uint first = random.NextUInt();
            random.State = initialState;

            Assert.That(initialState, Is.EqualTo(XorShift32.DefaultNonZeroSeed));
            Assert.That(random.NextUInt(), Is.EqualTo(first));
        }

        [Test]
        public void XorShift32_RangedOutputsRespectExclusiveBounds()
        {
            XorShift32 random = new XorShift32(42U);

            for (int index = 0; index < 10000; index++)
            {
                int integer = random.NextInt(-17, 29);
                FP scalar = random.NextFP01();
                Assert.That(integer, Is.InRange(-17, 28));
                Assert.That(scalar, Is.GreaterThanOrEqualTo(FP.Zero));
                Assert.That(scalar, Is.LessThan(FP.One));
            }
        }

        [Test]
        public void Fnv1a32_DefaultValueAndKnownByteVectorMatchReferenceHash()
        {
            Fnv1a32 hash = default;
            hash.AddBytes(new byte[] { 0x68, 0x65, 0x6c, 0x6c, 0x6f });

            Assert.That(hash.Value, Is.EqualTo(0x4F9F2CABU));
        }

        [Test]
        public void Fnv1a32_HashesFixedPointStateInStableLittleEndianOrder()
        {
            Fnv1a32 semanticHash = default;
            semanticHash.Add(FP.One);
            semanticHash.Add(new FPVector2(FP.FromRaw(2), FP.FromRaw(3)));

            Fnv1a32 rawHash = default;
            rawHash.Add(65536);
            rawHash.Add(2);
            rawHash.Add(3);

            Assert.That(semanticHash.Value, Is.EqualTo(rawHash.Value));
        }
    }
}
