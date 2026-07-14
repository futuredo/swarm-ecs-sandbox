using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation.Systems
{

/// <summary>
/// Pure fixed-point steering constraint. It converts a collision-avoidance velocity
/// candidate into a velocity reachable within one logic tick without storing heading.
/// A stopped agent therefore has no turn history and may start toward either direction.
/// </summary>
public static class KinematicVelocityLimiter
{
    private static readonly FP ClampSafetyScale = FP.FromRaw(FP.OneRaw - 2);

    /// <summary>
    /// Limits turn and delta-velocity in that order. <paramref name="maxTurnStep"/>
    /// is a first-quadrant unit pair containing cos(delta angle) and sin(delta angle).
    /// The current velocity is expected to already satisfy <paramref name="maxSpeed"/>.
    /// </summary>
    public static FPVector2 Limit(
        int stableEntityId,
        FPVector2 currentVelocity,
        FPVector2 targetVelocity,
        FP maxSpeed,
        FP maxAcceleration,
        FP fixedDeltaTime,
        FPVector2 maxTurnStep,
        out bool accelerationLimited,
        out bool turnLimited)
    {
        FP safeMaxSpeed = FPMath.Max(maxSpeed, FP.Zero);
        FPVector2 reachableTarget = ClampMagnitude(targetVelocity, safeMaxSpeed);
        turnLimited = ShouldLimitTurn(currentVelocity, reachableTarget, maxTurnStep);

        if (turnLimited)
        {
            FPVector2 currentDirection = FPMath.NormalizeSafe(currentVelocity);
            FP targetSpeed = FPMath.Sqrt(FPMath.Max(reachableTarget.SqrMagnitude, FP.Zero));
            FP determinant = FPMath.Det(currentVelocity, reachableTarget);
            int turnSign = determinant > FP.Zero
                ? 1
                : determinant < FP.Zero
                    ? -1
                    : (stableEntityId & 1) == 0 ? 1 : -1;
            FP signedSin = turnSign > 0 ? maxTurnStep.Y : -maxTurnStep.Y;
            FPVector2 turnedDirection = new(
                (currentDirection.X * maxTurnStep.X) - (currentDirection.Y * signedSin),
                (currentDirection.X * signedSin) + (currentDirection.Y * maxTurnStep.X));
            reachableTarget = ClampMagnitude(turnedDirection * targetSpeed, safeMaxSpeed);
        }

        FP safeAcceleration = FPMath.Max(maxAcceleration, FP.Zero);
        FP safeDeltaTime = FPMath.Max(fixedDeltaTime, FP.Zero);
        FP maxVelocityDelta = safeAcceleration * safeDeltaTime;
        FPVector2 velocityDelta = reachableTarget - currentVelocity;
        FPVector2 limitedDelta = ClampMagnitude(velocityDelta, maxVelocityDelta);
        accelerationLimited = limitedDelta != velocityDelta;
        return currentVelocity + limitedDelta;
    }

    private static bool ShouldLimitTurn(
        FPVector2 currentVelocity,
        FPVector2 targetVelocity,
        FPVector2 maxTurnStep)
    {
        if (currentVelocity.SqrMagnitude <= FP.Epsilon ||
            targetVelocity.SqrMagnitude <= FP.Epsilon)
        {
            return false;
        }

        FP dot = FPMath.Dot(currentVelocity, targetVelocity);
        if (dot < FP.Zero)
        {
            return true;
        }

        // For a turn step below 90 degrees:
        // abs(sin(theta)) * cos(limit) > cos(theta) * sin(limit)
        // iff the unsigned angle theta exceeds the configured limit. The shared
        // velocity magnitudes cancel, so this comparison needs no square root.
        FP absoluteDeterminant = FPMath.Abs(FPMath.Det(currentVelocity, targetVelocity));
        return (absoluteDeterminant * maxTurnStep.X) > (dot * maxTurnStep.Y);
    }

    private static FPVector2 ClampMagnitude(FPVector2 value, FP maxMagnitude)
    {
        if (maxMagnitude <= FP.Zero)
        {
            return FPVector2.Zero;
        }

        FP maxSquared = maxMagnitude * maxMagnitude;
        FP squaredLength = value.SqrMagnitude;
        if (squaredLength <= maxSquared)
        {
            return value;
        }

        FPVector2 direction = FPMath.NormalizeSafe(value);
        if (direction == FPVector2.Zero)
        {
            return FPVector2.Zero;
        }

        // Normalize first so axis-aligned deltas such as (0, -6) retain their exact
        // unit direction before the magnitude is applied. Computing max/length first
        // would truncate 1/3 and turn an exact 2-unit budget into 1.99997.
        FPVector2 clamped = direction * maxMagnitude;
        if (clamped.SqrMagnitude > maxSquared)
        {
            clamped = clamped * ClampSafetyScale;
        }

        return clamped;
    }
}
}
