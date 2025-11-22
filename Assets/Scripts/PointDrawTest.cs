using System;
using UnityEngine;

public class PointDrawTest : MonoBehaviour
{
    [SerializeField] private Material material;
    [SerializeField] private Mesh mesh;
    [SerializeField] private ComputeShader computeShader;

    private GraphicsBuffer commandBuf;
    private GraphicsBuffer.IndirectDrawIndexedArgs[] commandData;
    private const int commandCount = 2;

    [SerializeField]private int particleCapacity = 10;
    // パーティクルデータ配列
    private GraphicsBuffer particles;
    // 空きパーティクルのインデックスプール
    private GraphicsBuffer indexPool;
    // 空きパーティクル数のカウンタ
    private GraphicsBuffer indexCount;
    
    // compute shaderのカーネルID
    private int kernelID_InitFreeList;
    private int kernelID_SpawnParticles;
    private int kernelID_UpdateParticles;
    
    void Start()
    {
        InitKernelID();

        InitComputeBuffers();
    }

    private void InitComputeBuffers()
    {
        commandBuf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, commandCount,
            GraphicsBuffer.IndirectDrawIndexedArgs.size);
        commandData = new GraphicsBuffer.IndirectDrawIndexedArgs[commandCount];
        
        // パーティクルデータ配列
        particles = new GraphicsBuffer(GraphicsBuffer.Target.Structured, particleCapacity, sizeof(float) * 3);

        // フリーリスト用（Appendにする）
        indexPool = new GraphicsBuffer(GraphicsBuffer.Target.Append, particleCapacity, sizeof(uint));
        // カウンタ読み出し用（CopyStructureCountに使う）
        indexCount = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));
    }

    private void InitKernelID()
    {
        kernelID_InitFreeList = computeShader.FindKernel("InitFreeList");
        kernelID_SpawnParticles = computeShader.FindKernel("SpawnParticles");
        kernelID_UpdateParticles = computeShader.FindKernel("UpdateParticles");
    }

    private void OnDestroy()
    {
        commandBuf?.Dispose();
        commandBuf = null;
        particles?.Dispose();
        particles = null;
        indexPool?.Dispose();
        indexPool = null;
        indexCount?.Dispose();
        indexCount = null;
    }

    void Update()
    {
        RenderParams rp = new RenderParams(material);
        rp.worldBounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
        rp.matProps = new MaterialPropertyBlock();
        rp.matProps.SetMatrix("_ObjectToWorld", Matrix4x4.Translate(new Vector3(0f, 0f, 0f)));
        commandData[0].indexCountPerInstance = mesh.GetIndexCount(0);
        commandData[0].instanceCount = 10;
        commandData[1].indexCountPerInstance = mesh.GetIndexCount(0);
        commandData[1].instanceCount = 10;
        commandBuf.SetData(commandData);
        Graphics.RenderMeshIndirect(rp, mesh, commandBuf, commandCount);
    }
    
    // test code
}
