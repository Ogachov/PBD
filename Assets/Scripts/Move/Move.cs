using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

// RenderPrimitivesIndexedを使って座標の配列から点群ジオメトリを描画するサンプル
namespace RenderPrimitivesIndexed
{
    public class Move : MonoBehaviour
    {
        struct Particle
        {
            public float3 position;
            public float3 velocity;
        }
        
        private static readonly int PositionBuffer = Shader.PropertyToID("_PositionBuffer");
        private static readonly int ObjectToWorld = Shader.PropertyToID("_ObjectToWorld");

        [SerializeField] private Material material;
        [SerializeField, Range(1, 10000)] private int pointCount = 10000;
        [SerializeField, Range(0, 10000)] private int drawCount = 10000;
        
        [SerializeField] private float gravity = 9.81f;

        [SerializeField] private ComputeShader computeShader;
    
        private Material _copiedMaterial;

        private GraphicsBuffer _indexBuffer;
        private NativeArray<uint> _indexArray;
        private GraphicsBuffer _particleBuffer;

        // kernel UpdateParticles
        private int k_UpdateParticles;
        
        private void Start()
        {
            _particleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pointCount, sizeof(float) * 6);
            // var positions = new Vector3[pointCount];
            // for (var i = 0; i < pointCount; i++)
            // {
            //     positions[i] = Random.insideUnitSphere * 5f;
            // }
            // _vertexBuffer.SetData(positions);
            Particle[] particles = new Particle[pointCount];
            for (var i = 0; i < pointCount; i++)
            {
                particles[i].position = Random.insideUnitSphere * 5f;
                particles[i].velocity = float3.zero;
            }
            _particleBuffer.SetData(particles);
        
            _copiedMaterial = new Material(material);
            _copiedMaterial.SetBuffer(PositionBuffer, _particleBuffer);
        
            _indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pointCount, sizeof(uint));
            _indexArray = new NativeArray<uint>(pointCount, Allocator.Persistent);
            for (var i = 0; i < pointCount; i++)
            {
                _indexArray[i] = 0;
            }

            if (computeShader != null)
            {
                k_UpdateParticles = computeShader.FindKernel("UpdateParticles");
            }
        }

        private void OnDestroy()
        {
            _particleBuffer?.Dispose();
            _particleBuffer = null;
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
            if (computeShader == null)
            {
                return;
            }

            computeShader.SetInt("_Capacity", pointCount);
            computeShader.SetInt("_FrameCount", Time.frameCount);
            computeShader.SetFloat("_DeltaTime", Time.deltaTime);
            computeShader.SetFloat("_Gravity", gravity);
            
            computeShader.SetBuffer(k_UpdateParticles, "Particles", _particleBuffer);
            int threadGroups = 128;
            computeShader.Dispatch(k_UpdateParticles, Mathf.CeilToInt((float)pointCount / threadGroups), 1, 1);
            
            RenderParams rp = new RenderParams(_copiedMaterial);
            rp.worldBounds = new Bounds(Vector3.zero, Vector3.one * 100f);

            _copiedMaterial.SetFloat("_Size", material.GetFloat("_Size"));
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
