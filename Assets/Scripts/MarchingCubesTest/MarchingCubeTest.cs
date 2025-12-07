using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class MarchingCubeTest : MonoBehaviour
{
    [SerializeField]private GameObject cubePrefab;
    [SerializeField]private GameObject linePrefab;
    [SerializeField, Range(0f,1.0f)]private float isoLevel = 0.5f;
    [SerializeField]private Vector3 noiseOffset = Vector3.zero;
    [SerializeField]private float noiseScale = 1.0f;
    [SerializeField]private Vector3 gridSize = new Vector3(10f,10f,10f);

    private float _lastIsoLevel = -1.0f;
    private Vector3 _lastNoiseOffset = Vector3.zero;
    private float _lastNoiseScale = 1.0f;
    private Vector3 _lastGridSize;
    private float[] _cells;
    private List<GameObject> _cubes = new List<GameObject>();
    
    private MC33 _mc33 = new MC33();
    
    private int3 _gridN;    // 各次元ごとのグリッド数
    private int3 _cellN;    // 各次元ごとのセル数（グリッドを描画するための周囲の情報数なのでグリッド＋１の大きさになる）
    
    private MeshFilter _meshFilter;

    private void Start()
    {
        _meshFilter = GetComponent<MeshFilter>();
    }

    private void Update()
    {
        if (!Mathf.Approximately(isoLevel, _lastIsoLevel) || noiseOffset != _lastNoiseOffset || !Mathf.Approximately(noiseScale, _lastNoiseScale) || gridSize != _lastGridSize)
        {
            MakeCells();
            DrawCubes();
            _lastIsoLevel = isoLevel;
            _lastNoiseOffset = noiseOffset;
            _lastNoiseScale = noiseScale;
            _lastGridSize = gridSize;
            
            var grid = new MC33Grid
            {
                N = _gridN,
                L = new float3(_gridN),
                r0 = new float3( -0.5f * _gridN.x, 0f, -0.5f * _gridN.z),
                d = new float3(1.0f, 1.0f, 1.0f)
            };
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
        for (var z = 0; z < _cellN.z; z++)
        {
            for (var y = 0; y < _cellN.y; y++)
            {
                for (var x = 0; x < _cellN.x; x++)
                {
                    var position = new Vector3(x, y, z);
                    position = (position - positionCenter) * noiseScale + noiseOffset;
                    var value = Perlin.Noise(position.x, position.y, position.z);
                    _cells[x + y * _cellN.x + z * _cellN.x * _cellN.y] = value;
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
                    if (_cells[x + y * _cellN.x + z * _cellN.x * _cellN.y] < isoLevel)
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
