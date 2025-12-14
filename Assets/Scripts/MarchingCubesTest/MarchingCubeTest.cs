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
    [SerializeField, Range(0f, 1.0f)] private float isoLevel = 0.5f;
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

    private GraphicsBuffer _cellBuffer;
    private GraphicsBuffer _gradientBuffer;
    private GraphicsBuffer _vertexBuffer;
    private GraphicsBuffer _indirectArgsBuffer;
    private int k_BuildGradients;
    private int k_BuildIsoSurface;
    private int k_InitIndirectArgs;
    
    private float _lastIsoLevel = -1.0f;
    private Vector3 _lastNoiseOffset = Vector3.zero;
    private float _lastNoiseScale = 1.0f;
    private Vector3 _lastGridSize;
    private float _lastSphereRadius;

    private float[] _cells;
    private List<GameObject> _cubes = new List<GameObject>();

    private MC33 _mc33 = new MC33();

    // ComputeShader化するために各グリッドを独立して計算するように変更したバージョン
    private MC33PrepareCS _mc33PrepareCS = new MC33PrepareCS();

    private int3 _gridN; // 各次元ごとのグリッド数
    private int3 _cellN; // 各次元ごとのセル数（グリッドを描画するための周囲の情報数なのでグリッド＋１の大きさになる）

    private MeshFilter _meshFilter;

    private void Start()
    {
        _meshFilter = GetComponent<MeshFilter>();
        
        k_BuildGradients = computeShader.FindKernel("BuildGradients");
        k_BuildIsoSurface = computeShader.FindKernel("BuildIsoSurface");
        k_InitIndirectArgs = computeShader.FindKernel("InitIndirectArgs");
    }
    
    private void InitComputeShaderBuffers()
    {
        _cellBuffer?.Release();
        _gradientBuffer?.Release();
        _vertexBuffer?.Release();
        _indirectArgsBuffer?.Release();

        _cellBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _cells.Length, sizeof(float));

        _gradientBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _cells.Length, sizeof(float) * 3);

        var vertexCount = maxTriangles * 3;
        _vertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, vertexCount, sizeof(float) * 4);

        // Indirect args: uint4 = (vertexCountPerInstance, instanceCount, startVertex, startInstance)
        _indirectArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, sizeof(uint) * 4);
        _indirectArgsBuffer.SetData(new[] { 0, 1, 0, 0 });

        computeShader.SetBuffer(k_BuildGradients, "_Cells", _cellBuffer);
        computeShader.SetBuffer(k_BuildGradients, "_Gradients", _gradientBuffer);
        
        computeShader.SetBuffer(k_BuildIsoSurface, "_Cells", _cellBuffer);
        computeShader.SetBuffer(k_BuildIsoSurface, "_Gradients", _gradientBuffer);
        computeShader.SetBuffer(k_BuildIsoSurface, "_Vertices", _vertexBuffer);
        
        // 間接描画用の引数バッファを初期化
        computeShader.SetBuffer(k_InitIndirectArgs, "_IndirectArgs", _indirectArgsBuffer);
        computeShader.Dispatch(k_InitIndirectArgs, 1, 1, 1);
    }
    
    private void KickComputeShader(MC33Grid grid, float isoLevel)
    {
        computeShader.SetInts("_N", grid.N.x, grid.N.y, grid.N.z);
        computeShader.SetFloats("_Origin", grid.r0.x, grid.r0.y, grid.r0.z);
        computeShader.SetFloats("_Step", grid.d.x, grid.d.y, grid.d.z);
        computeShader.SetFloat("_Iso", isoLevel);

        var threadGroupsX = Mathf.CeilToInt((float)_gridN.x / 8);
        var threadGroupsY = Mathf.CeilToInt((float)_gridN.y / 8);
        var threadGroupsZ = Mathf.CeilToInt((float)_gridN.z / 8);

        _vertexBuffer.SetCounterValue(0);
        // 勾配計算
        computeShader.Dispatch(k_BuildGradients, threadGroupsX, threadGroupsY, threadGroupsZ);
        
        // 等値面計算
        computeShader.Dispatch(k_BuildIsoSurface, threadGroupsX, threadGroupsY, threadGroupsZ);
        
        // AppendCounter(頂点数) → args[0] (vertexCountPerInstance) にコピー
        GraphicsBuffer.CopyCount(_vertexBuffer, _indirectArgsBuffer, 0);

        int[] countArray = {0, 0, 0, 0};
        _indirectArgsBuffer.GetData(countArray);
        Debug.Log(countArray[0] + " vertices generated.");
        
        var n = Mathf.Min(4, countArray[0]);
        var tmp = new Vector3[n];
        _gradientBuffer.GetData(tmp, 0, 0, n);
        for (var i = 0; i < n; i++)
        {
            Debug.Log($"gradient {i}: {tmp[i]}");
        }
        
        // _vertexBufferのカウントをCPU側で使えるようにする
        
        if (material != null)
        {
            material.SetBuffer("_Vertices", _vertexBuffer);
        }
    }

    private void OnDestroy()
    {
        _cellBuffer?.Release();
        _gradientBuffer?.Release();
        _vertexBuffer?.Release();
        _indirectArgsBuffer?.Release();
    }

    private void Update()
    {
        if (!Mathf.Approximately(isoLevel, _lastIsoLevel) || noiseOffset != _lastNoiseOffset ||
            !Mathf.Approximately(noiseScale, _lastNoiseScale) || gridSize != _lastGridSize ||
            !Mathf.Approximately(_lastSphereRadius, sphereRadius))
        {
            MakeCells();
            if (drawCubes)
            {
                DrawCubes();
            }
        }

        // 前回とパラメータが異なっていたらバッファを初期化
        if (UseComputeShader)
        {
            if (_cellBuffer == null || !Mathf.Approximately(isoLevel, _lastIsoLevel) || noiseOffset != _lastNoiseOffset ||
                !Mathf.Approximately(noiseScale, _lastNoiseScale) || gridSize != _lastGridSize ||
                !Mathf.Approximately(_lastSphereRadius, sphereRadius))
            {
                InitComputeShaderBuffers();
            }

            if (_cellBuffer != null)
            {
                _cellBuffer.SetData(_cells);
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
            r0 = new float3(-0.5f * _gridN.x, 0f, -0.5f * _gridN.z),
            d = new float3(1.0f, 1.0f, 1.0f)
        };

        if (UseComputeShader)
        {
            _meshFilter.mesh = null;
            
            KickComputeShader(grid, isoLevel);

            if (material != null && _indirectArgsBuffer != null)
            {
                // カリングに必要な bounds（とりあえずグリッド全体を覆う）
                var center = transform.position + new Vector3(0f, _gridN.y * 0.5f, 0f);
                var size = new Vector3(_gridN.x, _gridN.y, _gridN.z) + boundsPadding;
                var bounds = new Bounds(center, size);

                var rp = new RenderParams(material)
                {
                    worldBounds = bounds
                };

                // MeshTopology.Triangles: args[0] は「頂点数」（三角形数*3）でOK
                Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, _indirectArgsBuffer, 1);
            }
        }
        else if (prepareComputeShader)
        {
            var mesh = _mc33PrepareCS.calculate_isosurface(grid, isoLevel, _cells);
            mesh.name = "MC33_isosurface";
            _meshFilter.mesh = mesh;
        }
        else
        {
            var mesh = _mc33.calculate_isosurface(grid, isoLevel, _cells);
            mesh.name = "MC33_isosurface";
            _meshFilter.mesh = mesh;
        }
    }


    private void MakeCells()
    {
        var gridSizeX = Mathf.FloorToInt(Mathf.Max(gridSize.x, 2));
        var gridSizeY = Mathf.FloorToInt(Mathf.Max(gridSize.y, 2));
        var gridSizeZ = Mathf.FloorToInt(Mathf.Max(gridSize.z, 2));
        _gridN = new int3(gridSizeX, gridSizeY, gridSizeZ);
        _cellN = _gridN + new int3(1, 1, 1);
        _cells = new float[_cellN.x * _cellN.y * _cellN.z];

        var positionCenter = new Vector3(_cellN.x * 0.5f, _cellN.y * 0.5f, _cellN.z * 0.5f);

        if (UseNoise)
        {
            for (var z = 0; z < _cellN.z; z++)
            {
                for (var y = 0; y < _cellN.y; y++)
                {
                    for (var x = 0; x < _cellN.x; x++)
                    {
                        if (x == 0 || x == _cellN.x - 1 || y == 0 || y == _cellN.y - 1 || z == 0 || z == _cellN.z - 1)
                        {
                            _cells[x + y * _cellN.x + z * _cellN.x * _cellN.y] = 0.0f;
                            continue;
                        }

                        var position = new Vector3(x, y, z);
                        position = (position - positionCenter) * noiseScale + noiseOffset;
                        var value = Perlin.Noise(position.x, position.y, position.z);
                        _cells[x + y * _cellN.x + z * _cellN.x * _cellN.y] = value;
                    }
                }
            }
        }
        else if (UseSphere)
        {
            for (var z = 0; z < _cellN.z; z++)
            {
                for (var y = 0; y < _cellN.y; y++)
                {
                    for (var x = 0; x < _cellN.x; x++)
                    {
                        if (x == 0 || x == _cellN.x - 1 || y == 0 || y == _cellN.y - 1 || z == 0 || z == _cellN.z - 1)
                        {
                            _cells[x + y * _cellN.x + z * _cellN.x * _cellN.y] = 0.0f;
                            continue;
                        }

                        var position = new Vector3(x - _cellN.x * 0.5f, y - _cellN.y * 0.5f, z - _cellN.z * 0.5f);
                        var distance = position.magnitude;
                        _cells[x + y * _cellN.x + z * _cellN.x * _cellN.y] =
                            distance < sphereRadius ? 1.0f - distance / sphereRadius : 0.0f;
                    }
                }
            }
        }
        else
        {
            int3 min, max;
            min = (_cellN - (int)sphereRadius) / 2;
            max = (_cellN + (int)sphereRadius) / 2;

            for (var z = 0; z < _cellN.z; z++)
            {
                for (var y = 0; y < _cellN.y; y++)
                {
                    for (var x = 0; x < _cellN.x; x++)
                    {
                        if (x == 0 || x == _cellN.x - 1 || y == 0 || y == _cellN.y - 1 || z == 0 || z == _cellN.z - 1)
                        {
                            _cells[x + y * _cellN.x + z * _cellN.x * _cellN.y] = 0.0f;
                            continue;
                        }

                        _cells[x + y * _cellN.x + z * _cellN.x * _cellN.y] =
                            (x >= min.x && x < max.x && y >= min.y && y < max.y && z >= min.z && z < max.z)
                                ? 1.0f
                                : 0.0f;
                    }
                }
            }
        }
    }

    private void DrawCubes()
    {
        foreach (var cube in _cubes)
        {
            Destroy(cube);
        }

        var positionRoot = new Vector3(_cellN.x * -0.5f + 0.5f, 0f, _cellN.z * -0.5f + 0.5f);

        for (var z = 0; z < _cellN.z; z++)
        {
            for (var y = 0; y < _cellN.y; y++)
            {
                for (var x = 0; x < _cellN.x; x++)
                {
                    if (_cells[x + y * _cellN.x + z * _cellN.x * _cellN.y] > isoLevel)
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
