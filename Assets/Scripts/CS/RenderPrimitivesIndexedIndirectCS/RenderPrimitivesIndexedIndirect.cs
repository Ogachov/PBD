using UnityEngine;

public class RenderPrimitivesIndexedIndirect : MonoBehaviour
{
    [SerializeField, Range(0, 1000)] private int primitiveCount = 100;
    [SerializeField] private Material material;
    
    private GraphicsBuffer _vertexBuffer;
    private GraphicsBuffer _indexBuffer;
    private GraphicsBuffer _indirectArgsBuffer;
    private GraphicsBuffer.IndirectDrawIndexedArgs[] commandData;
    
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
        _vertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, primitiveCount * 3, sizeof(float) * 12);
        Vector4[] vertexData = new Vector4[primitiveCount * 3 * 3]; // 3頂点 * (position + normal + color)
        for (int i = 0; i < primitiveCount; i++)
        {
            // 三角形の頂点位置をランダムに生成
            Vector3 p0 = Random.insideUnitSphere;
            Vector3 p1 = p0 + Random.onUnitSphere * 0.1f;
            Vector3 p2 = p0 + Random.onUnitSphere * 0.1f;
            // 法線を計算
            Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0).normalized;
            // 色をランダムに生成
            Vector4 color = new Vector4(Random.value, Random.value, Random.value, 1.0f);
            // 頂点データを設定
            vertexData[(i * 3 + 0) * 3 + 0] = new Vector4(p0.x, p0.y, p0.z, 1.0f); // position
            vertexData[(i * 3 + 0) * 3 + 1] = new Vector4(normal.x, normal.y, normal.z, 0.0f); // normal
            vertexData[(i * 3 + 0) * 3 + 2] = color; // color
            vertexData[(i * 3 + 1) * 3 + 0] = new Vector4(p1.x, p1.y, p1.z, 1.0f);
            vertexData[(i * 3 + 1) * 3 + 1] = new Vector4(normal.x, normal.y, normal.z, 0.0f);
            vertexData[(i * 3 + 1) * 3 + 2] = color;
            vertexData[(i * 3 + 2) * 3 + 0] = new Vector4(p2.x, p2.y, p2.z, 1.0f);
            vertexData[(i * 3 + 2) * 3 + 1] = new Vector4(normal.x, normal.y, normal.z, 0.0f);
            vertexData[(i * 3 + 2) * 3 + 2] = color;
        }
        _vertexBuffer.SetData(vertexData);
        // インデックスバッファの作成
        _indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index, primitiveCount * 3, sizeof(int));
        int[] indexData = new int[primitiveCount * 3];
        for (int i = 0; i < primitiveCount; i++)
        {
            indexData[i * 3 + 0] = i * 3 + 0;
            indexData[i * 3 + 1] = i * 3 + 1;
            indexData[i * 3 + 2] = i * 3 + 2;
        }
        _indexBuffer.SetData(indexData);
        // 間接描画用の引数バッファの作成
        _indirectArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
        commandData = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
        // マテリアルにバッファをセット
        // material.SetBuffer("_VertexBuffer", _vertexBuffer);
        // material.SetBuffer("_IndexBuffer", _indexBuffer);
        // material.SetBuffer("_IndirectArgsBuffer", _indirectArgsBuffer);
    }
    
    private void OnDestroy()
    {
        // バッファの解放
        _vertexBuffer?.Release();
        _indexBuffer?.Release();
        _indirectArgsBuffer?.Release();
    }

    // Update is called once per frame
    void Update()
    {
        if (material == null) return;
        // RenderPrimitivesIndexedIndirectを使って間接描画の実行

        var rp = new RenderParams(material);
        // カリングに使うバウンディングボックス（適当な範囲）
        rp.worldBounds = new Bounds(Vector3.zero, Vector3.one * 1000f);

        rp.matProps = new MaterialPropertyBlock();
        rp.matProps.SetBuffer("_Vertices", _vertexBuffer);
        rp.matProps.SetBuffer("_Triangles", _indexBuffer);
        // rp.matProps.SetMatrix("_ObjectToWorld", transform.localToWorldMatrix);

        commandData[0] = new GraphicsBuffer.IndirectDrawIndexedArgs();
        commandData[0].indexCountPerInstance = (uint)primitiveCount * 3; // 1つのインスタンスあたりのインデックス数
        commandData[0].instanceCount = 1; // インスタンス数
        commandData[0].startIndex = 0; // 開始インデックスの位置
        commandData[0].baseVertexIndex = 0; // ベース頂点位置
        commandData[0].startInstance = 0; // 開始インスタンス位置
        _indirectArgsBuffer.SetData(commandData);
        
        Graphics.RenderPrimitivesIndexedIndirect(
            rp,
            MeshTopology.Triangles,
            _indexBuffer,
            _indirectArgsBuffer,
            1,   // commandCount
            0    // startCommand
        );
        
    }
}
