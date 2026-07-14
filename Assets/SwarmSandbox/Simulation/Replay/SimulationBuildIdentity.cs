namespace SwarmECS.Simulation.Replay
{
    /// <summary>
    /// Manually versioned identity for authoritative simulation behavior. This value
    /// must change whenever a build can produce different state from the same replay.
    /// It is deliberately independent from the replay container schema version.
    /// </summary>
    public static class SimulationBuildIdentity
    {
        public const ulong CurrentLogicHash = 0x35B82A1C03E5E1B7UL;

        public const string ReplayFileExtension = ".swarmreplay";
    }
}
