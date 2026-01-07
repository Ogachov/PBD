#if !defined(CSDRAW_HLSL)
#define CSDRAW_HLSL

#include "UnityIndirect.cginc"
#include "MC33CS.hlsl"

RWStructuredBuffer<MCVertex> _Vertices;     // 3頂点=1三角形（MeshTopology.Triangles）
RWStructuredBuffer<uint> _Indices;      // インデックスバッファ（必要ならば使用）
RWStructuredBuffer<IndirectDrawArgs> _IndirectArgs;         // uint[4] (vertexCountPerInstance, instanceCount, startVertex, startInstance)
RWStructuredBuffer<uint> _IndexCounter;

[numthreads(1,1,1)]
void InitIndirectArgs(uint3 id : SV_DispatchThreadID)
{
    IndirectDrawArgs args;
    args.vertexCountPerInstance = 0;
    args.instanceCount = 1;
    args.startInstance = 0;
    args.startVertex = 0;
    _IndirectArgs[0] = args;
    
    _IndexCounter[0] = 0;
}

[numthreads(1,1,1)]
void SetIndirectArgs(uint3 id : SV_DispatchThreadID)
{
    IndirectDrawArgs args;
    args.vertexCountPerInstance = _IndexCounter[0];
    args.instanceCount = 1;
    args.startInstance = 0;
    args.startVertex = 0;
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
    InterlockedAdd(_IndirectArgs[0].vertexCountPerInstance, 3, prev);
    if (prev >= indexCapacity)
    {
        return;
    }
    
    _Indices[prev + 0] = i0;
    _Indices[prev + 1] = i1;
    _Indices[prev + 2] = i2;
}

// void AppendTriangle(in float3 p0, in float3 p1, in float3 p2, in float3 normal, in float3 color, in uint capacity)
// {
//     uint prev;
//     InterlockedAdd(_IndirectArgs[0].vertexCountPerInstance, 3, prev);
//     if (prev >= capacity)
//     {
//         return;
//     }
//     uint baseIndex = prev;
//     
//     MCVertex v;
//     v.position = float4(p0, 1);
//     v.normal = float4(normal,1);
//     v.color = float4(color,1);
//     _Vertices[baseIndex + 0] = v;
//     v.position = float4(p1, 1);
//     _Vertices[baseIndex + 1] = v;
//     v.position = float4(p2, 1);
//     _Vertices[baseIndex + 2] = v;
//     
//     InterlockedAdd(_IndexCounter[0], 3, prev);
// }
//
// void EmitTriangle(in MCVertex v0, in MCVertex v1, in MCVertex v2, in uint capacity)
// {
//     uint prev;
//     InterlockedAdd(_IndirectArgs[0].vertexCountPerInstance, 3, prev);
//     if (prev >= capacity)
//     {
//         return;
//     }
//     
//     _Vertices[prev + 0] = v0;
//     _Vertices[prev + 1] = v1;
//     _Vertices[prev + 2] = v2;
//     
//     InterlockedAdd(_IndexCounter[0], 3, prev);
// }
//
// void EmitTriangle(in float4 p0, in float4 p1, in float4 p2, in uint capacity)
// {
//     uint prev;
//     InterlockedAdd(_IndirectArgs[0].vertexCountPerInstance, 3, prev);
//     if (prev >= capacity)
//     {
//         return;
//     }
//     
//     float4 dmyColor = float4(1,1,1,1);
//     float3 n0 = UnpackOct16(asuint(p0.w));
//     float3 n1 = UnpackOct16(asuint(p1.w));
//     float3 n2 = UnpackOct16(asuint(p2.w));
//     
//     MCVertex v;
//     v.color = dmyColor;
//     v.position = float4(p0.xyz,1);
//     v.normal = float4(n0, 0);
//     _Vertices[prev] = v;
//     v.position = float4(p1.xyz,1);
//     v.normal = float4(n1, 0);
//     _Vertices[prev + 1] = v;
//     v.position = float4(p2.xyz,1);
//     v.normal = float4(n2, 0);
//     _Vertices[prev + 2] = v;
//     
//     InterlockedAdd(_IndexCounter[0], 3, prev);
// }


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
