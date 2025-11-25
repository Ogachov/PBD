using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

// RenderPrimitivesIndexedを使って座標の配列から点群ジオメトリを描画するサンプル
namespace RenderPrimitivesIndexed
{
    public class PBF : MonoBehaviour
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

        [SerializeField] private ComputeShader computeShader;
    
        public Material _copiedMaterial;

        private int numParticles;
        private GraphicsBuffer _particleBuffer;
        private GraphicsBuffer _activeBuffer;
        private GraphicsBuffer _activeCountBuffer;
        private GraphicsBuffer _poolBuffer;
        private GraphicsBuffer _poolCountBuffer;

        // kernel UpdateParticles
        private int k_InitPoolList;
        private int k_InitParticles;
        private int k_SpawnParticles;
        private int k_UpdateParticles;
        
        private const int ThreadGroupsX = 128;
        private int dispatchThreadGroupsX;

        private void Start()
        {
            numParticles = pointCount;
            dispatchThreadGroupsX = Mathf.CeilToInt((float)numParticles / ThreadGroupsX);
            
            if (computeShader != null)
            {
                k_InitPoolList = computeShader.FindKernel("InitPoolList");
                k_InitParticles = computeShader.FindKernel("InitParticles");
                k_SpawnParticles = computeShader.FindKernel("SpawnParticles");
                k_UpdateParticles = computeShader.FindKernel("UpdateParticles");
            }

            var particleSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Particle));
            _particleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numParticles, particleSize);
            _activeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, numParticles, sizeof(uint));
            _activeBuffer.SetCounterValue(0);
            _activeCountBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));
            _poolBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, numParticles, sizeof(uint));
            _poolBuffer.SetCounterValue(0);
            _poolCountBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));

            // プールリスト初期化
            computeShader.SetBuffer(k_InitPoolList, "PoolAppend", _poolBuffer);
            computeShader.SetInt("_Capacity", numParticles);
            computeShader.Dispatch(k_InitPoolList, dispatchThreadGroupsX, 1, 1);
            // パーティクル初期化
            computeShader.SetBuffer(k_InitParticles, "Particles", _particleBuffer);
            computeShader.SetInt("_Capacity", numParticles);
            computeShader.Dispatch(k_InitParticles, dispatchThreadGroupsX, 1, 1);
            
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
            if (computeShader == null)
            {
                return;
            }
            
            computeShader.SetInt("_Capacity", numParticles);
            computeShader.SetInt("_FrameCount", Time.frameCount);
            computeShader.SetFloat("_DeltaTime", Time.deltaTime);
            computeShader.SetFloat("_Gravity", gravity);
            computeShader.SetFloat("_RepulsionRadius", repulsionRadius);
            computeShader.SetFloat("_RepulsionStrength", repulsionStrength);

            // Spawn
            {
                // Poolのカウントを取得
                GraphicsBuffer.CopyCount(_poolBuffer, _poolCountBuffer, 0);
                int[] poolCountArray = { 0 };
                _poolCountBuffer.GetData(poolCountArray);
                var availablePoolCount = poolCountArray[0];
                var spawnCount = Mathf.Min(spawnPerFrame, availablePoolCount);
                computeShader.SetInt("_SpawnCount", spawnCount);
                if (spawnCount > 0)
                {
                    computeShader.SetBuffer(k_SpawnParticles, "Particles", _particleBuffer);
                    computeShader.SetBuffer(k_SpawnParticles, "PoolConsume", _poolBuffer);
                    computeShader.Dispatch(k_SpawnParticles, Mathf.CeilToInt((float)spawnCount / ThreadGroupsX), 1, 1);
                }
            }
            
            // Update
            {
                _activeBuffer.SetCounterValue(0);
                computeShader.SetBuffer(k_UpdateParticles, "Particles", _particleBuffer);
                computeShader.SetBuffer(k_UpdateParticles, "AliveList", _activeBuffer);
                computeShader.SetBuffer(k_UpdateParticles, "PoolAppend", _poolBuffer);
                computeShader.SetVector("_BoundsCenter", boundsCenter);
                computeShader.SetVector("_BoundsSize", boundsSize);
                computeShader.Dispatch(k_UpdateParticles, dispatchThreadGroupsX, 1, 1);
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
