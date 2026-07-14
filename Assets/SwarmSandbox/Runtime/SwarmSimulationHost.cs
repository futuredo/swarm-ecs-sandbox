using System;
using System.Diagnostics;
using SwarmECS.FixedPoint;
using SwarmECS.Runtime.Rendering;
using SwarmECS.Simulation;
using SwarmECS.Simulation.Netcode;
using SwarmECS.Simulation.Systems;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SwarmECS.Runtime
{
    [DisallowMultipleComponent]
    public sealed class SwarmSimulationHost : MonoBehaviour
    {
        private const int FixedRate = 30;
        private const float FixedSeconds = 1f / FixedRate;

        [SerializeField, Range(100, 10_000)] private int agentCount = 10_000;
        [SerializeField] private uint deterministicSeed = 0x5EED1234u;
        [SerializeField, Range(1, 16)] private int maxRealtimeTicksPerFrame = 4;
        [SerializeField, Range(1, 64)] private int maxCatchUpTicksPerFrame = 24;
        [SerializeField, Range(1, 60)] private int simulatedLatencyTicks = 18;

        private SwarmWorld _world;
        private SwarmSimulation _simulation;
        private RollbackController _rollback;
        private SwarmIndirectRenderer _renderer;
        private float _accumulator;
        private int _catchUpBacklog;
        private double _simulationMilliseconds;
        private long _lastAllocatedBytes;
        private ulong _currentHash;
        private bool _paused;
        private bool _determinismProbePassed;
        private int _renderFrameCount;
        private float _fpsWindowStart;
        private float _measuredFps;

        public SwarmWorld World => _world;

        public SwarmSimulation Simulation => _simulation;

        public RollbackController Rollback => _rollback;

        public int AgentCount => _world?.Count ?? 0;

        public int SimulationTick => _world?.Tick ?? 0;

        public int FixedRateHz => FixedRate;

        public int CatchUpBacklog => _catchUpBacklog;

        public int SimulatedLatencyTicks => simulatedLatencyTicks;

        public bool IsPaused => _paused;

        public bool DeterminismProbePassed => _determinismProbePassed;

        public double SimulationMilliseconds => _simulationMilliseconds;

        public long LastAllocatedBytes => _lastAllocatedBytes;

        public ulong CurrentHash => _currentHash;

        public float MeasuredFps => _measuredFps;

        private void Awake()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 120;
            Application.runInBackground = true;

            if (GetComponent<SwarmDebugHud>() == null)
            {
                gameObject.AddComponent<SwarmDebugHud>();
            }
        }

        private void Start()
        {
            Initialize(agentCount);
            _determinismProbePassed = RunDeterminismProbe();
            _fpsWindowStart = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            HandleKeyboard();
            UpdateFpsCounter();

            if (_world == null || _paused)
            {
                return;
            }

            if (_catchUpBacklog > 0)
            {
                int count = Mathf.Min(_catchUpBacklog, maxCatchUpTicksPerFrame);
                for (int i = 0; i < count; i++)
                {
                    StepMeasured();
                }

                _catchUpBacklog -= count;
                return;
            }

            _accumulator += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            int ticks = 0;
            while (_accumulator >= FixedSeconds && ticks < maxRealtimeTicksPerFrame)
            {
                StepMeasured();
                _accumulator -= FixedSeconds;
                ticks++;
            }
        }

        private void LateUpdate()
        {
            if (_catchUpBacklog == 0)
            {
                _renderer?.Render(_world);
            }
        }

        private void OnDestroy()
        {
            _renderer?.Dispose();
            _renderer = null;
            _simulation?.Dispose();
            _simulation = null;
        }

        public void TogglePause()
        {
            _paused = !_paused;
        }

        public void ResetSimulation()
        {
            Initialize(agentCount);
        }

        public void ToggleSpatialIndex()
        {
            if (_simulation == null)
            {
                return;
            }

            _simulation.Avoidance.Mode = _simulation.Avoidance.Mode == SpatialIndexMode.UniformGrid
                ? SpatialIndexMode.KdTree
                : SpatialIndexMode.UniformGrid;
        }

        public void QueueCatchUp(int ticks = 600)
        {
            _catchUpBacklog += Mathf.Max(1, ticks);
        }

        public void InjectLateCorrection()
        {
            if (_world == null || _world.Tick <= simulatedLatencyTicks)
            {
                return;
            }

            int correctionIndex = _rollback.RollbackCount;
            int group = correctionIndex & 3;
            FPVector2[] targets =
            {
                new(FP.FromInt(62), FP.FromInt(-20)),
                new(FP.FromInt(-20), FP.FromInt(62)),
                new(FP.FromInt(-62), FP.FromInt(20)),
                new(FP.FromInt(20), FP.FromInt(-62)),
            };
            _rollback.InjectLateGroupTarget(simulatedLatencyTicks, group, targets[group]);
            _currentHash = _world.ComputeStateHash();
        }

        public void SetAgentCount(int count)
        {
            agentCount = Mathf.Clamp(count, 100, 10_000);
            Initialize(agentCount);
        }

        private void Initialize(int count)
        {
            _renderer?.Dispose();
            _simulation?.Dispose();
            SwarmConfig config = SwarmConfig.PortfolioDefault(count);
            _world = new SwarmWorld(config);
            _world.InitializeDeterministicFormation(count, deterministicSeed);
            _simulation = new SwarmSimulation(_world);
            _rollback = new RollbackController(_world, _simulation, 64, 512);
            _renderer = new SwarmIndirectRenderer(count, (float)config.WorldHalfExtent.ToDouble());
            _renderer.SetObstacles(_simulation.Obstacles.Obstacles);
            _accumulator = 0f;
            _catchUpBacklog = 0;
            _simulationMilliseconds = 0d;
            _lastAllocatedBytes = 0;
            _currentHash = _world.ComputeStateHash();
            _paused = false;
        }

        private void StepMeasured()
        {
            long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            _rollback.Step();
            long elapsed = Stopwatch.GetTimestamp() - start;
            _lastAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
            double milliseconds = elapsed * 1000d / Stopwatch.Frequency;
            _simulationMilliseconds = _simulationMilliseconds <= 0d
                ? milliseconds
                : _simulationMilliseconds * 0.9d + milliseconds * 0.1d;

            if ((_world.Tick & 15) == 0)
            {
                _currentHash = _world.ComputeStateHash();
            }
        }

        private bool RunDeterminismProbe()
        {
            const int probeCount = 256;
            SwarmConfig config = SwarmConfig.PortfolioDefault(probeCount);
            SwarmWorld first = new(config);
            SwarmWorld second = new(config);
            first.InitializeDeterministicFormation(probeCount, deterministicSeed);
            second.InitializeDeterministicFormation(probeCount, deterministicSeed);
            SwarmSimulation firstSimulation = new(first);
            SwarmSimulation secondSimulation = new(second);
            try
            {
                for (int tick = 0; tick < 24; tick++)
                {
                    firstSimulation.Step(first);
                    secondSimulation.Step(second);
                    first.AdvanceTick();
                    second.AdvanceTick();
                }

                bool passed = first.ComputeStateHash() == second.ComputeStateHash();
                Debug.Log($"[SwarmECS] deterministic twin-world probe: {(passed ? "PASS" : "FAIL")}");
                return passed;
            }
            finally
            {
                firstSimulation.Dispose();
                secondSimulation.Dispose();
            }
        }

        private void HandleKeyboard()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                TogglePause();
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                ResetSimulation();
            }

            if (Input.GetKeyDown(KeyCode.L))
            {
                InjectLateCorrection();
            }

            if (Input.GetKeyDown(KeyCode.T))
            {
                QueueCatchUp();
            }

            if (Input.GetKeyDown(KeyCode.K))
            {
                ToggleSpatialIndex();
            }
        }

        private void UpdateFpsCounter()
        {
            _renderFrameCount++;
            float now = Time.realtimeSinceStartup;
            float elapsed = now - _fpsWindowStart;
            if (elapsed < 0.5f)
            {
                return;
            }

            _measuredFps = _renderFrameCount / elapsed;
            _renderFrameCount = 0;
            _fpsWindowStart = now;
        }
    }
}
