using UnityEngine;

public class RenderPrimitivesIndexedIndirect : MonoBehaviour
{
    [SerializeField, Range(0, 1000)] private int primitiveBufferCount = 1000;
    [SerializeField, Range(0, 1000)] private int drawCount = 1000;
    [SerializeField] private Material material;
    [SerializeField] private ComputeShader computeShader;
    
    private GraphicsBuffer _vertexBuffer;
    private GraphicsBuffer _indirectArgsBuffer;
    private GraphicsBuffer.IndirectDrawIndexedArgs[] commandData;
    private GraphicsBuffer _indexCounterBuffer;
    
    private int k_Init;
    private int k_Draw;
    private int k_SetDrawArgs;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (material == null)
        {
            Debug.LogError("Material is not assigned in the inspector.");
            return;
        }
        // 頂点バッファの作成 primitiveCount個の三角形を定義
        // 各頂点は float4 position, float4 normal, float4 color の形式
        _vertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, primitiveBufferCount * 3, sizeof(float) * 12);
        // 間接描画用の引数バッファの作成
        _indirectArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
        commandData = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
        commandData[0] = new GraphicsBuffer.IndirectDrawIndexedArgs();
        commandData[0].indexCountPerInstance = 0;
        commandData[0].instanceCount = 1; // インスタンス数
        commandData[0].startIndex = 0; // 開始インデックスの位置
        commandData[0].baseVertexIndex = 0; // ベース頂点位置
        commandData[0].startInstance = 0; // 開始インスタンス位置
        _indirectArgsBuffer.SetData(commandData);
        
        _indexCounterBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
        
        // マテリアルにバッファをセット
        material.SetBuffer("_Vertices", _vertexBuffer);

        k_Init = computeShader.FindKernel("Init");
        k_Draw = computeShader.FindKernel("Draw");
        k_SetDrawArgs = computeShader.FindKernel("SetDrawArgs");
        computeShader.SetBuffer(k_Init, "_DrawArgs", _indirectArgsBuffer);
        computeShader.SetBuffer(k_Init, "_IndexCounter", _indexCounterBuffer);
        
        computeShader.SetBuffer(k_Draw, "_Vertices", _vertexBuffer);
        computeShader.SetBuffer(k_Draw, "_DrawArgs", _indirectArgsBuffer);
        computeShader.SetBuffer(k_Draw, "_IndexCounter", _indexCounterBuffer);
        
        computeShader.SetBuffer(k_SetDrawArgs, "_DrawArgs", _indirectArgsBuffer);
        computeShader.SetBuffer(k_SetDrawArgs, "_IndexCounter", _indexCounterBuffer);
        
        computeShader.SetInt("_VerticesCapacity", primitiveBufferCount * 3);
    }
    
    private void OnDestroy()
    {
        // バッファの解放
        _vertexBuffer?.Release();
        // _indexBuffer?.Release();
        _indirectArgsBuffer?.Release();
    }

    // Update is called once per frame
    void Update()
    {
        computeShader.Dispatch(k_Init, 1, 1, 1);
        
        computeShader.SetInt("_NumDraw", drawCount);
        
        computeShader.Dispatch(k_Draw, 1, 1, 1);
        
        computeShader.Dispatch(k_SetDrawArgs, 1, 1, 1);
        
        if (material == null) return;

        var rp = new RenderParams(material);
        // カリングに使うバウンディングボックス（適当な範囲）
        rp.worldBounds = new Bounds(Vector3.zero, Vector3.one * 1000f);

        rp.matProps = new MaterialPropertyBlock();
        
        Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, _indirectArgsBuffer, 1, 0);
        
    }
}
