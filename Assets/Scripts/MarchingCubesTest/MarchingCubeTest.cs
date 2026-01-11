using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class MarchingCubeTest : MonoBehaviour
{
    [SerializeField] private bool updateEveryFrame = false;
    [SerializeField] private bool prepareComputeShader = false;
    [SerializeField] private GameObject cubePrefab;
    [SerializeField] private bool drawCubes = false;
    [SerializeField, Range(0f, 1000.0f)] private float isoLevel = 0.5f;
    [SerializeField] private bool UseNoise = false;
    [SerializeField] private Vector3 noiseOffset = Vector3.zero;
    [SerializeField] private float noiseScale = 1.0f;
    [SerializeField] private Vector3 gridSize = new Vector3(10f, 10f, 10f);

    [SerializeField] private bool UseSphere = false;
    [SerializeField] private float sphereRadius = 1.0f;
    
    // compute shader関係
    [SerializeField] private bool UseComputeShader = false;
    [SerializeField] private ComputeShader computeShader;
    [SerializeField, Range(1, 1_000_000)] private int maxTriangles = 1_000_000;
    [SerializeField] private Material material;
    [SerializeField] private Vector3 boundsPadding = new Vector3(2f, 2f, 2f);

    private const float VolumeFixedScale = 65536f;
    private const float VolumeFixedMin = -32768f;
    private const float VolumeFixedMax = 32767.99998474f;

    private GraphicsBuffer _volumeBuffer;
    private bool _warnedMissingVolumeBuffer = false;

    private MC33CS _mc33CS;
    
    private float _lastIsoLevel = -1.0f;
    private Vector3 _lastNoiseOffset = Vector3.zero;
    private float _lastNoiseScale = 1.0f;
    private Vector3 _lastGridSize;
    private float _lastSphereRadius;

    private float[] _volumes;
    private List<GameObject> _cubes = new List<GameObject>();

    private MC33 _mc33 = new MC33();

    // ComputeShader化するために各グリッドを独立して計算するように変更したバージョン
    private MC33PrepareCS _mc33PrepareCS = new MC33PrepareCS();

    private int3 _gridN; // 各次元ごとのグリッド数
    private int3 _volumeN; // 各次元ごとのセル数（グリッドを描画するための周囲の情報数なのでグリッド＋１の大きさになる）

    private MeshFilter _meshFilter;

    private void Start()
    {
        _meshFilter = GetComponent<MeshFilter>();

        if (computeShader != null)
        {
            _mc33CS = new MC33CS(computeShader, maxTriangles);
        }
    }

    public void SetVolumeBuffer(GraphicsBuffer volumeBuffer)
    {
        _volumeBuffer = volumeBuffer;
        _warnedMissingVolumeBuffer = false;
    }

    private void OnDestroy()
    {
        _mc33CS?.Dispose();
        _volumeBuffer?.Dispose();
    }

    private static int FloatToFixedVolume(float value)
    {
        var clamped = Mathf.Clamp(value, VolumeFixedMin, VolumeFixedMax);
        return Mathf.RoundToInt(clamped * VolumeFixedScale);
    }

    private void Update()
    {
        if (!Mathf.Approximately(isoLevel, _lastIsoLevel) || noiseOffset != _lastNoiseOffset ||
            !Mathf.Approximately(noiseScale, _lastNoiseScale) || gridSize != _lastGridSize ||
            !Mathf.Approximately(_lastSphereRadius, sphereRadius))
        {
            MakeVolumes();
            if (drawCubes)
            {
                DrawCubes();
            }
        }
        _lastIsoLevel = isoLevel;
        _lastNoiseOffset = noiseOffset;
        _lastNoiseScale = noiseScale;
        _lastGridSize = gridSize;
        _lastSphereRadius = sphereRadius;

        var grid = new MC33Grid
        {
            N = _gridN,
            L = new float3(_gridN),
            r0 = new float3(-5f, 0f, -5f),
            d = new float3(0.1f, 0.1f, 0.1f)
        };

        if (UseComputeShader)
        {
            _meshFilter.mesh = null;

            if (_mc33CS == null && computeShader != null)
            {
                _mc33CS = new MC33CS(computeShader, maxTriangles);
            }

            _mc33CS?.SetMaxTriangles(maxTriangles);

            if (_mc33CS != null && _volumeBuffer != null)
            {
                _mc33CS.Kick(_volumeBuffer, grid, isoLevel);

                if (material != null)
                {
                    material.SetBuffer("_Vertices", _mc33CS.VertexBuffer);
                }

                if (material != null && _mc33CS.IndirectArgsBuffer != null)
                {
                    // カリングに必要な bounds（とりあえずグリッド全体を覆う）
                    var center = transform.position + new Vector3(0f, _gridN.y * 0.5f, 0f);
                    var size = new Vector3(_gridN.x, _gridN.y, _gridN.z) + boundsPadding;
                    var bounds = new Bounds(center, size);

                    var rp = new RenderParams(material)
                    {
                        worldBounds = bounds
                    };

                    Graphics.RenderPrimitivesIndexedIndirect(rp, MeshTopology.Triangles, _mc33CS.IndexBuffer, _mc33CS.IndirectArgsBuffer);
                }
            }
            else if (_mc33CS != null && _volumeBuffer == null && !_warnedMissingVolumeBuffer)
            {
                Debug.LogWarning("Volume buffer is not set for MC33CS. Compute shader execution skipped.");
                _warnedMissingVolumeBuffer = true;
            }
        }
        else if (prepareComputeShader)
        {
            var mesh = _mc33PrepareCS.calculate_isosurface(grid, isoLevel, _volumes);
            mesh.name = "MC33_isosurface";
            _meshFilter.mesh = mesh;
        }
        else
        {
            var mesh = _mc33.calculate_isosurface(grid, isoLevel, _volumes);
            mesh.name = "MC33_isosurface";
            _meshFilter.mesh = mesh;
        }
    }


    private void MakeVolumes()
    {
        var gridSizeX = Mathf.FloorToInt(Mathf.Max(gridSize.x, 2));
        var gridSizeY = Mathf.FloorToInt(Mathf.Max(gridSize.y, 2));
        var gridSizeZ = Mathf.FloorToInt(Mathf.Max(gridSize.z, 2));
        _gridN = new int3(gridSizeX, gridSizeY, gridSizeZ);
        _volumeN = _gridN + new int3(1, 1, 1);
        _volumes = new float[_volumeN.x * _volumeN.y * _volumeN.z];
        var volumeFixed = new int[_volumes.Length];
        
        _volumeBuffer?.Dispose();
        _volumeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _volumes.Length, sizeof(int));

        var positionCenter = new Vector3(_volumeN.x * 0.5f, _volumeN.y * 0.5f, _volumeN.z * 0.5f);

        if (UseNoise)
        {
            for (var z = 0; z < _volumeN.z; z++)
            {
                for (var y = 0; y < _volumeN.y; y++)
                {
                    for (var x = 0; x < _volumeN.x; x++)
                    {
                        if (x == 0 || x == _volumeN.x - 1 || y == 0 || y == _volumeN.y - 1 || z == 0 || z == _volumeN.z - 1)
                        {
                            _volumes[x + y * _volumeN.x + z * _volumeN.x * _volumeN.y] = 0.0f;
                            continue;
                        }

                        var position = new Vector3(x, y, z);
                        position = (position - positionCenter) * noiseScale + noiseOffset;
                        var value = Perlin.Noise(position.x, position.y, position.z) * 0.5f + 0.5f;
                        _volumes[x + y * _volumeN.x + z * _volumeN.x * _volumeN.y] = value;
                    }
                }
            }
        }
        else if (UseSphere)
        {
            for (var z = 0; z < _volumeN.z; z++)
            {
                for (var y = 0; y < _volumeN.y; y++)
                {
                    for (var x = 0; x < _volumeN.x; x++)
                    {
                        if (x == 0 || x == _volumeN.x - 1 || y == 0 || y == _volumeN.y - 1 || z == 0 || z == _volumeN.z - 1)
                        {
                            _volumes[x + y * _volumeN.x + z * _volumeN.x * _volumeN.y] = 0.0f;
                            continue;
                        }

                        var position = new Vector3(x - _volumeN.x * 0.5f, y - _volumeN.y * 0.5f, z - _volumeN.z * 0.5f);
                        var distance = position.magnitude;
                        _volumes[x + y * _volumeN.x + z * _volumeN.x * _volumeN.y] =
                            distance < sphereRadius ? 1.0f - distance / sphereRadius : 0.0f;
                    }
                }
            }
        }
        else
        {
            int3 min, max;
            min = (_volumeN - (int)sphereRadius) / 2;
            max = (_volumeN + (int)sphereRadius) / 2;

            for (var z = 0; z < _volumeN.z; z++)
            {
                for (var y = 0; y < _volumeN.y; y++)
                {
                    for (var x = 0; x < _volumeN.x; x++)
                    {
                        if (x == 0 || x == _volumeN.x - 1 || y == 0 || y == _volumeN.y - 1 || z == 0 || z == _volumeN.z - 1)
                        {
                            _volumes[x + y * _volumeN.x + z * _volumeN.x * _volumeN.y] = 0.0f;
                            continue;
                        }

                        _volumes[x + y * _volumeN.x + z * _volumeN.x * _volumeN.y] =
                            (x >= min.x && x < max.x && y >= min.y && y < max.y && z >= min.z && z < max.z)
                                ? 1.0f
                                : 0.0f;
                    }
                }
            }
        }

        for (var i = 0; i < _volumes.Length; i++)
        {
            volumeFixed[i] = FloatToFixedVolume(_volumes[i]);
        }

        _volumeBuffer.SetData(volumeFixed);
    }

    private void DrawCubes()
    {
        foreach (var cube in _cubes)
        {
            Destroy(cube);
        }

        var positionRoot = new Vector3(_volumeN.x * -0.5f + 0.5f, 0f, _volumeN.z * -0.5f + 0.5f);

        for (var z = 0; z < _volumeN.z; z++)
        {
            for (var y = 0; y < _volumeN.y; y++)
            {
                for (var x = 0; x < _volumeN.x; x++)
                {
                    if (_volumes[x + y * _volumeN.x + z * _volumeN.x * _volumeN.y] > isoLevel)
                    {
                        var go = Instantiate(cubePrefab, new Vector3(x, y, z) + positionRoot, Quaternion.identity);
                        go.transform.parent = transform;
                        _cubes.Add(go);
                    }
                }
            }
        }
    }
}
