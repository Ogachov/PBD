using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

// RenderPrimitivesIndexedを使って座標の配列から点群ジオメトリを描画するサンプル
namespace RenderPrimitivesIndexed
{
    public class PBFMarchingCube : MonoBehaviour
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Particle
        {
            float3 position;
            float radiusScale;
            float3 velocity;
            float life;
            float4 color;
        };

        private static readonly int PositionBuffer = Shader.PropertyToID("_PositionBuffer");
        private static readonly int ObjectToWorld = Shader.PropertyToID("_ObjectToWorld");

        [SerializeField] private Material material;
        [SerializeField, Range(1, 10000)] private int pointCount = 10000;
        [SerializeField, Range(0, 10000)] private int drawCount = 10000;
        
        [SerializeField, Range(0,100)] private int spawnPerFrame = 10;
        
        [SerializeField] private float gravity = 9.81f;
        [SerializeField] private Vector3 boundsCenter = new Vector3(0f, 5f, 0f);
        [SerializeField] private Vector3 boundsSize = new Vector3(10f, 10f, 10f);
        [SerializeField, Range(0.001f, 1f)] private float repulsionRadius = 0.1f;
        [SerializeField, Range(0f, 100f)] private float repulsionStrength = 10f;

        [SerializeField] private int3 numDensityGrid = new int3(10, 10, 10);
        [SerializeField] private ComputeShader particleComputeShader;
        [SerializeField] private ComputeShader mc33ComputeShader;
    
        public Material _copiedMaterial;

        private int numParticles;
        private GraphicsBuffer _particleBuffer;
        private GraphicsBuffer _activeBuffer;
        private GraphicsBuffer _activeCountBuffer;
        private GraphicsBuffer _poolBuffer;
        private GraphicsBuffer _poolCountBuffer;
        
        private GraphicsBuffer _densityBuffer;  // 立法格子の中に存在するパーティクルカウント用
        private int3 _numDensityGridCell;

        // kernel UpdateParticles
        private int k_InitPoolList;
        private int k_InitParticles;
        private int k_SpawnParticles;
        private int k_UpdateParticles;
        
        private const int ThreadGroupsX = 128;
        private int dispatchThreadGroupsX;
        
        private int3 densityGridSize;

        private void Start()
        {
            numParticles = pointCount;
            dispatchThreadGroupsX = Mathf.CeilToInt((float)numParticles / ThreadGroupsX);
            
            if (particleComputeShader != null)
            {
                k_InitPoolList = particleComputeShader.FindKernel("InitPoolList");
                k_InitParticles = particleComputeShader.FindKernel("InitParticles");
                k_SpawnParticles = particleComputeShader.FindKernel("SpawnParticles");
                k_UpdateParticles = particleComputeShader.FindKernel("UpdateParticles");
            }

            var particleSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Particle));
            _particleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numParticles, particleSize);
            _activeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, numParticles, sizeof(uint));
            _activeBuffer.SetCounterValue(0);
            _activeCountBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));
            _poolBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, numParticles, sizeof(uint));
            _poolBuffer.SetCounterValue(0);
            _poolCountBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));
            
            // 密度格子
            
            
            

            // プールリスト初期化
            particleComputeShader.SetBuffer(k_InitPoolList, "PoolAppend", _poolBuffer);
            particleComputeShader.SetInt("_Capacity", numParticles);
            particleComputeShader.Dispatch(k_InitPoolList, dispatchThreadGroupsX, 1, 1);
            // パーティクル初期化
            particleComputeShader.SetBuffer(k_InitParticles, "Particles", _particleBuffer);
            particleComputeShader.SetInt("_Capacity", numParticles);
            particleComputeShader.Dispatch(k_InitParticles, dispatchThreadGroupsX, 1, 1);
            
            
            // マテリアルのコピーを作成してバッファをセット
            _copiedMaterial = new Material(material);
            _copiedMaterial.SetBuffer(PositionBuffer, _particleBuffer);
        }

        private void OnDestroy()
        {
            _particleBuffer?.Dispose();
            _particleBuffer = null;
            _activeBuffer?.Dispose();
            _activeBuffer = null;
            _activeCountBuffer?.Dispose();
            _activeCountBuffer = null;
            _poolBuffer?.Dispose();
            _poolBuffer = null;
            _poolCountBuffer?.Dispose();
            _poolCountBuffer = null;
            
            if (_copiedMaterial != null)
            {
                Destroy(_copiedMaterial);
                _copiedMaterial = null;
            }
        }

        private void Update()
        {
            if (particleComputeShader == null)
            {
                return;
            }
            
            particleComputeShader.SetInt("_Capacity", numParticles);
            particleComputeShader.SetInt("_FrameCount", Time.frameCount);
            particleComputeShader.SetFloat("_DeltaTime", Time.deltaTime);
            particleComputeShader.SetFloat("_Gravity", gravity);
            particleComputeShader.SetFloat("_RepulsionRadius", repulsionRadius);
            particleComputeShader.SetFloat("_RepulsionStrength", repulsionStrength);

            // Spawn
            {
                // Poolのカウントを取得
                GraphicsBuffer.CopyCount(_poolBuffer, _poolCountBuffer, 0);
                int[] poolCountArray = { 0 };
                _poolCountBuffer.GetData(poolCountArray);
                var availablePoolCount = poolCountArray[0];
                var spawnCount = Mathf.Min(spawnPerFrame, availablePoolCount);
                particleComputeShader.SetInt("_SpawnCount", spawnCount);
                if (spawnCount > 0)
                {
                    particleComputeShader.SetBuffer(k_SpawnParticles, "Particles", _particleBuffer);
                    particleComputeShader.SetBuffer(k_SpawnParticles, "PoolConsume", _poolBuffer);
                    particleComputeShader.Dispatch(k_SpawnParticles, Mathf.CeilToInt((float)spawnCount / ThreadGroupsX), 1, 1);
                }
            }
            
            // Update
            {
                _activeBuffer.SetCounterValue(0);
                particleComputeShader.SetBuffer(k_UpdateParticles, "Particles", _particleBuffer);
                particleComputeShader.SetBuffer(k_UpdateParticles, "AliveList", _activeBuffer);
                particleComputeShader.SetBuffer(k_UpdateParticles, "PoolAppend", _poolBuffer);
                particleComputeShader.SetVector("_BoundsCenter", boundsCenter);
                particleComputeShader.SetVector("_BoundsSize", boundsSize);
                particleComputeShader.Dispatch(k_UpdateParticles, dispatchThreadGroupsX, 1, 1);
            }
            
            GraphicsBuffer.CopyCount(_activeBuffer, _activeCountBuffer, 0);
            int[] countArray = {0};
            _activeCountBuffer.GetData(countArray);
            drawCount = countArray[0];
            
            var rp = new RenderParams(_copiedMaterial);
            rp.worldBounds = new Bounds(Vector3.zero, Vector3.one * 100f);

            _copiedMaterial.SetBuffer(PositionBuffer, _particleBuffer);
            _copiedMaterial.SetFloat("_Size", material.GetFloat("_Size"));
            _copiedMaterial.SetMatrix(ObjectToWorld, transform.localToWorldMatrix);
        
            Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Points, _activeBuffer, drawCount);
        }
    }
}
