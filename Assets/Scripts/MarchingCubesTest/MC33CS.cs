using System;
using UnityEngine;

public class MC33CS : IDisposable
{
    private readonly ComputeShader _computeShader;
    private readonly int _kBuildGradients;
    private readonly int _kBuildIsoSurface;
    private readonly int _kInitIndirectArgs;
    private readonly int _kSetIndirectArgs;

    private GraphicsBuffer _gradientBuffer;
    private GraphicsBuffer _vertexBuffer;
    private GraphicsBuffer _indexBuffer;
    private GraphicsBuffer _indirectArgsBuffer;
    private GraphicsBuffer _indexCounterBuffer;

    private int _maxTriangles;
    private int _currentVolumeLength;

    public GraphicsBuffer VertexBuffer => _vertexBuffer;
    public GraphicsBuffer IndexBuffer => _indexBuffer;
    public GraphicsBuffer IndirectArgsBuffer => _indirectArgsBuffer;

    public MC33CS(ComputeShader computeShader, int maxTriangles)
    {
        _computeShader = computeShader;
        _maxTriangles = Mathf.Max(1, maxTriangles);

        _kBuildGradients = _computeShader.FindKernel("BuildGradients");
        _kBuildIsoSurface = _computeShader.FindKernel("BuildIsoSurface");
        _kInitIndirectArgs = _computeShader.FindKernel("InitIndirectArgs");
        _kSetIndirectArgs = _computeShader.FindKernel("SetIndirectArgs");
    }

    public void Dispose()
    {
        ReleaseBuffers();
    }

    public void SetMaxTriangles(int maxTriangles)
    {
        if (_maxTriangles == maxTriangles)
        {
            return;
        }

        _maxTriangles = Mathf.Max(1, maxTriangles);
        ReleaseBuffers();
    }

    private void ReleaseBuffers()
    {
        _gradientBuffer?.Release();
        _vertexBuffer?.Release();
        _indexBuffer?.Release();
        _indirectArgsBuffer?.Release();
        _indexCounterBuffer?.Release();

        _gradientBuffer = null;
        _vertexBuffer = null;
        _indexBuffer = null;
        _indirectArgsBuffer = null;
        _indexCounterBuffer = null;

        _currentVolumeLength = 0;
    }

    private void EnsureBuffers(GraphicsBuffer volumeBuffer, MC33Grid grid)
    {
        if (volumeBuffer == null)
        {
            throw new ArgumentNullException(nameof(volumeBuffer));
        }

        var volumeLength = (grid.N.x + 1) * (grid.N.y + 1) * (grid.N.z + 1);

        if (_gradientBuffer != null &&
            _vertexBuffer != null &&
            _indexBuffer != null &&
            _indirectArgsBuffer != null &&
            _indexCounterBuffer != null &&
            _currentVolumeLength == volumeLength)
        {
            return;
        }

        ReleaseBuffers();

        _currentVolumeLength = volumeLength;

        _gradientBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, volumeLength, sizeof(float) * 3);

        var vertexCount = _maxTriangles * 3;
        _vertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vertexCount, sizeof(float) * 4 * 3);
        _indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vertexCount, sizeof(uint));

        _indirectArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured, 1,
            GraphicsBuffer.IndirectDrawIndexedArgs.size);
        _indexCounterBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
    }

    public void Kick(GraphicsBuffer volumeBuffer, MC33Grid grid, float isoLevel)
    {
        EnsureBuffers(volumeBuffer, grid);

        _computeShader.SetBuffer(_kBuildGradients, "_Volumes", volumeBuffer);
        _computeShader.SetBuffer(_kBuildGradients, "_Gradients", _gradientBuffer);

        _computeShader.SetBuffer(_kBuildIsoSurface, "_Volumes", volumeBuffer);
        _computeShader.SetBuffer(_kBuildIsoSurface, "_Gradients", _gradientBuffer);
        _computeShader.SetBuffer(_kBuildIsoSurface, "_Vertices", _vertexBuffer);
        _computeShader.SetBuffer(_kBuildIsoSurface, "_Indices", _indexBuffer);
        _computeShader.SetBuffer(_kBuildIsoSurface, "_IndirectArgs", _indirectArgsBuffer);
        _computeShader.SetBuffer(_kBuildIsoSurface, "_IndexCounter", _indexCounterBuffer);

        _computeShader.SetBuffer(_kInitIndirectArgs, "_IndirectArgs", _indirectArgsBuffer);
        _computeShader.SetBuffer(_kInitIndirectArgs, "_IndexCounter", _indexCounterBuffer);

        _computeShader.SetBuffer(_kSetIndirectArgs, "_IndirectArgs", _indirectArgsBuffer);
        _computeShader.SetBuffer(_kSetIndirectArgs, "_IndexCounter", _indexCounterBuffer);

        _computeShader.SetInts("_NumGrid", grid.N.x, grid.N.y, grid.N.z);
        _computeShader.SetFloats("_Origin", grid.r0.x, grid.r0.y, grid.r0.z);
        _computeShader.SetFloats("_Step", grid.d.x, grid.d.y, grid.d.z);
        _computeShader.SetFloat("_Iso", isoLevel);
        _computeShader.SetInt("_VerticesCapacity", _maxTriangles * 3);
        _computeShader.SetInt("_IndicesCapacity", _maxTriangles * 3);

        _computeShader.Dispatch(_kInitIndirectArgs, 1, 1, 1);

        var threadGroupsX = Mathf.CeilToInt((float)grid.N.x / 8);
        var threadGroupsY = Mathf.CeilToInt((float)grid.N.y / 8);
        var threadGroupsZ = Mathf.CeilToInt((float)grid.N.z / 8);

        _computeShader.Dispatch(_kBuildGradients, threadGroupsX, threadGroupsY, threadGroupsZ);
        _computeShader.Dispatch(_kBuildIsoSurface, threadGroupsX, threadGroupsY, threadGroupsZ);
    }
}