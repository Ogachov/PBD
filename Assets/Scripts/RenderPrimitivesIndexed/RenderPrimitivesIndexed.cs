using Unity.Collections;
using UnityEngine;

// RenderPrimitivesIndexedを使って座標の配列から点群ジオメトリを描画するサンプル
namespace RenderPrimitivesIndexed
{
    public class RenderPrimitivesIndexed : MonoBehaviour
    {
        private static readonly int PositionBuffer = Shader.PropertyToID("_PositionBuffer");
        private static readonly int ObjectToWorld = Shader.PropertyToID("_ObjectToWorld");

        [SerializeField] private Material material;
        [SerializeField, Range(1, 10000)] private int pointCount = 10000;
        [SerializeField, Range(0, 10000)] private int drawCount = 10000;
    
        private Material _copiedMaterial;

        private GraphicsBuffer _indexBuffer;
        private NativeArray<uint> _indexArray;
        private GraphicsBuffer _vertexBuffer;

        private void Start()
        {
            _vertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pointCount, sizeof(float) * 3);
            var positions = new Vector3[pointCount];
            for (var i = 0; i < pointCount; i++)
            {
                positions[i] = Random.insideUnitSphere * 5f;
            }
            _vertexBuffer.SetData(positions);
        
            _copiedMaterial = new Material(material);
            _copiedMaterial.SetBuffer(PositionBuffer, _vertexBuffer);
        
            _indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pointCount, sizeof(uint));
            _indexArray = new NativeArray<uint>(pointCount, Allocator.Persistent);
            for (var i = 0; i < pointCount; i++)
            {
                _indexArray[i] = 0;
            }
        }

        private void OnDestroy()
        {
            _vertexBuffer?.Dispose();
            _vertexBuffer = null;
            _indexBuffer?.Dispose();
            _indexBuffer = null;
            _indexArray.Dispose();
            if (_copiedMaterial != null)
            {
                Destroy(_copiedMaterial);
                _copiedMaterial = null;
            }
        }

        private void Update()
        {
            RenderParams rp = new RenderParams(_copiedMaterial);
            rp.worldBounds = new Bounds(Vector3.zero, Vector3.one * 100f);

            _copiedMaterial.SetMatrix(ObjectToWorld, transform.localToWorldMatrix);
            for (var i = 0; i < drawCount; i++)
            {
                _indexArray[i] = (uint)i;
            }
            _indexBuffer.SetData(_indexArray);
        
            Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Points, _indexBuffer, drawCount);
        }
    }
}
