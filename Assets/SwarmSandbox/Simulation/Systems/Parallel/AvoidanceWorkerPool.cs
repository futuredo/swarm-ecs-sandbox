using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation.Avoidance;

namespace SwarmECS.Simulation.Systems.Parallel
{
    /// <summary>
    /// Per-lane storage for allocation-free UniformGrid + ORCA execution.
    /// Every array is private to exactly one execution lane.
    /// </summary>
    internal sealed class AvoidanceWorkerScratch
    {
        public AvoidanceWorkerScratch(int capacity, int maxNeighbors)
        {
            long requestedQueryResults = (long)maxNeighbors + 1L;
            QueryLimit = requestedQueryResults < capacity
                ? (int)requestedQueryResults
                : capacity;
            QueryEntityIds = new int[QueryLimit];
            QueryDistances = new FP[QueryLimit];
            Neighbors = new AgentNeighbor[maxNeighbors];
            Lines = new OrcaLine[maxNeighbors];
            ProjectionLines = new OrcaLine[maxNeighbors];
        }

        public int[] QueryEntityIds { get; }

        public FP[] QueryDistances { get; }

        public AgentNeighbor[] Neighbors { get; }

        public OrcaLine[] Lines { get; }

        public OrcaLine[] ProjectionLines { get; }

        public int QueryLimit { get; }
    }

    /// <summary>
    /// Fixed set of background threads. Work is signaled with reusable events;
    /// no Task, Parallel.For, delegate, or managed object is created per tick.
    /// </summary>
    internal sealed class AvoidanceWorkerPool : IDisposable
    {
        private readonly NeighborAvoidanceSystem _owner;
        private readonly Worker[] _workers;
        private SwarmWorld _world;
        private int _stopping;
        private int _disposed;

        public AvoidanceWorkerPool(
            NeighborAvoidanceSystem owner,
            int backgroundWorkerCount,
            int capacity,
            int maxNeighbors)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (backgroundWorkerCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(backgroundWorkerCount));
            }

            _owner = owner;
            _workers = new Worker[backgroundWorkerCount];

            int started = 0;
            try
            {
                for (int i = 0; i < _workers.Length; ++i)
                {
                    Worker worker = new Worker(this, i, capacity, maxNeighbors);
                    _workers[i] = worker;
                    worker.Start();
                    ++started;
                }
            }
            catch
            {
                Volatile.Write(ref _stopping, 1);
                for (int i = 0; i < started; ++i)
                {
                    _workers[i].RequestStop();
                }

                for (int i = 0; i < started; ++i)
                {
                    _workers[i].JoinAndDispose();
                }

                throw;
            }
        }

        public int BackgroundWorkerCount => _workers.Length;

        private bool IsStopping => Volatile.Read(ref _stopping) != 0;

        public void Execute(
            SwarmWorld world,
            AvoidanceWorkerScratch mainScratch,
            out int neighborLinks,
            out int orcaLines)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (mainScratch == null)
            {
                throw new ArgumentNullException(nameof(mainScratch));
            }

            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(AvoidanceWorkerPool));
            }

            _world = world;
            int laneCount = _workers.Length + 1;

            // Lane zero remains on the caller. Every background thread owns one
            // deterministic contiguous range and writes only its own velocity slots.
            for (int i = 0; i < _workers.Length; ++i)
            {
                GetRange(world.Count, i + 1, laneCount, out int start, out int end);
                _workers[i].Prepare(start, end);
                _workers[i].SignalWork();
            }

            GetRange(world.Count, 0, laneCount, out int mainStart, out int mainEnd);
            ExceptionDispatchInfo failure = null;
            neighborLinks = 0;
            orcaLines = 0;

            try
            {
                _owner.ExecuteUniformGridRange(
                    world,
                    mainStart,
                    mainEnd,
                    mainScratch,
                    out neighborLinks,
                    out orcaLines);
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }

            for (int i = 0; i < _workers.Length; ++i)
            {
                Worker worker = _workers[i];
                worker.WaitForCompletion();
                neighborLinks += worker.NeighborLinks;
                orcaLines += worker.OrcaLines;

                if (failure == null && worker.Failure != null)
                {
                    failure = worker.Failure;
                }
            }

            _world = null;
            if (failure != null)
            {
                failure.Throw();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Volatile.Write(ref _stopping, 1);
            for (int i = 0; i < _workers.Length; ++i)
            {
                _workers[i].RequestStop();
            }

            for (int i = 0; i < _workers.Length; ++i)
            {
                _workers[i].JoinAndDispose();
            }

            _world = null;
        }

        private static void GetRange(int count, int lane, int laneCount, out int start, out int end)
        {
            start = (int)(((long)count * lane) / laneCount);
            end = (int)(((long)count * (lane + 1)) / laneCount);
        }

        private sealed class Worker
        {
            private readonly AvoidanceWorkerPool _pool;
            private readonly AutoResetEvent _workSignal;
            private readonly AutoResetEvent _completionSignal;
            private readonly Thread _thread;
            private readonly AvoidanceWorkerScratch _scratch;
            private int _start;
            private int _end;

            public Worker(
                AvoidanceWorkerPool pool,
                int index,
                int capacity,
                int maxNeighbors)
            {
                _pool = pool;
                _workSignal = new AutoResetEvent(false);
                _completionSignal = new AutoResetEvent(false);
                _scratch = new AvoidanceWorkerScratch(capacity, maxNeighbors);
                _thread = new Thread(Run)
                {
                    IsBackground = true,
                    Name = "Swarm ORCA " + index
                };
            }

            public int NeighborLinks { get; private set; }

            public int OrcaLines { get; private set; }

            public ExceptionDispatchInfo Failure { get; private set; }

            public void Start()
            {
                _thread.Start();
            }

            public void Prepare(int start, int end)
            {
                _start = start;
                _end = end;
                NeighborLinks = 0;
                OrcaLines = 0;
                Failure = null;
            }

            public void SignalWork()
            {
                _workSignal.Set();
            }

            public void WaitForCompletion()
            {
                _completionSignal.WaitOne();
            }

            public void RequestStop()
            {
                _workSignal.Set();
            }

            public void JoinAndDispose()
            {
                _thread.Join();
                _workSignal.Dispose();
                _completionSignal.Dispose();
            }

            private void Run()
            {
                while (true)
                {
                    _workSignal.WaitOne();
                    if (_pool.IsStopping)
                    {
                        return;
                    }

                    try
                    {
                        _pool._owner.ExecuteUniformGridRange(
                            _pool._world,
                            _start,
                            _end,
                            _scratch,
                            out int neighborLinks,
                            out int orcaLines);
                        NeighborLinks = neighborLinks;
                        OrcaLines = orcaLines;
                    }
                    catch (Exception exception)
                    {
                        Failure = ExceptionDispatchInfo.Capture(exception);
                    }
                    finally
                    {
                        _completionSignal.Set();
                    }
                }
            }
        }
    }
}
