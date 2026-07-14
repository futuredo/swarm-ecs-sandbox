using System;

namespace SwarmECS.Simulation
{

public readonly struct Entity : IEquatable<Entity>
{
    public static readonly Entity Invalid = new(-1, 0);

    public Entity(int index, ushort generation)
    {
        Index = index;
        Generation = generation;
    }

    public int Index { get; }

    public ushort Generation { get; }

    public bool IsValid => Index >= 0;

    public bool Equals(Entity other) => Index == other.Index && Generation == other.Generation;

    public override bool Equals(object obj) => obj is Entity other && Equals(other);

    public override int GetHashCode() => (Index * 397) ^ Generation;

    public static bool operator ==(Entity left, Entity right) => left.Equals(right);

    public static bool operator !=(Entity left, Entity right) => !left.Equals(right);
}

/// <summary>
/// Fixed-capacity entity allocator. All storage is reserved up front so Create/Destroy stay allocation-free.
/// </summary>
public sealed class EntityAllocator
{
    private readonly ushort[] _generations;
    private readonly bool[] _alive;
    private readonly int[] _freeStack;
    private int _nextIndex;
    private int _freeCount;

    public EntityAllocator(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        Capacity = capacity;
        _generations = new ushort[capacity];
        _alive = new bool[capacity];
        _freeStack = new int[capacity];
    }

    public int Capacity { get; }

    public int AliveCount { get; private set; }

    public Entity Create()
    {
        int index;
        if (_freeCount > 0)
        {
            index = _freeStack[--_freeCount];
        }
        else
        {
            if (_nextIndex >= Capacity)
            {
                return Entity.Invalid;
            }

            index = _nextIndex++;
        }

        _alive[index] = true;
        AliveCount++;
        return new Entity(index, _generations[index]);
    }

    public bool Destroy(Entity entity)
    {
        if (!IsAlive(entity))
        {
            return false;
        }

        _alive[entity.Index] = false;
        unchecked
        {
            _generations[entity.Index]++;
        }

        _freeStack[_freeCount++] = entity.Index;
        AliveCount--;
        return true;
    }

    public bool IsAlive(Entity entity)
    {
        int index = entity.Index;
        return (uint)index < (uint)Capacity && _alive[index] && _generations[index] == entity.Generation;
    }

    public void Reset()
    {
        Array.Clear(_alive, 0, _alive.Length);
        Array.Clear(_generations, 0, _generations.Length);
        _nextIndex = 0;
        _freeCount = 0;
        AliveCount = 0;
    }
}
}
