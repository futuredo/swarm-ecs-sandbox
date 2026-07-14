using System;
using System.Runtime.InteropServices;
using SwarmECS.Simulation;
using SwarmECS.Simulation.Collision;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwarmECS.Runtime.Rendering
{

/// <summary>
/// One indirect draw call for every simulated agent. No per-agent GameObjects are created.
/// </summary>
public sealed class SwarmIndirectRenderer : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct AgentGpuData
    {
        public Vector4 PositionVelocity;
        public Vector4 Metadata;
    }

    private static readonly int AgentDataId = Shader.PropertyToID("_AgentData");
    private static readonly int AgentScaleId = Shader.PropertyToID("_AgentScale");

    private readonly int _capacity;
    private readonly AgentGpuData[] _uploadData;
    private readonly uint[] _indirectArgs = new uint[5];
    private readonly MaterialPropertyBlock _properties;
    private readonly Bounds _bounds;
    private readonly Mesh _agentMesh;
    private readonly Mesh _groundMesh;
    private readonly Mesh _obstacleMesh;
    private readonly Material _agentMaterial;
    private readonly Material _groundMaterial;
    private readonly Material _obstacleMaterial;
    private readonly Matrix4x4[] _obstacleMatrices = new Matrix4x4[32];
    private int _obstacleCount;
    private ComputeBuffer _agentBuffer;
    private ComputeBuffer _argsBuffer;
    private bool _disposed;

    public SwarmIndirectRenderer(int capacity, float worldHalfExtent)
    {
        _capacity = capacity;
        _uploadData = new AgentGpuData[capacity];
        _properties = new MaterialPropertyBlock();
        _bounds = new Bounds(Vector3.zero, new Vector3(worldHalfExtent * 2.4f, 12f, worldHalfExtent * 2.4f));

        Shader agentShader = Resources.Load<Shader>("SwarmIndirect");
        Shader groundShader = Resources.Load<Shader>("SwarmGround");
        Shader obstacleShader = Resources.Load<Shader>("SwarmObstacle");
        if (agentShader == null || groundShader == null || obstacleShader == null)
        {
            throw new InvalidOperationException("Swarm sandbox shaders were not found in Resources.");
        }

        _agentMaterial = new Material(agentShader)
        {
            name = "Swarm Agent Indirect (Runtime)",
            enableInstancing = true,
            hideFlags = HideFlags.DontSave,
        };
        _groundMaterial = new Material(groundShader)
        {
            name = "Swarm Ground (Runtime)",
            hideFlags = HideFlags.DontSave,
        };
        _obstacleMaterial = new Material(obstacleShader)
        {
            name = "Swarm SAT Obstacles (Runtime)",
            enableInstancing = true,
            hideFlags = HideFlags.DontSave,
        };
        _agentMesh = CreateAgentMesh();
        _groundMesh = CreateGroundMesh(worldHalfExtent);
        _obstacleMesh = CreateObstacleMesh();

        _agentBuffer = new ComputeBuffer(capacity, Marshal.SizeOf<AgentGpuData>(), ComputeBufferType.Structured);
        _argsBuffer = new ComputeBuffer(1, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);
        _indirectArgs[0] = _agentMesh.GetIndexCount(0);
        _indirectArgs[1] = 0;
        _indirectArgs[2] = _agentMesh.GetIndexStart(0);
        _indirectArgs[3] = _agentMesh.GetBaseVertex(0);
        _indirectArgs[4] = 0;
        _argsBuffer.SetData(_indirectArgs);

        _properties.SetBuffer(AgentDataId, _agentBuffer);
        _properties.SetFloat(AgentScaleId, 0.92f);
    }

    public void Render(SwarmWorld world)
    {
        if (_disposed || world == null || world.Count <= 0)
        {
            return;
        }

        int count = Math.Min(world.Count, _capacity);
        const float fixedPointToFloat = 1f / 65536f;
        for (int i = 0; i < count; i++)
        {
            _uploadData[i].PositionVelocity = new Vector4(
                world.Positions[i].X.Raw * fixedPointToFloat,
                world.Positions[i].Y.Raw * fixedPointToFloat,
                world.Velocities[i].X.Raw * fixedPointToFloat,
                world.Velocities[i].Y.Raw * fixedPointToFloat);
            _uploadData[i].Metadata = new Vector4(
                world.Groups[i],
                world.Radii[i].Raw * fixedPointToFloat,
                world.MaxSpeeds[i].Raw * fixedPointToFloat,
                i);
        }

        _agentBuffer.SetData(_uploadData, 0, 0, count);
        if (_indirectArgs[1] != (uint)count)
        {
            _indirectArgs[1] = (uint)count;
            _argsBuffer.SetData(_indirectArgs);
        }

        Graphics.DrawMesh(
            _groundMesh,
            Matrix4x4.identity,
            _groundMaterial,
            0,
            null,
            0,
            null,
            ShadowCastingMode.Off,
            false,
            null,
            LightProbeUsage.Off,
            null);

        if (_obstacleCount > 0)
        {
            Graphics.DrawMeshInstanced(
                _obstacleMesh,
                0,
                _obstacleMaterial,
                _obstacleMatrices,
                _obstacleCount,
                null,
                ShadowCastingMode.Off,
                false,
                0,
                null,
                LightProbeUsage.Off,
                null);
        }

#pragma warning disable CS0618
        Graphics.DrawMeshInstancedIndirect(
            _agentMesh,
            0,
            _agentMaterial,
            _bounds,
            _argsBuffer,
            0,
            _properties,
            ShadowCastingMode.Off,
            false,
            0,
            null,
            LightProbeUsage.Off,
            null);
#pragma warning restore CS0618
    }

    public void SetObstacles(FPOrientedBox2[] obstacles)
    {
        _obstacleCount = obstacles == null ? 0 : Math.Min(obstacles.Length, _obstacleMatrices.Length);
        const float fixedPointToFloat = 1f / 65536f;
        for (int i = 0; i < _obstacleCount; i++)
        {
            FPOrientedBox2 box = obstacles[i];
            float centerX = box.Center.X.Raw * fixedPointToFloat;
            float centerZ = box.Center.Y.Raw * fixedPointToFloat;
            float axisXx = box.AxisX.X.Raw * fixedPointToFloat;
            float axisXz = box.AxisX.Y.Raw * fixedPointToFloat;
            float axisYx = box.AxisY.X.Raw * fixedPointToFloat;
            float axisYz = box.AxisY.Y.Raw * fixedPointToFloat;
            float width = box.HalfExtents.X.Raw * fixedPointToFloat * 2f;
            float depth = box.HalfExtents.Y.Raw * fixedPointToFloat * 2f;

            Matrix4x4 matrix = Matrix4x4.identity;
            matrix.SetColumn(0, new Vector4(axisXx * width, 0f, axisXz * width, 0f));
            matrix.SetColumn(1, new Vector4(0f, 1.8f, 0f, 0f));
            matrix.SetColumn(2, new Vector4(axisYx * depth, 0f, axisYz * depth, 0f));
            matrix.SetColumn(3, new Vector4(centerX, 0.9f, centerZ, 1f));
            _obstacleMatrices[i] = matrix;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _agentBuffer?.Release();
        _argsBuffer?.Release();
        _agentBuffer = null;
        _argsBuffer = null;
        DestroyObject(_agentMaterial);
        DestroyObject(_groundMaterial);
        DestroyObject(_obstacleMaterial);
        DestroyObject(_agentMesh);
        DestroyObject(_groundMesh);
        DestroyObject(_obstacleMesh);
    }

    private static Mesh CreateAgentMesh()
    {
        Mesh mesh = new()
        {
            name = "Swarm Agent Wedge",
            hideFlags = HideFlags.DontSave,
            vertices = new[]
            {
                new Vector3(-0.42f, 0f, -0.36f),
                new Vector3(0.42f, 0f, -0.36f),
                new Vector3(0f, 0f, 0.58f),
                new Vector3(0f, 0.62f, 0.02f),
            },
            triangles = new[]
            {
                0, 2, 1,
                0, 3, 2,
                2, 3, 1,
                1, 3, 0,
            },
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh CreateGroundMesh(float halfExtent)
    {
        Mesh mesh = new()
        {
            name = "Swarm Ground Quad",
            hideFlags = HideFlags.DontSave,
            vertices = new[]
            {
                new Vector3(-halfExtent, -0.04f, -halfExtent),
                new Vector3(halfExtent, -0.04f, -halfExtent),
                new Vector3(halfExtent, -0.04f, halfExtent),
                new Vector3(-halfExtent, -0.04f, halfExtent),
            },
            uv = new[]
            {
                Vector2.zero,
                Vector2.right,
                Vector2.one,
                Vector2.up,
            },
            triangles = new[] { 0, 2, 1, 0, 3, 2 },
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh CreateObstacleMesh()
    {
        Mesh mesh = new()
        {
            name = "Swarm SAT Obstacle Cube",
            hideFlags = HideFlags.DontSave,
            vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f),
            },
            triangles = new[]
            {
                0, 2, 1, 0, 3, 2,
                5, 6, 4, 4, 6, 7,
                4, 7, 0, 0, 7, 3,
                1, 2, 5, 5, 2, 6,
                3, 7, 2, 2, 7, 6,
                4, 0, 5, 5, 0, 1,
            },
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void DestroyObject(UnityEngine.Object value)
    {
        if (value == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(value);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
}
