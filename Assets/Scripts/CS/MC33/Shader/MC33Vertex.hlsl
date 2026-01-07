#ifndef MC33_VERTEX_UTILITY
#define MC33_VERTEX_UTILITY
#include "../MC33CS.hlsl"

#define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
#include "UnityIndirect.cginc"

StructuredBuffer<MCVertex> _Vertices; // xyz: position, w: packed normal
// StructuredBuffer<uint> _Indices;
// float4x4 _ObjectToWorld;

void MC33Vertex_float(in uint vertexId, out float3 position, out float3 normal, out float4 color)
{
    InitIndirectDrawArgs(0); // single-draw なので 0 固定で OK
    uint vid = GetIndirectVertexID_Base(vertexId);
    
#ifndef SHADERGRAPH_PREVIEW
    MCVertex v = _Vertices[vid];

    position = v.position.xyz;

#ifdef USE_OCT_ENCODED_NORMAL
    uint packed = asuint(v.position.w);
    normal = UnpackOct16(packed);
#else    
    normal = v.normal.xyz;
#endif
    color = v.color;
    
#else
    position = float3(0,0,0);
    normal = float3(0,1,0);
    color = float4(1,1,1,1);
#endif
}

void RenderPrimitivesIndexedIndirect_float(in uint instanceId, in uint vertexId, out float3 position, out float3 normal, out float4 color)
{
#ifndef SHADERGRAPH_PREVIEW
    InitIndirectDrawArgs(0); // single-draw なので 0 固定で OK
    
    // どのコマンドか（マルチドロー時用。ここでは常に 0）
    uint cmdID = GetCommandID(0);

    // インスタンス ID（DrawArgs と整合の取れる ID）
    uint instanceID = GetIndirectInstanceID(instanceId);

    // インデックスバッファから頂点インデックスを取得
    uint vertexIndex = GetIndirectVertexID(vertexId);
    MCVertex v = _Vertices[vertexIndex];

    position = v.position.xyz;
    
#ifdef USE_OCT_ENCODED_NORMAL
    uint packed = asuint(v.position.w);
    normal = UnpackOct16(packed);
#else    
    normal = v.normal.xyz;
#endif
    color = v.color;
#else
    position = float3(0,0,0);
    normal = float3(0,1,0);
    color = float4(1,1,1,1);
#endif
}


#endif
