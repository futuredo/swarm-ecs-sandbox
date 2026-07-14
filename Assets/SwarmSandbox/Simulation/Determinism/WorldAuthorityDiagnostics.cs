using System;
using System.Globalization;
using SwarmECS.Simulation.Pathfinding;

namespace SwarmECS.Simulation.Determinism
{
    /// <summary>Stable component domains used by layered authority hashes and desync reports.</summary>
    public enum WorldAuthorityComponent : byte
    {
        None = 0,
        Config = 1,
        WorldMetadata = 2,
        GroupTargets = 3,
        GroupPathStates = 4,
        NavigationRequestSequence = 5,
        AgentPositions = 6,
        AgentVelocities = 7,
        AgentPathCursors = 8,
        Full = 9,
    }

    /// <summary>Interpretation of the raw bits carried by a desync difference.</summary>
    public enum WorldAuthorityRawKind : byte
    {
        None = 0,
        Int32 = 1,
        UInt32 = 2,
        UInt64 = 3,
    }

    /// <summary>Component-level hashes plus the complete schema-ordered authority hash.</summary>
    public readonly struct WorldAuthorityHashes
    {
        internal WorldAuthorityHashes(
            ulong config,
            ulong worldMetadata,
            ulong groupTargets,
            ulong groupPathStates,
            ulong navigationRequestSequence,
            ulong agentPositions,
            ulong agentVelocities,
            ulong agentPathCursors,
            ulong full)
        {
            Config = config;
            WorldMetadata = worldMetadata;
            GroupTargets = groupTargets;
            GroupPathStates = groupPathStates;
            NavigationRequestSequence = navigationRequestSequence;
            AgentPositions = agentPositions;
            AgentVelocities = agentVelocities;
            AgentPathCursors = agentPathCursors;
            Full = full;
        }

        public ulong Config { get; }

        public ulong WorldMetadata { get; }

        public ulong GroupTargets { get; }

        public ulong GroupPathStates { get; }

        public ulong NavigationRequestSequence { get; }

        public ulong AgentPositions { get; }

        public ulong AgentVelocities { get; }

        public ulong AgentPathCursors { get; }

        public ulong Full { get; }

        public ulong Get(WorldAuthorityComponent component)
        {
            return component switch
            {
                WorldAuthorityComponent.Config => Config,
                WorldAuthorityComponent.WorldMetadata => WorldMetadata,
                WorldAuthorityComponent.GroupTargets => GroupTargets,
                WorldAuthorityComponent.GroupPathStates => GroupPathStates,
                WorldAuthorityComponent.NavigationRequestSequence => NavigationRequestSequence,
                WorldAuthorityComponent.AgentPositions => AgentPositions,
                WorldAuthorityComponent.AgentVelocities => AgentVelocities,
                WorldAuthorityComponent.AgentPathCursors => AgentPathCursors,
                WorldAuthorityComponent.Full => Full,
                _ => throw new ArgumentOutOfRangeException(nameof(component)),
            };
        }
    }

    /// <summary>The first differing authoritative scalar in stable schema order.</summary>
    public readonly struct WorldDesyncDifference
    {
        internal WorldDesyncDifference(
            WorldAuthorityComponent component,
            int entityIndex,
            string fieldName,
            WorldAuthorityRawKind rawKind,
            ulong expectedRaw,
            ulong actualRaw)
        {
            Component = component;
            EntityIndex = entityIndex;
            FieldName = fieldName;
            RawKind = rawKind;
            ExpectedRaw = expectedRaw;
            ActualRaw = actualRaw;
        }

        public bool HasDifference => Component != WorldAuthorityComponent.None;

        public WorldAuthorityComponent Component { get; }

        public string ComponentName => WorldAuthoritySchema.GetComponentName(Component);

        /// <summary>
        /// Group index for group components, agent index for agent components, or -1
        /// for singleton config/world/navigation fields.
        /// </summary>
        public int EntityIndex { get; }

        public string FieldName { get; }

        public WorldAuthorityRawKind RawKind { get; }

        public ulong ExpectedRaw { get; }

        public ulong ActualRaw { get; }

        public int ExpectedInt32 => unchecked((int)(uint)ExpectedRaw);

        public int ActualInt32 => unchecked((int)(uint)ActualRaw);

        public uint ExpectedUInt32 => (uint)ExpectedRaw;

        public uint ActualUInt32 => (uint)ActualRaw;

        public ulong ExpectedUInt64 => ExpectedRaw;

        public ulong ActualUInt64 => ActualRaw;

        public override string ToString()
        {
            if (!HasDifference)
            {
                return "No authoritative difference";
            }

            string entity = EntityIndex < 0
                ? "none"
                : EntityIndex.ToString(CultureInfo.InvariantCulture);
            return ComponentName + "[entity=" + entity + "]." + FieldName +
                " expected=" + FormatRaw(ExpectedRaw, RawKind) +
                " actual=" + FormatRaw(ActualRaw, RawKind);
        }

        private static string FormatRaw(ulong raw, WorldAuthorityRawKind kind)
        {
            return kind switch
            {
                WorldAuthorityRawKind.Int32 =>
                    unchecked((int)(uint)raw).ToString(CultureInfo.InvariantCulture) +
                    " (0x" + ((uint)raw).ToString("X8", CultureInfo.InvariantCulture) + ")",
                WorldAuthorityRawKind.UInt32 =>
                    ((uint)raw).ToString(CultureInfo.InvariantCulture) +
                    " (0x" + ((uint)raw).ToString("X8", CultureInfo.InvariantCulture) + ")",
                WorldAuthorityRawKind.UInt64 =>
                    raw.ToString(CultureInfo.InvariantCulture) +
                    " (0x" + raw.ToString("X16", CultureInfo.InvariantCulture) + ")",
                _ => "undefined",
            };
        }
    }

    /// <summary>
    /// Computes layered FNV-1a hashes and pinpoints the first scalar mismatch in a
    /// versioned, component-first comparison order. It reads only authoritative World state.
    /// </summary>
    public static class WorldAuthorityDiagnostics
    {
        public const int SchemaVersion = 1;

        public static WorldAuthorityHashes ComputeHashes(SwarmWorld world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            Fnv1a64 config = new(WorldAuthorityComponent.Config);
            Fnv1a64 worldMetadata = new(WorldAuthorityComponent.WorldMetadata);
            Fnv1a64 groupTargets = new(WorldAuthorityComponent.GroupTargets);
            Fnv1a64 groupPathStates = new(WorldAuthorityComponent.GroupPathStates);
            Fnv1a64 navigationSequence = new(WorldAuthorityComponent.NavigationRequestSequence);
            Fnv1a64 agentPositions = new(WorldAuthorityComponent.AgentPositions);
            Fnv1a64 agentVelocities = new(WorldAuthorityComponent.AgentVelocities);
            Fnv1a64 agentPathCursors = new(WorldAuthorityComponent.AgentPathCursors);
            Fnv1a64 full = new(WorldAuthorityComponent.Full);

            config.AddUInt64(world.Config.ConfigHash);
            full.AddUInt64(world.Config.ConfigHash);

            AddWorldMetadata(ref worldMetadata, world);
            AddWorldMetadata(ref full, world);

            for (int group = 0; group < SwarmWorld.GroupCount; group++)
            {
                groupTargets.AddInt32(world.GroupTargets[group].X.Raw);
                groupTargets.AddInt32(world.GroupTargets[group].Y.Raw);
                full.AddInt32(world.GroupTargets[group].X.Raw);
                full.AddInt32(world.GroupTargets[group].Y.Raw);
            }

            for (int group = 0; group < SwarmWorld.GroupCount; group++)
            {
                GroupPathState state = world.GroupPathStates[group];
                AddGroupPathState(ref groupPathStates, state);
                AddGroupPathState(ref full, state);
            }

            navigationSequence.AddInt32(world.NextPathRequestSequence);
            full.AddInt32(world.NextPathRequestSequence);

            for (int entity = 0; entity < world.Count; entity++)
            {
                agentPositions.AddInt32(world.Positions[entity].X.Raw);
                agentPositions.AddInt32(world.Positions[entity].Y.Raw);
                full.AddInt32(world.Positions[entity].X.Raw);
                full.AddInt32(world.Positions[entity].Y.Raw);
            }

            for (int entity = 0; entity < world.Count; entity++)
            {
                agentVelocities.AddInt32(world.Velocities[entity].X.Raw);
                agentVelocities.AddInt32(world.Velocities[entity].Y.Raw);
                full.AddInt32(world.Velocities[entity].X.Raw);
                full.AddInt32(world.Velocities[entity].Y.Raw);
            }

            for (int entity = 0; entity < world.Count; entity++)
            {
                agentPathCursors.AddUInt32(world.PathCursors[entity]);
                full.AddUInt32(world.PathCursors[entity]);
            }

            return new WorldAuthorityHashes(
                config.Value,
                worldMetadata.Value,
                groupTargets.Value,
                groupPathStates.Value,
                navigationSequence.Value,
                agentPositions.Value,
                agentVelocities.Value,
                agentPathCursors.Value,
                full.Value);
        }

        public static bool TryFindFirstDifference(
            SwarmWorld expected,
            SwarmWorld actual,
            out WorldDesyncDifference difference)
        {
            if (expected == null)
            {
                throw new ArgumentNullException(nameof(expected));
            }

            if (actual == null)
            {
                throw new ArgumentNullException(nameof(actual));
            }

            if (TryUInt64(
                WorldAuthorityComponent.Config,
                -1,
                "ConfigHash",
                expected.Config.ConfigHash,
                actual.Config.ConfigHash,
                out difference) ||
                TryInt32(WorldAuthorityComponent.WorldMetadata, -1, "Tick", expected.Tick, actual.Tick, out difference) ||
                TryInt32(WorldAuthorityComponent.WorldMetadata, -1, "Count", expected.Count, actual.Count, out difference) ||
                TryUInt32(WorldAuthorityComponent.WorldMetadata, -1, "Seed", expected.Seed, actual.Seed, out difference) ||
                TryUInt32(
                    WorldAuthorityComponent.WorldMetadata,
                    -1,
                    "SpatialIndexMode",
                    (uint)expected.SpatialIndexMode,
                    (uint)actual.SpatialIndexMode,
                    out difference))
            {
                return true;
            }

            for (int group = 0; group < SwarmWorld.GroupCount; group++)
            {
                if (TryInt32(
                        WorldAuthorityComponent.GroupTargets,
                        group,
                        "X.Raw",
                        expected.GroupTargets[group].X.Raw,
                        actual.GroupTargets[group].X.Raw,
                        out difference) ||
                    TryInt32(
                        WorldAuthorityComponent.GroupTargets,
                        group,
                        "Y.Raw",
                        expected.GroupTargets[group].Y.Raw,
                        actual.GroupTargets[group].Y.Raw,
                        out difference))
                {
                    return true;
                }
            }

            for (int group = 0; group < SwarmWorld.GroupCount; group++)
            {
                GroupPathState expectedState = expected.GroupPathStates[group];
                GroupPathState actualState = actual.GroupPathStates[group];
                if (TryInt32(WorldAuthorityComponent.GroupPathStates, group, "ResolvedStartIndex", expectedState.ResolvedStartIndex, actualState.ResolvedStartIndex, out difference) ||
                    TryInt32(WorldAuthorityComponent.GroupPathStates, group, "ResolvedGoalIndex", expectedState.ResolvedGoalIndex, actualState.ResolvedGoalIndex, out difference) ||
                    TryInt32(WorldAuthorityComponent.GroupPathStates, group, "ResolvedMapRevision", expectedState.ResolvedMapRevision, actualState.ResolvedMapRevision, out difference) ||
                    TryUInt32(WorldAuthorityComponent.GroupPathStates, group, "Status", (uint)expectedState.Status, (uint)actualState.Status, out difference) ||
                    TryInt32(WorldAuthorityComponent.GroupPathStates, group, "PendingStartIndex", expectedState.PendingStartIndex, actualState.PendingStartIndex, out difference) ||
                    TryInt32(WorldAuthorityComponent.GroupPathStates, group, "PendingGoalIndex", expectedState.PendingGoalIndex, actualState.PendingGoalIndex, out difference) ||
                    TryInt32(WorldAuthorityComponent.GroupPathStates, group, "PendingMapRevision", expectedState.PendingMapRevision, actualState.PendingMapRevision, out difference) ||
                    TryInt32(WorldAuthorityComponent.GroupPathStates, group, "PendingSequence", expectedState.PendingSequence, actualState.PendingSequence, out difference))
                {
                    return true;
                }
            }

            if (TryInt32(
                WorldAuthorityComponent.NavigationRequestSequence,
                -1,
                "NextPathRequestSequence",
                expected.NextPathRequestSequence,
                actual.NextPathRequestSequence,
                out difference))
            {
                return true;
            }

            int comparableCount = expected.Count < actual.Count ? expected.Count : actual.Count;
            for (int entity = 0; entity < comparableCount; entity++)
            {
                if (TryInt32(WorldAuthorityComponent.AgentPositions, entity, "X.Raw", expected.Positions[entity].X.Raw, actual.Positions[entity].X.Raw, out difference) ||
                    TryInt32(WorldAuthorityComponent.AgentPositions, entity, "Y.Raw", expected.Positions[entity].Y.Raw, actual.Positions[entity].Y.Raw, out difference))
                {
                    return true;
                }
            }

            for (int entity = 0; entity < comparableCount; entity++)
            {
                if (TryInt32(WorldAuthorityComponent.AgentVelocities, entity, "X.Raw", expected.Velocities[entity].X.Raw, actual.Velocities[entity].X.Raw, out difference) ||
                    TryInt32(WorldAuthorityComponent.AgentVelocities, entity, "Y.Raw", expected.Velocities[entity].Y.Raw, actual.Velocities[entity].Y.Raw, out difference))
                {
                    return true;
                }
            }

            for (int entity = 0; entity < comparableCount; entity++)
            {
                if (TryUInt32(
                    WorldAuthorityComponent.AgentPathCursors,
                    entity,
                    "PathCursor",
                    expected.PathCursors[entity],
                    actual.PathCursors[entity],
                    out difference))
                {
                    return true;
                }
            }

            difference = default;
            return false;
        }

        private static void AddWorldMetadata(ref Fnv1a64 hash, SwarmWorld world)
        {
            hash.AddInt32(world.Tick);
            hash.AddInt32(world.Count);
            hash.AddUInt32(world.Seed);
            hash.AddUInt32((uint)world.SpatialIndexMode);
        }

        private static void AddGroupPathState(ref Fnv1a64 hash, GroupPathState state)
        {
            hash.AddInt32(state.ResolvedStartIndex);
            hash.AddInt32(state.ResolvedGoalIndex);
            hash.AddInt32(state.ResolvedMapRevision);
            hash.AddUInt32((uint)state.Status);
            hash.AddInt32(state.PendingStartIndex);
            hash.AddInt32(state.PendingGoalIndex);
            hash.AddInt32(state.PendingMapRevision);
            hash.AddInt32(state.PendingSequence);
        }

        private static bool TryInt32(
            WorldAuthorityComponent component,
            int entityIndex,
            string fieldName,
            int expected,
            int actual,
            out WorldDesyncDifference difference)
        {
            if (expected == actual)
            {
                difference = default;
                return false;
            }

            difference = new WorldDesyncDifference(
                component,
                entityIndex,
                fieldName,
                WorldAuthorityRawKind.Int32,
                unchecked((uint)expected),
                unchecked((uint)actual));
            return true;
        }

        private static bool TryUInt32(
            WorldAuthorityComponent component,
            int entityIndex,
            string fieldName,
            uint expected,
            uint actual,
            out WorldDesyncDifference difference)
        {
            if (expected == actual)
            {
                difference = default;
                return false;
            }

            difference = new WorldDesyncDifference(
                component,
                entityIndex,
                fieldName,
                WorldAuthorityRawKind.UInt32,
                expected,
                actual);
            return true;
        }

        private static bool TryUInt64(
            WorldAuthorityComponent component,
            int entityIndex,
            string fieldName,
            ulong expected,
            ulong actual,
            out WorldDesyncDifference difference)
        {
            if (expected == actual)
            {
                difference = default;
                return false;
            }

            difference = new WorldDesyncDifference(
                component,
                entityIndex,
                fieldName,
                WorldAuthorityRawKind.UInt64,
                expected,
                actual);
            return true;
        }

        private struct Fnv1a64
        {
            private const ulong OffsetBasis = 14695981039346656037UL;
            private const ulong Prime = 1099511628211UL;
            private ulong _value;

            public Fnv1a64(WorldAuthorityComponent domain)
            {
                _value = OffsetBasis;
                AddInt32(SchemaVersion);
                AddUInt32((uint)domain);
            }

            public ulong Value => _value;

            public void AddInt32(int value)
            {
                AddUInt32(unchecked((uint)value));
            }

            public void AddUInt32(uint value)
            {
                AddByte((byte)value);
                AddByte((byte)(value >> 8));
                AddByte((byte)(value >> 16));
                AddByte((byte)(value >> 24));
            }

            public void AddUInt64(ulong value)
            {
                AddUInt32((uint)value);
                AddUInt32((uint)(value >> 32));
            }

            private void AddByte(byte value)
            {
                unchecked
                {
                    _value = (_value ^ value) * Prime;
                }
            }
        }
    }

    internal static class WorldAuthoritySchema
    {
        public static string GetComponentName(WorldAuthorityComponent component)
        {
            return component switch
            {
                WorldAuthorityComponent.Config => "Config",
                WorldAuthorityComponent.WorldMetadata => "WorldMetadata",
                WorldAuthorityComponent.GroupTargets => "GroupTargets",
                WorldAuthorityComponent.GroupPathStates => "GroupPathStates",
                WorldAuthorityComponent.NavigationRequestSequence => "NavigationRequestSequence",
                WorldAuthorityComponent.AgentPositions => "AgentPositions",
                WorldAuthorityComponent.AgentVelocities => "AgentVelocities",
                WorldAuthorityComponent.AgentPathCursors => "AgentPathCursors",
                WorldAuthorityComponent.Full => "Full",
                _ => "None",
            };
        }
    }
}
