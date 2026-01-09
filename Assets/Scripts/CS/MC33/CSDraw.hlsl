#if !defined(CSDRAW_HLSL)
#define CSDRAW_HLSL

#include "UnityIndirect.cginc"
#include "MC33CS.hlsl"

RWStructuredBuffer<MCVertex> _Vertices;     // 3頂点=1三角形（MeshTopology.Triangles）
RWStructuredBuffer<uint> _Indices;      // インデックスバッファ（必要ならば使用）
RWStructuredBuffer<IndirectDrawIndexedArgs> _IndirectArgs;         // uint[4] (vertexCountPerInstance, instanceCount, startVertex, startInstance)
RWStructuredBuffer<uint> _IndexCounter;

[numthreads(1,1,1)]
void InitIndirectArgs(uint3 id : SV_DispatchThreadID)
{
    IndirectDrawIndexedArgs args;
    args.indexCountPerInstance = 0;
    args.instanceCount = 1;
    args.startInstance = 0;
    args.baseVertexIndex = 0;
    args.startIndex = 0;
    _IndirectArgs[0] = args;
    
    _IndexCounter[0] = 0;
}

[numthreads(1,1,1)]
void SetIndirectArgs(uint3 id : SV_DispatchThreadID)
{
    IndirectDrawIndexedArgs args;
    args.indexCountPerInstance = _IndexCounter[0];
    args.instanceCount = 1;
    args.startInstance = 0;
    args.baseVertexIndex = 0;
    args.startIndex = 0;
    _IndirectArgs[0] = args;
}

uint AppendVertex(float3 position, float3 normal, float4 color, uint vertexCapacity)
{
    uint prev;
    InterlockedAdd(_IndexCounter[0], 1, prev);
    if (prev >= vertexCapacity)
    {
        return vertexCapacity - 1;
    }
    
    MCVertex v;
    v.position = float4(position,1);
    v.normal = float4(normal,0);
    v.color = color;
    _Vertices[prev] = v;
    return prev;
}

void AppendTriangle(uint i0, uint i1, uint i2, in uint indexCapacity)
{
    uint prev;
    InterlockedAdd(_IndirectArgs[0].indexCountPerInstance, 3, prev);
    if (prev >= indexCapacity)
    {
        return;
    }
    
    _Indices[prev + 0] = i0;
    _Indices[prev + 1] = i1;
    _Indices[prev + 2] = i2;
}

// encode sample
// void AppendVertex(float3 worldPos, float3 worldNormal)
// {
//     uint packed = PackOct16(worldNormal);
//
//     MCVertex v;
//     v.position = float4(worldPos, asfloat(packed)); // ←ここ
//     _Vertices.Append(v);
// }

// decode sample
// Varyings vert(uint id : SV_VertexID)
// {
//     MCVertex v = _Vertices[id];
//
//     uint packed = asuint(v.position.w);
//     float3 nWS = UnpackOct16(packed);
//
//     float4 posWS = float4(v.position.xyz, 1.0); // ←ここ重要（wを捨てて1にする）
//     float4 posCS = mul(UNITY_MATRIX_VP, posWS);
//
//     // ... 出力 ...
// }
#endif
