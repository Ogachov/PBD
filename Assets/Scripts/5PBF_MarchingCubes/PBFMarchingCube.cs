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

        [SerializeField] private Material particleMaterial;
        [SerializeField, Range(1, 10000)] private int pointCount = 10000;
        [SerializeField, Range(0, 10000)] private int drawCount = 10000;
        
        [SerializeField, Range(0,100)] private int spawnPerFrame = 10;
        
        [SerializeField] private float gravity = 9.81f;
        [SerializeField] private float3 boundsCenter = new float3(0f, 5f, 0f);
        [SerializeField] private float3 boundsSize = new float3(10f, 10f, 10f);
        [SerializeField, Range(0.001f, 1f)] private float repulsionRadius = 0.1f;
        [SerializeField, Range(0f, 100f)] private float repulsionStrength = 10f;

        [SerializeField] private int3 gridSize = new int3(10, 10, 10);
        [SerializeField] private int maxTriangles = 50000;
        [SerializeField, Range(0f, 1f)] private float isoLevel = 0.5f;
        [SerializeField] private Material surfaceMaterial;
        [SerializeField] private ComputeShader particleComputeShader;
        [SerializeField] private ComputeShader mc33ComputeShader;
        [SerializeField] private float particleRadius = 0.1f;
        [SerializeField] private float particleMass = 1f;
        
        private MC33CS _mc33CS;
        
        public Material _copiedMaterial;

        private int numParticles;
        private GraphicsBuffer _particleBuffer;
        private GraphicsBuffer _activeBuffer;
        private GraphicsBuffer _activeCountBuffer;
        private GraphicsBuffer _poolBuffer;
        private GraphicsBuffer _poolCountBuffer;

        private GraphicsBuffer _dispatchIndirectArgsBuffer;
        private GraphicsBuffer _volumeBuffer;  // 立法格子の中に存在するパーティクルカウント用

        // kernel UpdateParticles
        private int k_InitPoolList;
        private int k_InitParticles;
        private int k_SpawnParticles;
        private int k_UpdateParticles;
        private int k_ClearVolumes;
        private int k_BuildVolumes;
        
        private const int ThreadGroupsX = 128;
        private int dispatchThreadGroupsX;
        
        private int3 volumeSize;

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
                k_ClearVolumes = particleComputeShader.FindKernel("ClearVolumes");
                k_BuildVolumes = particleComputeShader.FindKernel("BuildVolumes");
            }

            var particleSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Particle));
            _particleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numParticles, particleSize);
            _activeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, numParticles, sizeof(uint));
            _activeBuffer.SetCounterValue(0);
            _activeCountBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));
            _poolBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, numParticles, sizeof(uint));
            _poolBuffer.SetCounterValue(0);
            _poolCountBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));
            
            _dispatchIndirectArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments, 1, sizeof(uint) * 3);
            // 1, 1, 1で初期化
            var args = new uint[] { 1, 1, 1 };
            _dispatchIndirectArgsBuffer.SetData(args);
            
            // 密度格子
            volumeSize = gridSize + new int3(1, 1, 1);
            // volumeSizeの最低値は３にする（MC33の仕様上）
            volumeSize = math.max(volumeSize, new int3(3, 3, 3));
            _volumeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, volumeSize.x * volumeSize.y * volumeSize.z, sizeof(uint));

            // プールリスト初期化
            particleComputeShader.SetBuffer(k_InitPoolList, "PoolAppend", _poolBuffer);
            particleComputeShader.SetInt("_Capacity", numParticles);
            particleComputeShader.Dispatch(k_InitPoolList, dispatchThreadGroupsX, 1, 1);
            // パーティクル初期化
            particleComputeShader.SetBuffer(k_InitParticles, "Particles", _particleBuffer);
            particleComputeShader.SetInt("_Capacity", numParticles);
            particleComputeShader.Dispatch(k_InitParticles, dispatchThreadGroupsX, 1, 1);
            
            
            // マテリアルのコピーを作成してバッファをセット
            _copiedMaterial = new Material(particleMaterial);
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
            _dispatchIndirectArgsBuffer?.Dispose();
            _dispatchIndirectArgsBuffer = null;
            _volumeBuffer?.Dispose();
            _volumeBuffer = null;
            
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
                particleComputeShader.SetFloats("_BoundsCenter", boundsCenter.x, boundsCenter.y, boundsCenter.z);
                particleComputeShader.SetFloats("_BoundsSize", boundsSize.x, boundsSize.y, boundsSize.z);
                particleComputeShader.Dispatch(k_UpdateParticles, dispatchThreadGroupsX, 1, 1);
                
                // 密度格子クリア
                particleComputeShader.SetInts("_NumGrid", gridSize.x, gridSize.y, gridSize.z);
                particleComputeShader.SetBuffer(k_ClearVolumes, "_Volumes", _volumeBuffer);
                var threadGroupsX = Mathf.CeilToInt((float)gridSize.x / 8);
                var threadGroupsY = Mathf.CeilToInt((float)gridSize.y / 8);
                var threadGroupsZ = Mathf.CeilToInt((float)gridSize.z / 8);
                particleComputeShader.Dispatch(k_ClearVolumes, threadGroupsX, threadGroupsY, threadGroupsZ);
                // 密度格子ビルド
                GraphicsBuffer.CopyCount(_activeBuffer, _dispatchIndirectArgsBuffer, 0);
                particleComputeShader.SetFloat("_ParticleRadius", particleRadius);
                particleComputeShader.SetFloat("_ParticleMass", particleMass);
                particleComputeShader.SetBuffer(k_BuildVolumes, "Particles", _particleBuffer);
                particleComputeShader.SetBuffer(k_BuildVolumes, "_DispatchArgs", _dispatchIndirectArgsBuffer);
                particleComputeShader.SetBuffer(k_BuildVolumes, "_ActiveList", _activeBuffer);
                particleComputeShader.SetBuffer(k_BuildVolumes, "_Volumes", _volumeBuffer);
                particleComputeShader.DispatchIndirect(k_BuildVolumes, _dispatchIndirectArgsBuffer);
                
                if (_mc33CS == null && mc33ComputeShader != null)
                {
                    _mc33CS = new MC33CS(mc33ComputeShader, maxTriangles);
                }
                _mc33CS?.SetMaxTriangles(maxTriangles);

                if (_mc33CS != null && _volumeBuffer != null)
                {
                    _mc33CS.Kick(_volumeBuffer, new MC33Grid
                    {
                        r0 = -boundsSize * 0.5f + boundsCenter,
                        L = new float3(gridSize),
                        d = boundsSize / gridSize,
                        N = gridSize
                    }, isoLevel);

                    if (surfaceMaterial != null && _mc33CS.IndirectArgsBuffer != null)
                    {
                        surfaceMaterial.SetBuffer("_Vertices", _mc33CS.VertexBuffer);
                        var center = transform.position + new Vector3(0f, gridSize.y * 0.5f, 0f);
                        var size = new Vector3(gridSize.x, gridSize.y, gridSize.z);
                        var bounds = new Bounds(center, size);

                        var surface_rp = new RenderParams(surfaceMaterial)
                        {
                            worldBounds = bounds
                        };

                        Graphics.RenderPrimitivesIndexedIndirect(surface_rp, MeshTopology.Triangles, _mc33CS.IndexBuffer, _mc33CS.IndirectArgsBuffer);
                    }
                }
            }
            
            GraphicsBuffer.CopyCount(_activeBuffer, _activeCountBuffer, 0);
            int[] countArray = {0};
            _activeCountBuffer.GetData(countArray);
            drawCount = countArray[0];
            
            var rp = new RenderParams(_copiedMaterial);
            rp.worldBounds = new Bounds(Vector3.zero, Vector3.one * 100f);

            _copiedMaterial.SetBuffer(PositionBuffer, _particleBuffer);
            _copiedMaterial.SetFloat("_Size", particleMaterial.GetFloat("_Size"));
            _copiedMaterial.SetMatrix(ObjectToWorld, transform.localToWorldMatrix);
        
            Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Points, _activeBuffer, drawCount);
        }
    }
}
